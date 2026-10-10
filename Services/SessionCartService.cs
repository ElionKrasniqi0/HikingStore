using System.Text.Json;

namespace HikingStore.Services
{
    /// <summary>Stores the cart of anonymous visitors in session until they sign in.</summary>
    public class SessionCartService
    {
        private const string SessionCartKey = "SessionCart";
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SessionCartService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public List<SessionCartItem> GetSessionCart()
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            var cartJson = session?.GetString(SessionCartKey);
            if (string.IsNullOrEmpty(cartJson))
                return new List<SessionCartItem>();

            try
            {
                return JsonSerializer.Deserialize<List<SessionCartItem>>(cartJson) ?? new List<SessionCartItem>();
            }
            catch (JsonException)
            {
                return new List<SessionCartItem>();
            }
        }

        public void SaveSessionCart(List<SessionCartItem> cart)
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            session?.SetString(SessionCartKey, JsonSerializer.Serialize(cart));
        }

        public void ClearSessionCart()
        {
            _httpContextAccessor.HttpContext?.Session.Remove(SessionCartKey);
        }

        public int GetSessionCartCount() => GetSessionCart().Sum(c => c.Qty);
    }

    public class SessionCartItem
    {
        public int ProId { get; set; }
        public int Qty { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
    }
}
