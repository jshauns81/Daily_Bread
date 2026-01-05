// Daily Bread Service Worker
// Provides offline caching and PWA functionality

const CACHE_NAME = 'daily-bread-v1';
const OFFLINE_URL = '/offline.html';

// Assets to cache immediately on install
const PRECACHE_ASSETS = [
    '/',
    '/offline.html',
    '/images/bread-icon.png',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap-icons/font/bootstrap-icons.min.css',
    '/app.css',
    '/Daily_Bread.styles.css'
];

// Install event - precache critical assets
self.addEventListener('install', (event) => {
    console.log('[ServiceWorker] Install');
    
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => {
                console.log('[ServiceWorker] Precaching assets');
                return cache.addAll(PRECACHE_ASSETS);
            })
            .then(() => {
                // Force the waiting service worker to become active
                return self.skipWaiting();
            })
    );
});

// Activate event - clean up old caches
self.addEventListener('activate', (event) => {
    console.log('[ServiceWorker] Activate');
    
    event.waitUntil(
        caches.keys()
            .then((cacheNames) => {
                return Promise.all(
                    cacheNames
                        .filter((cacheName) => cacheName !== CACHE_NAME)
                        .map((cacheName) => {
                            console.log('[ServiceWorker] Deleting old cache:', cacheName);
                            return caches.delete(cacheName);
                        })
                );
            })
            .then(() => {
                // Take control of all pages immediately
                return self.clients.claim();
            })
    );
});

// Fetch event - network-first strategy with offline fallback
self.addEventListener('fetch', (event) => {
    const { request } = event;
    const url = new URL(request.url);
    
    // Skip non-GET requests
    if (request.method !== 'GET') {
        return;
    }
    
    // Skip Blazor SignalR connections
    if (url.pathname.startsWith('/_blazor')) {
        return;
    }
    
    // Skip API calls - always go to network
    if (url.pathname.startsWith('/api') || url.pathname.startsWith('/Account')) {
        return;
    }
    
    // For navigation requests (HTML pages)
    if (request.mode === 'navigate') {
        event.respondWith(
            fetch(request)
                .catch(() => {
                    // If offline, show offline page
                    return caches.match(OFFLINE_URL);
                })
        );
        return;
    }
    
    // For static assets - cache-first strategy
    if (isStaticAsset(url.pathname)) {
        event.respondWith(
            caches.match(request)
                .then((cachedResponse) => {
                    if (cachedResponse) {
                        // Return cached version, but also update cache in background
                        event.waitUntil(
                            fetch(request)
                                .then((networkResponse) => {
                                    if (networkResponse.ok) {
                                        caches.open(CACHE_NAME)
                                            .then((cache) => cache.put(request, networkResponse));
                                    }
                                })
                                .catch(() => { /* Ignore network errors for background update */ })
                        );
                        return cachedResponse;
                    }
                    
                    // Not in cache - fetch from network and cache
                    return fetch(request)
                        .then((networkResponse) => {
                            if (networkResponse.ok) {
                                const responseClone = networkResponse.clone();
                                caches.open(CACHE_NAME)
                                    .then((cache) => cache.put(request, responseClone));
                            }
                            return networkResponse;
                        });
                })
        );
        return;
    }
    
    // Default: network-first
    event.respondWith(
        fetch(request)
            .then((response) => {
                // Cache successful responses
                if (response.ok && response.type === 'basic') {
                    const responseClone = response.clone();
                    caches.open(CACHE_NAME)
                        .then((cache) => cache.put(request, responseClone));
                }
                return response;
            })
            .catch(() => {
                // Try cache as fallback
                return caches.match(request);
            })
    );
});

// Helper function to identify static assets
function isStaticAsset(pathname) {
    const staticExtensions = [
        '.css', '.js', '.png', '.jpg', '.jpeg', '.gif', '.svg', '.ico',
        '.woff', '.woff2', '.ttf', '.eot', '.json'
    ];
    return staticExtensions.some(ext => pathname.endsWith(ext));
}

