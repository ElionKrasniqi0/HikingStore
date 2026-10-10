using System.Net;
using System.Text;
using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Services
{
    /// <summary>
    /// Sends order emails in the background. Each send runs in its own DI scope so it never touches the
    /// request's DbContext after the request has finished.
    /// </summary>
    public class OrderEmailService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderEmailService> _logger;

        public OrderEmailService(IServiceScopeFactory scopeFactory, ILogger<OrderEmailService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public void QueueOrderConfirmation(int orderId, string baseUrl) =>
            Queue(orderId, baseUrl, (order, settings, url) => (
                $"Order confirmation – #{order.OrderNumber}",
                BuildBody(order, settings, url,
                    "Thank you for your order!",
                    $"We've received order #{order.OrderNumber} and will confirm it shortly. " +
                    (settings.ShippingMethods.Any(m => m.Code == "pickup" && m.Name == order.ShippingService)
                        ? $"You'll pay {settings.FormatPrice(order.TotalAmount)} when you collect it at the store."
                        : $"You'll pay {settings.FormatPrice(order.TotalAmount)} on delivery."))));

        public void QueueStatusUpdate(int orderId, string baseUrl, bool notifyAdmin) =>
            Queue(orderId, baseUrl, (order, settings, url) => (
                $"Your order #{order.OrderNumber} is now {StatusLabel(order.Status)}",
                BuildBody(order, settings, url,
                    $"Order update: {StatusLabel(order.Status)}",
                    StatusMessage(order))), notifyAdmin);

        private void Queue(int orderId, string baseUrl, Func<Order, StoreSettings, string, (string Subject, string Body)> build, bool notifyAdmin = false)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                    var settings = scope.ServiceProvider.GetRequiredService<IOptions<StoreSettings>>().Value;
                    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                    var order = await context.Orders.AsNoTracking()
                        .Include(o => o.User)
                        .Include(o => o.OrderDetails)
                        .FirstOrDefaultAsync(o => o.OrderId == orderId);
                    if (order == null) return;

                    var recipient = order.Email ?? order.User?.Email;
                    var (subject, body) = build(order, settings, baseUrl);
                    if (!string.IsNullOrEmpty(recipient))
                    {
                        await sender.SendEmailAsync(recipient, subject, body);
                    }

                    if (notifyAdmin)
                    {
                        var adminEmail = configuration["EmailSettings:AdminEmail"];
                        if (string.IsNullOrWhiteSpace(adminEmail)) adminEmail = configuration["EmailSettings:SenderEmail"];
                        if (!string.IsNullOrWhiteSpace(adminEmail))
                        {
                            await sender.SendEmailAsync(adminEmail, $"[Admin] Order #{order.OrderNumber} → {StatusLabel(order.Status)}", body);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send email for order {OrderId}", orderId);
                }
            });
        }

        public static string StatusLabel(OrderStatus status) => status switch
        {
            OrderStatus.Received => "Received",
            OrderStatus.Verified => "Confirmed",
            OrderStatus.Processing => "Being prepared",
            OrderStatus.Shipped => "Shipped",
            OrderStatus.Completed => "Delivered",
            OrderStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };

        private static string StatusMessage(Order order) => order.Status switch
        {
            OrderStatus.Verified => "Your order has been confirmed and will be prepared for shipping soon.",
            OrderStatus.Processing => "We're packing your gear." + (order.EstimatedDelivery.HasValue ? $" Estimated delivery: {order.EstimatedDelivery:dd MMM yyyy}." : string.Empty),
            OrderStatus.Shipped => $"Your order is on its way with {order.Carrier}." + (string.IsNullOrEmpty(order.TrackingNumber) ? string.Empty : $" Tracking number: {order.TrackingNumber}."),
            OrderStatus.Completed => "Your order has been delivered. Enjoy the trail — and let us know what you think by leaving a review.",
            OrderStatus.Cancelled => "Your order has been cancelled. If you didn't request this, please contact us.",
            _ => "Your order status has been updated."
        };

        private static string BuildBody(Order order, StoreSettings settings, string baseUrl, string heading, string intro)
        {
            static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

            var rows = new StringBuilder();
            foreach (var line in order.OrderDetails)
            {
                var variant = string.Join(" / ", new[] { line.SelectedSize, line.SelectedColor }.Where(v => !string.IsNullOrEmpty(v)));
                rows.Append($@"<tr>
<td style=""padding:8px 0;border-bottom:1px solid #DED2BC"">{E(line.DisplayName)}{(variant.Length > 0 ? $"<br><span style=\"color:#73786F;font-size:13px\">{E(variant)}</span>" : "")}</td>
<td style=""padding:8px 0;border-bottom:1px solid #DED2BC;text-align:center"">{line.Quantity}</td>
<td style=""padding:8px 0;border-bottom:1px solid #DED2BC;text-align:right"">{settings.FormatPrice(line.LineTotal)}</td></tr>");
            }

            var discountRow = order.DiscountAmount > 0
                ? $@"<tr><td colspan=""2"">Discount ({E(order.CouponCode)})</td><td style=""text-align:right"">−{settings.FormatPrice(order.DiscountAmount)}</td></tr>"
                : string.Empty;

            return $@"<!DOCTYPE html><html><body style=""margin:0;background:#F3EFE5;font-family:Segoe UI,Arial,sans-serif;color:#252A25"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0""><tr><td align=""center"" style=""padding:24px"">
<table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;background:#fff;border-radius:8px;overflow:hidden"">
<tr><td style=""background:#163526;color:#fff;padding:24px;font-size:22px;font-weight:700"">{E(settings.Name)}</td></tr>
<tr><td style=""padding:24px"">
<h1 style=""font-size:20px;margin:0 0 12px"">{E(heading)}</h1>
<p style=""margin:0 0 16px;line-height:1.5"">Hi {E(order.FullName ?? order.User?.DisplayName)},<br>{E(intro)}</p>
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""font-size:14px"">
<tr><th align=""left"">Item</th><th>Qty</th><th align=""right"">Total</th></tr>
{rows}
<tr><td colspan=""2"" style=""padding-top:8px"">Subtotal</td><td style=""text-align:right;padding-top:8px"">{settings.FormatPrice(order.Subtotal)}</td></tr>
{discountRow}
<tr><td colspan=""2"">Shipping ({E(order.ShippingService)})</td><td style=""text-align:right"">{(order.ShippingCost == 0 ? "Free" : settings.FormatPrice(order.ShippingCost))}</td></tr>
<tr><td colspan=""2"" style=""font-weight:700;padding-top:8px"">Total</td><td style=""text-align:right;font-weight:700;padding-top:8px"">{settings.FormatPrice(order.TotalAmount)}</td></tr>
</table>
<p style=""margin:20px 0 4px;font-weight:600"">Shipping to</p>
<p style=""margin:0;line-height:1.5"">{E(order.FullName)}<br>{E(order.ShippingAddress)}<br>{E(order.PostalCode)} {E(order.City)}, {E(order.Country)}<br>{E(order.PhoneNumber)}</p>
<p style=""margin:24px 0""><a href=""{baseUrl}/Orders/Details/{order.OrderId}"" style=""background:#234B35;color:#fff;padding:12px 20px;border-radius:6px;text-decoration:none;font-weight:600"">View your order</a></p>
<p style=""color:#73786F;font-size:13px;margin:0"">Questions? Reply to this email or contact {E(settings.SupportEmail)}.</p>
</td></tr></table></td></tr></table></body></html>";
        }
    }
}
