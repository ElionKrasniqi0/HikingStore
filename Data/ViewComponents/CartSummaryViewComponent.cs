using HikingStore.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class CartSummaryViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public CartSummaryViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var userId = UserClaimsPrincipal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return View(0);
        }

        var cartItems = await _context.ShoppingCarts
            .Where(c => c.UserId == userId)
            .ToListAsync();

        var totalItems = cartItems.Sum(c => c.Qty);
        return View(totalItems);
    }
}
