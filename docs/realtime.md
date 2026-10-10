# Real-time (SignalR)

The app has two SignalR hubs, mapped in `Program.cs`. Both are `[Authorize]`, so only signed-in users (cookie auth) can connect. The browser client is the cdnjs `microsoft-signalr` **8.0.7** build.

Server options (`Program.cs`): max receive message size 64 KB, keep-alive 15 s, client timeout 30 s, handshake timeout 15 s, detailed errors only in Development.

| Hub | Path | Used for |
| --- | --- | --- |
| `RealtimeHub` (`Hubs/RealtimeHub.cs`) | `/realtimeHub` | Cart badge and order status notifications |
| `ChatHub` (`Hubs/ChatHub.cs`) | `/chatHub` | Customer ↔ support live chat |

Both hubs identify the user by `Context.UserIdentifier`, falling back to the `NameIdentifier` claim, and check the `Admin` role through `UserManager`.

## `RealtimeHub` — `/realtimeHub`

### Client

`wwwroot/js/realtime-updates.js` is included by `Views/Shared/_Layout.cshtml`, together with the SignalR library, **only when the user is signed in**. Every storefront page therefore has one connection. The admin layout (`_LayoutAdmin.cshtml`) includes both scripts too, so admins get the same notifications inside `/Admin`.

- The connection uses `withAutomaticReconnect()` with the default retry policy and logs warnings only.
- It exposes `window.realtimeUpdates`:
  - `joinOrderGroup(orderId)` and `leaveOrderGroup(orderId)` – no-ops until connected;
  - `isConnected()`.
- `Views/Orders/Details.cshtml` calls `realtimeUpdates.joinOrderGroup(orderId)` 1.5 s after the page loads.

### Groups

| Group | Members |
| --- | --- |
| `User_{userId}` | Every connection of that user (joined in `OnConnectedAsync`) |
| `Admins` | Connections of users in the `Admin` role (joined in `OnConnectedAsync`) |
| `Order_{orderId}` | Connections that called `JoinOrderGroup` and are the order's owner or an admin |

Connections leave `User_*` and `Admins` in `OnDisconnectedAsync`.

### Client → server

| Method | Parameters | Effect |
| --- | --- | --- |
| `JoinOrderGroup` | `orderId: int` | Joins `Order_{orderId}` if the caller owns the order or is an admin; otherwise does nothing |
| `LeaveOrderGroup` | `orderId: int` | Leaves `Order_{orderId}` |

### Server → client

The hub has no server-to-client calls of its own. All events are sent from controllers through `IHubContext<RealtimeHub>`:

| Event | Sent by | Target | Payload |
| --- | --- | --- | --- |
| `CartUpdated` | `CartsController.AddToCart` (on success, signed-in users) | `User_{userId}` | `{ cartItemCount }` |
| `CartUpdated` | `CartsController.UpdateCartItem`, `RemoveFromCart`, `RemoveAll` (signed-in users, after every call) | `User_{userId}` | `{ cartItemCount }` |
| `CartUpdated` | `CartsController.Checkout` POST (order placed) | `User_{userId}` | `{ cartItemCount: 0 }` |
| `OrderStatusUpdated` | `Areas/Admin/Controllers/OrdersController.UpdateStatus` (every successful update, even when only notes or tracking data changed) | `User_{order.UserId}` and `Order_{orderId}` | `{ orderId, status, oldStatus, trackingNumber, message }`, where `message` is e.g. "Order #ORD000123 is now Shipped." |
| `OrderStatusUpdated` | `Controllers/OrderController.cs` → `OrdersController.CancelOrder` (customer cancelled) | `Admins` | `{ orderId, status: "Cancelled", userId, message, href }`, where `href` is `/Admin/Orders/Details/{orderId}` |

Guests have no connection; their cart badge is updated from the JSON responses of the cart actions instead.

### Client handling (`realtime-updates.js`)

- **`CartUpdated`** – calls `window.HS.setCartCount(cartItemCount)` (defined in `site.js`) to update every `[data-cart-count]` badge, then dispatches a `cartUpdated` window event with the payload. No toast is shown.
- **`OrderStatusUpdated`**:
  1. Ignores the event if the same order and status arrived in the last 5 s. The admin status update is sent to two groups, so a customer on the order page would otherwise get it twice.
  2. Shows a toast with `message` (or "Order #{orderId} was updated." when there is none) and a "View order" link to `href`, falling back to `/Orders/Details/{orderId}`.
  3. If the current page is the customer page `/Orders/Details/{orderId}`, reloads it after 2.5 s. Admin pages are not reloaded.
  4. Dispatches an `orderStatusUpdated` window event.

