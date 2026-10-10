using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.Services.Payments;
using HikingStore.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Services
{
    public record PlaceOrderResult(bool Success, int OrderId, IReadOnlyList<string> Errors, bool AlreadyPlaced = false)
    {
        public static PlaceOrderResult Fail(params string[] errors) => new(false, 0, errors);
    }

    public class OrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly CartService _cartService;
        private readonly CouponService _couponService;
        private readonly StoreSettings _settings;
        private readonly PaymentProviderRegistry _payments;
        private readonly ILogger<OrderService> _logger;

        public OrderService(
            ApplicationDbContext context,
            CartService cartService,
            CouponService couponService,
            IOptions<StoreSettings> settings,
            PaymentProviderRegistry payments,
            ILogger<OrderService> logger)
        {
            _context = context;
            _cartService = cartService;
            _couponService = couponService;
            _settings = settings.Value;
            _payments = payments;
            _logger = logger;
        }

        /// <summary>
        /// Creates an order from the user's saved cart. Totals are recalculated from the database, stock is
        /// decremented atomically, and everything runs in one transaction so a failure leaves no partial order.
        /// Re-submitting the same checkout token returns the existing order instead of creating a duplicate.
        /// </summary>
        public async Task<PlaceOrderResult> PlaceOrderAsync(string userId, CheckoutInput input)
        {
            if (input.CheckoutToken == Guid.Empty)
            {
                return PlaceOrderResult.Fail("Your checkout session expired. Please review your order and try again.");
            }

            var existingOrderId = await _context.Orders
                .Where(o => o.CheckoutToken == input.CheckoutToken && o.UserId == userId)
                .Select(o => (int?)o.OrderId)
                .FirstOrDefaultAsync();
            if (existingOrderId.HasValue)
            {
                return new PlaceOrderResult(true, existingOrderId.Value, Array.Empty<string>(), AlreadyPlaced: true);
            }

            ICardPaymentProvider? cardProvider = null;
            if (input.PaymentMethod == PaymentMethods.Card)
            {
                cardProvider = _payments.ActiveProvider;
                if (cardProvider == null)
                {
                    return PlaceOrderResult.Fail("Card payment isn't available right now. Please choose another payment method.");
                }
            }
            else if (input.PaymentMethod != PaymentMethods.CashOnDelivery || !_payments.CashOnDeliveryEnabled)
            {
                return PlaceOrderResult.Fail("Please choose an available payment method.");
            }

            var shippingMethod = _settings.FindShippingMethod(input.ShippingMethod);
            if (shippingMethod == null)
            {
                return PlaceOrderResult.Fail("Please choose an available shipping method.");
            }

            var summary = await _cartService.GetSummaryAsync(userId, shippingMethod.Code);
            if (summary.IsEmpty)
            {
                return PlaceOrderResult.Fail("Your cart is empty.");
            }
            if (summary.HasIssues)
            {
                return PlaceOrderResult.Fail(summary.Lines.Where(l => l.Issue != null).Select(l => $"{l.Name}: {l.Issue}").ToArray());
            }
            if (summary.CouponError != null)
            {
                return PlaceOrderResult.Fail($"Coupon {summary.CouponCode}: {summary.CouponError} Remove it from your cart to continue.");
            }

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var transaction = await _context.Database.BeginTransactionAsync();

                foreach (var group in summary.Lines.GroupBy(l => l.ProId))
                {
                    var quantity = group.Sum(l => l.Quantity);
                    var updated = await _context.Products
                        .Where(p => p.ProId == group.Key && p.IsActive && p.StockQuantity >= quantity)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity));
                    if (updated == 0)
                    {
                        await transaction.RollbackAsync();
                        return PlaceOrderResult.Fail($"{group.First().Name} just sold out or has less stock than requested. Please update your cart.");
                    }
                }

                if (summary.Discount > 0 && summary.CouponCode != null)
                {
                    var couponUpdated = await _context.Coupons
                        .Where(c => c.Code == summary.CouponCode && c.IsActive && (c.UsageLimit == null || c.TimesUsed < c.UsageLimit))
                        .ExecuteUpdateAsync(s => s.SetProperty(c => c.TimesUsed, c => c.TimesUsed + 1));
                    if (couponUpdated == 0)
                    {
                        await transaction.RollbackAsync();
                        return PlaceOrderResult.Fail("This coupon has just reached its usage limit. Please remove it and try again.");
                    }
                }

                var now = DateTime.UtcNow;
                var order = new Order
                {
                    UserId = userId,
                    OrderDate = now,
                    FullName = input.FullName.Trim(),
                    Email = input.Email.Trim(),
                    PhoneNumber = input.PhoneNumber.Trim(),
                    ShippingAddress = input.AddressLine.Trim(),
                    City = input.City.Trim(),
                    PostalCode = input.PostalCode?.Trim(),
                    Country = input.Country.Trim(),
                    Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(),
                    Subtotal = summary.Subtotal,
                    DiscountAmount = summary.Discount,
                    ShippingCost = summary.ShippingCost,
                    TotalAmount = summary.Total,
                    CouponCode = summary.Discount > 0 ? summary.CouponCode : null,
                    PaymentMethod = cardProvider != null ? PaymentMethods.Card : PaymentMethods.CashOnDelivery,
                    PaymentStatus = PaymentStatuses.Pending,
                    PaymentProvider = cardProvider?.Name,
                    PaymentExpiresAt = cardProvider != null ? now.AddMinutes(_payments.Options.EffectiveTimeoutMinutes) : null,
                    CheckoutToken = input.CheckoutToken,
                    Status = OrderStatus.Received,
                    TrackingNumber = null,
                    Carrier = shippingMethod.Name,
                    ShippingService = shippingMethod.Name,
                    OrderDetails = summary.Lines.Select(l => new OrderDetail
                    {
                        ProId = l.ProId,
                        ProductName = l.Name,
                        SelectedSize = l.Size,
                        SelectedColor = l.Color,
                        Quantity = l.Quantity,
                        Price = l.UnitPrice
                    }).ToList(),
                    TrackingEvents = new List<TrackingEvent>
                    {
                        new()
                        {
                            EventDate = now,
                            Location = _settings.Name,
                            Status = OrderStatus.Received.ToString(),
                            Description = cardProvider != null ? "Order placed — awaiting card payment" : "Order placed",
                            IsMilestone = true
                        }
                    }
                };
                _context.Orders.Add(order);

                if (input.SaveAddress)
                {
                    var hasAddresses = await _context.Addresses.AnyAsync(a => a.UserId == userId);
                    var duplicate = await _context.Addresses.AnyAsync(a => a.UserId == userId
                        && a.AddressLine == order.ShippingAddress && a.City == order.City);
                    if (!duplicate)
                    {
                        _context.Addresses.Add(new Address
                        {
                            UserId = userId,
                            Label = "Home",
                            FullName = order.FullName!,
                            PhoneNumber = order.PhoneNumber,
                            AddressLine = order.ShippingAddress,
                            City = order.City,
                            PostalCode = order.PostalCode,
                            Country = order.Country ?? "Kosovo",
                            IsDefault = !hasAddresses
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await _context.ShoppingCarts.Where(c => c.UserId == userId).ExecuteDeleteAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Order {OrderId} placed by user {UserId}: subtotal {Subtotal}, discount {Discount}, shipping {Shipping}, total {Total}",
                    order.OrderId, userId, order.Subtotal, order.DiscountAmount, order.ShippingCost, order.TotalAmount);

                _cartService.RemoveCoupon();
                return new PlaceOrderResult(true, order.OrderId, Array.Empty<string>());
            });
        }

        /// <summary>Returns the order's items to stock and releases its coupon use. Call when an order becomes cancelled.</summary>
        public async Task RestockCancelledOrderAsync(int orderId)
        {
            var lines = await _context.OrderDetails.Where(d => d.OrderId == orderId)
                .GroupBy(d => d.ProId)
                .Select(g => new { ProId = g.Key, Quantity = g.Sum(d => d.Quantity) })
                .ToListAsync();

            foreach (var line in lines)
            {
                await _context.Products.Where(p => p.ProId == line.ProId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, p => p.StockQuantity + line.Quantity));
            }

            var couponCode = await _context.Orders.Where(o => o.OrderId == orderId).Select(o => o.CouponCode).FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(couponCode))
            {
                await _context.Coupons.Where(c => c.Code == couponCode && c.TimesUsed > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.TimesUsed, c => c.TimesUsed - 1));
            }
        }

        /// <summary>Cancels a customer's own order if it has not shipped yet.</summary>
        public async Task<(bool Success, string Message)> CancelByCustomerAsync(int orderId, string userId)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId);
                if (order == null)
                {
                    return (false, "Order not found.");
                }
                if (order.IsCardPayment)
                {
                    return (false, "Card orders are cancelled through the payment page so the payment can be stopped first.");
                }
                if (!order.CanBeCancelledByCustomer)
                {
                    return (false, order.Status == OrderStatus.Cancelled
                        ? "This order is already cancelled."
                        : "This order has already shipped and can no longer be cancelled.");
                }

                order.Status = OrderStatus.Cancelled;
                order.CancelledDate = DateTime.UtcNow;
                order.PaymentStatus = PaymentStatuses.Cancelled;
                _context.TrackingEvents.Add(new TrackingEvent
                {
                    OrderId = order.OrderId,
                    EventDate = DateTime.UtcNow,
                    Location = _settings.Name,
                    Status = OrderStatus.Cancelled.ToString(),
                    Description = "Order cancelled by customer",
                    IsMilestone = true
                });
                await _context.SaveChangesAsync();
                await RestockCancelledOrderAsync(order.OrderId);
                await transaction.CommitAsync();
                return (true, "Your order has been cancelled.");
            });
        }
    }
}
