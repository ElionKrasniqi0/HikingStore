# Routes and endpoints

Routing is configured in `Program.cs`:

| Name | Pattern |
| --- | --- |
| `areas` | `{area:exists}/{controller=Home}/{action=Index}/{id?}` |
| `default` | `{controller=Home}/{action=Index}/{id?}` |
| Razor Pages | `MapRazorPages()` – Identity UI under `/Identity/...` |
| SignalR | `/chatHub` (`ChatHub`), `/realtimeHub` (`RealtimeHub`) |

Everything uses conventional routing except `ErrorController`, which uses attribute routes. Query-string parameters are shown as `?name=`. Form fields are listed in parentheses.

**Auth column**

- **Public** – anyone.
- **User** – any signed-in user (`[Authorize]`). Anonymous requests are redirected to `/Identity/Account/Login`.
- **Admin** – `[Authorize(Roles = "Admin")]`. Signed-in non-admins are redirected to `/Error/AccessDenied`.

**HTTP methods and antiforgery.** Actions marked `[HttpGet]` or `[HttpPost]` are listed with that method. Actions without a verb attribute accept any method; they are listed as GET because that is how the app calls them. `AutoValidateAntiforgeryTokenAttribute` is registered globally, so **every non-GET request to a controller needs an antiforgery token**, either as a form field or in the `X-CSRF-TOKEN` header. `site.js` adds the header to all same-origin `fetch` and jQuery requests, using the `csrf-token` meta tag. Razor Pages validate the token on POST by default.

## Storefront (`Controllers/`)

### `HomeController`

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/`, `/Home`, `/Home/Index` | Public | Homepage (`HomeViewModel`) |
| GET | `/Home/Shop?q=&category=&minPrice=&maxPrice=&brand=&size=&color=&inStock=&onSale=&rating=&sort=&page=` | Public | Catalog page (`CatalogViewModel`, 24 per page) |
| GET | `/Home/Product?page=` | Public | 301 redirect to `/Home/Shop` (legacy) |
| GET | `/Home/ProductCategory/{id}` | Public | 301 redirect to `/Home/Shop?category={id}` (legacy) |
| GET | `/Home/Search?query=` | Public | Redirect to `/Home/Shop?q=` (or `/Home/Shop` if empty) |
| GET | `/Home/QuickSearch?term=` | Public | JSON `{ results: [{ text, type, price, image, url }] }` (empty below 2 characters) |
| GET | `/Home/ProductDetailes/{id}` | Public | Product page (`ProductDetailsViewModel`); 404 if missing or inactive |
| GET | `/Home/Contact?subject=` | Public | Contact form |
| POST | `/Home/Contact` (`Name`, `Email`, `Subject`, `Message`) | Public | Redirect to `/Home/Contact` with a toast, or the form with errors |
| POST | `/Home/Subscribe` (`email`, `returnUrl`) | Public | Local redirect to `returnUrl` (or `/`) with a toast |
| GET | `/Home/About`, `/Home/Services`, `/Home/Shipping`, `/Home/Returns`, `/Home/Faq`, `/Home/Privacy`, `/Home/Terms` | Public | Information pages |

### `CartsController`

The cart works for guests (session) and signed-in users (database). `lineKey` is `c{CartId}` for a saved line or `s{index}` for a session line. "Cart JSON" means `{ success, message, cartItemCount, subtotal, total }`, returned when the request has `X-Requested-With: XMLHttpRequest` or `Accept: application/json`; otherwise the action sets a toast and redirects to `/Carts`.

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Carts` | Public | Cart page (`CartSummary`) |
| GET | `/Carts/MiniCart` | Public | Partial `_MiniCart` (off-canvas mini cart) |
| GET | `/Carts/GetCartCount` | Public | JSON `{ count }` |
| POST | `/Carts/AddToCart` (`proId`, `qty`=1, `size`, `color`, `returnUrl`) | Public | AJAX: JSON `{ success, message, cartItemCount }`. Otherwise redirect to `/Carts`, or to the local `returnUrl` on failure |
| POST | `/Carts/UpdateCartItem` (`lineKey`, `quantity`; 0 removes) | Public | Cart JSON or redirect |
| POST | `/Carts/RemoveFromCart` (`lineKey`) | Public | Cart JSON or redirect |
| POST | `/Carts/RemoveAll` | Public | Cart JSON or redirect (also removes the coupon) |
| POST | `/Carts/ApplyCoupon` (`code`) | Public (a guest gets an error toast) | Redirect to `/Carts` |
| POST | `/Carts/RemoveCoupon` | Public | Redirect to `/Carts` |
| GET | `/Carts/Checkout` | User | Checkout form (`CheckoutViewModel`); redirects to `/Carts` if the cart is empty |
| POST | `/Carts/Checkout` (`Input.*` fields of `CheckoutInput`, including `Input.CheckoutToken` and `Input.PaymentMethod`) | User | Cash: redirect to `/Carts/OrderConfirmation/{id}`. Card: redirect to the provider's hosted page, or to `/Payments/Status/{id}` if it can't be reached. Or the form with errors |
| GET | `/Carts/CheckoutSummary?shippingMethod=` | User | Partial `_OrderSummary` with recalculated totals |
| GET | `/Carts/OrderConfirmation/{id}` | User (own order, else 404) | Confirmation page; unpaid card orders redirect to `/Payments/Status/{id}` |
| GET | `/Carts/RequireLogin?returnUrl=` | Public (`[AllowAnonymous]`) | Redirect to `/Identity/Account/Login` (default `returnUrl` is `/Carts`) |

