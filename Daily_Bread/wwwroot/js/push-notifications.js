// Push Notification JavaScript Interop for Blazor
// Handles browser push subscription management

window.PushNotifications = {
    
    // Check if push notifications are supported
    isSupported: function() {
        return 'serviceWorker' in navigator && 
               'PushManager' in window && 
               'Notification' in window;
    },
    
    // Get current permission status
    getPermissionStatus: function() {
        if (!this.isSupported()) {
            return 'unsupported';
        }
        return Notification.permission; // 'default', 'granted', or 'denied'
    },
    
    // Request permission from user
    requestPermission: async function() {
        if (!this.isSupported()) {
            return 'unsupported';
        }
        
        try {
            const permission = await Notification.requestPermission();
            return permission;
        } catch (error) {
            console.error('Error requesting notification permission:', error);
            return 'error';
        }
    },
    
    // Subscribe to push notifications
    subscribe: async function(vapidPublicKey) {
        if (!this.isSupported()) {
            throw new Error('Push notifications not supported');
        }
        
        // Wait for service worker to be ready
        const registration = await navigator.serviceWorker.ready;
        
        // Check existing subscription
        let subscription = await registration.pushManager.getSubscription();
        
        if (subscription) {
            // Already subscribed - return existing subscription
            console.log('Using existing push subscription');
            return JSON.stringify(subscription.toJSON());
        }
        
        // Create new subscription
        try {
            const applicationServerKey = this.urlBase64ToUint8Array(vapidPublicKey);
            
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true, // Required - must show notification for each push
                applicationServerKey: applicationServerKey
            });
            
            console.log('Created new push subscription');
            return JSON.stringify(subscription.toJSON());
        } catch (error) {
            console.error('Failed to subscribe to push notifications:', error);
            throw error;
        }
    },
    
    // Unsubscribe from push notifications
    unsubscribe: async function() {
        if (!this.isSupported()) {
            return true;
        }
        
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            
            if (subscription) {
                await subscription.unsubscribe();
                console.log('Unsubscribed from push notifications');
            }
            
            return true;
        } catch (error) {
            console.error('Failed to unsubscribe from push notifications:', error);
            return false;
        }
    },
    
    // Get current subscription (if any)
    getSubscription: async function() {
        if (!this.isSupported()) {
            return null;
        }
        
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            
            if (subscription) {
                return JSON.stringify(subscription.toJSON());
            }
            return null;
        } catch (error) {
            console.error('Failed to get push subscription:', error);
            return null;
        }
    },
    
    // Check if currently subscribed
    isSubscribed: async function() {
        const subscription = await this.getSubscription();
        return subscription !== null;
    },
    
    // Convert VAPID key from base64 URL to Uint8Array
    urlBase64ToUint8Array: function(base64String) {
        const padding = '='.repeat((4 - base64String.length % 4) % 4);
        const base64 = (base64String + padding)
            .replace(/\-/g, '+')
            .replace(/_/g, '/');
        
        const rawData = window.atob(base64);
        const outputArray = new Uint8Array(rawData.length);
        
        for (let i = 0; i < rawData.length; ++i) {
            outputArray[i] = rawData.charCodeAt(i);
        }
        return outputArray;
    },
    
    // Get device info for subscription identification
    getDeviceInfo: function() {
        const ua = navigator.userAgent;
        let deviceName = 'Unknown Device';
        
        // Detect device type and browser
        if (/iPhone/.test(ua)) {
            deviceName = 'iPhone';
        } else if (/iPad/.test(ua)) {
            deviceName = 'iPad';
        } else if (/Android/.test(ua)) {
            if (/Mobile/.test(ua)) {
                deviceName = 'Android Phone';
            } else {
                deviceName = 'Android Tablet';
            }
        } else if (/Windows/.test(ua)) {
            deviceName = 'Windows PC';
        } else if (/Mac/.test(ua)) {
            deviceName = 'Mac';
        } else if (/Linux/.test(ua)) {
            deviceName = 'Linux';
        }
        
        // Add browser
        if (/Chrome/.test(ua) && !/Edge/.test(ua)) {
            deviceName += ' (Chrome)';
        } else if (/Firefox/.test(ua)) {
            deviceName += ' (Firefox)';
        } else if (/Safari/.test(ua) && !/Chrome/.test(ua)) {
            deviceName += ' (Safari)';
        } else if (/Edge/.test(ua)) {
            deviceName += ' (Edge)';
        }
        
        return {
            deviceName: deviceName,
            userAgent: ua
        };
    },
    
    // Show a test notification (for debugging)
    showTestNotification: async function(title, body) {
        if (!this.isSupported()) {
            console.log('Notifications not supported');
            return false;
        }
        
        if (Notification.permission !== 'granted') {
            console.log('Notification permission not granted');
            return false;
        }
        
        try {
            const registration = await navigator.serviceWorker.ready;
            await registration.showNotification(title || 'Test Notification', {
                body: body || 'This is a test notification from Daily Bread',
                icon: '/web-app-manifest-192x192.png',
                badge: '/favicon-96x96.png',
                vibrate: [100, 50, 100],
                tag: 'test',
                data: { url: '/' }
            });
            return true;
        } catch (error) {
            console.error('Failed to show test notification:', error);
            return false;
        }
    }
};

// Listen for subscription change messages from service worker
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.addEventListener('message', (event) => {
        if (event.data && event.data.type === 'PUSH_SUBSCRIPTION_CHANGED') {
            console.log('Push subscription changed, triggering refresh...');
            // Dispatch custom event that Blazor can listen to
            window.dispatchEvent(new CustomEvent('pushsubscriptionchanged', {
                detail: event.data.subscription
            }));
        }
    });
}
