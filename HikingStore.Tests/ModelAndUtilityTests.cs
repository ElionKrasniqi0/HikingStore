using System.Text;
using HikingStore.Models;
using HikingStore.Services;

namespace HikingStore.Tests;

public class ProductTests
{
    [Fact]
    public void Sale_flags_and_discount_percent_come_from_compare_at_price()
    {
        Assert.False(new Product { Price = 80m }.IsOnSale);
        Assert.False(new Product { Price = 80m, CompareAtPrice = 80m }.IsOnSale);

        var sale = new Product { Price = 75m, CompareAtPrice = 100m };
        Assert.True(sale.IsOnSale);
        Assert.Equal(25, sale.DiscountPercent);
    }

    [Fact]
    public void Option_lists_are_trimmed_and_deduplicated()
    {
        var product = new Product { Sizes = " S, M ,M,, L ", Colors = null };
        Assert.Equal(new[] { "S", "M", "L" }, product.SizeOptions);
        Assert.Empty(product.ColorOptions);
    }

    [Fact]
    public void AllImagePaths_puts_main_image_first_without_duplicates()
    {
        var product = new Product { ProImage = "a.jpg", AdditionalImages = "b.jpg, a.jpg, c.jpg" };
        Assert.Equal(new[] { "a.jpg", "b.jpg", "c.jpg" }, product.AllImagePaths);
    }

    [Fact]
    public void SpecificationList_parses_key_value_lines()
    {
        var product = new Product { Specifications = "Weight: 450 g\nWaterproof:  Yes \nNotes only" };
        var specs = product.SpecificationList;

        Assert.Equal(3, specs.Count);
        Assert.Equal(new KeyValuePair<string, string>("Weight", "450 g"), specs[0]);
        Assert.Equal(new KeyValuePair<string, string>("Waterproof", "Yes"), specs[1]);
        Assert.Equal(new KeyValuePair<string, string>("Notes only", ""), specs[2]);
    }
}

public class ImageUploadServiceTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, ".png")]
    public void Detects_jpeg_and_png_by_signature(byte[] header, string expected)
    {
        Assert.Equal(expected, ImageUploadService.DetectImageExtension(header));
    }

    [Fact]
    public void Detects_gif_and_webp_by_signature()
    {
        Assert.Equal(".gif", ImageUploadService.DetectImageExtension(Encoding.ASCII.GetBytes("GIF89a....")));
        Assert.Equal(".webp", ImageUploadService.DetectImageExtension(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")));
    }

    [Theory]
    [InlineData("<svg xmlns=")]
    [InlineData("<html><script>")]
    [InlineData("MZ\u0090\0")]
    [InlineData("")]
    public void Rejects_non_image_content_regardless_of_file_name(string content)
    {
        Assert.Null(ImageUploadService.DetectImageExtension(Encoding.Latin1.GetBytes(content)));
    }
}

public class StoreSettingsTests
{
    private static readonly StoreSettings Settings = new() { FreeShippingThreshold = 100, CurrencySymbol = "€" };

    [Theory]
    [InlineData(99.99, 4.90)]
    [InlineData(100, 0)]
    public void Free_shipping_applies_from_threshold_for_eligible_methods(decimal subtotal, decimal expected)
    {
        var standard = new ShippingMethodOption { Cost = 4.90m, FreeOverThreshold = true };
        Assert.Equal(expected, Settings.ShippingCostFor(standard, subtotal));
    }

    [Fact]
    public void Express_is_never_free()
    {
        var express = new ShippingMethodOption { Cost = 9.90m, FreeOverThreshold = false };
        Assert.Equal(9.90m, Settings.ShippingCostFor(express, 500m));
    }

    [Fact]
    public void FormatPrice_uses_symbol_and_two_decimals()
    {
        Assert.Equal("€1,234.50", Settings.FormatPrice(1234.5m));
    }
}
