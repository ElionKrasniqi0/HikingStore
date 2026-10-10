using HikingStore.Data;
using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Services
{
    public record CouponValidationResult(bool IsValid, Coupon? Coupon, decimal Discount, string? Error)
    {
        public static CouponValidationResult Invalid(string error) => new(false, null, 0, error);
    }

    public class CouponService
    {
        private readonly ApplicationDbContext _context;

        public CouponService(ApplicationDbContext context)
        {
            _context = context;
        }

        public static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>Validates a coupon for a user and merchandise subtotal and returns the discount it grants.</summary>
        public async Task<CouponValidationResult> ValidateAsync(string? code, string userId, decimal subtotal)
        {
            var normalized = Normalize(code);
            if (normalized.Length == 0)
            {
                return CouponValidationResult.Invalid("Please enter a coupon code.");
            }

            var coupon = await _context.Coupons.AsNoTracking().FirstOrDefaultAsync(c => c.Code == normalized);
            if (coupon == null || !coupon.IsActive)
            {
                return CouponValidationResult.Invalid("This coupon code is not valid.");
            }

            var now = DateTime.UtcNow;
            if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
            {
                return CouponValidationResult.Invalid("This coupon is not active yet.");
            }
            if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < now)
            {
                return CouponValidationResult.Invalid("This coupon has expired.");
            }
            if (coupon.UsageLimit.HasValue && coupon.TimesUsed >= coupon.UsageLimit.Value)
            {
                return CouponValidationResult.Invalid("This coupon has reached its usage limit.");
            }
            if (coupon.MinimumOrderAmount.HasValue && subtotal < coupon.MinimumOrderAmount.Value)
            {
                return CouponValidationResult.Invalid($"This coupon requires a minimum order of €{coupon.MinimumOrderAmount.Value:N2}.");
            }
            if (coupon.UsageLimitPerUser.HasValue)
            {
                var usedByUser = await _context.Orders.CountAsync(o =>
                    o.UserId == userId && o.CouponCode == normalized && o.Status != OrderStatus.Cancelled);
                if (usedByUser >= coupon.UsageLimitPerUser.Value)
                {
                    return CouponValidationResult.Invalid("You have already used this coupon.");
                }
            }

            var discount = CalculateDiscount(coupon, subtotal);
            if (discount <= 0)
            {
                return CouponValidationResult.Invalid("This coupon does not apply to your cart.");
            }

            return new CouponValidationResult(true, coupon, discount, null);
        }

        public static decimal CalculateDiscount(Coupon coupon, decimal subtotal)
        {
            if (subtotal <= 0) return 0;

            var discount = coupon.DiscountType == DiscountType.Percentage
                ? Math.Round(subtotal * Math.Min(coupon.Value, 100) / 100m, 2, MidpointRounding.AwayFromZero)
                : coupon.Value;

            if (coupon.MaxDiscountAmount.HasValue)
            {
                discount = Math.Min(discount, coupon.MaxDiscountAmount.Value);
            }

            return Math.Min(discount, subtotal);
        }
    }
}
