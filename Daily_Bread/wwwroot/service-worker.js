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

// Push notifications (future enhancement)
self.addEventListener('push', (event) => {
    if (event.data) {
        const data = event.data.json();
        const options = {
            body: data.body,
            icon: '/images/icons/icon-192x192.png',
            badge: '/images/icons/badge-72x72.png',
            vibrate: [100, 50, 100],
            data: {
                url: data.url || '/'
            }
        };
        
        event.waitUntil(
            self.registration.showNotification(data.title, options)
        );
    }
});

// Handle notification clicks
self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    
    const url = event.notification.data?.url || '/';
    
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true })
            .then((clientList) => {
                // If app is already open, focus it
                for (const client of clientList) {
                    if (client.url.includes(self.location.origin) && 'focus' in client) {
                        client.navigate(url);
                        return client.focus();
                    }
                }
                // Otherwise open new window
                return clients.openWindow(url);
            })
    );
});
