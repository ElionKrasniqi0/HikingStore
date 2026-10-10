using HikingStore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace HikingStore.Data
{
    public class ApplicationUser : IdentityUser
    {
        [PersonalData]
        [StringLength(100)]
        public string? FirstName { get; set; }

        [PersonalData]
        [StringLength(100)]
        public string? LastName { get; set; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
                ? (UserName ?? Email ?? "Customer")
                : $"{FirstName} {LastName}".Trim();
    }

    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<ShoppingCart> ShoppingCarts { get; set; }
        public DbSet<Wishlist> Wishlists { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<TrackingEvent> TrackingEvents { get; set; }
        public DbSet<ChatRoom> ChatRooms { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Coupon> Coupons { get; set; }
        public DbSet<Address> Addresses { get; set; }
        public DbSet<NewsletterSubscriber> NewsletterSubscribers { get; set; }

        /// <summary>
        /// Removes rows whose foreign keys to AspNetUsers don't cascade, so the account itself can be deleted.
        /// Callers must refuse deletion when the user has orders: orders cascade and must be kept.
        /// </summary>
        public async Task RemoveNonCascadingUserDataAsync(string userId)
        {
            await ShoppingCarts.Where(c => c.UserId == userId).ExecuteDeleteAsync();
            await Wishlists.Where(w => w.UserId == userId).ExecuteDeleteAsync();
            await ChatMessages.Where(m => m.SenderId == userId && m.ChatRoom!.UserId != userId).ExecuteDeleteAsync();
            await ChatRooms.Where(r => r.AgentId == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.AgentId, (string?)null));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ApplicationUser>().Ignore(u => u.DisplayName);

            modelBuilder.Entity<Product>(entity =>
            {
                entity.Property(p => p.IsActive).HasDefaultValue(true);
                entity.Property(p => p.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(p => new { p.IsActive, p.CatId });
                entity.HasIndex(p => p.Price);
                entity.HasIndex(p => p.CreatedAt);
                entity.HasIndex(p => p.Brand);
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasOne(c => c.Parent)
                    .WithMany(c => c.Children)
                    .HasForeignKey(c => c.ParentCatId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Product)
                .WithMany()
                .HasForeignKey(od => od.ProId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasIndex(o => o.CheckoutToken).IsUnique().HasFilter("[CheckoutToken] IS NOT NULL");
                entity.HasIndex(o => new { o.UserId, o.OrderDate });
                entity.HasIndex(o => o.Status);
                entity.Property(o => o.PaymentMethod).HasDefaultValue(PaymentMethods.CashOnDelivery);
                entity.Property(o => o.PaymentStatus).HasDefaultValue(PaymentStatuses.Pending);
            });

            modelBuilder.Entity<ShoppingCart>()
                .HasIndex(c => new { c.UserId, c.ProId, c.SelectedSize, c.SelectedColor })
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL"); // SQL Server treats NULL variants as equal, so one line per product+variant

            modelBuilder.Entity<Wishlist>()
                .HasIndex(w => new { w.UserId, w.ProId })
                .IsUnique();

            modelBuilder.Entity<Review>(entity =>
            {
                entity.HasIndex(r => new { r.ProId, r.UserId }).IsUnique();
                entity.HasIndex(r => new { r.ProId, r.IsApproved });
                entity.HasOne(r => r.Product)
                    .WithMany(p => p.Reviews)
                    .HasForeignKey(r => r.ProId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Coupon>()
                .HasIndex(c => c.Code)
                .IsUnique();

            modelBuilder.Entity<NewsletterSubscriber>()
                .HasIndex(s => s.Email)
                .IsUnique();

            modelBuilder.Entity<Address>()
                .HasIndex(a => a.UserId);

            modelBuilder.Entity<ChatRoom>()
                .HasOne(cr => cr.Agent)
                .WithMany()
                .HasForeignKey(cr => cr.AgentId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
