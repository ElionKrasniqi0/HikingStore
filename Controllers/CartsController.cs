using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Logging;
using HikingStore.Hubs;

namespace HikingStore.Controllers
{
    public class CartsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CartsController> _logger;
        private readonly IHubContext<RealtimeHub> _hubContext;

        public CartsController(
            ApplicationDbContext context, 
            UserManager<IdentityUser> userManager, 
            IConfiguration configuration,
            ILogger<CartsController> logger,
            IHubContext<RealtimeHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _logger = logger;
            _hubContext = hubContext;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var cartItems = await _context.ShoppingCarts
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            var totalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);

            var model = new ShoppingCartDetails
            {
                Carts = cartItems,
                TotalAmount = totalAmount,
                IsEmpty = !cartItems.Any()
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int ProId, int qty = 1, string shippingOption = "Standard Shipping")
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { success = false, message = "Please login to add items to cart." });
            }

            var product = await _context.Products.FindAsync(ProId);
            if (product == null)
            {
                return Json(new { success = false, message = "Product not found." });
            }

            if (qty <= 0)
            {
                return Json(new { success = false, message = "Quantity must be at least 1." });
            }

            if (!product.IsInStock || product.StockQuantity < qty)
            {
                return Json(new { success = false, message = "Insufficient stock." });
            }

            // Validate shipping option
            if (string.IsNullOrWhiteSpace(shippingOption))
            {
                shippingOption = "Standard Shipping";
            }

            // Check if item already exists in cart
            var existingCartItem = await _context.ShoppingCarts
                .FirstOrDefaultAsync(c => c.UserId == userId && c.ProId == ProId);

            if (existingCartItem != null)
            {
                // Update quantity and shipping option
                existingCartItem.Qty += qty;
                if (existingCartItem.Qty > product.StockQuantity)
                {
                    existingCartItem.Qty = product.StockQuantity;
                }
                existingCartItem.ShippingOption = shippingOption;
            }
            else
            {
                // Add new item
                var cartItem = new ShoppingCart
                {
                    ProId = ProId,
                    UserId = userId,
                    Qty = qty,
                    ShippingOption = shippingOption
                };
                _context.ShoppingCarts.Add(cartItem);
            }

            await _context.SaveChangesAsync();

            // Get updated cart count
            var cartItemCount = await _context.ShoppingCarts
                .Where(c => c.UserId == userId)
                .SumAsync(c => c.Qty);

            // REAL-TIME: Notify user of cart update
            _ = _hubContext.Clients.Group($"User_{userId}").SendAsync("CartUpdated", new
            {
                cartItemCount = cartItemCount,
                message = "Item added to cart!",
                productId = ProId,
                productName = product.ProName
            });

            return Json(new { success = true, message = "Item added to cart!", cartItemCount = cartItemCount });
        }
        [AllowAnonymous]
        public IActionResult RequireLogin(string? returnUrl = null)
        {
            // Store the intended URL so we can return after login
            ViewBag.ReturnUrl = returnUrl ?? "/Carts";
            return View();
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RemoveFromCart(int CartId)
        {
            var userId = _userManager.GetUserId(User);
            var cartItem = await _context.ShoppingCarts
                .FirstOrDefaultAsync(c => c.CartId == CartId && c.UserId == userId);

            if (cartItem != null)
            {
                _context.ShoppingCarts.Remove(cartItem);
                await _context.SaveChangesAsync();

                // Get updated cart count
                var cartItemCount = await _context.ShoppingCarts
                    .Where(c => c.UserId == userId)
                    .SumAsync(c => c.Qty);

                // REAL-TIME: Notify user of cart update
                _ = _hubContext.Clients.Group($"User_{userId}").SendAsync("CartUpdated", new
                {
                    cartItemCount = cartItemCount,
                    message = "Item removed from cart."
                });
            }
            return RedirectToAction(nameof(Index));
        }

        [Authorize]
        public async Task<IActionResult> Checkout()
        {
            var userId = _userManager.GetUserId(User);
            var cartItems = await _context.ShoppingCarts
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            var totalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);

            ViewBag.Carts = cartItems;
            ViewBag.TotalAmount = totalAmount;

            return View();
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Checkout(Order order)
        {
            // SECURITY: Always get userId from authenticated user, never from request
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("SECURITY: Checkout attempted without authenticated user. IP: {IP}", 
                    HttpContext.Connection.RemoteIpAddress?.ToString());
                return Unauthorized();
            }

            // SECURITY: Validate and prevent user impersonation
            // If order.UserId is provided, it must match the authenticated user
            if (!string.IsNullOrEmpty(order.UserId) && order.UserId != userId)
            {
                _logger.LogWarning("SECURITY: User impersonation attempt detected. Authenticated UserId: {AuthUserId}, Provided UserId: {ProvidedUserId}, IP: {IP}", 
                    userId, order.UserId, HttpContext.Connection.RemoteIpAddress?.ToString());
                ModelState.AddModelError("", "Invalid request. Security violation detected.");
                return BadRequest("Invalid request.");
            }

            // SECURITY: Load cart items only for the authenticated user
            var cartItems = await _context.ShoppingCarts
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                ModelState.AddModelError("", "Your cart is empty.");
                ViewBag.Carts = cartItems;
                ViewBag.TotalAmount = 0;
                return View(order);
            }

            // Check stock for all items before processing checkout
            var outOfStockItems = new List<string>();
            foreach (var cartItem in cartItems)
            {
                var product = cartItem.Product;
                if (!product.IsInStock)
                {
                    outOfStockItems.Add(product.ProName);
                }
                else if (cartItem.Qty > product.StockQuantity)
                {
                    outOfStockItems.Add($"{product.ProName} (only {product.StockQuantity} available)");
                }
            }

            if (outOfStockItems.Any())
            {
                ModelState.AddModelError("", $"Some items are out of stock or have insufficient quantity: {string.Join(", ", outOfStockItems)}");
                ViewBag.Carts = cartItems;
                ViewBag.TotalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);
                return View("Checkout", order);
            }

            // Calculate total amount once
            var totalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);

            // Deduct stock quantities
            foreach (var cartItem in cartItems)
            {
                var product = cartItem.Product;
                product.StockQuantity -= cartItem.Qty;
                _context.Products.Update(product);
            }

            // Get the most common shipping option from cart items, or default to first one
            var shippingOption = cartItems
                .Where(c => !string.IsNullOrEmpty(c.ShippingOption))
                .GroupBy(c => c.ShippingOption)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? cartItems.FirstOrDefault()?.ShippingOption ?? "Standard Shipping";

            // SECURITY: Always set UserId from authenticated user, ignore any value from request
            // This prevents user impersonation attacks
            order.UserId = userId; // Override any UserId that might have been sent from frontend
            order.OrderDate = DateTime.UtcNow;
            order.TotalAmount = totalAmount;
            order.Status = OrderStatus.Received; // SECURITY: Status set server-side only
            order.TrackingNumber = "N/A";
            order.AdminNotes = $"Order created by authenticated user {userId}";
            order.Carrier = shippingOption;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync(); // Need OrderId for order details

            // Prepare order details in batch
            var orderDetails = new List<OrderDetail>();
            foreach (var cartItem in cartItems)
            {
                orderDetails.Add(new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProId = cartItem.ProId,
                    Quantity = cartItem.Qty,
                    Price = cartItem.Product.Price
                });
            }
            _context.OrderDetails.AddRange(orderDetails);

            _context.ShoppingCarts.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            // SECURITY: Log successful order creation for audit trail
            _logger.LogInformation("Order {OrderId} created successfully for authenticated user {UserId} with total {TotalAmount}", 
                order.OrderId, userId, totalAmount);

            // Get user email before redirecting
            var user = await _userManager.GetUserAsync(User);
            var userEmail = user?.Email;

            // SECURITY: Verify the user object matches the authenticated user
            if (user == null || user.Id != userId)
            {
                _logger.LogError("SECURITY: User mismatch detected after order creation. OrderId: {OrderId}, Expected UserId: {UserId}", 
                    order.OrderId, userId);
                // Order already created, but log the security issue
            }

            // Send order confirmation email asynchronously (fire-and-forget to not block response)
            if (!string.IsNullOrEmpty(userEmail))
            {
                // Capture data needed for email before redirect
                var orderForEmail = new Order
                {
                    OrderId = order.OrderId,
                    OrderDate = order.OrderDate,
                    TotalAmount = order.TotalAmount,
                    ShippingAddress = order.ShippingAddress,
                    City = order.City,
                    PhoneNumber = order.PhoneNumber,
                    Comment = order.Comment
                };

                var cartItemsForEmail = cartItems.Select(c => new ShoppingCart
                {
                    Qty = c.Qty,
                    Product = new Product { ProName = c.Product.ProName, Price = c.Product.Price }
                }).ToList();

                // Capture configuration and base URL before background task
                var emailConfig = _configuration;
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                // Fire and forget - don't wait for email to complete
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendOrderConfirmationEmailAsync(emailConfig, userEmail, orderForEmail, cartItemsForEmail, baseUrl);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send confirmation email: {ex.Message}");
                    }
                });
            }

            return RedirectToAction("OrderConfirmation", new { id = order.OrderId });
        }

        private static async Task SendOrderConfirmationEmailAsync(IConfiguration configuration, string userEmail, Order order, List<ShoppingCart> cartItems, string baseUrl = "https://hikingstore.com")
        {
            try
            {
                var emailSettings = configuration.GetSection("EmailSettings");

                var fromEmail = emailSettings["SenderEmail"];
                var fromPassword = emailSettings["SenderPassword"];
                var smtpHost = emailSettings["SmtpServer"] ?? "smtp.gmail.com";
                var smtpPort = emailSettings["SmtpPort"] ?? "587";
                var senderName = emailSettings["SenderName"] ?? "HikingStore";

                if (string.IsNullOrEmpty(fromEmail) || string.IsNullOrEmpty(fromPassword))
                {
                    throw new Exception("Email configuration is missing.");
                }

                if (!int.TryParse(smtpPort, out int port))
                {
                    port = 587;
                }

                // Build email subject
                var subject = $"Order Confirmation - #ORD{order.OrderId:D6}";

                // Build email body
                var emailBody = BuildOrderConfirmationEmail(order, cartItems, baseUrl);

                var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, senderName),
                    Subject = subject,
                    Body = emailBody,
                    IsBodyHtml = true
                };

                message.To.Add(userEmail);

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
                throw new Exception($"Failed to send order confirmation email: {ex.Message}");
            }
        }

        private static string BuildOrderConfirmationEmail(Order order, List<ShoppingCart> cartItems, string baseUrl = "https://hikingstore.com")
        {
            var sb = new StringBuilder();

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
        .success-badge {{
            background: linear-gradient(135deg, #28a745 0%, #20c997 100%);
            color: white;
            padding: 12px 20px;
            border-radius: 6px;
            text-align: center;
            margin-bottom: 24px;
            font-size: 15px;
            font-weight: 600;
            box-shadow: 0 2px 8px rgba(40, 167, 69, 0.2);
        }}
        .greeting {{
            font-size: 14px;
            color: #555555;
            margin-bottom: 24px;
            line-height: 1.6;
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
        .product-table-wrapper {{
            margin: 24px 0;
            overflow-x: auto;
        }}
        .product-table {{
            width: 100%;
            border-collapse: collapse;
            background: white;
            border-radius: 6px;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0,0,0,0.08);
        }}
        .product-table thead {{
            background: linear-gradient(135deg, #3b5d50 0%, #2d4a3f 100%);
            color: white;
        }}
        .product-table th {{
            padding: 12px;
            text-align: left;
            font-weight: 600;
            font-size: 12px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        .product-table td {{
            padding: 12px;
            border-bottom: 1px solid #e9ecef;
            font-size: 13px;
            color: #495057;
        }}
        .product-table tbody tr:hover {{
            background-color: #f8f9fa;
        }}
        .product-table tbody tr:last-child td {{
            border-bottom: none;
        }}
        .product-name {{
            font-weight: 600;
            color: #212529;
        }}
        .total-row {{
            background: #f8f9fa !important;
            font-weight: 700;
        }}
        .total-row td {{
            font-size: 14px;
            color: #3b5d50;
            padding: 14px 12px;
        }}
        .next-steps {{
            background: linear-gradient(135deg, #e7f5ff 0%, #d0ebff 100%);
            border-left: 4px solid #3b5d50;
            padding: 18px;
            margin: 24px 0;
            border-radius: 6px;
        }}
        .next-steps h3 {{
            color: #3b5d50;
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
            color: #495057;
            font-size: 13px;
        }}
        .next-steps li::before {{
            content: '✓';
            position: absolute;
            left: 0;
            color: #3b5d50;
            font-weight: bold;
            font-size: 16px;
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
            padding: 30px;
            text-align: center;
            font-size: 13px;
        }}
        .footer p {{
            margin: 6px 0;
            opacity: 0.9;
        }}
        .footer a {{
            color: #ffffff;
            text-decoration: underline;
        }}
        .footer .company-name {{
            font-size: 18px;
            font-weight: 700;
            margin-bottom: 12px;
            opacity: 1;
        }}
        @media only screen and (max-width: 600px) {{
            .content {{ padding: 24px 20px; }}
            .header {{ padding: 30px 20px; }}
            .header h1 {{ font-size: 26px; }}
            .info-row {{
                flex-direction: column;
            }}
            .info-label {{
                margin-bottom: 4px;
            }}
            .product-table {{
                font-size: 12px;
            }}
            .product-table th,
            .product-table td {{
                padding: 12px 8px;
            }}
        }}
    </style>
</head>
<body>
    <div class=""email-wrapper"">
            <div class=""header"">
            <h1>HikingStore</h1>
            <div class=""subtitle"">Order Confirmation</div>
        </div>
        
        <div class=""content"">
            <div class=""success-badge"">
                ✓ Order Confirmed Successfully!
            </div>
            
            <div class=""greeting"">
                <p>Dear Valued Customer,</p>
                <p>Thank you for your purchase! We're excited to confirm that we've received your order and it's now being processed. We'll send you another email once your order ships.</p>
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
            </div>

            <div class=""info-card"">
                <h3>Shipping Information</h3>
                <div class=""info-row"">
                    <div class=""info-label"">Delivery Address:</div>
                    <div class=""info-value"">{order.ShippingAddress}</div>
                </div>
                <div class=""info-row"">
                    <div class=""info-label"">City:</div>
                    <div class=""info-value"">{order.City}, Kosovo</div>
                </div>
                <div class=""info-row"">
                    <div class=""info-label"">Phone Number:</div>
                    <div class=""info-value"">+383 {order.PhoneNumber}</div>
                </div>
                {(!string.IsNullOrEmpty(order.Comment) ? $@"
                <div class=""info-row"">
                    <div class=""info-label"">Order Notes:</div>
                    <div class=""info-value"">{order.Comment}</div>
                </div>" : "")}
            </div>

            <div class=""product-table-wrapper"">
                <table class=""product-table"">
                    <thead>
                        <tr>
                            <th>Product</th>
                            <th style=""text-align: center;"">Quantity</th>
                            <th style=""text-align: right;"">Unit Price</th>
                            <th style=""text-align: right;"">Subtotal</th>
                        </tr>
                    </thead>
                    <tbody>");

            foreach (var item in cartItems)
            {
                sb.AppendLine($@"
                        <tr>
                            <td class=""product-name"">{item.Product?.ProName ?? "Product"}</td>
                            <td style=""text-align: center;"">{item.Qty}</td>
                            <td style=""text-align: right;"">€{(item.Product?.Price ?? 0):N2}</td>
                            <td style=""text-align: right; font-weight: 600;"">€{((item.Product?.Price ?? 0) * item.Qty):N2}</td>
                        </tr>");
            }

            sb.AppendLine($@"
                        <tr class=""total-row"">
                            <td colspan=""3"" style=""text-align: right; padding-right: 12px;"">Grand Total:</td>
                            <td style=""text-align: right; font-size: 16px;"">€{order.TotalAmount:N2}</td>
                        </tr>
                    </tbody>
                </table>
            </div>

            <div class=""next-steps"">
                <h3>What Happens Next?</h3>
                <ul>
                    <li>Your order is being prepared and will be shipped soon</li>
                    <li>You'll receive a shipping confirmation email with tracking information</li>
                    <li>Expected delivery: 3-5 business days</li>
                    <li>Track your order anytime from your account dashboard</li>
                </ul>
            </div>

            <div style=""text-align: center; margin: 30px 0; padding: 20px; background: #f8f9fa; border-radius: 8px;"">
                <h3 style=""color: #3b5d50; margin-bottom: 15px;"">📄 Download Your Invoice</h3>
                <p style=""margin-bottom: 20px; color: #495057;"">Download your order invoice for your records.</p>
                <a href=""{baseUrl}/Orders/Invoice/{order.OrderId}?download=true"" 
                   style=""display: inline-block; background: #3b5d50; color: white; padding: 12px 30px; 
                          text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 14px;"">
                    Download Invoice
                </a>
            </div>

            <div class=""contact-info"">
                <p><strong>Need Help?</strong></p>
                <p>If you have any questions about your order, our customer service team is here to help.</p>
                <p>📧 <a href=""mailto:support@HikingStore.com"">support@HikingStore.com</a> | 📞 <a href=""tel:+38345804555"">+383 (458) 04-555</a></p>
                <p style=""margin-top: 12px; font-size: 12px; color: #868e96;"">We typically respond within 24 hours</p>
            </div>

            <p style=""text-align: center; margin-top: 24px; color: #6c757d; font-size: 13px;"">
                Thank you for choosing <strong style=""color: #3b5d50;"">HikingStore</strong>! We appreciate your business.
            </p>
        </div>
        
        <div class=""footer"">
            <p class=""company-name"">HikingStore</p>
            <p>Rr Prshtina Re, Kosovo</p>
            <p>📧 support@HikingStore.com | 📞 +383 (458) 04-555</p>
            <p style=""margin-top: 16px; opacity: 0.7;"">
                <a href=""#"">Visit our website</a> | <a href=""#"">Privacy Policy</a> | <a href=""#"">Terms of Service</a>
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

        [Authorize]
        public async Task<IActionResult> OrderConfirmation(int id)
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

            return View(order);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCartItem(int cartId, int quantity)
        {
            var userId = _userManager.GetUserId(User);
            var cartItem = await _context.ShoppingCarts
                .Include(c => c.Product)
                .FirstOrDefaultAsync(c => c.CartId == cartId && c.UserId == userId);

            if (cartItem == null)
            {
                return Json(new { success = false, message = "Cart item not found." });
            }

            // Check stock if increasing quantity
            if (quantity > cartItem.Qty)
            {
                var availableStock = cartItem.Product.StockQuantity;
                if (quantity > availableStock)
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Only {availableStock} items available in stock."
                    });
                }
            }

            if (quantity < 1)
            {
                _context.ShoppingCarts.Remove(cartItem);
            }
            else
            {
                cartItem.Qty = quantity;
                _context.ShoppingCarts.Update(cartItem);
            }

            await _context.SaveChangesAsync();

            // Get updated cart count and total
            var cartItemCount = await _context.ShoppingCarts
                .Where(c => c.UserId == userId)
                .SumAsync(c => c.Qty);

            var totalAmount = await _context.ShoppingCarts
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .SumAsync(c => c.Product.Price * c.Qty);

            return Json(new
            {
                success = true,
                cartItemCount = cartItemCount,
                itemTotal = cartItem.Product.Price * quantity,
                totalAmount = totalAmount
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCartCount()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Json(new { count = 0 });
            }

            var count = await _context.ShoppingCarts
                .Where(c => c.UserId == userId)
                .SumAsync(c => (int?)c.Qty) ?? 0;

            return Json(new { count = count });
        }

        public class CartItemUpdate
        {
            public int CartId { get; set; }
            public int Quantity { get; set; }
        }
    }
}