### `PaymentsController`

See [Card payments](payments.md). Returning to the store never changes an order's payment state.

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Payments/Status/{id}?result=success\|cancelled` | User (own, else 404) | Payment status page (confirming / awaiting payment / cancelled / needs attention). Redirects to `/Carts/OrderConfirmation/{id}` once paid |
| GET | `/Payments/StatusJson/{id}` | User (own, else 404) | JSON `{ paymentStatus, cancelled, failureReason }`, `Cache-Control: no-store` (polled by the status page) |
| POST | `/Payments/Pay/{id}` | User (own) | Opens a new hosted session (closing the previous one) and redirects to the provider. On error, redirects to the status page with the message |
| POST | `/Payments/Cancel/{id}` | User (own) | Closes the provider session, cancels and restocks the unpaid order, and puts the items back in the cart. Redirects to `/Carts` |
| POST | `/payments/webhooks/{provider}` (`stripe` or `hostedgateway`) | Anonymous; no antiforgery check; provider signature required; body ≤ 512 KB | 200 when applied, duplicate or ignored; 400 for an invalid signature; 404 for an unknown or unconfigured provider |

### `OrdersController` (`Controllers/OrderController.cs`, `[Authorize]`)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Orders?status=&page=` | User | Own orders, 10 per page, with status counts |
| GET | `/Orders/Details/{id}` | User (own, else 404) | Order details with tracking history |
| GET | `/Orders/Invoice/{id}?download=` | User (own, else 404) | Printable invoice (no layout); with `download=true`, served as attachment `Invoice_{OrderNumber}.html` |
| POST | `/Orders/CancelOrder/{id}` | User (own) | AJAX: JSON `{ success, message }`. Otherwise redirect to `/Orders/Details/{id}` with a toast |

### `AccountController` (`[Authorize]`)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Account` | User | Account dashboard |
| GET | `/Account/Profile` | User | Profile form |
| POST | `/Account/Profile` (`FirstName`, `LastName`, `PhoneNumber`) | User | Redirect to `/Account/Profile`, or the form with errors |
| GET | `/Account/Addresses` | User | Saved addresses |
| GET | `/Account/AddressForm/{id?}` | User (own, else 404) | New or edit address form |
| POST | `/Account/AddressForm/{id?}` (`Address` fields) | User (own) | Redirect to `/Account/Addresses` |
| POST | `/Account/DeleteAddress/{id}` | User (own, else 404) | Redirect to `/Account/Addresses` |
| POST | `/Account/SetDefaultAddress/{id}` | User (own, else 404) | Redirect to `/Account/Addresses` |
| GET | `/Account/Reviews` | User | Own reviews |

