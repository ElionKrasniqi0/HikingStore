# HikingStore

HikingStore is an online shop for hiking and outdoor gear built with **ASP.NET Core 8 MVC**. Customers can:

- browse a database-driven catalog with server-side filters, search and a mega menu;
- view products with variants, a gallery and moderated reviews;
- keep a wishlist and a cart, which works for guests and is merged at sign-in;
- check out with coupons and server-calculated totals, paying by debit/credit card on a hosted payment page (Stripe Checkout or a bank's page), cash on delivery or at store pickup;
- track and cancel orders, download invoices, manage addresses;
- chat live with support.

Administrators get a separate area at `/Admin` for the dashboard, orders, products, categories, coupons, reviews, reports, users and roles, newsletter, contact messages and support chat.

## Tech stack

| Layer | Technology |
| --- | --- |
| Web framework | ASP.NET Core 8 MVC + Razor Pages (Identity UI) |
| Data access | Entity Framework Core 8 (SQL Server) |
| Authentication | ASP.NET Core Identity with `ApplicationUser` (cookie auth, roles, email confirmation, optional Google login) |
| Real-time | SignalR (`/chatHub`, `/realtimeHub`) |
| Email | SMTP via `System.Net.Mail` (logged to the console in Development when no SMTP password is set) |
| Excel export | EPPlus 7 (non-commercial license) |
| Front end | Bootstrap 5.3.3, Font Awesome 6.5.2 and SignalR JS from CDNs with SRI; custom design system in `wwwroot/css/hikingstore.css` |
| Tests | xUnit + EF Core SQLite in-memory (`HikingStore.Tests`) |

## Quick start

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and a SQL Server instance (LocalDB, Express or full).

1. Set the connection string. The committed `appsettings.json` points at a developer machine; override it with user secrets:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=HikingStore;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
   ```

2. Set an admin password so the admin account is seeded (it is skipped otherwise):

   ```powershell
   dotnet user-secrets set "SeedData:AdminPassword" "<a strong password>"
   ```

3. Optionally configure SMTP (`EmailSettings:SenderEmail`, `EmailSettings:SenderPassword`, `EmailSettings:AdminEmail`). Without a password in Development, emails such as account confirmation links are written to the application log instead of being sent. See [Configuration](docs/configuration.md).

4. Run the app:

   ```powershell
   dotnet run --launch-profile https
   ```

   The app opens on `https://localhost:7183` (HTTP: `http://localhost:5045`).

On startup the app applies pending EF Core migrations and seeds the `Admin` and `User` roles plus the admin account (`SeedData:AdminEmail`, default `admin@hikingstore.com`). The admin area is at `/Admin`.

## Payments

Card payments are **off until a provider is configured**. Cash on delivery and pay at pickup work out of the box.

- **Stripe Checkout** (`Payments:CardProvider = Stripe`) needs a Stripe account in a [supported country](https://stripe.com/global). Kosovo is not one of them, so live payments need a business entity registered elsewhere. Test mode works for anyone.
- **A bank or processor hosted payment page** (`Payments:CardProvider = HostedGateway`) needs an e-commerce acquiring agreement with that bank.

Cards are entered only on the provider's page; the app never sees or stores card numbers or CVV codes. Orders are created as *payment pending* and are marked **Paid only by a signature-verified webhook**, never by the customer returning to the site. Keys go in user secrets or environment variables, never in `appsettings.json`.

See **[Card payments](docs/payments.md)** for setup, test cards, webhook events, merchant-account requirements and the production checklist.

## Tests

```powershell
dotnet test HikingStore.Tests
```

The 68 tests cover:

- `CartService`: variant validation, stock limits, guest-cart merge, ownership, database-priced totals, free-shipping threshold and coupons;
- `CouponService`: discount calculation and validation rules;
- image signature detection, shipping cost rules and product model helpers;
- the card-payment flow: Stripe and hosted-gateway signature verification, the order payment states, duplicate events, amount mismatches, cancellation with restock, and expiry release.

> **Windows Smart App Control:** on machines with Smart App Control enabled, Windows may block the freshly built, unsigned `HikingStore.dll`. When that happens, `dotnet run` and `dotnet test` fail because the assembly cannot be loaded. Run on a machine without Smart App Control, in a dev container or in CI.

## Documentation

| Document | Contents |
| --- | --- |
| [Architecture](docs/architecture.md) | Project layout, request pipeline, services, dependency injection, front-end assets |
| [Data model](docs/data-model.md) | Entities, relationships, indexes, enums, migrations, seeding |
| [Customer features](docs/customer-features.md) | Storefront, catalog, cart, checkout, orders, reviews, wishlist, account, chat |
| [Admin guide](docs/admin-guide.md) | Everything available under `/Admin` |
| [Routes and endpoints](docs/routes.md) | Every controller action, its HTTP method, auth requirement and response |
| [Real-time (SignalR)](docs/realtime.md) | Hubs, groups, client methods and server events |
| [Configuration and deployment](docs/configuration.md) | `appsettings`, secrets, email, Google login, store settings, database, deployment |
| [Card payments](docs/payments.md) | Stripe and bank hosted-page setup, merchant requirements, webhooks, payment statuses, test mode, production checklist |
| [Known issues](docs/known-issues.md) | Open limitations, required actions (secret rotation) and fixed items |

Earlier, more focused write-ups are also in the repository root: `SECURITY_IMPLEMENTATION.md`, `SECURITY_ENHANCEMENTS_SUMMARY.md`, `SECURITY_ORDER_VALIDATION.md` and `PERFORMANCE_OPTIMIZATIONS.md`.
