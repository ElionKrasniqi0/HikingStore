// Live cart and order-status updates for signed-in users (SignalR /realtimeHub).
(function () {
    'use strict';

    if (typeof signalR === 'undefined') return;

    let connected = false;
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/realtimeHub')
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on('CartUpdated', data => {
        if (data && typeof data.cartItemCount === 'number') {
            window.HS?.setCartCount(data.cartItemCount);
        }
        window.dispatchEvent(new CustomEvent('cartUpdated', { detail: data }));
    });

    // The server sends to both the user group and the order group, so the same update can arrive twice.
    const recentUpdates = new Map();
    connection.on('OrderStatusUpdated', data => {
        if (!data) return;
        const key = data.orderId + ':' + data.status;
        const now = Date.now();
        if (now - (recentUpdates.get(key) || 0) < 5000) return;
        recentUpdates.set(key, now);
        const message = data.message || ('Order #' + data.orderId + ' was updated.');
        window.HS?.toast(message, 'success', { href: data.href || '/Orders/Details/' + encodeURIComponent(data.orderId), text: 'View order' });
        const match = window.location.pathname.match(/^\/Orders\/Details\/(\d+)/i);
        if (match && String(data.orderId) === match[1]) {
            setTimeout(() => window.location.reload(), 2500);
        }
        window.dispatchEvent(new CustomEvent('orderStatusUpdated', { detail: data }));
    });

    connection.onreconnected(() => { connected = true; });
    connection.onclose(() => { connected = false; });
    connection.start().then(() => { connected = true; }).catch(() => { connected = false; });

    window.realtimeUpdates = {
        joinOrderGroup: orderId => connected && connection.invoke('JoinOrderGroup', orderId).catch(() => { }),
        leaveOrderGroup: orderId => connected && connection.invoke('LeaveOrderGroup', orderId).catch(() => { }),
        isConnected: () => connected
    };
})();