### `ReviewsController` (`[Authorize]`)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| POST | `/Reviews/Create` (`ProId`, `Rating`, `Title`, `Comment`) | User who bought the product | Redirect to `/Home/ProductDetailes/{id}#reviews` with a toast. The review awaits approval |
| POST | `/Reviews/Delete/{id}` | User (own review, else 404) | Redirect to `/Account/Reviews` |

### `WishlistController` (`[Authorize]`)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Wishlist` | User | Saved products as product cards |
| POST | `/Wishlist/Toggle` (`productId`) | User | JSON `{ success, inWishlist, message, count }` |
| POST | `/Wishlist/AddToWishlist` (`productId`) | User | JSON (same shape as `Toggle`, or `{ success, message, count }` if already saved) |
| POST | `/Wishlist/RemoveFromWishlist` (`productId`) | User | XHR: JSON `{ success, message, count }`. Otherwise redirect to `/Wishlist` |
| GET | `/Wishlist/GetWishlistCount` | User | JSON `{ count }` |

The current views and scripts only call `Toggle`. `AddToWishlist`, `RemoveFromWishlist`, `GetWishlistCount`, `/Carts/GetCartCount` and `/Carts/RequireLogin` are not referenced by any view or script.

### `ChatController` (`[Authorize]`)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/Chat` | User | Own chat rooms |
| GET | `/Chat/Room/{id}` | User (room owner; another user gets 403, unknown room 404) | Chat room; marks support messages read |
| POST | `/Chat/CreateRoom` (`subject`) | User | Redirect to `/Chat/Room/{newId}` |
| POST | `/Chat/CloseRoom` (`id`) | User (owner, else 404) | Redirect to `/Chat` |

### `ErrorController` (attribute routes)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| Any | `/Error` | Public | Exception handler page (production). Details only in Development |
| Any | `/Error/{statusCode}` | Public | `NotFound` view for 404; `Error` view for 401, 403, 500 and others |
| Any | `/Error/NotFound` | Public | 404 page |
| Any | `/Error/AccessDenied` | Public | 403 page (cookie `AccessDeniedPath`) |

### `TestEmailController` (`[Authorize(Roles = "Admin")]`, no area)

| Method | Route | Auth | Returns |
| --- | --- | --- | --- |
| GET | `/TestEmail` | Admin | Test form |
| POST | `/TestEmail/SendTestEmail` (`testEmail`) | Admin | The same view with a success or error message. Refuses to send when `DevelopmentEmailSender` is active |

## Admin area (`Areas/Admin/Controllers/`, all `[Area("Admin")]` + Admin role)

### `HomeController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin`, `/Admin/Home/Index` | Dashboard (`DashboardViewModel`: revenue, counts, 14-day sales, top products, low stock, recent orders) |
| GET | `/Admin/Home/Users?search=` | Users with roles, lock state and order counts |
| GET | `/Admin/Home/EditRole?userId=` | Role editor for one user |
| POST | `/Admin/Home/EditRole` (`UserId`, `RoleId`) | Adds the role; redirect to `EditRole` |
| POST | `/Admin/Home/RemoveRole` (`roleName`, `userId`) | Removes the role (not your own Admin role); redirect to `EditRole` |
| POST | `/Admin/Home/DeleteUser?userId=` | Deletes a user without orders (not yourself); redirect to `Users` |
| POST | `/Admin/Home/LockUser?userId=` | Locks for 100 years and invalidates sessions; redirect to `Users` |
| POST | `/Admin/Home/UnlockUser?userId=` | Unlocks; redirect to `Users` |
| GET | `/Admin/Home/ShowMessage` | Contact messages |
| POST | `/Admin/Home/DeleteMessage` (`id`) | JSON `{ success, message }` |
| POST | `/Admin/Home/SendReply` (`id`, `replySubject`, `replyMessage`) | Emails the reply; JSON `{ success, message }` |