No script currently listens to the `cartUpdated` or `orderStatusUpdated` window events.

## `ChatHub` — `/chatHub`

### Clients

- **Customer:** `Views/Chat/Room.cshtml`. The SignalR library comes from the storefront layout.
- **Admin:** `Areas/Admin/Views/Chat/Room.cshtml`, which loads the SignalR library itself.

Both pages run the same flow:

1. Connect with `withAutomaticReconnect()`, then invoke `JoinRoom(chatRoomId)`. If the connection fails, retry every 5 s.
2. Invoke `JoinRoom` again after an automatic reconnect.
3. Send the input text with `SendMessage`.
4. Invoke `Typing(true)` on the first keystroke and `Typing(false)` after 1.2 s of inactivity or after sending.
5. Handle three events:
   - `ReceiveMessage` – append the message bubble, rendered with `textContent`;
   - `UserTyping` – show the typing indicator, hidden again after 4 s;
   - `ErrorMessage` – show an error toast.
6. Show a status line while reconnecting or disconnected.

The admin chat list (`Areas/Admin/Views/Chat/Index.cshtml`) does not use SignalR. It polls `GET /Admin/Chat/GetUnreadCount` every 15 s.

### Groups

| Group | Members |
| --- | --- |
| `Agents` | Every admin connection (joined in `OnConnectedAsync`); also an assigned non-admin agent after `JoinRoom` |
| `{userId}` | Each non-admin user's connections (joined in `OnConnectedAsync` and again in `JoinRoom` for the room owner) |
| `Room_{chatRoomId}` | Connections that called `JoinRoom` and are the room owner, the assigned agent or an admin |

### Client → server

| Method | Parameters | Effect |
| --- | --- | --- |
| `JoinRoom` | `chatRoomId: int` | If the caller is the owner, the assigned agent or an admin: joins `Room_{id}`, marks the other side's unread messages read and sends `MessagesRead` to the room. Otherwise does nothing |
| `SendMessage` | `chatRoomId: int`, `message: string` | Trims the text (empty is ignored; more than 2000 characters is rejected), checks the caller is the owner, the assigned agent or an admin, saves a `ChatMessage`, updates the room and broadcasts it |
| `Typing` | `chatRoomId: int`, `isTyping: bool` | For the owner, the assigned agent or an admin: sends `UserTyping` to the other connections in `Room_{id}` |

Room rules applied by `SendMessage`:

- `LastMessageAt` is updated.
- A message in a `Closed` or `Resolved` room reopens it as `InProgress` and clears `ResolvedAt`.
- A message from an admin or agent in an `Open` room sets it to `InProgress` and assigns the sender if no agent is assigned.
- `IsAgentMessage` is true for admins and the assigned agent. A message sent by the owner is stored as already read.

Viewing a room as admin (`GET /Admin/Chat/Room/{id}`) also assigns the viewing admin when the room is unassigned.

### Server → client

| Event | Target | Payload |
| --- | --- | --- |
| `ReceiveMessage` | `Room_{chatRoomId}` | `{ id, chatRoomId, senderId, senderName, message, isAgentMessage, sentAt, isRead }` |
| `NewUserMessage` | `Agents` (when the sender is not an agent) | `{ chatRoomId, userId, userName, message, timestamp }` |
| `RoomUpdated` | Other connections in `Agents` (when an agent writes) | `{ chatRoomId, lastMessageAt }` |
| `UserTyping` | Other connections in `Room_{chatRoomId}` | `{ userId, userName, isTyping }` |
| `MessagesRead` | `Room_{chatRoomId}` | `chatRoomId` |
| `AgentConnected` / `AgentDisconnected` | Other connections in `Agents` | Admin user name |
| `ErrorMessage` | Caller | Error text (not signed in, message too long, no permission, or a server error while sending) |

`ReceiveMessage` goes only to the room group, so each participant gets every message once. The chat pages handle only `ReceiveMessage`, `UserTyping` and `ErrorMessage`. `NewUserMessage`, `RoomUpdated`, `MessagesRead`, `AgentConnected` and `AgentDisconnected` are sent, but no current page subscribes to them. Nothing is sent to the per-user `{userId}` group.

## Scaling note

SignalR groups and the rate-limiting counters (`IMemoryCache`) are held in memory. Running more than one server instance requires a SignalR backplane (for example Azure SignalR Service or Redis), sticky sessions, and a shared store for sessions and rate limits.
