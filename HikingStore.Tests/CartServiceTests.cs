using HikingStore.Models;
using HikingStore.Services;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Tests;

public class CartServiceTests
{
    [Fact]
    public void ValidateVariant_requires_and_normalizes_listed_options()
    {
        var product = new Product { Sizes = "S, M, L", Colors = "Forest, Sand" };

        string? size = null, color = "forest";
        Assert.Equal("Please choose an available size.", CartService.ValidateVariant(product, ref size, ref color));

        size = " m "; color = "forest";
        Assert.Null(CartService.ValidateVariant(product, ref size, ref color));
        Assert.Equal("M", size);
        Assert.Equal("Forest", color);

        size = "M"; color = "Pink";
        Assert.Equal("Please choose an available color.", CartService.ValidateVariant(product, ref size, ref color));
    }

    [Fact]
    public void ValidateVariant_clears_options_the_product_does_not_have()
    {
        var product = new Product();
        string? size = "XL", color = "Red";

        Assert.Null(CartService.ValidateVariant(product, ref size, ref color));
        Assert.Null(size);
        Assert.Null(color);
    }

    [Fact]
    public async Task Guest_add_is_limited_by_stock_across_lines()
    {
        using var db = new TestDb();
        var product = db.AddProduct(stock: 3, sizes: "S,M");
        var cart = db.CreateCartService();

        Assert.True((await cart.AddAsync(null, product.ProId, 2, "S", null)).Success);
        var tooMany = await cart.AddAsync(null, product.ProId, 2, "M", null);

        Assert.False(tooMany.Success);
        Assert.Equal(2, await cart.GetItemCountAsync(null));
    }

    [Fact]
    public async Task Add_rejects_inactive_products_and_invalid_quantities()
    {
        using var db = new TestDb();
        var hidden = db.AddProduct(active: false);
        var visible = db.AddProduct(name: "Boots");
        var cart = db.CreateCartService();

        Assert.False((await cart.AddAsync(null, hidden.ProId, 1, null, null)).Success);
        Assert.False((await cart.AddAsync(null, visible.ProId, 0, null, null)).Success);
        Assert.False((await cart.AddAsync(null, visible.ProId, CartService.MaxQuantityPerLine + 1, null, null)).Success);
    }

    [Fact]
    public async Task Signed_in_add_merges_same_variant_into_one_line()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(stock: 10, sizes: "M");
        var cart = db.CreateCartService();

        await cart.AddAsync(user.Id, product.ProId, 1, "M", null);
        await cart.AddAsync(user.Id, product.ProId, 2, "m", null);

        var lines = await db.Context.ShoppingCarts.AsNoTracking().Where(c => c.UserId == user.Id).ToListAsync();
        var line = Assert.Single(lines);
        Assert.Equal(3, line.Qty);
        Assert.Equal("M", line.SelectedSize);
    }

    [Fact]
    public async Task MergeGuestCart_moves_items_to_account_and_clears_session()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(stock: 5);
        var cart = db.CreateCartService();

        await cart.AddAsync(null, product.ProId, 2, null, null);
        await cart.AddAsync(user.Id, product.ProId, 1, null, null);
        await cart.MergeGuestCartAsync(user.Id);

        Assert.Equal(0, await cart.GetItemCountAsync(null));
        Assert.Equal(3, await cart.GetItemCountAsync(user.Id));
    }

    [Fact]
    public async Task Users_cannot_change_or_remove_other_users_lines()
    {
        using var db = new TestDb();
        var owner = db.AddUser("owner@example.com");
        var other = db.AddUser("other@example.com");
        var product = db.AddProduct();
        var cart = db.CreateCartService();
        await cart.AddAsync(owner.Id, product.ProId, 1, null, null);
        var cartId = db.Context.ShoppingCarts.Single().CartId;

        Assert.False((await cart.UpdateQuantityAsync(other.Id, $"c{cartId}", 2)).Success);
        Assert.False((await cart.RemoveAsync(other.Id, $"c{cartId}")).Success);
        Assert.Equal(1, await cart.GetItemCountAsync(owner.Id));
    }

    [Fact]
    public async Task UpdateQuantity_cannot_exceed_stock()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(stock: 4);
        var cart = db.CreateCartService();
        await cart.AddAsync(user.Id, product.ProId, 1, null, null);
        var key = $"c{db.Context.ShoppingCarts.Single().CartId}";

        Assert.False((await cart.UpdateQuantityAsync(user.Id, key, 5)).Success);
        Assert.True((await cart.UpdateQuantityAsync(user.Id, key, 4)).Success);
    }

    [Fact]
    public async Task Summary_uses_database_prices_and_free_shipping_threshold()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(price: 45m, stock: 10);
        var cart = db.CreateCartService();
        await cart.AddAsync(user.Id, product.ProId, 2, null, null);

        var below = await cart.GetSummaryAsync(user.Id, "standard");
        Assert.Equal(90m, below.Subtotal);
        Assert.Equal(4.90m, below.ShippingCost);
        Assert.Equal(94.90m, below.Total);
        Assert.Equal(10m, below.AmountUntilFreeShipping);

        await cart.AddAsync(user.Id, product.ProId, 1, null, null);
        var above = await cart.GetSummaryAsync(user.Id, "standard");
        Assert.Equal(0m, above.ShippingCost);
        Assert.Equal(135m, above.Total);

        var express = await cart.GetSummaryAsync(user.Id, "express");
        Assert.Equal(9.90m, express.ShippingCost);
    }

    [Fact]
    public async Task Summary_applies_coupon_for_signed_in_user_only()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(price: 100m);
        db.Context.Coupons.Add(new Coupon { Code = "TEN", DiscountType = DiscountType.Percentage, Value = 10 });
        await db.Context.SaveChangesAsync();
        var cart = db.CreateCartService();

        await cart.AddAsync(null, product.ProId, 1, null, null);
        cart.SetCoupon("ten");
        var guest = await cart.GetSummaryAsync(null);
        Assert.Equal(0m, guest.Discount);
        Assert.NotNull(guest.CouponError);

        await cart.MergeGuestCartAsync(user.Id);
        var signedIn = await cart.GetSummaryAsync(user.Id, "standard");
        Assert.Equal(10m, signedIn.Discount);
        Assert.Null(signedIn.CouponError);
        // Free shipping is judged on the discounted subtotal (90 < 100).
        Assert.Equal(4.90m, signedIn.ShippingCost);
        Assert.Equal(94.90m, signedIn.Total);
    }

    [Fact]
    public async Task Summary_flags_lines_that_became_unavailable()
    {
        using var db = new TestDb();
        var user = db.AddUser();
        var product = db.AddProduct(price: 20m, stock: 5);
        var cart = db.CreateCartService();
        await cart.AddAsync(user.Id, product.ProId, 3, null, null);

        product.StockQuantity = 2;
        await db.Context.SaveChangesAsync();
        var lowStock = await cart.GetSummaryAsync(user.Id);
        Assert.True(lowStock.HasIssues);

        product.IsActive = false;
        await db.Context.SaveChangesAsync();
        var inactive = await cart.GetSummaryAsync(user.Id);
        Assert.False(inactive.Lines.Single().IsAvailable);
        Assert.Equal(0m, inactive.Subtotal);
    }
}