### `ProductsController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Products?search=&category=&stock=out\|low&status=active\|hidden\|sale&page=` | List, 25 per page |
| GET | `/Admin/Products/Create` | `Form` view |
| POST | `/Admin/Products/Create` (multipart: product fields, `File` required, `GalleryFiles` up to 8) | Redirect to the list, or the form with errors |
| GET | `/Admin/Products/Edit/{id}` | `Form` view |
| POST | `/Admin/Products/Edit/{id}` (multipart, optional `File`, `GalleryFiles`, `removeImages`) | Redirect to the list |
| POST | `/Admin/Products/UpdateStock` (`id`, `stockQuantity`) | JSON `{ success, message }` |
| POST | `/Admin/Products/ToggleActive/{id}` | Show/hide in store; redirect to the list |
| POST | `/Admin/Products/Delete/{id}` | Deletes the product, or hides it if it appears in orders; redirect to the list |

### `CategoriesController`

Every POST also clears the cached navigation tree.

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Categories` | List with product counts |
| GET/POST | `/Admin/Categories/Create` (multipart, optional `File`) | `Form` view / redirect to the list |
| GET/POST | `/Admin/Categories/Edit/{id}` | `Form` view / redirect to the list |
| POST | `/Admin/Categories/Delete/{id}` | Refused while the category has subcategories or products; redirect to the list |

### `CouponsController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Coupons` | List |
| GET/POST | `/Admin/Coupons/Create` | `Form` view / redirect to the list |
| GET/POST | `/Admin/Coupons/Edit/{id}` | `Form` view / redirect to the list |
| POST | `/Admin/Coupons/ToggleActive/{id}` | Redirect to the list |
| POST | `/Admin/Coupons/Delete/{id}` | Deletes the coupon, or deactivates it if it was used; redirect to the list |

### `OrdersController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Orders?status=&search=&page=` | List, 25 per page, with status counts |
| GET | `/Admin/Orders/Details/{id}` | Details with status and tracking forms |
| GET | `/Admin/Orders/Invoice/{id}` | Invoice (reuses `Views/Orders/Invoice.cshtml`) |
| POST | `/Admin/Orders/UpdateStatus` (`id`, `status`, `trackingNumber`, `adminNotes`, `carrier`) | JSON `{ success, message }`. Pushes `OrderStatusUpdated`; emails the customer when the status changes; restocks on cancellation |
| POST | `/Admin/Orders/AddTrackingEvent` (`orderId`, `location`, `status`, `description`, `isMilestone`) | JSON `{ success, message }` |
| GET | `/Admin/Orders/GetTrackingHistory?orderId=` | Partial `Views/Orders/_TrackingHistory.cshtml` |
| GET | `/Admin/Orders/ExportToExcel?status=&search=` | `orders-yyyyMMdd-HHmm.xlsx` |

### `ReviewsController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Reviews?status=pending\|approved\|all&page=` | Moderation list, 30 per page (default `pending`) |
| POST | `/Admin/Reviews/Approve/{id}?status=` | Redirect to the list |
| POST | `/Admin/Reviews/Unpublish/{id}?status=` | Redirect to the list |
| POST | `/Admin/Reviews/Delete/{id}?status=` | Redirect to the list |

### `ReportsController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Reports?from=&to=` | Sales report (default last 30 days, max 366 days) |
| GET | `/Admin/Reports/Newsletter` | Subscriber list |
| GET | `/Admin/Reports/NewsletterCsv` | `newsletter-yyyyMMdd.csv` |
| POST | `/Admin/Reports/DeleteSubscriber/{id}` | Redirect to `Newsletter` |

### `RoleManagerController`

`Admin` and `User` are protected roles: they cannot be renamed or deleted.

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/RoleManager` | Roles |
| POST | `/Admin/RoleManager/CreateRole` (`roleName`) | Redirect to `Index` |
| GET | `/Admin/RoleManager/EditRole/{id}` | Role name and members |
| POST | `/Admin/RoleManager/EditRoleConfirmed/{id}` (`roleName`) | Rename; redirect to `Index`, or back to `EditRole` on error |
| POST | `/Admin/RoleManager/RemoveRole/{id}` | Delete; redirect to `Index` |

