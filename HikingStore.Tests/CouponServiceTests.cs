using HikingStore.Enum;
using HikingStore.Models;
using HikingStore.Services;

namespace HikingStore.Tests;

public class CouponServiceTests
{
    private static Coupon Percent(decimal value, decimal? max = null) =>
        new() { Code = "SAVE", DiscountType = DiscountType.Percentage, Value = value, MaxDiscountAmount = max };

    private static Coupon Fixed(decimal value) =>
        new() { Code = "FLAT", DiscountType = DiscountType.FixedAmount, Value = value };

    [Theory]
    [InlineData(10, 200, 20)]
    [InlineData(15, 33.33, 5.00)]
    [InlineData(150, 80, 80)]
    public void CalculateDiscount_percentage_is_rounded_and_capped_at_100_percent(decimal percent, decimal subtotal, decimal expected)
    {
        Assert.Equal(expected, CouponService.CalculateDiscount(Percent(percent), subtotal));
    }

    [Fact]
    public void CalculateDiscount_respects_max_discount_amount()
    {
        Assert.Equal(25m, CouponService.CalculateDiscount(Percent(50, max: 25), 200m));
    }

    [Fact]
    public void CalculateDiscount_fixed_amount_never_exceeds_subtotal()
    {
        Assert.Equal(10m, CouponService.CalculateDiscount(Fixed(10), 50m));
        Assert.Equal(30m, CouponService.CalculateDiscount(Fixed(40), 30m));
    }

    [Fact]
    public void CalculateDiscount_is_zero_for_empty_cart()
    {
        Assert.Equal(0m, CouponService.CalculateDiscount(Fixed(10), 0m));
    }

    [Theory]
    [InlineData("  summer10 ", "SUMMER10")]
    [InlineData(null, "")]
    public void Normalize_trims_and_uppercases(string? input, string expected)
    {
        Assert.Equal(expected, CouponService.Normalize(input));
    }

    [Fact]
    public async Task ValidateAsync_accepts_valid_code_case_insensitively()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        db.Context.Coupons.Add(new Coupon { Code = "TRAIL10", DiscountType = DiscountType.Percentage, Value = 10 });
        await db.Context.SaveChangesAsync();

        var result = await new CouponService(db.Context).ValidateAsync(" trail10 ", user.Id, 120m);

        Assert.True(result.IsValid);
        Assert.Equal(12m, result.Discount);
    }

    [Fact]
    public async Task ValidateAsync_rejects_empty_unknown_and_inactive_codes()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        db.Context.Coupons.Add(new Coupon { Code = "OFF", DiscountType = DiscountType.FixedAmount, Value = 5, IsActive = false });
        await db.Context.SaveChangesAsync();
        var service = new CouponService(db.Context);

        Assert.False((await service.ValidateAsync("", user.Id, 50m)).IsValid);
        Assert.False((await service.ValidateAsync("NOPE", user.Id, 50m)).IsValid);
        Assert.False((await service.ValidateAsync("OFF", user.Id, 50m)).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_enforces_dates_minimum_and_usage_limit()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        db.Context.Coupons.AddRange(
            new Coupon { Code = "FUTURE", DiscountType = DiscountType.FixedAmount, Value = 5, StartsAt = DateTime.UtcNow.AddDays(1) },
            new Coupon { Code = "OLD", DiscountType = DiscountType.FixedAmount, Value = 5, ExpiresAt = DateTime.UtcNow.AddDays(-1) },
            new Coupon { Code = "MIN50", DiscountType = DiscountType.FixedAmount, Value = 5, MinimumOrderAmount = 50 },
            new Coupon { Code = "USEDUP", DiscountType = DiscountType.FixedAmount, Value = 5, UsageLimit = 3, TimesUsed = 3 });
        await db.Context.SaveChangesAsync();
        var service = new CouponService(db.Context);

        Assert.Equal("This coupon is not active yet.", (await service.ValidateAsync("FUTURE", user.Id, 100m)).Error);
        Assert.Equal("This coupon has expired.", (await service.ValidateAsync("OLD", user.Id, 100m)).Error);
        Assert.False((await service.ValidateAsync("MIN50", user.Id, 49.99m)).IsValid);
        Assert.True((await service.ValidateAsync("MIN50", user.Id, 50m)).IsValid);
        Assert.Equal("This coupon has reached its usage limit.", (await service.ValidateAsync("USEDUP", user.Id, 100m)).Error);
    }

    [Fact]
    public async Task ValidateAsync_per_user_limit_ignores_cancelled_orders()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        db.Context.Coupons.Add(new Coupon { Code = "ONCE", DiscountType = DiscountType.FixedAmount, Value = 5, UsageLimitPerUser = 1 });
        var order = new Order
        {
            UserId = user.Id, OrderDate = DateTime.UtcNow, ShippingAddress = "Street 1", City = "Prishtinë",
            PhoneNumber = "+38345000000", TotalAmount = 45, CouponCode = "ONCE", Status = OrderStatus.Cancelled
        };
        db.Context.Orders.Add(order);
        await db.Context.SaveChangesAsync();
        var service = new CouponService(db.Context);

        Assert.True((await service.ValidateAsync("ONCE", user.Id, 50m)).IsValid);

        order.Status = OrderStatus.Received;
        await db.Context.SaveChangesAsync();

        Assert.Equal("You have already used this coupon.", (await service.ValidateAsync("ONCE", user.Id, 50m)).Error);
    }
}
