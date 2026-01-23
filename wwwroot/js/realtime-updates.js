// Real-time updates using SignalR
(function() {
    'use strict';

    let connection = null;
    let isConnected = false;

    // Initialize SignalR connection for real-time updates
    function initRealtimeConnection() {
        // Only initialize if user is authenticated
        if (typeof signalR === 'undefined') {
            console.warn('SignalR library not loaded. Real-time updates disabled.');
            return;
        }

        try {
            connection = new signalR.HubConnectionBuilder()
                .withUrl("/realtimeHub", {
                    transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
                    accessTokenFactory: () => {
                        // SignalR will automatically use cookies for authentication
                        return "";
                    }
                })
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: retryContext => {
                        if (retryContext.elapsedMilliseconds < 60000) {
                            return 2000; // Retry after 2 seconds
                        } else {
                            return null; // Stop retrying after 60 seconds
                        }
                    }
                })
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Cart updates
            connection.on("CartUpdated", function(data) {
                console.log("Cart updated:", data);
                
                // Update cart count in UI
                const cartBadge = document.querySelector('.cart-badge, .cart-count, [data-cart-count]');
                if (cartBadge && data.cartItemCount !== undefined) {
                    cartBadge.textContent = data.cartItemCount;
                    cartBadge.style.display = data.cartItemCount > 0 ? 'inline' : 'none';
                }

                // Show notification if message provided
                if (data.message) {
                    showRealtimeNotification(data.message, 'success');
                }

                // Trigger custom event for other scripts
                window.dispatchEvent(new CustomEvent('cartUpdated', { detail: data }));
            });

            // Order status updates
            connection.on("OrderStatusUpdated", function(data) {
                console.log("Order status updated:", data);
                
                // Show notification
                if (data.message) {
                    showRealtimeNotification(data.message, 'info');
                } else {
                    showRealtimeNotification(`Order #${data.orderId} status updated to ${data.status}`, 'info');
                }

                // Reload order page if currently viewing that order
                const currentPath = window.location.pathname;
                if (currentPath.includes('/Orders/Details/') && currentPath.includes(data.orderId)) {
                    setTimeout(() => {
                        window.location.reload();
                    }, 2000);
                }

                // Trigger custom event
                window.dispatchEvent(new CustomEvent('orderStatusUpdated', { detail: data }));
            });

            // Connection events
            connection.onreconnecting((error) => {
                console.log("Real-time connection lost. Reconnecting...", error);
                isConnected = false;
            });

            connection.onreconnected((connectionId) => {
                console.log("Real-time connection reestablished:", connectionId);
                isConnected = true;
            });

            connection.onclose((error) => {
                console.log("Real-time connection closed", error);
                isConnected = false;
            });

            // Start connection
            connection.start()
                .then(() => {
                    console.log("Real-time connection established");
                    isConnected = true;
                })
                .catch((err) => {
                    console.error("Error starting real-time connection:", err);
                    isConnected = false;
                });

        } catch (error) {
            console.error("Error initializing real-time connection:", error);
        }
    }

    // Show real-time notification
    function showRealtimeNotification(message, type = 'info') {
        // Try to use existing notification system
        if (typeof showNotification === 'function') {
            showNotification(message, type);
            return;
        }

        // Fallback: Create simple notification
        const notification = document.createElement('div');
        notification.className = `alert alert-${type === 'success' ? 'success' : 'info'} alert-dismissible fade show position-fixed`;
        notification.style.cssText = 'top: 20px; right: 20px; z-index: 9999; min-width: 300px;';
        notification.innerHTML = `
            ${message}
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        `;
        document.body.appendChild(notification);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            notification.remove();
        }, 5000);
    }

    // Join order group when viewing order details
    function joinOrderGroup(orderId) {
        if (connection && isConnected && orderId) {
            connection.invoke("JoinOrderGroup", orderId)
                .catch(err => console.error("Error joining order group:", err));
        }
    }

    // Leave order group
    function leaveOrderGroup(orderId) {
        if (connection && isConnected && orderId) {
            connection.invoke("LeaveOrderGroup", orderId)
                .catch(err => console.error("Error leaving order group:", err));
        }
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initRealtimeConnection);
    } else {
        initRealtimeConnection();
    }

    // Export functions for use in other scripts
    window.realtimeUpdates = {
        joinOrderGroup: joinOrderGroup,
        leaveOrderGroup: leaveOrderGroup,
        isConnected: () => isConnected
    };
})();
