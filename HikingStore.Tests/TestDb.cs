using HikingStore.Data;
using HikingStore.Models;
using HikingStore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HikingStore.Tests;

/// <summary>An isolated SQLite in-memory database with the application's model, plus an HTTP context with a working session.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Context { get; }
    public HttpContextAccessor HttpContextAccessor { get; } = new();
    public StoreSettings Settings { get; } = new()
    {
        FreeShippingThreshold = 100,
        ShippingMethods =
        {
            new ShippingMethodOption { Code = "standard", Name = "Standard", Cost = 4.90m, FreeOverThreshold = true },
            new ShippingMethodOption { Code = "express", Name = "Express", Cost = 9.90m, FreeOverThreshold = false },
        }
    };

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        Context = CreateContext();
        Context.Database.EnsureCreated();

        HttpContextAccessor.HttpContext = new DefaultHttpContext();
        HttpContextAccessor.HttpContext.Features.Set<ISessionFeature>(new SessionFeature { Session = new TestSession() });
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public CartService CreateCartService(ApplicationDbContext? context = null)
    {
        var ctx = context ?? Context;
        return new CartService(ctx, new SessionCartService(HttpContextAccessor), new CouponService(ctx),
            HttpContextAccessor, Options.Create(Settings));
    }

    public Product AddProduct(string name = "Trail Jacket", decimal price = 50m, int stock = 10,
        string? sizes = null, string? colors = null, bool active = true)
    {
        var category = Context.Categories.FirstOrDefault() ?? Context.Categories.Add(new Category { CatName = "Jackets" }).Entity;
        var product = new Product
        {
            ProName = name,
            Descreption = $"{name} for testing",
            Price = price,
            StockQuantity = stock,
            Sizes = sizes,
            Colors = colors,
            IsActive = active,
            Category = category,
            CreatedAt = DateTime.UtcNow
        };
        Context.Products.Add(product);
        Context.SaveChanges();
        return product;
    }

    public ApplicationUser AddUser(string email = "hiker@example.com")
    {
        var user = new ApplicationUser { UserName = email, Email = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant() };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }
}
