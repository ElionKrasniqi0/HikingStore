# Architecture

HikingStore is a single ASP.NET Core 8 web project (`HikingStore.csproj`) using classic MVC, with an MVC **Area** for the admin panel and the scaffolded **Identity** Razor Pages for accounts. Business logic (cart, pricing, coupons, checkout, catalog queries, uploads, order emails) lives in `Services/`; controllers are thin. A separate xUnit project, `HikingStore.Tests`, sits in the same folder and is excluded from the web project's compilation.

## Project layout

```
HikingStore/
├── Program.cs                     Bootstrap: DI registrations, middleware pipeline, routes, startup migration/seeding
├── appsettings*.json              Connection string, Google auth, EmailSettings, SeedData, Store settings
├── Areas/
│   ├── Admin/                     Admin panel, Admin role only
│   │   ├── Controllers/           Categories, Chat, Coupons, Home (dashboard, users, messages), Orders, Products,
│   │   │                          Reports, Reviews, RoleManager, Wishlist
│   │   ├── ViewModel/             DashboardViewModel, ReportViewModel, role/user view models
│   │   └── Views/                 Admin views + Shared/_LayoutAdmin, _AdminPagination, _StatusBadge
│   └── Identity/
│       ├── Data/HikingStoreContext.cs   Leftover scaffolded context; not registered in DI
│       └── Pages/                 Scaffolded Identity pages (login, register, 2FA, manage account, ...)
├── Controllers/                   Storefront: Account, Carts, Chat, Error, Home, Orders (OrderController.cs),
│                                  Reviews, TestEmail (Admin only), Wishlist
├── Data/
│   ├── ApplicationDbContext.cs    EF Core context + ApplicationUser
│   ├── DbInitializer.cs           Applies migrations and seeds roles/admin at startup
│   └── Migrations/                EF Core migrations
├── Enum/                          OrderStatus, UserStatus, RequestStatus
├── Hubs/                          SignalR hubs: ChatHub, RealtimeHub
├── Middleware/                    RateLimitingMiddleware
├── Models/                        EF entities (Product, Category, Order, Coupon, Review, Address, ...)
├── Services/                      Cart, catalog, coupons, orders, image upload, email, audit, StoreSettings
├── ViewModels/                    CatalogViewModels.cs (filter, cards, facets, home, product details),
│                                  CheckoutViewModel.cs (CheckoutInput, CheckoutViewModel)
├── Views/                         Storefront Razor views + Shared layout and partials
├── wwwroot/                       Static files (see Front-end assets)
└── HikingStore.Tests/             xUnit tests (CartService, CouponService, models/utilities) on SQLite in-memory
```

## Request pipeline

Configured in `Program.cs`, in this order:

1. **Exception handling** – Developer exception page and `UseMigrationsEndPoint` in Development; `UseExceptionHandler("/Error")` and `UseHsts` otherwise.
2. **Security headers** (inline middleware) – `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: geolocation=(), microphone=(), camera=()`. No Content-Security-Policy is set.
3. `UseResponseCompression` – Brotli and Gzip, enabled for HTTPS.
4. `UseHttpsRedirection`
5. `UseStaticFiles` – `Cache-Control: public,max-age=31536000` (one year) when the request has a `?v=` query (i.e. `asp-append-version` URLs), otherwise `max-age=86400` (one day).
6. `UseStatusCodePagesWithReExecute("/Error/{0}")` – error status codes re-execute against `ErrorController`.
7. `UseRouting`
8. **`RateLimitingMiddleware`** (see below)
9. `UseSession` – 7-day idle timeout, HttpOnly essential cookie `.HikingStore.Session`, backed by the distributed memory cache.
10. `UseAuthentication` / `UseAuthorization`
11. Endpoints (routes, Razor Pages, hubs).

