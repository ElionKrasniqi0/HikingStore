# HikingStore Security Implementation Guide

## Overview
This document describes the comprehensive security measures implemented in the HikingStore project to protect user data, prevent unauthorized access, and ensure secure order processing.

## 1. User Registration and Login Security

### Password Requirements
- **Minimum Length**: 8 characters (increased from 6)
- **Required Characters**: 
  - At least one uppercase letter
  - At least one lowercase letter
  - At least one digit
  - At least one special character (@$!%*?&)
- **Unique Characters**: At least 2 unique characters required
- **Validation**: Server-side regex validation in `Register.cshtml.cs`

### Account Lockout Protection
- **Max Failed Attempts**: 5 attempts
- **Lockout Duration**: 15 minutes (increased from 5)
- **Lockout Enabled**: Yes, for all new users
- **Brute Force Protection**: Enabled via `lockoutOnFailure: true`

### Rate Limiting
- **Login Attempts**: 5 attempts per 15 minutes per IP address
- **Registration Attempts**: 3 attempts per hour per IP address
- **Implementation**: `RateLimitingMiddleware` applied to all authentication endpoints
- **Response**: HTTP 429 (Too Many Requests) with Retry-After header

### Security Logging
- **Successful Logins**: Logged with IP address and timestamp
- **Failed Logins**: Logged with IP address for monitoring brute force attempts
- **Account Lockouts**: Logged with IP address and user email
- **User Registration**: Logged with IP address for audit trail

### Email Confirmation
- **Required**: Yes (`RequireConfirmedAccount = true`)
- **Purpose**: Prevents fake account creation
- **Flow**: User must confirm email before first login

### SQL Injection Protection
- **EF Core**: All database queries use parameterized queries (built-in protection)
- **No Raw SQL**: No direct SQL string concatenation
- **Input Validation**: All user inputs validated server-side

## 2. Order Placement Security

### Authentication Requirements
- **All Order Endpoints**: Protected with `[Authorize]` attribute
- **User Verification**: User ID always retrieved from authenticated session
- **No User Impersonation**: UserId from request is validated and overridden

### User Identity Validation
```csharp
// SECURITY: Always get userId from authenticated user, never from request
var userId = _userManager.GetUserId(User);
if (string.IsNullOrEmpty(userId))
{
    return Unauthorized();
}

// SECURITY: Validate and prevent user impersonation
if (!string.IsNullOrEmpty(order.UserId) && order.UserId != userId)
{
    _logger.LogWarning("SECURITY: User impersonation attempt detected");
    return BadRequest("Invalid request.");
}

// SECURITY: Always set UserId from authenticated user
order.UserId = userId; // Override any UserId from frontend
```

### Price Calculation Security
- **Server-Side Only**: All prices calculated from database product prices
- **No Frontend Trust**: Frontend price values are ignored
- **Database Transaction**: Atomic order creation with stock deduction

### CSRF Protection
- **All POST Endpoints**: Protected with `[ValidateAntiForgeryToken]`
- **Token Validation**: Server validates anti-forgery token on every POST request
- **Configuration**: Secure cookie settings (HttpOnly, Secure, SameSite=Strict)

### Order Access Control
- **User Orders Only**: Users can only view/modify their own orders
- **Query Filtering**: All queries include `o.UserId == userId` filter
- **Unauthorized Access**: Logged as security events

## 3. Admin Area Security

### Role-Based Authorization
- **Strict Role Check**: `[Authorize(Roles = "Admin")]` on all admin controllers
- **Defense in Depth**: Additional role verification in action methods
- **Access Denied**: Returns `Forbid()` for unauthorized access attempts

### Admin Audit Logging
- **Service**: `AdminAuditService` logs all admin actions
- **Logged Information**:
  - Admin username and ID
  - Action performed
  - Target resource
  - IP address
  - User agent
  - Timestamp
- **Actions Logged**:
  - Order status updates
  - Order deletions
  - Dashboard access
  - Any data modifications

### Admin Action Logging Examples
```csharp
// Log order status change
await _auditService.LogAdminModificationAsync("Order", orderId, "STATUS_UPDATE", oldStatus, newStatus);

// Log order deletion
await _auditService.LogAdminDeletionAsync("Order", orderId, orderDetails);

// Log dashboard access
await _auditService.LogAdminAccessAsync("Admin Dashboard", "VIEW");
```

