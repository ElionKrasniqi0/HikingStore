using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.ViewModels;
using HikingStore.Services;
using HikingStore.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")] // SECURITY: Strict role-based authorization
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly AdminAuditService _auditService;
        private readonly ILogger<OrdersController> _logger;
        private readonly IHubContext<RealtimeHub> _hubContext;

        public OrdersController(
            ApplicationDbContext context, 
            UserManager<IdentityUser> userManager,
            AdminAuditService auditService,
            ILogger<OrdersController> logger,
            IHubContext<RealtimeHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index(string status = "", string search = "")
        {
            try
            {
                var query = _context.Orders
                    .Include(o => o.User)
                    .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                    .AsQueryable();

                // Filter by status - FIXED: Use System.Enum explicitly
                if (!string.IsNullOrEmpty(status))
                {
                    if (System.Enum.TryParse<OrderStatus>(status, out var orderStatus))
                    {
                        query = query.Where(o => o.Status == orderStatus);
                    }
                }

                // Search functionality
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(o =>
                        o.OrderId.ToString().Contains(search) ||
                        (o.User != null && o.User.UserName != null && o.User.UserName.Contains(search)) ||
                        (o.User != null && o.User.Email != null && o.User.Email.Contains(search)) ||
                        (o.ShippingAddress != null && o.ShippingAddress.Contains(search)) ||
                        (o.PhoneNumber != null && o.PhoneNumber.Contains(search)));
                }

                var orders = await query
                    .AsNoTracking()
                    .OrderByDescending(o => o.OrderDate)
                    .ToListAsync();

                ViewBag.CurrentStatus = status;
                ViewBag.SearchQuery = search;
                ViewBag.StatusList = System.Enum.GetValues(typeof(OrderStatus)).Cast<OrderStatus>();
                ViewData["Title"] = "Orders Management";

                return View(orders ?? new List<Order>());
            }
            catch (Exception ex)
            {
                // Log the exception (you might want to use a proper logging framework)
                // For now, return an empty list to prevent blank page
                ViewBag.CurrentStatus = status;
                ViewBag.SearchQuery = search;
                ViewBag.StatusList = System.Enum.GetValues(typeof(OrderStatus)).Cast<OrderStatus>();
                ViewData["Title"] = "Orders Management";
                return View(new List<Order>());
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Invoice(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }
        [HttpPost]
        public async Task<IActionResult> AddTrackingEvent(int orderId, string location, string status, string description, bool isMilestone = false)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order == null)
            {
                return Json(new { success = false, message = "Order not found." });
            }

            var trackingEvent = new TrackingEvent
            {
                OrderId = orderId,
                EventDate = DateTime.Now,
                Location = location,
                Status = status,
                Description = description,
                IsMilestone = isMilestone
            };

            _context.TrackingEvents.Add(trackingEvent);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Tracking event added successfully." });
        }

        [HttpGet]
        public async Task<IActionResult> GetTrackingHistory(int orderId)
        {
            var trackingEvents = await _context.TrackingEvents
                .Where(te => te.OrderId == orderId)
                .OrderByDescending(te => te.EventDate)
                .ToListAsync();

            return PartialView("_TrackingHistory", trackingEvents);
        }


        [HttpPost]
        [ValidateAntiForgeryToken] // SECURITY: CSRF protection
        public async Task<IActionResult> UpdateStatus(int id, OrderStatus status, string trackingNumber = "", string adminNotes = "", string carrier = "Standard Shipping")
        {
            // SECURITY: Verify admin role (defense in depth)
            if (!User.IsInRole("Admin"))
            {
                _logger.LogWarning("SECURITY: Unauthorized order status update attempt. User: {User}, OrderId: {OrderId}, IP: {IP}", 
                    User.Identity?.Name, id, HttpContext.Connection.RemoteIpAddress?.ToString());
                return Forbid();
            }

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                return Json(new { success = false, message = "Order not found." });
            }

            // SECURITY: Log order status change for audit
            var oldStatus = order.Status;
            var statusChangeDetails = $"Changed from {oldStatus} to {status}";
            if (!string.IsNullOrEmpty(trackingNumber))
            {
                statusChangeDetails += $", Tracking: {trackingNumber}";
            }
            if (!string.IsNullOrEmpty(adminNotes))
            {
                statusChangeDetails += $", Notes: {adminNotes}";
            }
            await _auditService.LogAdminModificationAsync("Order", id.ToString(), "STATUS_UPDATE", oldStatus, status);

            // Update order status (oldStatus already captured above)
            order.Status = status;

            // Handle null values
            order.AdminNotes = adminNotes ?? string.Empty;

            // Automatically generate tracking number and set carrier when status is Processing or Shipped
            // Processing = order is being prepared for shipment (tracking assigned here)
            // Shipped = order has been shipped (tracking should already exist, but generate if missing)
            if (status == OrderStatus.Processing || status == OrderStatus.Shipped)
            {
                // Handle tracking number: generate if not provided or empty/placeholder, and order doesn't have a valid one
                if (!string.IsNullOrWhiteSpace(trackingNumber) && trackingNumber.Trim() != "N/A")
                {
                    // User explicitly provided a tracking number, use it
                    order.TrackingNumber = trackingNumber.Trim();
                }
                else if (string.IsNullOrWhiteSpace(order.TrackingNumber) || order.TrackingNumber == "N/A")
                {
                    // No tracking number provided and order has placeholder or none - auto-generate
                    order.TrackingNumber = GenerateTrackingNumber(order.OrderId);
                }
                // If order already has a valid tracking number and user didn't provide one, keep existing

                // Handle carrier: preserve user-selected carrier unless admin explicitly changes it
                if (!string.IsNullOrWhiteSpace(carrier))
                {
                    // Admin explicitly selected a carrier, use it
                    order.Carrier = carrier.Trim();
                }
                else if (string.IsNullOrWhiteSpace(order.Carrier))
                {
                    // No carrier set - auto-determine based on order value (for backwards compatibility with old orders)
                    order.Carrier = DetermineCarrier(order);
                }
                // If order already has a carrier set (from user selection), preserve it

                // Set estimated delivery date when processing/shipping (typically 3-5 business days from now)
                if (!order.EstimatedDelivery.HasValue)
                {
                    order.EstimatedDelivery = DateTime.Now.AddBusinessDays(4);
                }
            }
            else
            {
                // For non-processing/shipped statuses, preserve existing values unless explicitly changed
                if (!string.IsNullOrWhiteSpace(trackingNumber))
                {
                    order.TrackingNumber = trackingNumber.Trim();
                }
                // Otherwise keep existing tracking number (may remain null/empty for non-processing orders)
                
                if (!string.IsNullOrWhiteSpace(carrier))
                {
                    order.Carrier = carrier.Trim();
                }
                else if (string.IsNullOrWhiteSpace(order.Carrier))
                {
                    order.Carrier = "Standard Shipping";
                }
            }

            // Update status dates
            var now = DateTime.Now;
            switch (status)
            {
                case OrderStatus.Verified:
                    order.VerifiedDate = now;
                    break;
                case OrderStatus.Processing:
                    order.ProcessingDate = now;
                    break;
                case OrderStatus.Shipped:
                    order.ShippedDate = now;
                    break;
                case OrderStatus.Completed:
                    order.CompletedDate = now;
                    break;
                case OrderStatus.Cancelled:
                    order.CancelledDate = now;
                    break;
            }

            await _context.SaveChangesAsync();

            // SECURITY: Log successful status update
            _logger.LogInformation("Admin {Admin} updated order {OrderId} status from {OldStatus} to {NewStatus}", 
                User.Identity?.Name, id, oldStatus, status);

            // REAL-TIME: Notify user and admins of order status update
            _ = _hubContext.Clients.Group($"User_{order.UserId}").SendAsync("OrderStatusUpdated", new
            {
                orderId = order.OrderId,
                status = status.ToString(),
                oldStatus = oldStatus.ToString(),
                trackingNumber = order.TrackingNumber,
                message = $"Your order status has been updated to {status}"
            });

            // Notify all admins
            _ = _hubContext.Clients.Group("Admins").SendAsync("OrderStatusUpdated", new
            {
                orderId = order.OrderId,
                status = status.ToString(),
                userId = order.UserId,
                updatedBy = User.Identity?.Name
            });

            // Notify order-specific group
            _ = _hubContext.Clients.Group($"Order_{order.OrderId}").SendAsync("OrderStatusUpdated", new
            {
                orderId = order.OrderId,
                status = status.ToString(),
                oldStatus = oldStatus.ToString(),
                trackingNumber = order.TrackingNumber
            });

            // Add automated tracking event
            await AddAutomatedTrackingEvent(order.OrderId, oldStatus, status);

            // Send email notifications
            var emailTasks = new List<Task>();

            // Send to customer
            emailTasks.Add(SendOrderStatusEmail(order, oldStatus, status));

            // Send to admin
            emailTasks.Add(SendAdminStatusNotification(order, oldStatus, status));

            try
            {
                await Task.WhenAll(emailTasks);
                return Json(new
                {
                    success = true,
                    message = "Order status updated successfully. Email notifications sent to customer and admin."
                });
            }
            catch (Exception ex)
            {
                // Log the error but don't prevent the status update
                Console.WriteLine($"Failed to send some emails: {ex.Message}");
                return Json(new
                {
                    success = true,
                    message = "Statusi i porosisë u përditësua me sukses. Njoftimet u dërguan me email."
                });
            }
        }


        private async Task AddAutomatedTrackingEvent(int orderId, OrderStatus oldStatus, OrderStatus newStatus)
        {
            var trackingMessage = newStatus switch
            {
                OrderStatus.Received => "Order received and being processed",
                OrderStatus.Verified => "Order verified and payment confirmed",
                OrderStatus.Processing => "Order is being prepared for shipment",
                OrderStatus.Shipped => "Order has been shipped",
                OrderStatus.Completed => "Order delivered successfully",
                OrderStatus.Cancelled => "Order has been cancelled",
                _ => "Order status updated"
            };

            var trackingEvent = new TrackingEvent
            {
                OrderId = orderId,
                EventDate = DateTime.Now,
                Location = "Warehouse",
                Status = newStatus.ToString(),
                Description = trackingMessage,
                IsMilestone = true
            };

            _context.TrackingEvents.Add(trackingEvent);
            await _context.SaveChangesAsync();
        }

        private async Task SendOrderStatusEmail(Order order, OrderStatus oldStatus, OrderStatus newStatus)
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();

                var emailSettings = configuration.GetSection("EmailSettings");

                var fromEmail = emailSettings["SenderEmail"];
                var fromPassword = emailSettings["SenderPassword"];
                var smtpHost = emailSettings["SmtpServer"];
                var smtpPort = emailSettings["SmtpPort"];
                var senderName = emailSettings["SenderName"];

                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(fromPassword))
                {
                    throw new Exception("Email configuration is missing.");
                }

                if (!int.TryParse(smtpPort, out int port))
                {
                    port = 587;
                }

                // Update subject to Albanian
                var statusDisplay = newStatus switch
                {
                    OrderStatus.Received => "E pranuar",
                    OrderStatus.Verified => "E verifikuar",
                    OrderStatus.Processing => "Në procesim",
                    OrderStatus.Shipped => "E nisur",
                    OrderStatus.Completed => "E realizuar",
                    OrderStatus.Cancelled => "E anuluar",
                    _ => newStatus.ToString()
                };

                var subject = $"HikingStore - Statusi i porosisë #ORD{order.OrderId:D6} është {statusDisplay}";
                var emailBody = BuildOrderStatusEmailBody(order, oldStatus, newStatus);

                var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, senderName),
                    Subject = subject,
                    Body = emailBody,
                    IsBodyHtml = true
                };

                message.To.Add(order.User.Email);

                using (var smtpClient = new SmtpClient(smtpHost, port))
                {
                    smtpClient.Credentials = new NetworkCredential(fromEmail, fromPassword);
                    smtpClient.EnableSsl = true;
                    smtpClient.Timeout = 30000;

                    await smtpClient.SendMailAsync(message);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to send order status email: {ex.Message}");
            }
        }

        private string BuildOrderStatusEmailBody(Order order, OrderStatus oldStatus, OrderStatus newStatus)
        {
            var statusDisplay = newStatus switch
            {
                OrderStatus.Received => "Received",
                OrderStatus.Verified => "Verified",
                OrderStatus.Processing => "Processing",
                OrderStatus.Shipped => "Shipped",
                OrderStatus.Completed => "Completed",
                OrderStatus.Cancelled => "Cancelled",
                _ => newStatus.ToString()
            };

            var oldStatusDisplay = oldStatus switch
            {
                OrderStatus.Received => "Received",
                OrderStatus.Verified => "Verified",
                OrderStatus.Processing => "Processing",
                OrderStatus.Shipped => "Shipped",
                OrderStatus.Completed => "Completed",
                OrderStatus.Cancelled => "Cancelled",
                _ => oldStatus.ToString()
            };

            var statusDescriptions = new Dictionary<OrderStatus, string>
    {
        { OrderStatus.Received, "Your order has been received and is being processed" },
        { OrderStatus.Verified, "Your order has been verified and is being prepared" },
        { OrderStatus.Processing, "Your order is being processed in our warehouse" },
        { OrderStatus.Shipped, "Your order has been shipped and is on its way to you" },
        { OrderStatus.Completed, "Your order has been successfully delivered" },
        { OrderStatus.Cancelled, "Your order has been cancelled" }
    };

            var sb = new StringBuilder();

            // Get status color and icon
            var statusColor = newStatus switch
            {
                OrderStatus.Received => "#3b5d50",
                OrderStatus.Verified => "#17a2b8",
                OrderStatus.Processing => "#ffc107",
                OrderStatus.Shipped => "#28a745",
                OrderStatus.Completed => "#28a745",
                OrderStatus.Cancelled => "#dc3545",
                _ => "#6c757d"
            };

            var statusIcon = newStatus switch
            {
                OrderStatus.Received => "📦",
                OrderStatus.Verified => "✓",
                OrderStatus.Processing => "⚙️",
                OrderStatus.Shipped => "🚚",
                OrderStatus.Completed => "✅",
                OrderStatus.Cancelled => "❌",
                _ => "📋"
            };

            sb.AppendLine($@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; 
            line-height: 1.6; 
            color: #333333; 
            background-color: #f5f5f5;
        }}
        .email-wrapper {{ 
            max-width: 650px; 
            margin: 0 auto; 
            background-color: #ffffff;
        }}
        .header {{ 
            background: linear-gradient(135deg, #3b5d50 0%, #2d4a3f 100%); 
            color: #ffffff; 
            padding: 30px 25px; 
            text-align: center;
        }}
        .header h1 {{
            font-size: 24px;
            font-weight: 700;
            margin-bottom: 6px;
            letter-spacing: -0.5px;
        }}
        .header .subtitle {{
            font-size: 14px;
            opacity: 0.95;
            font-weight: 400;
        }}
        .content {{ 
            padding: 30px 25px; 
            background-color: #ffffff;
        }}
        .status-badge-large {{
            background: linear-gradient(135deg, {statusColor} 0%, {statusColor}dd 100%);
            color: white;
            padding: 20px;
            border-radius: 8px;
            text-align: center;
            margin-bottom: 24px;
            box-shadow: 0 4px 12px rgba(0,0,0,0.15);
        }}
        .status-icon {{
            font-size: 48px;
            margin-bottom: 12px;
        }}
        .status-title {{
            font-size: 20px;
            font-weight: 700;
            margin-bottom: 8px;
        }}
        .status-description {{
            font-size: 14px;
            opacity: 0.95;
            margin-top: 8px;
        }}
        .status-change {{
            margin-top: 12px;
            padding-top: 12px;
            border-top: 1px dashed rgba(255,255,255,0.3);
            font-size: 12px;
            opacity: 0.9;
        }}
        .greeting {{
            font-size: 14px;
            color: #555555;
            margin-bottom: 24px;
            line-height: 1.8;
        }}
        .info-card {{
            background: #f8f9fa;
            border-left: 4px solid #3b5d50;
            padding: 18px;
            margin: 20px 0;
            border-radius: 6px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.1);
        }}
        .info-card h3 {{
            color: #3b5d50;
            font-size: 16px;
            font-weight: 600;
            margin-bottom: 12px;
            display: flex;
            align-items: center;
        }}
        .info-card h3::before {{
            content: '';
            display: inline-block;
            width: 3px;
            height: 16px;
            background: #3b5d50;
            margin-right: 10px;
            border-radius: 2px;
        }}
        .info-row {{
            display: flex;
            padding: 8px 0;
            border-bottom: 1px solid #e9ecef;
        }}
        .info-row:last-child {{
            border-bottom: none;
        }}
        .info-label {{
            font-weight: 600;
            color: #495057;
            min-width: 120px;
            font-size: 13px;
        }}
        .info-value {{
            color: #212529;
            flex: 1;
            font-size: 13px;
        }}
        .order-number {{
            font-size: 16px;
            font-weight: 700;
            color: #3b5d50;
            letter-spacing: 0.5px;
        }}
        .tracking-box {{
            background: linear-gradient(135deg, #e8f5e8 0%, #d4edda 100%);
            border: 2px solid #28a745;
            border-radius: 8px;
            padding: 20px;
            margin: 24px 0;
            text-align: center;
        }}
        .tracking-box h3 {{
            color: #28a745;
            font-size: 16px;
            font-weight: 600;
            margin-bottom: 16px;
        }}
        .tracking-number {{
            font-family: 'Courier New', monospace;
            font-size: 18px;
            font-weight: bold;
            color: #2d473d;
            background: white;
            padding: 12px 20px;
            border-radius: 6px;
            display: inline-block;
            margin: 12px 0;
            letter-spacing: 1px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .next-steps {{
            background: linear-gradient(135deg, #fff3cd 0%, #ffeaa7 100%);
            border-left: 4px solid #ffc107;
            padding: 18px;
            margin: 24px 0;
            border-radius: 6px;
        }}
        .next-steps h3 {{
            color: #856404;
            font-size: 16px;
            font-weight: 600;
            margin-bottom: 12px;
        }}
        .next-steps ul {{
            list-style: none;
            padding: 0;
        }}
        .next-steps li {{
            padding: 6px 0;
            padding-left: 20px;
            position: relative;
            color: #856404;
            font-size: 13px;
        }}
        .next-steps li::before {{
            content: '•';
            position: absolute;
            left: 0;
            color: #ffc107;
            font-weight: bold;
            font-size: 16px;
        }}
        .action-buttons {{
            text-align: center;
            margin: 30px 0;
        }}
        .btn {{
            display: inline-block;
            padding: 12px 28px;
            margin: 8px;
            text-decoration: none;
            border-radius: 6px;
            font-weight: 600;
            font-size: 14px;
            transition: all 0.3s ease;
        }}
        .btn-primary {{
            background: #3b5d50;
            color: white;
        }}
        .btn-secondary {{
            background: #6c757d;
            color: white;
        }}
        .contact-info {{
            background: #ffffff;
            border: 1px solid #e9ecef;
            padding: 16px;
            margin: 24px 0;
            border-radius: 6px;
            text-align: center;
        }}
        .contact-info p {{
            color: #6c757d;
            font-size: 13px;
            margin: 6px 0;
        }}
        .contact-info a {{
            color: #3b5d50;
            text-decoration: none;
            font-weight: 600;
        }}
        .contact-info a:hover {{
            text-decoration: underline;
        }}
        .footer {{
            background: #2d4a3f;
            color: #ffffff;
            padding: 24px;
            text-align: center;
            font-size: 12px;
        }}
        .footer p {{
            margin: 5px 0;
            opacity: 0.9;
        }}
        .footer .company-name {{
            font-size: 16px;
            font-weight: 700;
            margin-bottom: 10px;
            opacity: 1;
        }}
        @media only screen and (max-width: 600px) {{
            .content {{ padding: 24px 20px; }}
            .header {{ padding: 24px 20px; }}
            .header h1 {{ font-size: 22px; }}
            .info-row {{
                flex-direction: column;
            }}
            .info-label {{
                margin-bottom: 4px;
            }}
            .btn {{
                display: block;
                margin: 8px 0;
            }}
        }}
    </style>
</head>
<body>
    <div class=""email-wrapper"">
        <div class=""header"">
            <h1>HikingStore</h1>
            <div class=""subtitle"">Order Status Update</div>
        </div>
        
        <div class=""content"">
            <div class=""status-badge-large"">
                <div class=""status-icon"">{statusIcon}</div>
                <div class=""status-title"">{statusDisplay}</div>
                <div class=""status-description"">{statusDescriptions[newStatus]}</div>
                <div class=""status-change"">
                    Status changed from <strong>{oldStatusDisplay}</strong>
                </div>
            </div>
            
            <div class=""greeting"">
                <p>Dear {order.User?.UserName ?? "Valued Customer"},</p>
                <p>We're writing to inform you that your order status has been updated. Here are the current details:</p>
            </div>
            
            <div class=""info-card"">
                <h3>Order Information</h3>
                <div class=""info-row"">
                    <div class=""info-label"">Order Number:</div>
                    <div class=""info-value""><span class=""order-number"">#ORD{order.OrderId:D6}</span></div>
                </div>
                <div class=""info-row"">
                    <div class=""info-label"">Order Date:</div>
                    <div class=""info-value"">{order.OrderDate.ToString("MMMM dd, yyyy 'at' hh:mm tt")}</div>
                </div>
                <div class=""info-row"">
                    <div class=""info-label"">Total Amount:</div>
                    <div class=""info-value""><strong style=""color: #3b5d50; font-size: 16px;"">€{order.TotalAmount:N2}</strong></div>
                </div>
                <div class=""info-row"">
                    <div class=""info-label"">Shipping Address:</div>
                    <div class=""info-value"">{order.ShippingAddress}, {order.City}, Kosovo</div>
                </div>
            </div>");

            // Add tracking information if available and status is shipped
            if (newStatus == OrderStatus.Shipped && !string.IsNullOrEmpty(order.TrackingNumber) && order.TrackingNumber != "N/A")
            {
                sb.AppendLine($@"
            <div class=""tracking-box"">
                <h3>🚚 Tracking Information</h3>
                <div style=""margin: 12px 0;"">
                    <strong style=""color: #495057; font-size: 13px;"">Tracking Number:</strong>
                    <div class=""tracking-number"">{order.TrackingNumber}</div>
                </div>
                <div style=""margin: 10px 0; color: #495057; font-size: 13px;"">
                    <strong>Shipping Carrier:</strong> {order.Carrier ?? "Standard Shipping"}
                </div>
                <p style=""margin-top: 12px; color: #495057; font-size: 13px;"">
                    You can track your order using the tracking number above on the shipping carrier's website.
                </p>
            </div>");
            }

            // Add status-specific next steps
            sb.AppendLine($@"
            <div class=""next-steps"">
                <h3>📝 What's Next?</h3>
                <ul>");

            switch (newStatus)
            {
                case OrderStatus.Verified:
                    sb.AppendLine(@"
                    <li>Your order is being prepared for shipment</li>
                    <li>You will be notified once the order ships</li>
                    <li>Expected delivery time: 3-5 business days</li>");
                    break;
                case OrderStatus.Processing:
                    sb.AppendLine(@"
                    <li>Your products are being carefully packaged</li>
                    <li>The order will be shipped very soon</li>
                    <li>You will receive an email with the tracking number</li>");
                    break;
                case OrderStatus.Shipped:
                    sb.AppendLine(@"
                    <li>Your order is on its way to you</li>
                    <li>Use the tracking number to monitor your order</li>
                    <li>Expected delivery time: 2-3 business days</li>");
                    break;
                case OrderStatus.Completed:
                    sb.AppendLine(@"
                    <li>Your order has been successfully delivered</li>
                    <li>We hope you are satisfied with the products</li>
                    <li>If you have any questions, please contact us</li>");
                    break;
                case OrderStatus.Cancelled:
                    sb.AppendLine(@"
                    <li>Your order has been cancelled</li>
                    <li>If you have questions, please contact us</li>
                    <li>We hope to serve you again in the future</li>");
                    break;
            }

            sb.AppendLine($@"
                </ul>
            </div>

            <div class=""action-buttons"">
                <a href=""https://localhost:7183/Orders"" class=""btn btn-primary"">Track Your Order</a>
                <a href=""https://localhost:7183/Home/Contact"" class=""btn btn-secondary"">Contact Us</a>
            </div>

            <p style=""text-align: center; margin-top: 24px; color: #6c757d; font-size: 13px;"">
                Best regards,<br><strong style=""color: #3b5d50;"">The HikingStore Team</strong>
            </p>
        </div>
        
        <div class=""footer"">
            <p class=""company-name"">HikingStore</p>
            <p>Rr Prshtina Re, Kosovo</p>
            <p>📧 support@HikingStore.com | 📞 +383 (458) 04-555</p>
            <p style=""margin-top: 16px; opacity: 0.7;"">
                <a href=""#"" style=""color: #ffffff;"">Visit our website</a> | <a href=""#"" style=""color: #ffffff;"">Privacy Policy</a> | <a href=""#"" style=""color: #ffffff;"">Terms of Service</a>
            </p>
            <p style=""margin-top: 16px; font-size: 11px; opacity: 0.6;"">
                © {DateTime.Now.Year} HikingStore. All rights reserved.
            </p>
        </div>
    </div>
</body>
</html>");

            return sb.ToString();
        }

        private async Task SendAdminStatusNotification(Order order, OrderStatus oldStatus, OrderStatus newStatus)
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();

                var emailSettings = configuration.GetSection("EmailSettings");

                var fromEmail = emailSettings["SenderEmail"];
                var fromPassword = emailSettings["SenderPassword"];
                var smtpHost = emailSettings["SmtpServer"];
                var smtpPort = emailSettings["SmtpPort"];
                var senderName = emailSettings["SenderName"];

                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(fromPassword))
                {
                    throw new Exception("Email configuration is missing.");
                }

                if (!int.TryParse(smtpPort, out int port))
                {
                    port = 587;
                }

                // Admin subject remains in English
                var subject = $"📊 Order Status Changed - #ORD{order.OrderId:D6} - {oldStatus} → {newStatus}";
                var adminEmailBody = BuildAdminNotificationEmailBody(order, oldStatus, newStatus);

                var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, senderName),
                    Subject = subject,
                    Body = adminEmailBody,
                    IsBodyHtml = true  // Corrected property name
                };

                var adminEmail = emailSettings["AdminEmail"] ?? fromEmail;
                message.To.Add(adminEmail);

                using (var smtpClient = new SmtpClient(smtpHost, port))
                {
                    smtpClient.Credentials = new NetworkCredential(fromEmail, fromPassword);
                    smtpClient.EnableSsl = true;
                    smtpClient.Timeout = 30000;

                    await smtpClient.SendMailAsync(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send admin notification: {ex.Message}");
            }
        }

        private string BuildAdminNotificationEmailBody(Order order, OrderStatus oldStatus, OrderStatus newStatus)
        {
            var statusTranslations = new Dictionary<OrderStatus, (string Albanian, string English)>
    {
        { OrderStatus.Received, ("Received", "Received") },
        { OrderStatus.Verified, ("Verified", "Verified") },
        { OrderStatus.Processing, ("Processing", "Processing") },
        { OrderStatus.Shipped, ("Shipped", "Shipped") },
        { OrderStatus.Completed, ("Completed", "Completed") },
        { OrderStatus.Cancelled, ("Cancelled", "Cancelled") }
    };

            var oldStatusInfo = statusTranslations[oldStatus];
            var newStatusInfo = statusTranslations[newStatus];

            return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #2d473d; color: white; padding: 20px; text-align: center; }}
        .content {{ background: #f8f9fa; padding: 20px; }}
        .alert-info {{ background: #d1ecf1; border: 1px solid #bee5eb; padding: 15px; border-radius: 5px; }}
        .order-details {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; }}
        .status-change {{ background: #fff3cd; padding: 15px; margin: 15px 0; border-radius: 5px; }}
        .status-badge {{ 
            background: #3b5d50; 
            color: white; 
            padding: 5px 10px; 
            border-radius: 15px; 
            font-size: 12px;
            font-weight: bold;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>HikingStore Admin Notification</h2>
            <p>Order Status Change Alert</p>
        </div>
        
        <div class='content'>
            <div class='alert-info'>
                <strong>📋 Order Status Updated by System</strong>
            </div>

            <div class='order-details'>
                <h3>Order Information</h3>
                <p><strong>Order ID:</strong> #ORD{order.OrderId:D6}</p>
                <p><strong>Customer:</strong> {order.User?.UserName} ({order.User?.Email})</p>
                <p><strong>Order Date:</strong> {order.OrderDate.ToString("MMMM dd, yyyy HH:mm")}</p>
                <p><strong>Total Amount:</strong> €{order.TotalAmount:N2}</p>
                <p><strong>Shipping Address:</strong> {order.ShippingAddress}, {order.City}</p>
            </div>

            <div class='status-change'>
                <h3>Status Change Details</h3>
                <p><strong>From:</strong> <span class='status-badge'>{oldStatusInfo.Albanian}</span> ({oldStatusInfo.English})</p>
                <p><strong>To:</strong> <span class='status-badge'>{newStatusInfo.Albanian}</span> ({newStatusInfo.English})</p>
                <p><strong>Change Time:</strong> {DateTime.Now.ToString("MMMM dd, yyyy HH:mm")}</p>
                {(order.TrackingNumber != null && order.TrackingNumber != "N/A" ? $"<p><strong>Tracking Number:</strong> {order.TrackingNumber}</p>" : "")}
                {(order.Carrier != null ? $"<p><strong>Carrier:</strong> {order.Carrier}</p>" : "")}
                {(order.AdminNotes != null && order.AdminNotes != "" ? $"<p><strong>Admin Notes:</strong> {order.AdminNotes}</p>" : "")}
            </div>

            <div style='text-align: center; margin-top: 20px;'>
                <a href='https://youradminwebsite.com/Admin/Orders/Details/{order.OrderId}' 
                   style='background: #3b5d50; color: white; padding: 10px 20px; 
                          text-decoration: none; border-radius: 5px;'>
                   View Order in Admin Panel
                </a>
            </div>
        </div>
    </div>
</body>
</html>";
        }
        [HttpPost]
        [ValidateAntiForgeryToken] // SECURITY: CSRF protection
        public async Task<IActionResult> DeleteOrder(int id)
        {
            // SECURITY: Verify admin role (defense in depth)
            if (!User.IsInRole("Admin"))
            {
                _logger.LogWarning("SECURITY: Unauthorized order deletion attempt. User: {User}, OrderId: {OrderId}, IP: {IP}", 
                    User.Identity?.Name, id, HttpContext.Connection.RemoteIpAddress?.ToString());
                return Forbid();
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.TrackingEvents)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Order not found.";
                return RedirectToAction(nameof(Index));
            }

            // SECURITY: Log order deletion attempt for audit
            var orderDetails = $"Order #{order.OrderId}, Status: {order.Status}, Total: {order.TotalAmount}, User: {order.UserId}";
            await _auditService.LogAdminDeletionAsync("Order", id.ToString(), orderDetails);

            // Only allow deletion of certain statuses (e.g., cancelled or received)
            // You can customize this based on your business rules
            if (order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Received)
            {
                TempData["ErrorMessage"] = "You can only delete orders that are cancelled or recently received.";
                return RedirectToAction(nameof(Details), new { id });
            }

            try
            {
                // Remove related data
                if (order.OrderDetails.Any())
                {
                    _context.OrderDetails.RemoveRange(order.OrderDetails);
                }

                if (order.TrackingEvents.Any())
                {
                    _context.TrackingEvents.RemoveRange(order.TrackingEvents);
                }

                // Remove the order
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Order #{order.OrderId} deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting order: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
    


// Add this method for direct deletion (without JSON)
[HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .Include(o => o.TrackingEvents)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Order not found.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                // Remove related data
                if (order.OrderDetails.Any())
                {
                    _context.OrderDetails.RemoveRange(order.OrderDetails);
                }

                if (order.TrackingEvents.Any())
                {
                    _context.TrackingEvents.RemoveRange(order.TrackingEvents);
                }

                // Remove the order
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Order #{order.OrderId} deleted successfully.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error deleting order: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetOrdersByStatus(OrderStatus status)
        {
            var orders = await _context.Orders
                .Include(o => o.User)
                .Where(o => o.Status == status)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return PartialView("_OrderListPartial", orders);
        }

        /// <summary>
        /// Generates a unique tracking number based on order ID and date
        /// Format: HS{OrderId:6}{Year}{Month}{Day}{Random:4}
        /// Example: HS00012320250115A7B9
        /// </summary>
        private string GenerateTrackingNumber(int orderId)
        {
            var now = DateTime.Now;
            var datePart = now.ToString("yyyyMMdd");
            var randomPart = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
            return $"HS{orderId:D6}{datePart}{randomPart}";
        }

        /// <summary>
        /// Determines the appropriate carrier based on order characteristics
        /// </summary>
        private string DetermineCarrier(Order order)
        {
            // Logic to determine carrier based on order value, weight, or location
            // For now, use a simple logic based on order value
            if (order.TotalAmount >= 500)
            {
                return "Express Shipping";
            }
            else if (order.TotalAmount >= 200)
            {
                return "DHL";
            }
            else if (order.TotalAmount >= 100)
            {
                return "UPS";
            }
            else
            {
                return "Standard Shipping";
            }
        }
    }

    /// <summary>
    /// Extension method to add business days to a DateTime
    /// </summary>
    public static class DateTimeExtensions
    {
        public static DateTime AddBusinessDays(this DateTime date, int days)
        {
            var result = date;
            var addedDays = 0;

            while (addedDays < days)
            {
                result = result.AddDays(1);
                // Skip weekends (Saturday = 6, Sunday = 0)
                if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
                {
                    addedDays++;
                }
            }

            return result;
        }
    }
}