// Handle messages from the app
self.addEventListener('message', (event) => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

// Background sync for offline chore completions (future enhancement)
self.addEventListener('sync', (event) => {
    if (event.tag === 'sync-chores') {
        console.log('[ServiceWorker] Syncing chores...');
        // Future: sync offline chore completions when back online
    }
});

// ============================================
// PUSH NOTIFICATIONS
// ============================================

self.addEventListener('push', (event) => {
    console.log('[ServiceWorker] Push received');
    
    if (!event.data) {
        console.log('[ServiceWorker] Push event but no data');
        return;
    }
    
    let data;
    try {
        data = event.data.json();
    } catch (e) {
        // If not JSON, use text
        data = {
            title: 'Daily Bread',
            body: event.data.text()
        };
    }
    
    const options = {
        body: data.body || 'You have a new notification',
        icon: data.icon || '/web-app-manifest-192x192.png',
        badge: data.badge || '/favicon-96x96.png',
        vibrate: [100, 50, 100, 50, 100],
        tag: data.tag || 'default',
        renotify: true, // Vibrate again even if replacing notification with same tag
        requireInteraction: true, // Keep notification visible until user interacts
        data: {
            url: data.url || '/',
            ...data.data
        },
        actions: getNotificationActions(data)
    };
    
    event.waitUntil(
        self.registration.showNotification(data.title || 'Daily Bread', options)
    );
});

// Get appropriate actions based on notification type
function getNotificationActions(data) {
    const type = data.data?.type || data.type;
    
    if (type === 'help-request') {
        return [
            { action: 'view', title: '👀 View', icon: '/favicon-96x96.png' },
            { action: 'dismiss', title: '✖️ Dismiss' }
        ];
    }
    
    if (type === 'chore-approval') {
        return [
            { action: 'approve', title: '✅ Approve' },
            { action: 'view', title: '👀 View' }
        ];
    }
    
    // Default actions
    return [
        { action: 'view', title: '👀 Open', icon: '/favicon-96x96.png' }
    ];
}

// Handle notification clicks
self.addEventListener('notificationclick', (event) => {
    console.log('[ServiceWorker] Notification clicked:', event.action);
    
    event.notification.close();
    
    const data = event.notification.data || {};
    let url = data.url || '/';
    
    // Handle specific actions
    if (event.action === 'dismiss') {
        return; // Just close the notification
    }
    
    if (event.action === 'approve' && data.choreLogId) {
        // Could call API to approve directly, but for now just navigate
        url = `/tracker?approve=${data.choreLogId}`;
    }
    
    // For help-request notifications, ensure the URL includes the helpRequestId
    // The URL should already be set correctly from the push payload (e.g., /?helpRequestId=123)
    // But if the user clicks "View" action, make sure we navigate correctly
    if (data.type === 'help-request' && data.choreLogId && event.action === 'view') {
        url = `/?helpRequestId=${data.choreLogId}`;
    }
    
    console.log('[ServiceWorker] Navigating to:', url);
    
    // Open or focus the app
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true })
            .then((clientList) => {
                // If app is already open, focus it and navigate
                for (const client of clientList) {
                    if (client.url.includes(self.location.origin) && 'focus' in client) {
                        return client.focus().then((focusedClient) => {
                            if (focusedClient && url !== '/') {
                                focusedClient.navigate(url);
                            } else if (focusedClient && url.includes('?')) {
                                // Even if base URL is '/', navigate if there are query params
                                focusedClient.navigate(url);
                            }
                            return focusedClient;
                        });
                    }
                }
                // Otherwise open new window
                return clients.openWindow(url);
            })
    );
});

// Handle notification close (user dismissed without clicking)
self.addEventListener('notificationclose', (event) => {
    console.log('[ServiceWorker] Notification closed by user');
    // Could track dismissals for analytics
});

// Handle push subscription change (browser rotated keys)
self.addEventListener('pushsubscriptionchange', (event) => {
    console.log('[ServiceWorker] Push subscription changed');
    
    event.waitUntil(
        self.registration.pushManager.subscribe({ userVisibleOnly: true })
            .then((subscription) => {
                // Notify the app to update the subscription on the server
                return clients.matchAll({ type: 'window' })
                    .then((clientList) => {
                        clientList.forEach((client) => {
                            client.postMessage({
                                type: 'PUSH_SUBSCRIPTION_CHANGED',
                                subscription: subscription.toJSON()
                            });
                        });
                    });
            })
    );
});
