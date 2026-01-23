# Order Security Implementation

## Overview
This document describes the security measures implemented to prevent user impersonation and ensure that orders can only be placed by authenticated users for themselves.

## Security Measures Implemented

### 1. Authentication Verification
- **Location**: `CartsController.Checkout()` method
- **Implementation**: 
  - All order endpoints require `[Authorize]` attribute
  - User ID is always retrieved from `_userManager.GetUserId(User)` - never from request parameters
  - If no authenticated user is found, the request is rejected with `Unauthorized()`

### 2. User Impersonation Prevention
- **Location**: `CartsController.Checkout()` method
- **Implementation**:
  - If `order.UserId` is provided in the request, it is validated against the authenticated user's ID
  - If there's a mismatch, the request is rejected and logged as a security violation
  - The authenticated user's ID always overrides any UserId from the request

### 3. Server-Side User ID Assignment
- **Location**: `CartsController.Checkout()` method
- **Implementation**:
  - `order.UserId = userId;` is always set from the authenticated user
  - This happens AFTER validation, ensuring any malicious UserId from frontend is overwritten
  - Order status, dates, and totals are also set server-side only

### 4. Cart Ownership Verification
- **Location**: `CartsController.Checkout()` method
- **Implementation**:
  - Cart items are loaded with `.Where(c => c.UserId == userId)` filter
  - Only items belonging to the authenticated user are processed
  - This prevents users from ordering items from other users' carts

### 5. Order Access Control
- **Location**: `OrdersController` methods (Index, Details, Invoice, CancelOrder)
- **Implementation**:
  - All queries filter by `o.UserId == userId`
  - Users can only view/modify their own orders
  - Unauthorized access attempts are logged

### 6. Security Logging
- **Location**: All order-related methods
- **Implementation**:
  - Failed authentication attempts are logged with IP address
  - User impersonation attempts are logged with details
  - Successful order creation is logged for audit trail
  - Unauthorized access attempts are logged

### 7. CSRF Protection
- **Location**: All POST endpoints
- **Implementation**:
  - `[ValidateAntiForgeryToken]` attribute on all POST methods
  - Prevents cross-site request forgery attacks

## Code Examples

### Secure Order Creation
```csharp
[HttpPost]
[Authorize]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Checkout(Order order)
{
    // 1. Get authenticated user ID (never from request)
    var userId = _userManager.GetUserId(User);
    if (string.IsNullOrEmpty(userId))
    {
        _logger.LogWarning("SECURITY: Checkout attempted without authenticated user");
        return Unauthorized();
    }

    // 2. Validate against impersonation
    if (!string.IsNullOrEmpty(order.UserId) && order.UserId != userId)
    {
        _logger.LogWarning("SECURITY: User impersonation attempt detected");
        return BadRequest("Invalid request.");
    }

    // 3. Load cart items only for authenticated user
    var cartItems = await _context.ShoppingCarts
        .Where(c => c.UserId == userId)
        .ToListAsync();

    // 4. Always set UserId from authenticated user (override any from request)
    order.UserId = userId;
    order.Status = OrderStatus.Received; // Set server-side only
    order.OrderDate = DateTime.UtcNow; // Set server-side only
    order.TotalAmount = totalAmount; // Calculated server-side only
}
```

### Secure Order Access
```csharp
public async Task<IActionResult> Details(int id)
{
    var userId = _userManager.GetUserId(User);
    
    // Only load order if it belongs to authenticated user
    var order = await _context.Orders
        .FirstOrDefaultAsync(o => o.OrderId == id && o.UserId == userId);
    
    if (order == null)
    {
        return NotFound();
    }
    
    return View(order);
}
```

## Security Checklist

✅ Authentication required for all order operations
✅ User ID always retrieved from authenticated session
✅ User impersonation attempts detected and blocked
✅ Cart items filtered by authenticated user
✅ Order UserId always set server-side
✅ Order status set server-side only
✅ Order totals calculated server-side only
✅ CSRF protection on all POST endpoints
✅ Security events logged for audit trail
✅ Unauthorized access attempts logged

## Testing Security

To test the security implementation:

1. **Authentication Test**: Try accessing `/Carts/Checkout` without logging in - should redirect to login
2. **Impersonation Test**: Try sending a POST request with a different UserId - should be rejected
3. **Cart Ownership Test**: Try to checkout with items from another user's cart - should only process your own items
4. **Order Access Test**: Try accessing another user's order details - should return NotFound

## Notes

- The frontend should NEVER send UserId in order requests
- All user identification comes from the authentication token/session
- Server-side validation is the single source of truth
- All security violations are logged for monitoring
