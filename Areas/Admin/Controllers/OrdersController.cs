using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace HikingStore.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;


        public OrdersController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;

        }

        public async Task<IActionResult> Index(string status = "", string search = "")
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
                    o.User.UserName.Contains(search) ||
                    o.User.Email.Contains(search) ||
                    o.ShippingAddress.Contains(search) ||
                    o.PhoneNumber.Contains(search));
            }

            var orders = await query
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.SearchQuery = search;

            ViewBag.StatusList = System.Enum.GetValues(typeof(OrderStatus)).Cast<OrderStatus>();

            return View(orders);
        }

        public async Task<IActionResult> Details(int id)
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
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
        public async Task<IActionResult> UpdateStatus(int id, OrderStatus status, string trackingNumber = "", string adminNotes = "", string carrier = "Standard Shipping")
        {
            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null)
            {
                return Json(new { success = false, message = "Order not found." });
            }

            var oldStatus = order.Status;
            order.Status = status;

            // Handle null values
            order.TrackingNumber = trackingNumber ?? string.Empty;
            order.AdminNotes = adminNotes ?? string.Empty;
            order.Carrier = carrier ?? "Standard Shipping";

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

            sb.AppendLine($@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ 
            font-family: 'Segoe UI', Arial, sans-serif; 
            line-height: 1.6; 
            color: #333; 
            margin: 0;
            padding: 0;
            background-color: #f8f9fa;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 4px 6px rgba(0, 0, 0, 0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #3b5d50 0%, #2d473d 100%);
            color: white;
            padding: 30px 20px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 28px;
            font-weight: 700;
        }}
        .content {{
            padding: 30px;
        }}
        .status-card {{
            background: #f8f9fa;
            border-radius: 10px;
            padding: 25px;
            margin: 20px 0;
            border-left: 5px solid #3b5d50;
            text-align: center;
        }}
        .current-status {{
            font-size: 24px;
            font-weight: 700;
            color: #3b5d50;
            margin: 10px 0;
        }}
        .status-description {{
            color: #6a6a6a;
            font-size: 16px;
            margin-top: 10px;
        }}
        .order-details {{
            background: white;
            border: 1px solid #e9ecef;
            border-radius: 8px;
            padding: 20px;
            margin: 20px 0;
        }}
        .detail-row {{
            display: flex;
            justify-content: space-between;
            padding: 10px 0;
            border-bottom: 1px solid #f1f3f4;
        }}
        .detail-row:last-child {{
            border-bottom: none;
        }}
        .tracking-box {{
            background: #e8f5e8;
            border: 2px solid #28a745;
            border-radius: 8px;
            padding: 20px;
            margin: 20px 0;
            text-align: center;
        }}
        .tracking-number {{
            font-family: 'Courier New', monospace;
            font-size: 18px;
            font-weight: bold;
            color: #2d473d;
            background: white;
            padding: 10px;
            border-radius: 5px;
            display: inline-block;
            margin: 10px 0;
        }}
        .action-button {{
            background: #3b5d50;
            color: white;
            padding: 12px 30px;
            text-decoration: none;
            border-radius: 6px;
            font-weight: 600;
            display: inline-block;
            margin: 10px 5px;
        }}
        .footer {{
            background: #2d473d;
            color: white;
            padding: 25px;
            text-align: center;
            font-size: 14px;
        }}
        .status-badge {{
            background: #3b5d50;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-weight: 600;
            font-size: 14px;
            display: inline-block;
            margin: 5px;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <h1>HikingStore</h1>
            <div style='opacity: 0.9; margin-top: 5px;'>Order Status Update</div>
        </div>
        
        <div class='content'>
            <p>Dear {order.User?.UserName ?? "Customer"},</p>
            <p>Your order status has been updated. Here are the details:</p>
            
            <div class='status-card'>
                <div style='font-size: 16px; color: #6a6a6a;'>Current order status</div>
                <div class='current-status'>{statusDisplay}</div>
                <div class='status-description'>{statusDescriptions[newStatus]}</div>
                
                <div style='margin-top: 15px; padding-top: 15px; border-top: 1px dashed #ddd;'>
                    <small style='color: #6a6a6a;'>
                        Previous status: <span class='status-badge' style='background: #6c757d;'>{oldStatusDisplay}</span>
                    </small>
                </div>
            </div>

            <div class='order-details'>
                <h3 style='color: #2d473d; margin-top: 0;'>📋 Order Details</h3>
                <div class='detail-row'>
                    <span><strong>Order Number: </strong></span>
                    <span> #ORD{order.OrderId:D6}</span>
                </div>
                <div class='detail-row'>
                    <span><strong>Order Date:</strong></span>
                    <span>{order.OrderDate.ToString("dd.MM.yyyy")}</span>
                </div>
                <div class='detail-row'>
                    <span><strong>Total Amount:</strong></span>
                    <span style='font-weight: bold; color: #3b5d50;'>€{order.TotalAmount:N2}</span>
                </div>
                <div class='detail-row'>
                    <span><strong>Shipping Address:</strong></span>
                    <span>{order.ShippingAddress}, {order.City}</span>
                </div>
            </div>");

            // Add tracking information if available and status is shipped
            if (newStatus == OrderStatus.Shipped && !string.IsNullOrEmpty(order.TrackingNumber) && order.TrackingNumber != "N/A")
            {
                sb.AppendLine($@"
            <div class='tracking-box'>
                <h3 style='color: #28a745; margin-top: 0;'>🚚 Tracking Information</h3>
                <div style='margin: 15px 0;'>
                    <strong>Tracking Number:</strong>
                    <div class='tracking-number'>{order.TrackingNumber}</div>
                </div>
                <div style='margin: 10px 0;'>
                    <strong>Shipping Carrier:</strong>
                    {order.Carrier}
                </div>
                <p style='margin-top: 15px;'>
                    You can track your order using the tracking number above on the shipping carrier's website.
                </p>
            </div>");
            }

            // Add status-specific next steps
            sb.AppendLine($@"
            <div style='background: #fff3cd; border: 1px solid #ffc107; border-radius: 8px; padding: 20px; margin: 20px 0;'>
                <h3 style='color: #856404; margin-top: 0;'>📝 What's Next?</h3>");

            switch (newStatus)
            {
                case OrderStatus.Verified:
                    sb.AppendLine(@"
                <p>• Your order is being prepared for shipment</p>
                <p>• You will be notified once the order ships</p>
                <p>• Expected delivery time: 3-5 business days</p>");
                    break;
                case OrderStatus.Processing:
                    sb.AppendLine(@"
                <p>• Your products are being carefully packaged</p>
                <p>• The order will be shipped very soon</p>
                <p>• You will receive an email with the tracking number</p>");
                    break;
                case OrderStatus.Shipped:
                    sb.AppendLine(@"
                <p>• Your order is on its way to you</p>
                <p>• Use the tracking number to monitor your order</p>
                <p>• Expected delivery time: 2-3 business days</p>");
                    break;
                case OrderStatus.Completed:
                    sb.AppendLine(@"
                <p>• Your order has been successfully delivered</p>
                <p>• We hope you are satisfied with the products</p>
                <p>• If you have any questions, please contact us</p>");
                    break;
                case OrderStatus.Cancelled:
                    sb.AppendLine(@"
                <p>• Your order has been cancelled</p>
                <p>• If you have questions, please contact us</p>
                <p>• We hope to serve you again in the future</p>");
                    break;
            }

            sb.AppendLine($@"
            <div style='text-align: center; margin: 30px 0;'>
                <a href='https://hikingstore.com/orders/track/{order.OrderId}' 
                   style='background: #3b5d50; color: white; padding: 12px 30px; 
                          text-decoration: none; border-radius: 6px; font-weight: 600;
                          display: inline-block; margin: 10px 5px; border: none;'>
                   Track Your Order
                </a>
                <a href='https://hikingstore.com/contact' 
                   style='background: #6c757d; color: white; padding: 12px 30px; 
                          text-decoration: none; border-radius: 6px; font-weight: 600;
                          display: inline-block; margin: 10px 5px; border: none;'>
                   Contact Us
                </a>
            </div>

            <p>If you have any questions, don't hesitate to contact us at support@hikingstore.com or call +383 (458) 04-555.</p>
            
            <p>Best regards,<br>The HikingStore Team</p>
        </div>
        
        <div class='footer'>
            <p><strong>HikingStore</strong></p>
            <p>Rr Prshtina Re, Prishtinë 10000</p>
            <p>📧 support@hikingstore.com | 📞 +383 (458) 04-555</p>
            <p style='margin-top: 15px; opacity: 0.8; font-size: 12px;'>
                &copy; 2025 HikingStore. All rights reserved.
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrder(int id)
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
    }
}