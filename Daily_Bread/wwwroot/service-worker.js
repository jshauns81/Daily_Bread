// Daily Bread Service Worker
// Provides offline caching and PWA functionality

const CACHE_VERSION = 'daily-bread-v2';
const CACHE_NAME = CACHE_VERSION;
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

// Install event - precache critical assets (tolerant of missing files)
self.addEventListener('install', (event) => {
    console.log('[ServiceWorker] Install');
    
    event.waitUntil(
        (async () => {
            const cache = await caches.open(CACHE_NAME);
            for (const asset of PRECACHE_ASSETS) {
                try {
                    const response = await fetch(asset, { cache: 'no-cache' });
                    if (response && response.ok) {
                        await cache.put(asset, response.clone());
                    }
                } catch (err) {
                    // Ignore failures to keep install resilient
                }
            }
            await self.skipWaiting();
        })()
    );
});

// Activate event - clean up old caches
self.addEventListener('activate', (event) => {
    console.log('[ServiceWorker] Activate');
    
    event.waitUntil(
        (async () => {
            const cacheNames = await caches.keys();
            await Promise.all(
                cacheNames
                    .filter((cacheName) => cacheName !== CACHE_NAME)
                    .map((cacheName) => caches.delete(cacheName))
            );
            await self.clients.claim();
        })()
    );
});

// Fetch event - explicit strategies for navigation and static assets
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
    
    // Navigation requests: network-first with shell/offline fallback
    if (request.mode === 'navigate') {
        event.respondWith(handleNavigationRequest(request));
        return;
    }
    
    // Static assets: cache-first stale-while-revalidate
    if (isStaticAsset(url.pathname)) {
        event.respondWith(cacheFirstWithRevalidate(request));
        return;
    }
    
    // Default: try network then cache fallback
    event.respondWith(networkThenCache(request));
});

async function handleNavigationRequest(request) {
    try {
        const networkResponse = await fetch(request);
        if (networkResponse && networkResponse.ok) {
            const cache = await caches.open(CACHE_NAME);
            cache.put('/', networkResponse.clone());
        }
        return networkResponse;
    } catch {
        const cache = await caches.open(CACHE_NAME);
        const cachedHome = await cache.match('/');
        if (cachedHome) {
            return cachedHome;
        }
        const offline = await cache.match(OFFLINE_URL);
        if (offline) {
            return offline;
        }
        return Response.redirect(OFFLINE_URL);
    }
}

async function cacheFirstWithRevalidate(request) {
    const cache = await caches.open(CACHE_NAME);
    const cachedResponse = await cache.match(request);
    const networkPromise = fetch(request)
        .then((networkResponse) => {
            if (networkResponse && networkResponse.ok) {
                cache.put(request, networkResponse.clone());
            }
            return networkResponse;
        })
        .catch(() => null);
    
    if (cachedResponse) {
        // Update cache in background
        networkPromise.catch(() => {});
        return cachedResponse;
    }
    
    const networkResponse = await networkPromise;
    if (networkResponse) {
        return networkResponse;
    }
    return caches.match(OFFLINE_URL);
}

async function networkThenCache(request) {
    try {
        const response = await fetch(request);
        if (response && response.ok && response.type === 'basic') {
            const cache = await caches.open(CACHE_NAME);
            cache.put(request, response.clone());
        }
        return response;
    } catch {
        const cached = await caches.match(request);
        if (cached) {
            return cached;
        }
        return caches.match(OFFLINE_URL);
    }
}

// Helper function to identify static assets
function isStaticAsset(pathname) {
    if (pathname.startsWith('/_framework/') || pathname.startsWith('/_content/')) {
        return true;
    }
    if (pathname.startsWith('/css/') || pathname.startsWith('/js/') || pathname.startsWith('/images/')) {
        return true;
    }
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
            { action: 'view', title: '\uD83D\uDC40 View', icon: '/favicon-96x96.png' },
            { action: 'dismiss', title: '\u2716\uFE0F Dismiss' }
        ];
    }
    
    if (type === 'chore-approval') {
        return [
            { action: 'approve', title: '\u2705 Approve' },
            { action: 'view', title: '\uD83D\uDC40 View' }
        ];
    }
    
    // Default actions
    return [
        { action: 'view', title: '\uD83D\uDC40 Open', icon: '/favicon-96x96.png' }
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
