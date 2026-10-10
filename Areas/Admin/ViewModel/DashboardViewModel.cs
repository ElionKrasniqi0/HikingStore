using HikingStore.Models;

namespace HikingStore.Areas.Admin.ViewModel
{
    public class DashboardViewModel
    {
        public decimal TotalRevenue { get; set; }
        public decimal Revenue30Days { get; set; }
        public int OrderCount { get; set; }
        public int Orders30Days { get; set; }
        public int OpenOrders { get; set; }
        public int CustomerCount { get; set; }
        public int ActiveProducts { get; set; }
        public int PendingReviews { get; set; }
        public int MessageCount { get; set; }
        public int NewsletterSubscribers { get; set; }
        public List<Product> LowStock { get; set; } = new();
        public List<Order> RecentOrders { get; set; } = new();
        public List<DailySales> SalesByDay { get; set; } = new();
        public List<TopProduct> TopProducts { get; set; } = new();
        public Dictionary<HikingStore.Enum.OrderStatus, int> OrdersByStatus { get; set; } = new();
    }

    public class ReportViewModel
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int OrderCount { get; set; }
        public int CancelledCount { get; set; }
        public decimal Revenue { get; set; }
        public decimal Discounts { get; set; }
        public decimal ShippingCollected { get; set; }
        public int NewCustomers { get; set; }
        public decimal AverageOrderValue => OrderCount == 0 ? 0 : Math.Round(Revenue / OrderCount, 2);
        public List<DailySales> Daily { get; set; } = new();
        public List<TopProduct> TopProducts { get; set; } = new();
        public List<CategorySales> ByCategory { get; set; } = new();
        public List<CouponUsage> Coupons { get; set; } = new();
    }

    public record CategorySales(string Name, int Units, decimal Revenue);

    public record CouponUsage(string Code, int Uses, decimal Discount);

    public record DailySales(DateTime Date, int Orders, decimal Revenue);

    public record TopProduct(int ProId, string Name, int Units, decimal Revenue);
}
