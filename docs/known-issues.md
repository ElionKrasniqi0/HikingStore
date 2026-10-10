# Known issues

Open problems and limitations in the current code, plus the issues from the earlier audit that have since been fixed. File references point to where the behavior lives.

## Action required

### Secrets remain in git history
Earlier commits of `appsettings.json` contained a Google OAuth client secret, a Gmail app password and a default admin password. The current file no longer contains them (all secret values are empty and must come from user secrets or environment variables, see [Configuration](configuration.md)), but anyone with access to the repository history can still read the old values. **Revoke the Google client secret and the Gmail app password** and choose a new admin password. Rewriting history alone is not enough once the repository has been cloned or pushed.

### Content that needs owner review
The About, Services, Help/FAQ, shipping, returns, privacy and terms pages contain generic text written for the refinement. It matches the store configuration (return days, shipping methods, free-shipping threshold), but legal and policy wording must be checked by the store owner before going live.

## Limitations

- **Card payments need a merchant account.** Stripe and bank hosted-page integrations are implemented but stay off until configured, see [Card payments](payments.md). Stripe live mode requires a business registered in a Stripe-supported country, and Kosovo is not one. The bank integration uses a generic signed-form protocol that must be adapted to the chosen bank's specification. Neither has been run end to end against a real provider account yet.
- **Refunds are manual.** Paid orders that are cancelled become `RefundDue`; staff refund them in the provider dashboard, and the order becomes `Refunded` when Stripe's `charge.refunded` webhook arrives. Partial refunds are not reflected on the order.
- **Variant stock is shared.** Sizes and colors are chosen per cart line, but stock is tracked per product (`Product.StockQuantity`), not per variant.
- **Rate limiting is basic.** `Middleware/RateLimitingMiddleware.cs` counts every POST to login (5 per 15 min) and register (3 per hour), including successful ones. It keeps the counters in memory per process and keys them by `RemoteIpAddress`. Without forwarded-headers configuration, every user behind a reverse proxy shares one counter.
- **Emails are fire-and-forget.** `OrderEmailService` and the registration confirmation send in a background `Task.Run`. A failure is logged, but the email is not retried or persisted in a queue.

## Data notes

- **Orders placed before the refinement** have no `FullName` and no coupon or shipping breakdown. Views fall back to the account name or show only the stored total.
- **Category name typo in data:** the existing category "New Arrivels" is stored that way in the database. Rename it in Admin, then Categories.
- **Carrier field:** at checkout `OrderService` sets `Carrier` to the shipping method name (for example "Standard Shipping"). Admins should overwrite it with the real courier when marking an order as shipped, because the "shipped" email says "on its way with {Carrier}".

## Minor code notes

- Opening an admin chat room (a GET request) assigns the current admin as the room's agent if none is set. The separate `AssignAgent` action is not linked from the UI.
- `Store:AnnouncementText` is free text, so its "€100" and "30-day" wording must be kept in sync with `FreeShippingThreshold` and `ReturnDays` by hand. `Store:HeroImageCredit` is read but not displayed.
- Admin status changes email the customer but not `EmailSettings:AdminEmail`. Only customer cancellations send the admin copy.
- Order audit entries are written to the application log, not to the database.
- Unused leftovers remain: `Areas/Identity/Data/HikingStoreContext.cs`, the `RequestStatus` enum, `ShoppingCart.ShippingOption`, and old theme assets in `wwwroot` (`style.css`, `munchbox.js`, `AdminTheme`).
- Some endpoints are not called by any page: `/Carts/RequireLogin`, `/Carts/GetCartCount`, `/Wishlist/AddToWishlist`, `/Wishlist/RemoveFromWishlist`, `/Wishlist/GetWishlistCount`. Some `ChatHub` events (`NewUserMessage`, `RoomUpdated`, `MessagesRead`, `AgentConnected`, `AgentDisconnected`) are sent, but no page listens for them.
- The cart model allows quantities up to 999, but `CartService` caps each line at 99.
- `appsettings.json` still contains a developer machine's SQL Server name. Override `ConnectionStrings:DefaultConnection` per environment.

## UX gaps noted during testing

- The account dropdown in the header shows the email address rather than the customer's name.
- The contact form is not prefilled for signed-in users.

## Build

`dotnet build` succeeds with 0 errors and 106 warnings. They are mostly nullable-reference warnings (CS8618/CS8625) in the scaffolded Identity Razor Pages and older models such as `TrackingEvent`.

## Fixed since the previous audit

These items from the first version of this document have been resolved:

- `ApplicationUser` is now the Identity user type, and first and last names are persisted (migration `StorefrontRefinement`).
- `RealtimeHub.JoinOrderGroup` only admits the order owner or an admin.
- The seeded admin password is no longer reset on startup. The admin is only created when `SeedData:AdminPassword` is set.
- Cancelling an order, either by the customer or by an admin, restocks its items through `OrderService.RestockCancelledOrderAsync`.
- Status emails use the injected configuration through `OrderEmailService`.
- Deleting a product that appears in orders deactivates it (`IsActive = false`) instead of deleting it. Order lines use `DeleteBehavior.Restrict`.
- Chat messages are sent to the room group only, so they are no longer duplicated.
- The admin user list computes "locked" from `LockoutEnd`.
- Antiforgery validation is global (`AutoValidateAntiforgeryToken`), and state-changing actions are POST-only.
- Registration no longer uses request-scoped services in a background task.
- Uploads go through `ImageUploadService`, which validates type and size and builds cross-platform paths.
- Unknown product IDs return 404. The admin contact reply sends a real email.
- Guest carts work and are merged into the user cart at sign-in.
- Dead code (duplicate `OrderStatus` enum, orphaned `UserManager` view) was removed.
- CDN assets are pinned (Bootstrap 5.3.3, Font Awesome 6.5.2) with Subresource Integrity hashes.
