using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace HikingStore.Controllers
{
    [Authorize]
    public class CartsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public CartsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

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
                IsEmpty = !cartItems.Any() // Add a flag to indicate if the cart is empty
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AddToCart(int ProId, int qty)
        {
            var userId = _userManager.GetUserId(User);
            var cartItem = await _context.ShoppingCarts
                .FirstOrDefaultAsync(c => c.UserId == userId && c.ProId == ProId);

            if (cartItem == null)
            {
                cartItem = new ShoppingCart
                {
                    UserId = userId,
                    ProId = ProId,
                    Qty = qty
                };
                _context.ShoppingCarts.Add(cartItem);
            }
            else
            {
                cartItem.Qty += qty;
            }

            await _context.SaveChangesAsync();

            // Get the updated cart item count
            var cartItemCount = await _context.ShoppingCarts
                .Where(c => c.UserId == userId)
                .SumAsync(c => c.Qty);

            return Json(new { success = true, cartItemCount = cartItemCount });
        }
        [HttpPost]
        public async Task<IActionResult> RemoveFromCart(int CartId)
        {
            var cartItem = await _context.ShoppingCarts.FindAsync(CartId);
            if (cartItem != null)
            {
                _context.ShoppingCarts.Remove(cartItem);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

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
        public async Task<IActionResult> Checkout(Order order)
        {
            var userId = _userManager.GetUserId(User);
            var cartItems = await _context.ShoppingCarts
                .Include(c => c.Product)
                .Where(c => c.UserId == userId)
                .ToListAsync();

            if (!cartItems.Any())
            {
                ModelState.AddModelError("", "Your cart is empty.");
                return View(order);
            }

            order.UserId = userId;
            order.OrderDate = DateTime.Now;
            order.TotalAmount = cartItems.Sum(c => c.Product.Price * c.Qty);

            // Set default values for the new fields
            order.Status = OrderStatus.Received;
            order.TrackingNumber = "N/A";
            order.AdminNotes = "Order created";
            order.VerifiedDate = null;
            order.ProcessingDate = null;
            order.ShippedDate = null;
            order.CompletedDate = null;
            order.CancelledDate = null;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            foreach (var cartItem in cartItems)
            {
                var orderDetail = new OrderDetail
                {
                    OrderId = order.OrderId,
                    ProId = cartItem.ProId,
                    Quantity = cartItem.Qty,
                    Price = cartItem.Product.Price
                };
                _context.OrderDetails.Add(orderDetail);
            }

            _context.ShoppingCarts.RemoveRange(cartItems);
            await _context.SaveChangesAsync();

            // Send order confirmation email
            try
            {
                var user = await _userManager.GetUserAsync(User);
                await SendOrderConfirmationEmail(user.Email, order, cartItems);
            }
            catch (Exception ex)
            {
                // Log the error but don't prevent the order from being completed
                Console.WriteLine($"Failed to send confirmation email: {ex.Message}");
            }

            return RedirectToAction("OrderConfirmation", new { id = order.OrderId });
        }

        private async Task SendOrderConfirmationEmail(string userEmail, Order order, List<ShoppingCart> cartItems)
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

                // Build email subject
                var subject = $"Order Confirmation - #ORD{order.OrderId:D6}";

                // Build email body
                var emailBody = BuildOrderConfirmationEmail(order, cartItems);

                var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, senderName),
                    Subject = subject,
                    Body = emailBody,
                    IsBodyHtml = true // Changed to true for better formatting
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

        private string BuildOrderConfirmationEmail(Order order, List<ShoppingCart> cartItems)
        {
            var sb = new StringBuilder();

            sb.AppendLine($@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: #3b5d50; color: white; padding: 20px; text-align: center; border-radius: 5px 5px 0 0; }}
        .content {{ background: #f9f9f9; padding: 20px; }}
        .order-details {{ background: white; padding: 15px; margin: 15px 0; border-radius: 5px; border-left: 4px solid #3b5d50; }}
        .product-table {{ width: 100%; border-collapse: collapse; margin: 15px 0; }}
        .product-table th, .product-table td {{ padding: 12px; text-align: left; border-bottom: 1px solid #ddd; }}
        .product-table th {{ background: #f8f9fa; }}
        .total-row {{ font-weight: bold; background: #f8f9fa; }}
        .footer {{ text-align: center; margin-top: 20px; padding: 20px; background: #f1f1f1; border-radius: 0 0 5px 5px; }}
        .thank-you {{ color: #3b5d50; font-size: 18px; margin-bottom: 10px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>HikingStore</h1>
            <h2>Order Confirmation</h2>
        </div>
        
        <div class='content'>
            <div class='thank-you'>Thank you for your order!</div>
            <p>Dear {order.User?.UserName ?? "Customer"},</p>
            <p>We're pleased to confirm that we've received your order and it's being processed. Here are your order details:</p>
            
            <div class='order-details'>
                <h3>Order Information</h3>
                <p><strong>Order Number:</strong> #ORD{order.OrderId:D6}</p>
                <p><strong>Order Date:</strong> {order.OrderDate.ToString("MMMM dd, yyyy 'at' hh:mm tt")}</p>
                <p><strong>Total Amount:</strong> €{order.TotalAmount:N2}</p>
            </div>

            <div class='order-details'>
                <h3>Shipping Information</h3>
                <p><strong>Address:</strong> {order.ShippingAddress}</p>
                <p><strong>City:</strong> {order.City}</p>
                <p><strong>Phone:</strong> {order.PhoneNumber}</p>
                {(!string.IsNullOrEmpty(order.Comment) ? $"<p><strong>Order Notes:</strong> {order.Comment}</p>" : "")}
            </div>

            <h3>Order Items</h3>
            <table class='product-table'>
                <thead>
                    <tr>
                        <th>Product</th>
                        <th>Quantity</th>
                        <th>Price</th>
                        <th>Total</th>
                    </tr>
                </thead>
                <tbody>");

            foreach (var item in cartItems)
            {
                sb.AppendLine($@"
                    <tr>
                        <td>{item.Product.ProName}</td>
                        <td>{item.Qty}</td>
                        <td>€{item.Product.Price:N2}</td>
                        <td>€{(item.Product.Price * item.Qty):N2}</td>
                    </tr>");
            }

            sb.AppendLine($@"
                    <tr class='total-row'>
                        <td colspan='3' style='text-align: right;'><strong>Grand Total:</strong></td>
                        <td><strong>€{order.TotalAmount:N2}</strong></td>
                    </tr>
                </tbody>
            </table>

            <div class='order-details'>
                <h3>What's Next?</h3>
                <p>• You'll receive another email when your order ships</p>
                <p>• Expected delivery: 3-5 business days</p>
                <p>• Track your order by visiting your account dashboard</p>
            </div>

            <p>If you have any questions about your order, please contact our customer service team at support@HikingStore.com or call us at +38345804555.</p>
            
            <p>Thank you for choosing HikingStore!</p>
        </div>
        
        <div class='footer'>
            <p><strong>HikingStore</strong></p>
            <p>Rr Prshtina Re</p>
            <p>Email: support@HikingStore.com | Phone:+383 (458) 04-555</p>
            <p><a href='https://yourwebsite.com' style='color: #3b5d50;'>Visit our website</a></p>
        </div>
    </div>
</body>
</html>");

            return sb.ToString();
        }
        public IActionResult OrderConfirmation(int id)
        {
            var order = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefault(o => o.OrderId == id);

            if (order == null)
            {
                return NotFound();
            }

            // Add a message to indicate email was sent
            TempData["EmailSent"] = "Order confirmation email has been sent to your email address.";

            return View(order);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateCartQuantities(Dictionary<int, CartItemUpdate> cartItems)
        {
            try
            {
                var userId = _userManager.GetUserId(User);

                foreach (var item in cartItems.Values)
                {
                    var cartItem = await _context.ShoppingCarts
                        .FirstOrDefaultAsync(c => c.CartId == item.CartId && c.UserId == userId);

                    if (cartItem != null)
                    {
                        if (item.Quantity < 1)
                        {
                            // Remove item if quantity is 0 or less
                            _context.ShoppingCarts.Remove(cartItem);
                        }
                        else
                        {
                            // Update quantity
                            cartItem.Qty = item.Quantity;
                            _context.ShoppingCarts.Update(cartItem);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Handle error - you might want to show an error message
                return RedirectToAction(nameof(Index));
            }
        }

        public class CartItemUpdate
        {
            public int CartId { get; set; }
            public int Quantity { get; set; }
        }
    }
}