After the pipeline is built, `DbInitializer.InitializeAsync` runs (see [Data model](data-model.md#startup-migration-and-seeding)); any exception is logged and the app still starts.

### Antiforgery

`AutoValidateAntiforgeryTokenAttribute` is added as a global MVC filter, so every non-GET/HEAD/OPTIONS/TRACE controller action requires a token. The token is also accepted from the `X-CSRF-TOKEN` header. Both layouts render it in `<meta name="csrf-token">`, and `site.js` attaches it to same-origin `fetch` and jQuery AJAX requests automatically.

### Rate limiting

`RateLimitingMiddleware` keeps per-IP counters in `IMemoryCache` and only inspects POST requests:

| Path | Limit | Response when exceeded |
| --- | --- | --- |
| `/Identity/Account/Login` | 5 per 15 minutes | `429`, `Retry-After: 900` |
| `/Identity/Account/Register` | 3 per hour | `429`, `Retry-After: 3600` |

Every POST counts, including successful ones, and each attempt re-sets the cache entry's expiry. `ForgotPassword` and `ResendEmailConfirmation` are recognised as authentication endpoints by the middleware but have no limit applied. Counters live in process memory (reset on restart, not shared between instances).

### Routes

| Name | Pattern |
| --- | --- |
| `areas` | `{area:exists}/{controller=Home}/{action=Index}/{id?}` |
| `default` | `{controller=Home}/{action=Index}/{id?}` |
| Razor Pages | `/Identity/Account/...` |
| SignalR | `/chatHub` (`ChatHub`), `/realtimeHub` (`RealtimeHub`) |

`ErrorController` uses attribute routes: `/Error`, `/Error/{statusCode}`, `/Error/NotFound`, `/Error/AccessDenied`. See [Routes and endpoints](routes.md) for the full list.

## Authentication and authorization

- **Identity** is registered with `AddDefaultIdentity<ApplicationUser>` plus `AddRoles<IdentityRole>`, stored in `ApplicationDbContext`.
- **Account rules**: confirmed account/email required to sign in, unique email, password ≥ 8 chars with upper, lower, digit and symbol, at least 2 unique characters.
- **Lockout**: 5 failed attempts lock the account for 15 minutes (also for new users).
- **Cookie**: 30-day sliding expiration, HttpOnly, `SecurePolicy = SameAsRequest`. Login `/Identity/Account/Login`, logout `/Identity/Account/Logout`, access denied `/Error/AccessDenied`.
- **Google login** is registered only when both `Authentication:Google:ClientId` and `ClientSecret` are set.
- **Roles**: `Admin` and `User` (constants `DbInitializer.AdminRole` / `UserRole`). All admin-area controllers and `TestEmailController` require the `Admin` role; storefront `Account`, `Chat`, `Orders`, `Reviews` and `Wishlist` controllers require sign-in; `CartsController` requires sign-in only for checkout-related actions (guests can use the cart).
- After sign-in (`Login`, `LoginWith2fa`, `LoginWithRecoveryCode` and `ExternalLogin` pages), the guest session cart is merged into the account via `CartService.MergeGuestCartAsync`.

## Registered services

| Service | Lifetime | Purpose |
| --- | --- | --- |
| `ApplicationDbContext` | Scoped | SQL Server (`DefaultConnection`, required at startup), retry on failure (5 retries, 30 s max delay), split queries |
| Identity (`ApplicationUser`, `IdentityRole`) | – | See above |
| `StoreSettings` | Options | `Configure<StoreSettings>` bound to the `Store` configuration section; consumed via `IOptions<StoreSettings>` |
| `IHttpContextAccessor` | Singleton | Used by session-based services |
| `IMemoryCache` | Singleton | Rate-limit counters, catalog navigation cache (`AddMemoryCache` is called twice; harmless) |
| Distributed memory cache + session | – | Session storage for the guest cart and applied coupon |
| `IEmailSender` → `DevelopmentEmailSender` or `EmailSender` | Transient | `DevelopmentEmailSender` only in Development **and** when `EmailSettings:SenderPassword` is empty; `EmailSender` (SMTP) otherwise |
| `IEmailTemplateService` → `EmailTemplateService` | Scoped | Branded HTML account emails |
| `SessionCartService` | Scoped | Guest cart in session |
| `CouponService` | Scoped | Coupon validation and discount calculation |
| `CartService` | Scoped | Cart contents and pricing |
| `OrderService` | Scoped | Checkout and cancellation |
| `CatalogService` | Scoped | Storefront product queries |
| `ImageUploadService` | Scoped | Validated image uploads |
| `OrderEmailService` | **Singleton** | Background order emails |
| `AdminAuditService` | Scoped | Admin audit logging |
| SignalR | – | Detailed errors in Development, 64 KB max message, 15 s keep-alive and handshake timeout, 30 s client timeout |
| Response compression | – | Brotli + Gzip, HTTPS enabled |
| MVC + Razor Pages | – | Global antiforgery filter; antiforgery header `X-CSRF-TOKEN` |

## Services in detail

### `StoreSettings` (`Services/StoreSettings.cs`)
Options class for the `Store` section: `Name`, `Currency`, `CurrencySymbol`, `SupportEmail`, `SupportPhone`, `Address`, `OpeningHours`, `FreeShippingThreshold` (default 100), `ReturnDays` (30), `VatRate` (0.18), `AnnouncementText`, `HeroImageUrl`, `HeroImageCredit`, `ShippingMethods` (list of `ShippingMethodOption`: `Code`, `Name`, `Cost`, `EstimatedDays`, `FreeOverThreshold`) and `SocialLinks`. Helpers: `DefaultShippingMethod` (first configured method, or a free "standard" fallback), `FindShippingMethod(code)` (case-insensitive), `ShippingCostFor(method, subtotalAfterDiscount)` (free when the method has `FreeOverThreshold` and the threshold is reached) and `FormatPrice` (symbol + `N2`, invariant culture). `appsettings.json` ships three methods: `standard` (4.90, free over threshold), `express` (9.90) and `pickup` (0).

### `SessionCartService`
Stores a JSON list of `SessionCartItem { ProId, Qty, Size, Color }` under session key `SessionCart`. Get/save/clear/count only; all business rules are in `CartService`.

### `CartService`
Single source of truth for cart contents and pricing; prices, discounts and shipping are always recomputed from the database.

- Works on the DB cart (`ShoppingCarts`) for signed-in users and on `SessionCartService` for guests. Line keys are `c{CartId}` (saved) or `s{index}` (session).
- `AddAsync` only accepts active products, quantity 1–99 (`MaxQuantityPerLine`), validates size/colour against the product's `Sizes`/`Colors` (`ValidateVariant`, case-insensitive, normalised to the stored spelling) and checks total quantity per product against stock. Same product + variant merges into one line.
- `UpdateQuantityAsync` (0 removes), `RemoveAsync`, `ClearAsync` (also removes the coupon), `MergeGuestCartAsync` (re-adds guest items through `AddAsync`, so stock rules apply, then clears the session cart).
- The applied coupon code is kept in session key `Cart.CouponCode`. Guests see "Sign in to use this coupon".
- `GetSummaryAsync(userId, shippingMethodCode)` returns a `CartSummary`: lines (flagged with an `Issue` when the product is missing, inactive, out of stock or over stock), `Subtotal` (available lines only), `Discount`, `ShippingMethod`, `ShippingCost` (0 for an empty cart), `Total = max(0, Subtotal − Discount) + ShippingCost`, and `AmountUntilFreeShipping`.

### `CouponService`
`Normalize` trims and upper-cases codes. `ValidateAsync(code, userId, subtotal)` checks existence/`IsActive`, `StartsAt`/`ExpiresAt` (UTC), total `UsageLimit` vs `TimesUsed`, `MinimumOrderAmount`, and `UsageLimitPerUser` (counted from the user's non-cancelled orders with that `CouponCode`). `CalculateDiscount` applies a percentage (capped at 100 %, rounded to 2 decimals) or fixed amount, then `MaxDiscountAmount`, and never exceeds the subtotal.

### `OrderService`
- `PlaceOrderAsync(userId, CheckoutInput)` – idempotent per `CheckoutToken` (re-submits return the existing order). Only `CashOnDelivery` is accepted; the shipping method must exist in `StoreSettings`. Rebuilds the cart summary and rejects empty carts, line issues and coupon errors. Inside an execution-strategy transaction it: decrements stock with a conditional `ExecuteUpdate` per product (fails if stock is insufficient), increments the coupon's `TimesUsed` (fails if the limit was just reached), creates the `Order` with `OrderDetails` snapshots (name, size, colour, unit price) and an initial "Order placed" `TrackingEvent`, optionally saves the address to `Addresses` (skips duplicates; first address becomes default), deletes the user's DB cart and commits. Then removes the session coupon.
- `RestockCancelledOrderAsync(orderId)` – returns quantities to stock and decrements the coupon's `TimesUsed`.
- `CancelByCustomerAsync(orderId, userId)` – allowed only before `Shipped`; sets `Cancelled`, `CancelledDate`, `PaymentStatus = Cancelled`, adds a tracking event and restocks, in one transaction.

### `CatalogService`
Storefront queries over active products (`IsActive = true`):

- `SearchAsync(CatalogFilter, userId)` – text search (name, description, brand, material, category name; max 100 chars), category including all descendants, price range, size, colour, in-stock, on-sale, minimum approved rating, brand facet (counts ignore the brand filter itself), sort (`newest` default, `price-asc`, `price-desc`, `rating`, `popular` = units in non-cancelled orders, `discount`) and paging (`CatalogFilter.DefaultPageSize` = 24). Also returns size/colour lists, price floor/ceiling and a category facet tree with counts.
- Home/product page lists: `GetNewArrivalsAsync`, `GetBestSellersAsync` (non-cancelled units; unsold products excluded), `GetOnSaleAsync`, `GetFeaturedAsync` (featured and in stock), `GetRelatedAsync` (same category or brand), `GetByIdsAsync`.
- `GetNavigationTreeAsync` – category tree ordered by `DisplayOrder` then name, cached 5 minutes in `IMemoryCache` under `Catalog.NavigationCacheKey`; `CategoriesController` evicts it on changes.
- `BuildCardsAsync` – `ProductCardModel` with approved-review average/count and wishlist state.

### `ImageUploadService`
Saves uploads to `wwwroot/Product` (`ProductFolder`) or `wwwroot/category` (`CategoryFolder`) as `{guid}.{ext}`. Max 5 MB. The type is detected from magic bytes (JPEG, PNG, GIF, WebP), never from the file name or content type. `Delete` ignores names containing path segments. Used by the admin `ProductsController` and `CategoriesController`.

### Email senders
- **`EmailSender`** reads `EmailSettings` (`SenderEmail`, `SenderPassword` with spaces stripped, `SmtpServer` default `smtp.gmail.com`, `SmtpPort` default 587, `SenderName`, `EnableSsl`) and sends HTML mail via `SmtpClient` (60 s timeout). Missing credentials throw `InvalidOperationException`; SMTP errors are logged and rethrown with Gmail-specific hints.
- **`DevelopmentEmailSender`** logs the recipient, subject and body at `Warning` level instead of sending.

### `EmailTemplateService`
Branded HTML layout (store name header, support email/phone/address footer from `StoreSettings`). `GenerateEmailConfirmationTemplate` (registration / resend confirmation) and `GenerateActionEmail` (single call-to-action emails such as password reset and email change; arguments are HTML-encoded here).

### `OrderEmailService`
Singleton that queues emails with fire-and-forget `Task.Run`, creating its own DI scope (fresh `ApplicationDbContext`, `IEmailSender`) for each send. `QueueOrderConfirmation` is called after checkout; `QueueStatusUpdate` after admin status changes (`notifyAdmin: false`) and customer cancellations (`notifyAdmin: true`, sent to `EmailSettings:AdminEmail`, falling back to `SenderEmail`). The recipient is `Order.Email`, falling back to the user's email. Failures are only logged; queued emails are lost if the process stops. `StatusLabel` maps statuses to customer wording (e.g. `Verified` → "Confirmed", `Completed` → "Delivered").

### `AdminAuditService`
Logs admin actions at `Warning` level with the `ADMIN_AUDIT` prefix (admin name/ID, IP, user agent, UTC timestamp). Helpers: `LogAdminActionAsync`, `LogAdminAccessAsync`, `LogAdminModificationAsync`, `LogAdminDeletionAsync`. Logs only; nothing is written to the database.

## SignalR hubs

Both hubs require authentication (`[Authorize]`).

- **`RealtimeHub`** (`/realtimeHub`) – on connect, adds the connection to `User_{userId}` and, for admins, `Admins`. `JoinOrderGroup(orderId)` joins `Order_{orderId}` only for the order owner or an admin. Controllers push through `IHubContext<RealtimeHub>`: `CartsController` sends `CartUpdated` to `User_{userId}`; the admin order status update sends `OrderStatusUpdated` to both `User_{userId}` and `Order_{orderId}` (`realtime-updates.js` ignores a repeat of the same order and status within 5 s, so a client in both groups shows one toast); a customer cancellation sends `OrderStatusUpdated` to `Admins`.
- **`ChatHub`** (`/chatHub`) – support chat: `SendMessage`, `JoinRoom`, `Typing`; groups `Room_{id}`, `Agents` and per-user groups.

## View components

There are none. The header data that used to come from `CartSummaryViewComponent` is now computed directly in `_Layout.cshtml`, and the mini-cart drawer body is loaded over AJAX from `CartsController.MiniCart` (partial `Views/Carts/_MiniCart.cshtml`).

## Layouts

### Public layout (`Views/Shared/_Layout.cshtml`)
Used by storefront views (`Views/_ViewStart.cshtml`) and by the Identity pages (`Areas/Identity/Pages/_ViewStart.cshtml` and the Manage `_Layout`). It injects `StoreSettings`, `CatalogService`, `CartService`, `ApplicationDbContext`, `SignInManager`/`UserManager` and `IAntiforgery`, and on every render loads the cached category tree, the cart item count and (signed in) the wishlist count. It renders:

- SEO head: title/description/canonical/Open Graph from `ViewData` (`Title`, `Description`, `Canonical`, `OgImage`, `OgType`, `NoIndex`, `BodyClass`), `schema.org` `OnlineStore` JSON-LD, the CSRF meta tag.
- Optional announcement bar (`Store:AnnouncementText`), header with quick search, account dropdown (admin link for the `Admin` role), wishlist and cart badges, category mega-menu (first 6 top-level categories), mobile offcanvas menu, mini-cart offcanvas.
- Footer with newsletter form (`Home/Subscribe`), store contact details, social links, help/company links.
- A toast stack fed from `TempData` keys `Success`/`SuccessMessage` and `Error`/`ErrorMessage`.
- Scripts: jQuery (`~/lib`), Bootstrap 5.3.3 bundle (CDN), `site.js`; for signed-in users also the SignalR client (CDN) and `realtime-updates.js`. Sections: `Head`, `Scripts`.

`Views/Orders/Invoice.cshtml` uses no layout (printable page).

### Admin layout (`Areas/Admin/Views/Shared/_LayoutAdmin.cshtml`)
Set by `Areas/Admin/Views/_ViewStart.cshtml`. Sidebar navigation grouped into Sales (Orders, Coupons, Reports), Catalog (Products, Categories, Reviews, Wishlists), Customers (Customers, Newsletter, Messages, Live chat) and Settings (Roles, Email test), with badges for orders in `Received` status and unapproved reviews (two count queries per render). Top bar with a "View store" link and the admin's name, the same `TempData` toast stack, and a small inline script for the mobile sidebar toggle. Loads `hikingstore.css` + `admin.css`, jQuery, Bootstrap and `site.js`. Sections: `Styles`, `Scripts`.

## Front-end assets

Both layouts load Bootstrap 5.3.3 and Font Awesome 6.5.2 from CDNs (with SRI hashes) and the Inter / Barlow Condensed Google Fonts.

- **`wwwroot/css/hikingstore.css`** – the design system, layered on Bootstrap. `:root` defines `--hs-*` tokens: palette (`--hs-forest`, `--hs-forest-deep`, `--hs-olive`, `--hs-sage`, `--hs-beige`, `--hs-sand`, `--hs-charcoal`, `--hs-gray`, `--hs-sale`, status colours, `--hs-line`), typography (`--hs-font-body`, `--hs-font-display`, `--hs-fs-xs` … `--hs-fs-3xl`), spacing (`--hs-space-1` … `--hs-space-8`), radii, shadows, `--hs-container` (1320px), `--hs-header-h` and `--hs-transition`. It maps Bootstrap variables (`--bs-primary`, `--bs-link-color`, `--bs-border-radius`, button variables, …) onto these tokens.
- **`wwwroot/css/admin.css`** – admin shell (sidebar, top bar, content) built on the same tokens.
- **`wwwroot/js/site.js`** – shared helpers, exposed as `window.HS = { toast, setCartCount, setWishlistCount, openMiniCart, loadMiniCart }`:
  - wraps `fetch` and jQuery AJAX to add `X-CSRF-TOKEN` to same-origin state-changing requests;
  - `HS.toast(message, type, link)` – success/error toasts in `[data-toast-stack]` (auto-dismiss after 5 s, errors 9 s);
  - `form[data-add-to-cart]` – AJAX add to cart, updates `[data-cart-count]` badges (`data-add-to-cart="checkout"` redirects to the cart);
  - `[data-wishlist-toggle]` – AJAX `/Wishlist/Toggle`, redirects guests to login;
  - `form[data-confirm]` – `window.confirm` before submit;
  - `form[data-once]` – blocks double submission and shows a busy spinner on the submit button (skipped while jQuery validation fails);
  - quantity steppers (`[data-step]`), `[data-auto-submit]`, password show/hide (`[data-toggle-password]`), mega-menu, mini-cart loader (`/Carts/MiniCart`) and quick-search suggestions (`/Home/QuickSearch`, 250 ms debounce, keyboard navigation).
- **`wwwroot/js/realtime-updates.js`** – connects to `/realtimeHub`, updates cart badges on `CartUpdated`, shows a toast on `OrderStatusUpdated` (and reloads an open order details page), exposes `window.realtimeUpdates.joinOrderGroup/leaveOrderGroup`.
- **Upload folders** – `wwwroot/Product/` (product images) and `wwwroot/category/` (category images).
- `wwwroot/lib/` holds jQuery and jQuery Validation (+ unobtrusive). Other files in `wwwroot/css`, `wwwroot/js` and `wwwroot/AdminTheme` (e.g. `style.css`, `responsive.css`, `munchbox.js`, the SB Admin theme) are not referenced by either layout or any view.
