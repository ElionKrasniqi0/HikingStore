using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HikingStore.Data
{
    public static class DbInitializer
    {
        public const string AdminRole = "Admin";
        public const string UserRole = "User";

        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            var context = services.GetRequiredService<ApplicationDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var configuration = services.GetRequiredService<IConfiguration>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

            await context.Database.MigrateAsync();

            foreach (var roleName in new[] { AdminRole, UserRole })
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            var adminEmail = configuration["SeedData:AdminEmail"];
            var adminPassword = configuration["SeedData:AdminPassword"];
            if (string.IsNullOrWhiteSpace(adminEmail))
            {
                return;
            }

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                if (string.IsNullOrWhiteSpace(adminPassword))
                {
                    logger.LogWarning("No admin account exists and SeedData:AdminPassword is not configured; skipping admin seeding.");
                    return;
                }

                adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FirstName = configuration["SeedData:AdminFirstName"] ?? "Admin",
                    LastName = configuration["SeedData:AdminLastName"] ?? "User"
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (!result.Succeeded)
                {
                    logger.LogError("Could not create the seed admin account: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                    return;
                }
            }

            // Only guarantee the role and confirmed email; an existing admin password is never overwritten.
            if (!await userManager.IsInRoleAsync(adminUser, AdminRole))
            {
                await userManager.AddToRoleAsync(adminUser, AdminRole);
            }

            if (!adminUser.EmailConfirmed)
            {
                adminUser.EmailConfirmed = true;
                await userManager.UpdateAsync(adminUser);
            }
        }
    }
}