### IP Address Tracking
- **All Admin Actions**: Logged with IP address
- **Unauthorized Attempts**: Logged with IP for security monitoring
- **Access**: Available via `HttpContext.Connection.RemoteIpAddress`

### CSRF Protection
- **All Admin POST Actions**: Protected with `[ValidateAntiForgeryToken]`
- **Secure Tokens**: Anti-forgery tokens use secure cookie settings

## 4. Session and Cookie Security

### Cookie Security Settings
- **HttpOnly**: `true` - Prevents XSS attacks
- **Secure**: `true` in production - HTTPS only
- **SameSite**: `Strict` - CSRF protection
- **Session Timeout**: 30 minutes of inactivity

### Authentication Cookie
- **Name**: `.HikingStore.Auth`
- **Expiration**: 30 days with sliding expiration
- **Secure in Production**: Always uses HTTPS

## 5. Security Headers

### HTTP Security Headers
- **X-Content-Type-Options**: `nosniff` - Prevents MIME type sniffing
- **X-Frame-Options**: `DENY` - Prevents clickjacking
- **X-XSS-Protection**: `1; mode=block` - XSS protection
- **Referrer-Policy**: `strict-origin-when-cross-origin`
- **Content-Security-Policy**: Restricts resource loading
- **HSTS**: HTTP Strict Transport Security (production only)

## 6. Rate Limiting Implementation

### Middleware
- **Location**: `Middleware/RateLimitingMiddleware.cs`
- **Applied To**: All authentication endpoints
- **Storage**: In-memory cache (can be upgraded to distributed cache)

### Limits
- **Login**: 5 attempts per 15 minutes per IP
- **Registration**: 3 attempts per hour per IP
- **Response**: HTTP 429 with Retry-After header

## 7. Security Best Practices Implemented

### ✅ Authentication
- Strong password requirements
- Account lockout on failed attempts
- Email confirmation required
- Secure session management

### ✅ Authorization
- Role-based access control
- Defense in depth (multiple authorization checks)
- User can only access their own data

### ✅ Input Validation
- Server-side validation on all inputs
- DataAnnotations for model validation
- SQL injection protection via EF Core

### ✅ CSRF Protection
- Anti-forgery tokens on all POST requests
- Secure token storage

### ✅ Security Logging
- All security events logged
- IP address tracking
- Admin action audit trail

### ✅ Rate Limiting
- Brute force protection
- Abuse prevention

### ✅ Secure Configuration
- HTTPS enforcement in production
- Secure cookie settings
- Security headers

## 8. Security Monitoring

### Log Levels
- **Information**: Successful operations, normal activity
- **Warning**: Security events, failed attempts, unauthorized access
- **Error**: Security violations, system errors

### Key Security Events to Monitor
1. Failed login attempts (especially from same IP)
2. Account lockouts
3. User impersonation attempts
4. Unauthorized admin access attempts
5. Order manipulation attempts
6. Rate limit violations

## 9. Testing Security

### Test Scenarios
1. **Brute Force**: Try 6+ login attempts - should lockout account
2. **Rate Limiting**: Try 6+ login attempts from same IP - should get 429
3. **User Impersonation**: Try to place order with different UserId - should be rejected
4. **Unauthorized Admin**: Try to access admin area without Admin role - should be forbidden
5. **CSRF**: Try POST without token - should be rejected

## 10. Future Enhancements

### Recommended Additions
1. **Two-Factor Authentication (2FA)**: For admin accounts
2. **IP Whitelisting**: For admin access
3. **Distributed Rate Limiting**: Using Redis for production
4. **Security Event Database**: Store audit logs in database
5. **Automated Security Alerts**: Email notifications for security events
6. **Password History**: Prevent password reuse
7. **Account Activity Monitoring**: Track unusual login patterns

## Conclusion

The HikingStore project now implements comprehensive security measures across all critical areas:
- ✅ Secure user registration and login
- ✅ Protected order placement
- ✅ Restricted admin access with audit logging
- ✅ Rate limiting and brute force protection
- ✅ CSRF and XSS protection
- ✅ Comprehensive security logging

All security measures follow industry best practices and provide defense in depth against common attacks.
