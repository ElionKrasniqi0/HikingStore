using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HikingStore.Data;
using HikingStore.Enum;

namespace HikingStore.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            var context = services.GetRequiredService<ApplicationDbContext>();
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var configuration = services.GetRequiredService<IConfiguration>();

            // Ensure database is created
            await context.Database.MigrateAsync();

            // Seed Admin Role
            string adminRoleName = "Admin";
            if (!await roleManager.RoleExistsAsync(adminRoleName))
            {
                await roleManager.CreateAsync(new IdentityRole(adminRoleName));
            }

            // Seed User Role
            string userRoleName = "User";
            if (!await roleManager.RoleExistsAsync(userRoleName))
            {
                await roleManager.CreateAsync(new IdentityRole(userRoleName));
            }

            // Seed Admin User
            string adminEmail = configuration["SeedData:AdminEmail"] ?? "admin@hikingstore.com";
            string adminPassword = configuration["SeedData:AdminPassword"] ?? "Admin@123";
            string adminFirstName = configuration["SeedData:AdminFirstName"] ?? "Admin";
            string adminLastName = configuration["SeedData:AdminLastName"] ?? "User";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true, // Auto-confirm admin email so they can log in immediately
                    FirstName = adminFirstName,
                    LastName = adminLastName,
                    UserStatus = UserStatus.Client
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, adminRoleName);
                }
            }
            else
            {
                // Ensure admin user is in Admin role even if they already exist
                if (!await userManager.IsInRoleAsync(adminUser, adminRoleName))
                {
                    await userManager.AddToRoleAsync(adminUser, adminRoleName);
                }
            }
        }
    }
}

