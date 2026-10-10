# Configuration and deployment

## Where settings come from

The app uses the default `WebApplication.CreateBuilder` configuration sources, in this order (later sources win):

1. `appsettings.json`
2. `appsettings.{Environment}.json` (only `appsettings.Development.json` exists; it just sets log levels)
3. User secrets — Development only (`UserSecretsId` is set in `HikingStore.csproj`)
4. Environment variables
5. Command-line arguments

All code reads configuration through `IConfiguration` or `IOptions<T>`, so every key below can be overridden by user secrets or environment variables. Nothing reads `appsettings.json` from disk directly.

## Configuration keys

| Key | Used by | Description |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `Program.cs` → `ApplicationDbContext` | SQL Server connection string. **Required**: startup throws `InvalidOperationException` if it is missing |
| `Authentication:Google:ClientId` / `ClientSecret` | `Program.cs` | Google sign-in, only registered when **both** are non-empty |
| `EmailSettings:*` | `EmailSender`, `OrderEmailService`, `Program.cs` | SMTP settings, see [Email](#email) |
| `SeedData:*` | `DbInitializer` | First admin account, see [Seeded admin](#seeded-admin) |
| `Store:*` | `StoreSettings` (`IOptions<StoreSettings>`) | Store name, contact details, shipping, VAT, see [Store settings](#store-settings) |
| `Logging:LogLevel:*` | Logging | `appsettings.json` sets `HikingStore.Services` to `Information` so email and order logs are visible |
| `AllowedHosts` | Host filtering | `*` by default; set to your host name(s) in production |

## Email

`EmailSettings` section:

| Key | Default in code | Description |
| --- | --- | --- |
| `SenderEmail` | — | From address and SMTP user name |
| `SenderPassword` | — | SMTP password (for Gmail, an app password; spaces are stripped) |
| `SmtpServer` | `smtp.gmail.com` | SMTP host |
| `SmtpPort` | `587` | SMTP port |
| `SenderName` | `HikingStore` | Display name of the From address |
| `EnableSsl` | `true` | Passed to `SmtpClient.EnableSsl` |
| `AdminEmail` | falls back to `SenderEmail` | Receives an `[Admin] Order #… → Cancelled` copy when a **customer** cancels an order. Admin-initiated status changes don't send an admin copy |

Which sender is used is decided once in `Program.cs`:

- **Development and `EmailSettings:SenderPassword` empty** → `DevelopmentEmailSender`. Emails are not sent; each one is written to the log as a warning (`Email not sent (SMTP not configured)…`) including recipient, subject and body. `/TestEmail` detects this and tells you email isn't configured.
- **Otherwise** → `EmailSender` (SMTP via `System.Net.Mail.SmtpClient`, 60-second timeout). If `SenderEmail` or `SenderPassword` is missing it throws `Email is not configured…`, so in Production a missing password fails loudly instead of silently dropping mail.

Every email in the app goes through `IEmailSender`:

| Email | Trigger |
| --- | --- |
| Confirm your email | Registration, resend confirmation, Google sign-up (`Areas/Identity/Pages/Account/*`) |
| Password reset, email change | Identity pages |
| Order confirmation | Checkout (`OrderEmailService.QueueOrderConfirmation`) |
| Order status update | Admin changes the status, or the customer cancels (`OrderEmailService.QueueStatusUpdate`) |
| Admin cancellation copy | Customer cancels an order; sent to `AdminEmail` or `SenderEmail` |
| Reply to a contact message | Admin → Messages → Reply |
| Test email | `/TestEmail` |

Order emails are sent in the background; failures are logged and never fail the request.

Registration requires a confirmed email (`RequireConfirmedAccount`/`RequireConfirmedEmail`), so outside Development a working SMTP account is needed for anyone except the seeded admin to sign in. In Development with `DevelopmentEmailSender`, copy the confirmation link from the log.

Using Gmail:
1. Enable 2-Step Verification on the Google account.
2. Create an app password at <https://myaccount.google.com/apppasswords>.
3. Put the address in `SenderEmail` and the app password in `SenderPassword`.
4. Restart the app, sign in as admin and send a test message from `/TestEmail`.

## Google sign-in

`Program.cs` calls `AddGoogle` (scopes `profile` and `email`) only when both `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are non-empty. Otherwise the scheme isn't registered and the login/register pages show no Google button.

1. Create an OAuth 2.0 Client ID (Web application) in Google Cloud Console.
2. Add the authorized redirect URI `https://<your-host>/signin-google` (locally `https://localhost:7183/signin-google`).
3. Set both keys as secrets (see below) and restart.

## Seeded admin

`SeedData` section, read by `DbInitializer` on every startup:

| Key | Default | Description |
| --- | --- | --- |
| `AdminEmail` | `admin@hikingstore.com` in `appsettings.json` | Email and user name. If empty, seeding is skipped |
| `AdminPassword` | empty | Must satisfy the Identity password rules (8+ characters, upper, lower, digit, symbol, 2 unique characters) |
| `AdminFirstName` / `AdminLastName` | `Admin` / `User` | Used only when the account is created |

- If no user with `AdminEmail` exists and `AdminPassword` is empty, **no admin is created**; a warning is logged.
- If the user exists, the seeder only ensures it has the `Admin` role and a confirmed email. **The password is never reset**, so `AdminPassword` only matters for the first run.
- The `Admin` and `User` roles are always created if missing.

## Store settings

The `Store` section is bound to `HikingStore.Services.StoreSettings` with `builder.Services.Configure<StoreSettings>(...GetSection("Store"))` and injected into views and services as `IOptions<StoreSettings>`. Changes require a restart.

| Key | Type (default in code) | Used for |
| --- | --- | --- |
| `Name` | string (`HikingStore`) | Store name in emails and pages |
| `Currency` | string (`EUR`) | ISO code (product structured data, terms page) |
| `CurrencySymbol` | string (`€`) | Price formatting (`FormatPrice`) |
| `SupportEmail`, `SupportPhone`, `Address` | string | Contact page, footer, emails |
| `OpeningHours` | list of strings | Contact and order-confirmation pages |
| `FreeShippingThreshold` | decimal (`100`) | Discounted subtotal at or above which methods with `FreeOverThreshold` are free; `0` disables free shipping |
| `ReturnDays` | int (`30`) | Return-policy text (home, product, cart, returns, FAQ, terms, invoice) |
| `VatRate` | decimal (`0.18`) | VAT shown as included on invoices (prices are VAT-inclusive) |
| `AnnouncementText` | string | Top announcement bar; hidden when empty |
| `HeroImageUrl` | string | Home page hero image (app-relative `/...` or absolute URL) |
| `HeroImageCredit` | string | Bound but not displayed anywhere |
| `ShippingMethods` | list | Checkout options; see below |
| `SocialLinks` | dictionary name → URL | Footer links; empty values are hidden |

Each `ShippingMethods` entry:

| Key | Description |
| --- | --- |
| `Code` | Identifier posted by checkout (`standard`, `express`, `pickup`); matched case-insensitively. The order-confirmation page treats `pickup` as "pay at pickup" |
| `Name` | Display name; also stored on the order as shipping service and initial carrier |
| `Cost` | Shipping price |
| `EstimatedDays` | Free text such as `3–5 business days` |
| `FreeOverThreshold` | Whether the method becomes free at `FreeShippingThreshold` |

The first entry is the default method. Checkout rejects a code that isn't in the list. `AnnouncementText` is free text: if you change `FreeShippingThreshold` or `ReturnDays`, update it too.

## Payment

The only payment method is **cash on delivery** (`PaymentMethods.CashOnDelivery`); for the `pickup` shipping method the customer pays at the store. `OrderService` rejects any other payment method. No online payment provider is integrated and **no card data is collected or stored**. Orders start with payment status `Pending`; it becomes `Paid` when an admin marks the order `Completed`, and `Cancelled` when the order is cancelled.

## Secrets

`appsettings.json` contains no credentials: `SenderPassword`, `AdminPassword` and the Google keys are empty. Keep it that way and supply secrets per environment.

Development (user secrets, run in the project folder):

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=HikingStore;..."
dotnet user-secrets set "EmailSettings:SenderEmail" "<address>"
dotnet user-secrets set "EmailSettings:SenderPassword" "<app password>"
dotnet user-secrets set "EmailSettings:AdminEmail" "<address for admin copies>"
dotnet user-secrets set "Authentication:Google:ClientId" "<client id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client secret>"
dotnet user-secrets set "SeedData:AdminPassword" "<strong password>"
dotnet user-secrets list
```

Production (environment variables; `:` becomes `__`):

```powershell
$env:ConnectionStrings__DefaultConnection = "<connection string>"
$env:EmailSettings__SenderEmail = "<address>"
$env:EmailSettings__SenderPassword = "<app password>"
$env:Authentication__Google__ClientId = "<client id>"
$env:Authentication__Google__ClientSecret = "<client secret>"
$env:SeedData__AdminPassword = "<strong password>"
```

Array entries use their index, e.g. `Store__ShippingMethods__0__Cost=5.50`. A secret store such as Azure Key Vault works too, as long as it is added as a configuration provider.

> **Secrets in git history.** Earlier commits of `appsettings.json` contained a real Google OAuth `ClientSecret` and a Gmail app password. They have been removed from the current file, but **they are still readable in the repository history**. Treat both as compromised: delete/regenerate the OAuth client secret in Google Cloud Console and revoke the app password at <https://myaccount.google.com/apppasswords>, then configure the new values only through user secrets or environment variables. Rewriting history (e.g. `git filter-repo`) does not help for copies that were already cloned or pushed, so rotation is required either way.

## Database

- SQL Server via EF Core, with connection resiliency (`EnableRetryOnFailure`, 5 retries, up to 30 s delay) and split queries.
- **Migrations are applied automatically at startup**: `Program.cs` calls `DbInitializer.InitializeAsync`, which runs `context.Database.MigrateAsync()` before seeding. If that fails the error is logged ("An error occurred while migrating or seeding the database.") and the app keeps starting. The database account therefore needs permission to create and alter tables.
- To apply migrations manually instead:

  ```powershell
  dotnet tool install --global dotnet-ef
  dotnet ef database update
  ```

- **Never drop the database** (`dotnet ef database drop`, `EnsureDeleted`) on a shared or production server: orders, users and uploaded-image references live there. Add a new migration for schema changes instead.
- In Development, `UseMigrationsEndPoint` lets you apply pending migrations from the error page.

## Local development

```powershell
dotnet restore
dotnet run --launch-profile https   # https://localhost:7183, http://localhost:5045
```

Launch profiles are in `Properties/launchSettings.json` (`http`, `https`, `IIS Express`), all with `ASPNETCORE_ENVIRONMENT=Development`.

## Deployment checklist

- Set `ASPNETCORE_ENVIRONMENT=Production`. This enables HSTS (`UseHsts`, default 30 days) and the `/Error` exception page; Development uses the developer exception page instead.
- Serve over HTTPS. `UseHttpsRedirection` redirects HTTP; the auth cookie uses `CookieSecurePolicy.SameAsRequest`, so it is only marked `Secure` when the request is HTTPS. Behind a reverse proxy, forward the original scheme.
- Security headers added to every response by middleware in `Program.cs`: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: geolocation=(), microphone=(), camera=()`. No Content-Security-Policy is set.
- Provide the connection string, email, Google and seed settings through environment variables or a secret store. Set `SeedData:AdminPassword` for the first start if no admin exists yet.
- Uploaded images are written to **`wwwroot/Product/`** and **`wwwroot/category/`** (under `WebRootPath`). The app's identity needs write access, and these folders must be persisted across deployments (don't overwrite them when publishing, or put them on a persistent volume).
- EPPlus 7 (orders Excel export) runs with `ExcelPackage.LicenseContext = LicenseContext.NonCommercial`, set in `OrdersController.ExportToExcel`. Commercial use requires a purchased EPPlus license and changing that line.
- Sessions, the memory cache, rate-limit counters and SignalR groups are in-memory. For more than one instance add a distributed cache, a SignalR backplane and sticky sessions.
- Static files get `Cache-Control: public,max-age=31536000` when requested with a `?v=` query (as produced by `asp-append-version`), otherwise one day.
