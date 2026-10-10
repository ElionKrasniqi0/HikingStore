# Customer features

This page describes what a shopper can do on the storefront and how each feature works behind the scenes. Controllers live in `Controllers/`, business logic in `Services/`, views in `Views/`. Store-wide values (name, currency, shipping methods, VAT rate, free-shipping threshold, return days, contact details) come from the `Store` section of `appsettings.json`, bound to `StoreSettings`.

All prices are in euros and formatted by `StoreSettings.FormatPrice` (e.g. `€4.90`).

## Header, navigation and footer

Rendered by `Views/Shared/_Layout.cshtml` on every storefront page.

- **Announcement bar** – shows `Store:AnnouncementText` when set.
- **Search box** – a GET form to `/Home/Shop?q=...` with autocomplete (see [Search](#search)).
- **Account menu** – for signed-in users: My account, Orders & tracking, Wishlist, Support chat, **Admin dashboard** (only for the `Admin` role) and Sign out. Guests see **Sign in**.
- **Wishlist and cart badges** – item counts computed server-side on each page load and updated live by JavaScript (`window.HS.setCartCount` / `setWishlistCount` in `wwwroot/js/site.js`).
- **Mini cart** – clicking the cart icon opens an off-canvas panel that loads `GET /Carts/MiniCart` (partial `_MiniCart.cshtml`). On `/Carts...` pages the icon is a normal link.
- **Category bar / mega menu** – "Shop all", "New in" (`sort=newest`), the first 6 top-level categories, "Best sellers" (`sort=popular`) and "Sale" (`onSale=true`). A top-level category with subcategories opens a mega menu listing its children plus the category photo; it opens on hover (pointer devices) or click, and closes on Escape, focus loss or an outside click. Mobile uses an off-canvas menu listing every category and subcategory.
- The category tree comes from `CatalogService.GetNavigationTreeAsync`, cached in memory for 5 minutes. Any POST in the admin `CategoriesController` clears the cache.
- **Footer** – newsletter form, store address/phone/email, social links (`Store:SocialLinks`, only non-empty ones), Shop/Help/Company links, "We accept: Cash on delivery" and "Prices include VAT."
- **Toasts** – `TempData["Success"]` / `TempData["Error"]` are rendered as toasts; JavaScript code shows the same toasts through `window.HS.toast`.

## Homepage

`GET /` → `HomeController.Index` builds a `HomeViewModel` entirely from the database:

| Section | Source |
| --- | --- |
| Hero | `Store:HeroImageUrl` background; buttons to new arrivals and to the sale (or "Browse all gear" when nothing is on sale) |
| Benefits strip | Free-shipping threshold, return days, cash on delivery, support phone from `StoreSettings` |
| Shop by category | First 6 top-level categories (photo from `/category/{file}`) |
| New arrivals | 8 newest active products |
| Promo tiles | Best sellers, plus Sale (or "In stock now" when nothing is on sale) |
| Best sellers | Up to 8 active products by units sold in non-cancelled orders; products with no sales are left out |
| Trail essentials | Up to 4 products marked **Featured** that are in stock |
| On sale now | Up to 8 products with `CompareAtPrice > Price`, biggest discount first |
| What hikers are saying | 3 newest approved reviews with 4+ stars on active products |

Empty sections are hidden. Product cards (`_ProductCard.cshtml`) show image (second image on hover), Sold out / −x% / New (created in the last 30 days) badges, brand, price and was-price, average rating from approved reviews, number of colours and a wishlist heart.

## Catalog (shop)

`GET /Home/Shop` → `CatalogService.SearchAsync`. All filtering, sorting and paging happen in SQL. Only active products (`IsActive`) are listed.

| Query parameter | Effect |
| --- | --- |
| `q` | Matches product name, description, brand, material or category name (trimmed, max 100 characters) |
| `category` | That category **and its subcategories**; an unknown ID is ignored |
| `minPrice`, `maxPrice` | Price range; negative values are dropped, swapped if min > max |
| `brand` (repeatable) | Any of the selected brands (max 20) |
| `size`, `color` | Products whose comma-separated size/colour list contains the value |
| `inStock=true` | Stock > 0 |
| `onSale=true` | `CompareAtPrice > Price` |
| `rating` (1–5) | Average of approved reviews ≥ value |
| `sort` | `newest` (default), `popular` (units sold), `rating`, `price-asc`, `price-desc`, `discount`; unknown values fall back to `newest` |
| `page` | 24 products per page; clamped to the last page |

The page title depends on the filter: the category name and description, "Results for “q”", "Sale", "Best sellers" or "Shop all gear". The sidebar (and a mobile filter drawer) is `_ShopFilters.cshtml`:

- Category tree with product counts (counts cover all active products, not the current filter).
- Brand checkboxes with counts that ignore the brand filter itself, so shoppers can widen their selection.
- Size and colour chips collected from all active products; price inputs with the catalog's min/max as placeholders.
- Checkbox, radio and sort changes auto-submit the form. Active filters are shown as removable chips with a "Clear all" link. Filtered and search result pages get `noindex`.

Legacy URLs still work: `/Home/Product?page=N` and `/Home/ProductCategory/{id}` permanently redirect (301) to `/Home/Shop`, and `/Home/Search?query=` redirects to `/Home/Shop?q=` (or to the plain shop when empty).

## Search

- **Header autocomplete** – after 2+ characters and a 250 ms pause, `site.js` calls `GET /Home/QuickSearch?term=...`. The server returns up to 6 active products matching name or brand (in-stock first) and up to 4 categories matching the name, as `{ results: [{ text, type, price, image, url }] }`. The list is keyboard-navigable (arrows, Enter, Escape) and always ends with a "See all results" link to `/Home/Shop?q=`.
- **Full search** – submitting the header form opens the catalog with `q` (see above).

## Product detail page

`GET /Home/ProductDetailes/{id}` (the action name is spelled this way in code). Inactive or unknown products return 404.

- **Breadcrumb** – Home › Shop › category ancestors (up to 5 levels) › product.
- **Gallery** – main image (`ProImage`) followed by additional images (`AdditionalImages`); thumbnails swap the main image client-side. Images are served from `/Product/{file}`.
- **Price** – current price, was-price and saving when on sale; "Price includes VAT" and the free-shipping threshold.
- **Variants** – colour and size are radio chips built from the product's comma-separated `Colors` / `Sizes`. A choice is required when options exist; a single option is pre-selected. The server re-validates the choice (`CartService.ValidateVariant`, case-insensitive) and rejects unknown values.
- **Stock** – "In stock", "Only N left" (≤ 5) or "Out of stock". The quantity stepper is limited to `min(stock, 99)`.
- **Add to cart** – the form posts to `/Carts/AddToCart` via `fetch` (JSON response, toast with a "View cart" link). Without JavaScript it posts normally and redirects. On mobile, a sticky buy bar appears when the form scrolls out of view.
- **Wishlist** – heart button (see [Wishlist](#wishlist)). Sold-out products show a "Save" button instead of Add to cart.
- **Info** – every configured shipping method with its cost and delivery estimate, return policy and cash on delivery.
- **Description and specifications** – specifications come from `Specifications` lines in `Key: Value` form, plus brand and material.
- **Related products** – up to 8 active products from the same category or brand.
- **Recently viewed** – the last 12 viewed product IDs are kept in session (`RecentlyViewed`); up to 6 others are shown.
- **SEO** – JSON-LD `Product` with offer, availability and aggregate rating; Open Graph tags; canonical URL.

### Reviews

- Only **approved** reviews are shown, newest first, with average rating, 1–5 star breakdown and a "Verified purchase" badge.
- **Purchase-required gate.** The review form is shown only to signed-in users who have the product in one of their own **non-cancelled** orders and have not reviewed it yet. Otherwise the box shows the reason: sign in and purchase, purchase required, or already reviewed.
- `POST /Reviews/Create` (`ReviewsController`, `[Authorize]`) repeats every check on the server: active product, valid input (rating 1–5, title 3–100 characters, comment 10–2000 characters), purchase in a non-cancelled order and no previous review. A unique index on (product, user) also prevents duplicates.
- New reviews are saved with `IsApproved = false`. `IsVerifiedPurchase` is set when the user has a **Completed** order containing the product.
- **Admin moderation** – under `/Admin/Reviews` (pending / approved / all), admins can Approve, Unpublish or Delete reviews. Each action is audit-logged.
- Customers see their own reviews, labelled "Published" or "Awaiting moderation", at `/Account/Reviews` and can delete them (`POST /Reviews/Delete/{id}`).

## Wishlist

Requires sign-in (`WishlistController` is `[Authorize]`).

- Heart buttons on product cards and the product page call `POST /Wishlist/Toggle` (`productId`), which adds or removes the product and returns `{ success, inWishlist, message, count }`. Guests are sent to the login page by the client script first.
- Only active products can be added. A unique index on (user, product) prevents duplicates.
- `/Wishlist` shows saved products as product cards, newest first. Products an admin has hidden are left out of the page and the wishlist counts, and reappear if the product is reactivated. Products in stock that have no sizes and at most one colour get a direct **Add to cart** button; others get **Choose options**. Unsaving a card on this page removes it immediately.

## Cart

`CartsController` and `CartService` (the single source of truth for cart contents and prices). The cart works for guests and signed-in users.

| | Guest | Signed-in user |
| --- | --- | --- |
| Storage | Session key `SessionCart` (cookie `.HikingStore.Session`, 7-day idle timeout), via `SessionCartService` | `ShoppingCarts` table, unique per (user, product, size, colour) |
| Line key | `s{index}` | `c{CartId}` |

Rules applied on every add or update:

- Quantity per line is 1–99. The product must be active, and size/colour must be valid options.
- The total quantity of a product across all its variant lines may not exceed its stock.
- Adding the same product and variant again increases the existing line.
- Setting a quantity of 0 removes the line.

Every cart view recalculates prices from the database. A line is flagged with an issue when the product is no longer available, is out of stock, or has less stock than the line quantity. Flagged lines are excluded from the subtotal, and checkout is blocked until they are fixed.

Cart actions (`AddToCart`, `UpdateCartItem`, `RemoveFromCart`, `RemoveAll`) return JSON for AJAX requests (`X-Requested-With: XMLHttpRequest` or `Accept: application/json`): `{ success, message, cartItemCount, ... }`. Otherwise they set a toast message and redirect. For signed-in users each action also pushes a `CartUpdated` SignalR event (see [Real-time](realtime.md)).

The cart page (`/Carts`) shows quantity steppers (auto-submit on change), Remove, Empty cart (with confirmation), a free-shipping progress meter, the summary (subtotal, discount, shipping, total, "Includes VAT") and the coupon box. Guests see "Sign in to check out" and "create an account" links that return to `/Carts/Checkout`.

### Guest cart merge on sign-in

`CartService.MergeGuestCartAsync(userId)` replays each session line through `AddAsync` for the user, so stock and variant rules apply (lines that fail are dropped), then clears the session cart. It is called from:

- `Areas/Identity/Pages/Account/Login.cshtml.cs`, after a successful email/password sign-in;
- `LoginWith2fa.cshtml.cs` and `LoginWithRecoveryCode.cshtml.cs`, after a successful two-factor sign-in;
- `Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs`, after an external (Google) sign-in with an already linked login, and after linking the external login to an existing account.

## Coupons

- Applying a coupon requires sign-in (`POST /Carts/ApplyCoupon`, field `code`). Codes are trimmed and upper-cased. The applied code is stored in session (`Cart.CouponCode`) and removed with `POST /Carts/RemoveCoupon`.
- `CouponService.ValidateAsync` checks, in order: the code exists and is active, the start date has passed, it has not expired, the total usage limit, the minimum order amount (against the cart subtotal) and the per-user limit (counting the user's non-cancelled orders that used the code).
- **Discount** – a percentage of the subtotal (capped at 100% and rounded to cents) or a fixed amount. It is further capped by `MaxDiscountAmount` and never exceeds the subtotal.
- The coupon is re-validated every time the cart summary is built. If it becomes invalid the error is shown in the cart and checkout is refused until it is removed. Guests holding a code in session see "Sign in to use this coupon."
- Usage is counted at checkout and released again when an order is cancelled (see below).

## Checkout

`GET /Carts/Checkout` requires sign-in (`[Authorize]`; guests go to `/Identity/Account/Login`). An empty cart redirects back to the cart with an error.

The form (`Views/Carts/Checkout.cshtml`, view model `CheckoutViewModel`) has four sections:

1. **Contact** – email and phone, prefilled from the account.
2. **Shipping address** – full name, street, city, postal code (optional), country (Kosovo, Albania, North Macedonia, Montenegro) and order notes (max 500 characters). The default saved address, or otherwise the first one, is prefilled, and saved-address chips fill the form. A "Save this address to my account" box is ticked when the user has no saved addresses.
3. **Delivery method** – radio list built from `Store:ShippingMethods`. Changing it reloads the order summary from `GET /Carts/CheckoutSummary?shippingMethod=` (a server-rendered partial).
4. **Payment** – only **Cash on delivery** (a hidden `PaymentMethod=CashOnDelivery` field). The page states that online card payments are not available.

### Shipping methods

Configured in `Store:ShippingMethods` and `Store:FreeShippingThreshold` (100):

| Code | Name | Cost | Free over threshold | Estimate |
| --- | --- | --- | --- | --- |
| `standard` | Standard Shipping | €4.90 | Yes – free when the subtotal **after discount** is ≥ €100 | 3–5 business days |
| `express` | Express Shipping | €9.90 | No | 1–2 business days |
| `pickup` | Store Pickup (Prishtinë) | €0 | – | Ready next business day |

The first method in the list is the default. `StoreSettings.ShippingCostFor` computes the cost, and an empty cart has no shipping cost.

### Totals and VAT

Totals are always computed on the server by `CartService.GetSummaryAsync`:

```
Subtotal = Σ unit price × quantity   (available lines only, prices read from the database)
Total    = max(0, Subtotal − Discount) + ShippingCost
```

Prices are **VAT-inclusive**. VAT is never added to the total and is not stored on the order. The cart and checkout summaries only show the label "Includes VAT". The invoice shows the VAT contained in the order total, computed with `Store:VatRate` (0.18) as `Total − Total / (1 + VatRate)`.

### Placing the order

`POST /Carts/Checkout` validates the `CheckoutInput` model and calls `OrderService.PlaceOrderAsync`:

1. **Double-submit protection.** `GET /Carts/Checkout` puts a fresh `CheckoutToken` (GUID) in the form.
   - If an order with the same token already exists for the user, its ID is returned instead of creating a new order.
   - A unique filtered index on `Orders.CheckoutToken` catches concurrent duplicates; the controller handles the `DbUpdateException` by redirecting to the existing order.
   - On the client, the form has `data-once`, so `site.js` ignores a second submit and shows a busy button.
2. It rejects an empty token, a payment method other than `CashOnDelivery`, an unknown shipping method, an empty cart, cart lines with issues, and an invalid coupon.
3. **One database transaction** (inside the EF execution strategy):
   - For each product, the stock is decremented with a conditional `UPDATE ... WHERE IsActive AND StockQuantity >= qty`. If no row is updated, the transaction rolls back with a "just sold out" message.
   - If a discount applies, the coupon's `TimesUsed` is incremented under the same usage-limit condition (rolls back if the limit was just reached).
   - The `Order` is created: contact and address fields, subtotal, discount, shipping cost, total, coupon code, `PaymentMethod = CashOnDelivery`, `PaymentStatus = Pending`, `Status = Received`, and `Carrier`/`ShippingService` set to the shipping method name. One `OrderDetail` is created per cart line, with product name, variant, quantity and unit price snapshot, plus a first tracking event "Order placed".
   - When "Save this address" is ticked and the address (street + city) is not saved yet, an `Address` labelled "Home" is added. It becomes the default when it is the user's first address.
   - The user's cart rows are deleted and the transaction is committed.
4. The coupon is removed from session, a `CartUpdated` event with count 0 is pushed, the order-confirmation email is queued, and the browser is redirected to `/Carts/OrderConfirmation/{id}`.

If anything fails, the form is shown again with the errors and a freshly calculated summary.

### Order confirmation page

`GET /Carts/OrderConfirmation/{id}` (`[Authorize]`, own orders only; otherwise 404) shows:

- the order number (`ORD` + 6-digit ID, e.g. `ORD000123`);
- items, totals and the amount to pay, labelled "Total to pay on delivery" or "Total to pay at pickup";
- the delivery address, or for pickup the store address and opening hours;
- payment method and "What happens next" steps.

Pickup is detected by matching the order's `ShippingService` with the name of the configured `pickup` method.

## My orders

`OrdersController` (file `Controllers/OrderController.cs`, `[Authorize]`). Every query is filtered by the signed-in user's ID, so another user's order returns 404.

| URL | Description |
| --- | --- |
| `/Orders?status=&page=` | Own orders, newest first, 10 per page, with status filter chips and per-status counts |
| `/Orders/Details/{id}` | Status progress (Received → Confirmed → Being prepared → Shipped → Delivered, or Cancelled), estimated delivery (Processing/Shipped), carrier and tracking number, tracking history, items, totals, address, payment method and status, notes. Delivered orders show "Write a review" links |
| `/Orders/Invoice/{id}` | Stand-alone printable invoice (no layout) with a "Print / save as PDF" button and the VAT line |
| `/Orders/Invoice/{id}?download=true` | Same invoice served as an attachment `Invoice_ORD000123.html` |
| `POST /Orders/CancelOrder/{id}` | Customer cancellation (see below) |

The details page joins the SignalR order group so it reloads automatically when an admin changes the status (see [Real-time](realtime.md)).

### Customer cancellation

The **Cancel order** button is shown while `Order.CanBeCancelledByCustomer` is true, i.e. the status is Received, Verified or Processing. `OrderService.CancelByCustomerAsync` runs in a transaction:

1. Loads the order with an ownership check and re-checks that it can be cancelled.
2. Sets `Status = Cancelled`, `CancelledDate` and `PaymentStatus = Cancelled`, and adds an "Order cancelled by customer" tracking event.
3. **Restocks** every product by the ordered quantity and decrements the coupon's `TimesUsed` (`RestockCancelledOrderAsync`).

On success the controller pushes `OrderStatusUpdated` to the `Admins` SignalR group and queues a status-update email to the customer with a copy to the admin address. It returns JSON `{ success, message }` for AJAX requests; otherwise it redirects to the details page with a toast.

## Emails

Order emails are sent by `OrderEmailService` (singleton). Each email is built and sent on a background task in its own DI scope; failures are logged and never affect the request. The recipient is `Order.Email`, falling back to the account email. The HTML body contains the item table, subtotal, discount, shipping, total, shipping address and a "View your order" link to `/Orders/Details/{id}`.

| Email | Trigger | Subject |
| --- | --- | --- |
| Order confirmation | Successful checkout | `Order confirmation – #ORD000123` |
| Status update | Admin changes the order status (`Admin/OrdersController.UpdateStatus`, only when the status actually changes) | `Your order #ORD000123 is now {label}` with a status-specific message (tracking number when shipped, estimated delivery when processing) |
| Status update + admin copy | Customer cancels an order | Same as above to the customer, plus `[Admin] Order #ORD000123 → Cancelled` to `EmailSettings:AdminEmail` (or `SenderEmail` if empty) |

Status labels: Received, Confirmed (Verified), Being prepared (Processing), Shipped, Delivered (Completed), Cancelled.

Account emails are sent by the Identity pages using `EmailTemplateService`: account confirmation (register, resend, external-login sign-up), password reset, and confirmation of an email change. Admin replies to contact messages are also emailed. In Development, when `EmailSettings:SenderPassword` is empty, `DevelopmentEmailSender` writes emails to the log instead of sending them.

## Account area

**Customer pages** – `AccountController` (`[Authorize]`), with a shared side menu (`_AccountNav.cshtml`):

| URL | Description |
| --- | --- |
| `/Account` | Dashboard: greeting, counts (orders, wishlist, addresses), the 3 latest orders, shortcuts to profile and security |
| `/Account/Profile` | Edit first name, last name and phone. Email is read-only here and links to the Identity email page |
| `/Account/Addresses` | Saved addresses with Edit, Make default and Delete |
| `/Account/AddressForm/{id?}` | Create or edit an address (default country Kosovo). The first address is always the default; setting a default clears the others; deleting the default promotes the oldest remaining address |
| `/Account/Reviews` | The user's reviews with moderation status and Delete |

The side menu also links to `/Orders`, `/Wishlist`, `/Chat` and **Sign-in & security** (`/Identity/Account/Manage/ChangePassword`).

**Identity pages** – the scaffolded ASP.NET Core Identity Razor Pages under `Areas/Identity/Pages/Account/`, restyled for the store:

- **Register** – first and last name, email and password. Passwords need at least 8 characters with a digit, a lowercase letter, an uppercase letter, a symbol and 2 unique characters. New users get the `User` role and a confirmation email; signing in is blocked until the email is confirmed.
- **Login** – email and password, or Google when `Authentication:Google:ClientId`/`ClientSecret` are set. Accounts lock for 15 minutes after 5 failed passwords. `RateLimitingMiddleware` limits login POSTs to 5 per IP per 15 minutes and registration POSTs to 3 per IP per hour (HTTP 429). The auth cookie lasts 30 days with sliding expiration.
- **Other pages** – forgot/reset password, resend confirmation, two-factor sign-in with an authenticator app, recovery codes, external logins, change email or password, and download/delete personal data under `/Identity/Account/Manage/...`. `/Identity/Account/Manage` itself redirects to `/Account/Profile`.

## Support chat

Requires sign-in (`ChatController`).

1. `/Chat` lists the user's conversations, newest activity first, with a status ("Waiting for reply", "In progress", "Resolved", "Closed"), the last message and a count of unread support messages.
2. **Start chat** (`POST /Chat/CreateRoom`, `subject` up to 150 characters, default "General question") creates an `Open` room and opens it.
3. `/Chat/Room/{id}` (owner only; other users get 403) marks support messages as read and connects to `/chatHub`. It supports live messages (max 2000 characters) and a "Support is typing…" indicator.
4. **Close chat** (`POST /Chat/CloseRoom`) sets the room to `Closed`. Sending a message in a closed or resolved room reopens it as `InProgress`.

Admins answer from `/Admin/Chat`. See [Real-time](realtime.md) for the hub protocol.

## Contact form

`GET /Home/Contact?subject=` (an optional subject prefill, max 150 characters) shows the form, the store's email, phone, address and opening hours, and a subject list (Product question, Order question, Shipping question, Return request, Damaged or wrong item, Service request, Other).

`POST /Home/Contact`:

- Validates name (max 100), email (max 256), subject (max 150, defaults to "General question") and a required message (max 2000).
- Saves a `Contact` record and redirects back with a thank-you toast.
- Admins read and reply under **Messages** (`/Admin/Home/ShowMessage`); replies are sent by email.

## Newsletter

The footer form posts to `POST /Home/Subscribe` (`email`, `returnUrl`).

- The address is validated (max 256 characters), lower-cased and saved to `NewsletterSubscribers` unless it already exists. A unique index guards against races.
- The user is redirected back to the local `returnUrl` with a toast.
- There is no self-service unsubscribe; admins manage and export the list under `/Admin/Reports/Newsletter`.

## Information pages

All served by `HomeController` from `Views/Home/` and sharing the help side menu `_HelpNav.cshtml`. Their text uses `StoreSettings` values (shipping methods and costs, threshold, return days, address, hours).

| URL | Page |
| --- | --- |
| `/Home/Shipping` | Shipping & delivery: methods, costs, cash on delivery |
| `/Home/Returns` | Returns & refunds |
| `/Home/Faq` | FAQ grouped by ordering & payment, delivery, returns & cancellations, products & account |
| `/Home/Contact` | Contact form (above) |
| `/Home/Services` | Store services |
| `/Home/About` | About us |
| `/Home/Privacy` | Privacy policy |
| `/Home/Terms` | Terms & conditions (prices include VAT; cash on delivery only) |

## Error pages

- Status codes are re-executed through `/Error/{statusCode}`: 404 renders `Views/Error/NotFound.cshtml`; 401, 403, 500 and other codes render `Views/Error/Error.cshtml` with a matching message.
- In non-Development environments, unhandled exceptions go to `/Error`. Exception details are shown only when `ASPNETCORE_ENVIRONMENT` is `Development`.
- Access-denied redirects from the auth cookie go to `/Error/AccessDenied` (status 403).