### `WishlistController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Wishlist?userId=&search=` | All wishlist entries |
| POST | `/Admin/Wishlist/Delete` (`id`) | JSON `{ success, message }` |
| GET | `/Admin/Wishlist/Statistics` | Totals and the 10 most-wished products |

### `ChatController`

| Method | Route | Returns |
| --- | --- | --- |
| GET | `/Admin/Chat` | All chat rooms. The unread badge polls `GetUnreadCount` every 15 s |
| GET | `/Admin/Chat/Room/{id}` | Chat room. Assigns the viewing admin if the room is unassigned (status `InProgress`) and marks customer messages read |
| POST | `/Admin/Chat/AssignAgent` (`id`, `agentId`) | Redirect to `Room` |
| POST | `/Admin/Chat/ResolveRoom/{id}` | Redirect to `Index` |
| POST | `/Admin/Chat/CloseRoom/{id}` | Redirect to `Index` |
| POST | `/Admin/Chat/ReopenRoom/{id}` | Redirect to `Room` |
| POST | `/Admin/Chat/DeleteRoom/{id}` | Only resolved or closed rooms (also has an explicit `[ValidateAntiForgeryToken]`); redirect to `Index` |
| GET | `/Admin/Chat/GetUnreadCount` | JSON `{ count }` – rooms with unread customer messages |

## Identity (Razor Pages, `Areas/Identity/Pages/Account/`)

All pages live under `/Identity/Account/...`.

- **Anonymous** (`[AllowAnonymous]`): `Login`, `Register`, `RegisterConfirmation`, `ConfirmEmail`, `ConfirmEmailChange`, `ResendEmailConfirmation`, `ForgotPassword`, `ForgotPasswordConfirmation`, `ResetPassword`, `ResetPasswordConfirmation`, `LoginWith2fa`, `LoginWithRecoveryCode`, `Lockout`, `Logout` (POST signs out).
- **External login**: `ExternalLogin` (POST starts the provider challenge, `?handler=Callback` handles the return, `?handler=Confirmation` creates or links the account; GET redirects to `Login`).
- **Signed-in users** (Identity UI's default convention for the `Manage` folder):
  - `Manage/Index` (redirects to `/Account/Profile`), `Manage/Email`, `Manage/ChangePassword`, `Manage/SetPassword`, `Manage/ExternalLogins`;
  - `Manage/TwoFactorAuthentication`, `Manage/EnableAuthenticator`, `Manage/Disable2fa`, `Manage/ResetAuthenticator`, `Manage/GenerateRecoveryCodes`, `Manage/ShowRecoveryCodes`;
  - `Manage/PersonalData`, `Manage/DownloadPersonalData` (POST), `Manage/DeletePersonalData`.
- `AccessDenied` and `/Identity/Error` also exist, but the app cookie sends access-denied users to `/Error/AccessDenied`.

`RateLimitingMiddleware` returns 429 for POSTs to `/Identity/Account/Login` (more than 5 per IP per 15 minutes) and `/Identity/Account/Register` (more than 3 per IP per hour).

## SignalR hubs

| Path | Hub | Auth |
| --- | --- | --- |
| `/chatHub` | `ChatHub` | User |
| `/realtimeHub` | `RealtimeHub` | User |

See [Real-time](realtime.md).
