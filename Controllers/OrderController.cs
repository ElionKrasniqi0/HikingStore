using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HikingStore.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(
            ApplicationDbContext context, 
            UserManager<IdentityUser> userManager,
            ILogger<OrdersController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string status = "")
        {
            var userId = _userManager.GetUserId(User);

            // Optimize: Get status counts in a single query using GroupBy
            var statusCountsQuery = await _context.Orders
                .Where(o => o.UserId == userId)
                .AsNoTracking()
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var statusCounts = new Dictionary<OrderStatus, int>();
            foreach (OrderStatus orderStatus in System.Enum.GetValues(typeof(OrderStatus)))
            {
                statusCounts[orderStatus] = 0;
            }
            foreach (var item in statusCountsQuery)
            {
                statusCounts[item.Status] = item.Count;
            }

            var totalCount = statusCounts.Values.Sum();

            // Now filter the query if status is provided
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .AsNoTracking()
                .Where(o => o.UserId == userId);

            // Filter by status - FIXED: Use System.Enum explicitly
            if (!string.IsNullOrEmpty(status))
            {
                if (System.Enum.TryParse<OrderStatus>(status, out var orderStatus))
                {
                    query = query.Where(o => o.Status == orderStatus);
                }
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.StatusCounts = statusCounts;
            ViewBag.TotalCount = totalCount;
            ViewBag.StatusList = System.Enum.GetValues(typeof(OrderStatus)).Cast<OrderStatus>();

            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        public async Task<IActionResult> Invoice(int id, bool download = false)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null)
            {
                return NotFound();
            }

            if (download)
            {
                var request = HttpContext.Request;
                var baseUrl = $"{request.Scheme}://{request.Host}";
                var htmlContent = GenerateInvoiceHtml(order, baseUrl);
                var fileName = $"Invoice_ORD{order.OrderId:D6}_{DateTime.Now:yyyyMMdd}.html";
                Response.Headers.Add("Content-Disposition", $"attachment; filename={fileName}");
                return Content(htmlContent, "text/html");
            }

            return View(order);
        }

        private string GenerateInvoiceHtml(Order order, string baseUrl)
        {
            var vatRate = 0.18m;
            var subtotalWithoutVat = order.TotalAmount / (1 + vatRate);
            var vatAmount = order.TotalAmount - subtotalWithoutVat;
            
            // Calculate subtotal of all items
            var itemsSubtotal = order.OrderDetails.Sum(od => od.Price * od.Quantity);
            var discountAmount = itemsSubtotal - subtotalWithoutVat;
            if (discountAmount < 0) discountAmount = 0;

            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Invoice #ORD{order.OrderId:D6}</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: 'Arial', 'Helvetica', sans-serif; 
            font-size: 9pt; 
            line-height: 1.3; 
            color: #000; 
            background: #fff; 
            padding: 15mm;
        }}
        .invoice {{ max-width: 210mm; margin: 0 auto; }}
        .header {{ 
            display: flex; 
            justify-content: space-between; 
            margin-bottom: 12pt; 
            padding-bottom: 8pt; 
            border-bottom: 2pt solid #000; 
        }}
        .company {{ flex: 1; }}
        .company-name {{ font-size: 16pt; font-weight: bold; margin-bottom: 4pt; }}
        .company-details {{ font-size: 7.5pt; color: #333; line-height: 1.4; }}
        .invoice-info {{ text-align: right; flex: 1; }}
        .invoice-label {{ font-size: 9pt; font-weight: bold; margin-bottom: 4pt; }}
        .invoice-number {{ font-size: 14pt; font-weight: bold; margin-bottom: 6pt; }}
        .invoice-date {{ font-size: 7.5pt; color: #333; }}
        .details {{ 
            display: flex; 
            justify-content: space-between; 
            margin-bottom: 15pt; 
            gap: 15pt; 
        }}
        .section {{ flex: 1; }}
        .section-title {{
            font-size: 8pt;
            font-weight: bold;
            margin-bottom: 6pt;
            text-transform: uppercase;
            letter-spacing: 0.5pt;
            border-bottom: 0.5pt solid #000;
            padding-bottom: 3pt;
        }}
        .detail-item {{ font-size: 7.5pt; margin-bottom: 3pt; line-height: 1.4; }}
        .detail-label {{ font-weight: 600; }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 12pt 0;
            font-size: 8pt;
        }}
        th {{
            background: #f0f0f0;
            border: 0.5pt solid #000;
            padding: 4pt 6pt;
            text-align: left;
            font-weight: bold;
        }}
        th.text-right {{ text-align: right; }}
        th.text-center {{ text-align: center; }}
        td {{
            border: 0.5pt solid #000;
            padding: 4pt 6pt;
            vertical-align: middle;
        }}
        td.text-right {{ text-align: right; }}
        td.text-center {{ text-align: center; }}
        .product-image {{
            width: 50pt;
            height: 50pt;
            object-fit: cover;
            border: 0.5pt solid #ddd;
        }}
        .product-name {{ font-weight: 600; }}
        .totals {{
            margin-top: 10pt;
            padding-top: 8pt;
            border-top: 1pt solid #000;
        }}
        .total-row {{
            display: flex;
            justify-content: space-between;
            padding: 2pt 0;
            font-size: 8pt;
        }}
        .grand-total {{
            border-top: 1pt solid #000;
            font-weight: bold;
            font-size: 9pt;
            margin-top: 4pt;
            padding-top: 4pt;
        }}
        .footer {{
            margin-top: 20pt;
            padding-top: 8pt;
            border-top: 0.5pt solid #ccc;
            font-size: 7pt;
            color: #666;
            text-align: center;
        }}
    </style>
</head>
<body>
    <div class=""invoice"">
        <div class=""header"">
            <div class=""company"">
                <div class=""company-name"">HikingStore</div>
                <div class=""company-details"">
                    Rr Prshtina Re, Kosovo<br>
                    support@hikingstore.com<br>
                    +383 (458) 04-555
                </div>
            </div>
            <div class=""invoice-info"">
                <div class=""invoice-label"">INVOICE</div>
                <div class=""invoice-number"">#ORD{order.OrderId:D6}</div>
                <div class=""invoice-date"">{order.OrderDate:MMMM dd, yyyy}</div>
            </div>
        </div>

        <div class=""details"">
            <div class=""section"">
                <div class=""section-title"">Bill To</div>
                <div class=""detail-item""><span class=""detail-label"">Name:</span> {order.User.UserName}</div>
                <div class=""detail-item""><span class=""detail-label"">Email:</span> {order.User.Email}</div>
                <div class=""detail-item""><span class=""detail-label"">Phone:</span> {order.PhoneNumber}</div>
            </div>
            <div class=""section"">
                <div class=""section-title"">Ship To</div>
                <div class=""detail-item""><span class=""detail-label"">Address:</span> {order.ShippingAddress}</div>
                <div class=""detail-item""><span class=""detail-label"">City:</span> {order.City}, Kosovo</div>
                <div class=""detail-item""><span class=""detail-label"">Shipping:</span> {order.Carrier ?? "Standard Shipping"}</div>";

            if (order.Status == OrderStatus.Shipped && !string.IsNullOrEmpty(order.TrackingNumber) && order.TrackingNumber != "N/A")
            {
                html += $@"
                <div class=""detail-item""><span class=""detail-label"">Tracking:</span> {order.TrackingNumber}</div>";
            }

            html += $@"
            </div>
        </div>

        <table>
            <thead>
                <tr>
                    <th style=""width: 10%;"" class=""text-center"">Photo</th>
                    <th style=""width: 40%;"">Product</th>
                    <th style=""width: 10%;"" class=""text-center"">Qty</th>
                    <th style=""width: 20%;"" class=""text-right"">Unit Price</th>
                    <th style=""width: 20%;"" class=""text-right"">Total</th>
                </tr>
            </thead>
            <tbody>";

            int itemNumber = 1;
            foreach (var item in order.OrderDetails)
            {
                html += $@"
                <tr>
                    <td class=""text-center"">
                        <img src=""{baseUrl}/Product/{item.Product.ProImage}"" 
                             alt=""{item.Product.ProName}"" 
                             class=""product-image"" 
                             onerror=""this.style.display='none'"">
                    </td>
                    <td class=""product-name"">{item.Product.ProName}</td>
                    <td class=""text-center"">{item.Quantity}</td>
                    <td class=""text-right"">€{item.Price:N2}</td>
                    <td class=""text-right"">€{(item.Price * item.Quantity):N2}</td>
                </tr>";
                itemNumber++;
            }

            html += $@"
            </tbody>
        </table>

        <div class=""totals"">
            <div class=""total-row"">
                <span>Subtotal (excl. VAT)</span>
                <span>€{subtotalWithoutVat:N2}</span>
            </div>
            <div class=""total-row"">
                <span>Shipping</span>
                <span>FREE</span>
            </div>
            <div class=""total-row"">
                <span>VAT (18%)</span>
                <span>€{vatAmount:N2}</span>
            </div>
            <div class=""total-row grand-total"">
                <span>TOTAL</span>
                <span>€{order.TotalAmount:N2}</span>
            </div>
        </div>

        <div class=""footer"">
            <p>Thank you for your business!</p>
            <p>This is an official invoice for your records.</p>
        </div>
    </div>
</body>
</html>";

            return html;
        }

        [HttpPost]
        [ValidateAntiForgeryToken] // CSRF protection
        public async Task<IActionResult> CancelOrder(int id)
        {
            // SECURITY: Always get userId from authenticated user, never from request
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("SECURITY: CancelOrder attempted without authenticated user. OrderId: {OrderId}, IP: {IP}", 
                    id, HttpContext.Connection.RemoteIpAddress?.ToString());
                return Json(new { success = false, message = "Unauthorized." });
            }

            // SECURITY: Users can only cancel their own orders - verify ownership
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);

            if (order == null)
            {
                // SECURITY: Check if order exists with different user (potential unauthorized access attempt)
                var orderExists = await _context.Orders.AnyAsync(o => o.OrderId == id);
                if (orderExists)
                {
                    _logger.LogWarning("SECURITY: Unauthorized order access attempt. User {UserId} attempted to cancel order {OrderId} belonging to another user. IP: {IP}", 
                        userId, id, HttpContext.Connection.RemoteIpAddress?.ToString());
                }
                return Json(new { success = false, message = "Order not found." });
            }

            // Only allow cancellation for orders that haven't been shipped
            if (order.Status >= OrderStatus.Shipped)
            {
                return Json(new { success = false, message = "Cannot cancel order that has already been shipped." });
            }

            // SECURITY: Set status server-side only, never trust frontend
            order.Status = OrderStatus.Cancelled;
            order.CancelledDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Order {OrderId} cancelled by authenticated user {UserId}", id, userId);
            return Json(new { success = true, message = "Order cancelled successfully." });
        }
    }
}
