using System.Drawing;
using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Hubs;
using HikingStore.Models;
using HikingStore.Services;
using HikingStore.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = DbInitializer.AdminRole)]
    public class OrdersController : Controller
    {
        private const int PageSize = 25;
        private readonly ApplicationDbContext _context;
        private readonly AdminAuditService _auditService;
        private readonly OrderService _orderService;
        private readonly OrderEmailService _orderEmails;
        private readonly ILogger<OrdersController> _logger;
        private readonly IHubContext<RealtimeHub> _hubContext;
        private readonly PaymentService _paymentService;

        public OrdersController(
            ApplicationDbContext context,
            AdminAuditService auditService,
            OrderService orderService,
            OrderEmailService orderEmails,
            ILogger<OrdersController> logger,
            IHubContext<RealtimeHub> hubContext,
            PaymentService paymentService)
        {
            _paymentService = paymentService;
            _context = context;
            _auditService = auditService;
            _orderService = orderService;
            _orderEmails = orderEmails;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index(string status = "", string search = "", int page = 1)
        {
            var query = Filter(status, search);
            var total = await query.CountAsync();
            var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            page = Math.Clamp(page, 1, pageCount);

            var orders = await query
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .AsNoTracking()
                .OrderByDescending(o => o.OrderDate)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.SearchQuery = search;
            ViewBag.Page = page;
            ViewBag.PageCount = pageCount;
            ViewBag.Total = total;
            ViewBag.StatusCounts = await _context.Orders.GroupBy(o => o.Status)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);
            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var order = await LoadOrderAsync(id);
            return order == null ? NotFound() : View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Invoice(int id)
        {
            var order = await LoadOrderAsync(id);
            return order == null ? NotFound() : View("~/Views/Orders/Invoice.cshtml", order);
        }

        [HttpPost]
        public async Task<IActionResult> AddTrackingEvent(int orderId, string location, string status, string description, bool isMilestone = false)
        {
            if (string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(description))
            {
                return Json(new { success = false, message = "Location, status and description are required." });
            }
            if (!await _context.Orders.AnyAsync(o => o.OrderId == orderId))
            {
                return Json(new { success = false, message = "Order not found." });
            }

            _context.TrackingEvents.Add(new TrackingEvent
            {
                OrderId = orderId,
                EventDate = DateTime.UtcNow,
                Location = Truncate(location, 100),
                Status = Truncate(status, 100),
                Description = Truncate(description, 500),
                IsMilestone = isMilestone
            });
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Tracking event added." });
        }

        [HttpGet]
        public async Task<IActionResult> GetTrackingHistory(int orderId)
        {
            var events = await _context.TrackingEvents.AsNoTracking()
                .Where(te => te.OrderId == orderId)
                .OrderByDescending(te => te.EventDate)
                .ToListAsync();
            return PartialView("~/Views/Orders/_TrackingHistory.cshtml", events);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, OrderStatus status, string? trackingNumber, string? adminNotes, string? carrier)
        {
            if (!System.Enum.IsDefined(status))
            {
                return Json(new { success = false, message = "Unknown status." });
            }

            var current = await _context.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == id);
            if (current != null && current.IsCardPayment && current.Status != OrderStatus.Cancelled)
            {
                if (status is not (OrderStatus.Received or OrderStatus.Cancelled) && current.PaymentStatus != PaymentStatuses.Paid)
                {
                    return Json(new { success = false, message = $"This card order can't be moved to {status} because its payment is \"{current.PaymentStatusLabel}\". Only orders whose payment the provider has confirmed can be fulfilled." });
                }
                if (status == OrderStatus.Cancelled && current.IsAwaitingCardPayment)
                {
                    bool closed;
                    try
                    {
                        closed = await _paymentService.TryCloseSessionForOrderAsync(current, HttpContext.RequestAborted);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Could not close the payment session of order {OrderId}", id);
                        return Json(new { success = false, message = "The payment provider couldn't be reached to stop the customer's payment. Try again in a moment." });
                    }
                    if (!closed)
                    {
                        return Json(new { success = false, message = "The customer has just completed payment and it's being confirmed. Wait for the confirmation, then cancel the order (a refund will be flagged)." });
                    }
                }
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            var outcome = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == id);
                if (order == null)
                {
                    return new StatusUpdateOutcome(false, "Order not found.", null, null);
                }
                if (order.Status == OrderStatus.Cancelled && status != OrderStatus.Cancelled)
                {
                    return new StatusUpdateOutcome(false, "Cancelled orders can't be reopened because their stock has already been released. Ask the customer to place a new order.", null, null);
                }

                var oldStatus = order.Status;
                var now = DateTime.UtcNow;
                order.Status = status;
                order.AdminNotes = string.IsNullOrWhiteSpace(adminNotes) ? order.AdminNotes : Truncate(adminNotes, 2000);
                if (!string.IsNullOrWhiteSpace(carrier)) order.Carrier = Truncate(carrier, 100);
                if (!string.IsNullOrWhiteSpace(trackingNumber)) order.TrackingNumber = Truncate(trackingNumber, 100);

                if (status is OrderStatus.Processing or OrderStatus.Shipped)
                {
                    order.TrackingNumber ??= GenerateTrackingNumber(order.OrderId);
                    order.EstimatedDelivery ??= AddBusinessDays(now, 4);
                }

                switch (status)
                {
                    case OrderStatus.Verified: order.VerifiedDate ??= now; break;
                    case OrderStatus.Processing: order.ProcessingDate ??= now; break;
                    case OrderStatus.Shipped: order.ShippedDate ??= now; break;
                    case OrderStatus.Completed:
                        order.CompletedDate ??= now;
                        if (order.PaymentMethod == PaymentMethods.CashOnDelivery && order.PaymentStatus == PaymentStatuses.Pending)
                        {
                            order.PaymentStatus = PaymentStatuses.Paid;
                        }
                        break;
                    case OrderStatus.Cancelled:
                        order.CancelledDate ??= now;
                        if (order.PaymentStatus == PaymentStatuses.Pending) order.PaymentStatus = PaymentStatuses.Cancelled;
                        else if (order.IsCardPayment && order.PaymentStatus is PaymentStatuses.Paid or PaymentStatuses.RequiresReview)
                            order.PaymentStatus = PaymentStatuses.RefundDue;
                        break;
                }

                if (oldStatus != status)
                {
                    _context.TrackingEvents.Add(new TrackingEvent
                    {
                        OrderId = order.OrderId,
                        EventDate = now,
                        Location = "HikingStore",
                        Status = status.ToString(),
                        Description = TrackingMessage(status),
                        IsMilestone = true
                    });
                }

                await _context.SaveChangesAsync();
                if (oldStatus != OrderStatus.Cancelled && status == OrderStatus.Cancelled)
                {
                    await _orderService.RestockCancelledOrderAsync(order.OrderId);
                }
                await transaction.CommitAsync();
                return new StatusUpdateOutcome(true, "Order updated.", oldStatus, order);
            });

            if (!outcome.Success || outcome.Order == null)
            {
                return Json(new { success = false, message = outcome.Message });
            }

            var updated = outcome.Order;
            var previous = outcome.Old!.Value;
            await _auditService.LogAdminModificationAsync("Order", id.ToString(), "STATUS_UPDATE", previous, status);

            var payload = new
            {
                orderId = updated.OrderId,
                status = status.ToString(),
                oldStatus = previous.ToString(),
                trackingNumber = updated.TrackingNumber,
                message = $"Order #{updated.OrderNumber} is now {OrderEmailService.StatusLabel(status)}."
            };
            await _hubContext.Clients.Group($"User_{updated.UserId}").SendAsync("OrderStatusUpdated", payload);
            await _hubContext.Clients.Group($"Order_{updated.OrderId}").SendAsync("OrderStatusUpdated", payload);

            var message = "Order updated.";
            if (previous != status)
            {
                _orderEmails.QueueStatusUpdate(updated.OrderId, $"{Request.Scheme}://{Request.Host}", notifyAdmin: false);
                message = "Order updated. The customer is being emailed about the new status.";
            }
            if (updated.PaymentStatus == PaymentStatuses.RefundDue)
            {
                message += $" This order was paid by card: refund payment {updated.PaymentTransactionId} in the {updated.PaymentProvider} dashboard.";
            }
            return Json(new { success = true, message });
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string status = "", string search = "")
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var orders = await Filter(status, search)
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .AsNoTracking()
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("Orders");
            string[] headers =
            {
                "Order", "Date (UTC)", "Customer", "Email", "Phone", "Address", "City", "Postal code", "Country",
                "Status", "Payment", "Payment status", "Subtotal", "Discount", "Coupon", "Shipping", "Total",
                "Tracking number", "Carrier", "Items", "Customer comment", "Admin notes"
            };
            for (var i = 0; i < headers.Length; i++)
            {
                sheet.Cells[1, i + 1].Value = headers[i];
            }
            using (var range = sheet.Cells[1, 1, 1, headers.Length])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(0x23, 0x4B, 0x35));
                range.Style.Font.Color.SetColor(Color.White);
            }

            var row = 2;
            foreach (var order in orders)
            {
                var col = 1;
                sheet.Cells[row, col++].Value = order.OrderNumber;
                sheet.Cells[row, col++].Value = order.OrderDate.ToString("yyyy-MM-dd HH:mm");
                sheet.Cells[row, col++].Value = order.FullName ?? order.User?.DisplayName;
                sheet.Cells[row, col++].Value = order.Email ?? order.User?.Email;
                sheet.Cells[row, col++].Value = order.PhoneNumber;
                sheet.Cells[row, col++].Value = order.ShippingAddress;
                sheet.Cells[row, col++].Value = order.City;
                sheet.Cells[row, col++].Value = order.PostalCode;
                sheet.Cells[row, col++].Value = order.Country;
                sheet.Cells[row, col++].Value = OrderEmailService.StatusLabel(order.Status);
                sheet.Cells[row, col++].Value = PaymentMethods.DisplayName(order.PaymentMethod);
                sheet.Cells[row, col++].Value = order.PaymentStatus;
                foreach (var amount in new[] { order.Subtotal, order.DiscountAmount })
                {
                    sheet.Cells[row, col].Value = amount;
                    sheet.Cells[row, col++].Style.Numberformat.Format = "#,##0.00";
                }
                sheet.Cells[row, col++].Value = order.CouponCode;
                foreach (var amount in new[] { order.ShippingCost, order.TotalAmount })
                {
                    sheet.Cells[row, col].Value = amount;
                    sheet.Cells[row, col++].Style.Numberformat.Format = "#,##0.00";
                }
                sheet.Cells[row, col++].Value = order.TrackingNumber;
                sheet.Cells[row, col++].Value = order.Carrier;
                sheet.Cells[row, col++].Value = string.Join("; ", order.OrderDetails.Select(d => $"{d.DisplayName} × {d.Quantity} @ {d.Price:0.00}"));
                sheet.Cells[row, col++].Value = order.Comment;
                sheet.Cells[row, col].Value = order.AdminNotes;
                row++;
            }
            if (sheet.Dimension != null)
            {
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns(8, 60);
            }

            await _auditService.LogAdminActionAsync("EXPORT_ORDERS", $"Exported {orders.Count} orders");
            return File(package.GetAsByteArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"orders-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
        }

        private IQueryable<Order> Filter(string? status, string? search)
        {
            var query = _context.Orders.AsQueryable();
            if (!string.IsNullOrEmpty(status) && System.Enum.TryParse<OrderStatus>(status, out var parsed))
            {
                query = query.Where(o => o.Status == parsed);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var digits = term.StartsWith("ORD", StringComparison.OrdinalIgnoreCase) ? term[3..].TrimStart('0') : term;
                query = query.Where(o =>
                    o.OrderId.ToString() == digits ||
                    (o.FullName != null && o.FullName.Contains(term)) ||
                    (o.Email != null && o.Email.Contains(term)) ||
                    (o.User != null && o.User.Email != null && o.User.Email.Contains(term)) ||
                    o.PhoneNumber.Contains(term) ||
                    (o.TrackingNumber != null && o.TrackingNumber.Contains(term)));
            }
            return query;
        }

        private Task<Order?> LoadOrderAsync(int id) => _context.Orders
            .Include(o => o.User)
            .Include(o => o.OrderDetails).ThenInclude(d => d.Product)
            .Include(o => o.TrackingEvents)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == id);

        private static string TrackingMessage(OrderStatus status) => status switch
        {
            OrderStatus.Received => "Order received",
            OrderStatus.Verified => "Order confirmed by HikingStore",
            OrderStatus.Processing => "Order is being packed",
            OrderStatus.Shipped => "Order handed to the carrier",
            OrderStatus.Completed => "Order delivered",
            OrderStatus.Cancelled => "Order cancelled by HikingStore",
            _ => "Order status updated"
        };

        private static string GenerateTrackingNumber(int orderId) =>
            $"HS{orderId:D6}{DateTime.UtcNow:yyMMdd}{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        private static DateTime AddBusinessDays(DateTime date, int days)
        {
            var result = date;
            while (days > 0)
            {
                result = result.AddDays(1);
                if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
            }
            return result;
        }

        private sealed record StatusUpdateOutcome(bool Success, string Message, OrderStatus? Old, Order? Order);

        private static string Truncate(string value, int max)
        {
            value = value.Trim();
            return value.Length <= max ? value : value[..max];
        }
    }
}
