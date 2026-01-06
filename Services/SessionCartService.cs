using HikingStore.Data;
using HikingStore.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HikingStore.Services
{
    public class SessionCartService
    {
        private const string SessionCartKey = "SessionCart";
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _context;

        public SessionCartService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext context)
        {
            _httpContextAccessor = httpContextAccessor;
            _context = context;
        }

        public List<SessionCartItem> GetSessionCart()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return new List<SessionCartItem>();

            var cartJson = session.GetString(SessionCartKey);
            if (string.IsNullOrEmpty(cartJson))
                return new List<SessionCartItem>();

            try
            {
                return JsonSerializer.Deserialize<List<SessionCartItem>>(cartJson) ?? new List<SessionCartItem>();
            }
            catch
            {
                return new List<SessionCartItem>();
            }
        }

        public void SaveSessionCart(List<SessionCartItem> cart)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return;

            var cartJson = JsonSerializer.Serialize(cart);
            session.SetString(SessionCartKey, cartJson);
        }

        public void AddToSessionCart(int proId, int qty)
        {
            var cart = GetSessionCart();
            var existingItem = cart.FirstOrDefault(c => c.ProId == proId);

            if (existingItem != null)
            {
                existingItem.Qty += qty;
            }
            else
            {
                cart.Add(new SessionCartItem { ProId = proId, Qty = qty });
            }

            SaveSessionCart(cart);
        }

        public void UpdateSessionCartItem(int proId, int qty)
        {
            var cart = GetSessionCart();
            var item = cart.FirstOrDefault(c => c.ProId == proId);

            if (item != null)
            {
                if (qty <= 0)
                {
                    cart.Remove(item);
                }
                else
                {
                    item.Qty = qty;
                }
                SaveSessionCart(cart);
            }
        }

        public void RemoveFromSessionCart(int proId)
        {
            var cart = GetSessionCart();
            var item = cart.FirstOrDefault(c => c.ProId == proId);
            if (item != null)
            {
                cart.Remove(item);
                SaveSessionCart(cart);
            }
        }

        public void ClearSessionCart()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            session?.Remove(SessionCartKey);
        }

        public int GetSessionCartCount()
        {
            return GetSessionCart().Sum(c => c.Qty);
        }

        public async Task MergeSessionCartToUserCartAsync(string userId)
        {
            var sessionCart = GetSessionCart();
            if (!sessionCart.Any()) return;

            foreach (var sessionItem in sessionCart)
            {
                var existingCartItem = await _context.ShoppingCarts
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.ProId == sessionItem.ProId);

                if (existingCartItem != null)
                {
                    // Merge quantities
                    existingCartItem.Qty += sessionItem.Qty;
                    _context.ShoppingCarts.Update(existingCartItem);
                }
                else
                {
                    // Add new item
                    var cartItem = new ShoppingCart
                    {
                        ProId = sessionItem.ProId,
                        UserId = userId,
                        Qty = sessionItem.Qty
                    };
                    _context.ShoppingCarts.Add(cartItem);
                }
            }

            await _context.SaveChangesAsync();
            ClearSessionCart();
        }
    }

    public class SessionCartItem
    {
        public int ProId { get; set; }
        public int Qty { get; set; }
    }
}

