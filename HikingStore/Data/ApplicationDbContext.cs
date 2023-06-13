using HikingStore.Enum;
using HikingStore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace HikingStore.Data
{
    public class ApplicationUser : IdentityUser
    {
        //public bool IsRoleResctricted { get; set; }
        public UserStatus UserStatus { get; set; } = UserStatus.Client;

        [ForeignKey("UserId")]
        public virtual ICollection<Request> Requests { get; set; }
    }
        public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<ShoppingCart> ShoppingCarts { get; set; }

    }
}