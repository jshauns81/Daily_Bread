// PWA Update Banner JavaScript Module
// Handles service worker updates with user-friendly notifications

let dotNetHelper = null;
let waitingWorker = null;

// Debug function - call from console: window.pwaShowUpdateBanner()
window.pwaShowUpdateBanner = () => {
    if (dotNetHelper) {
        dotNetHelper.invokeMethodAsync('ShowUpdateBanner');
    } else {
        console.log('PWA Update: dotNetHelper not initialized yet');
    }
};

export function initialize(helper) {
    dotNetHelper = helper;
    
    if (!('serviceWorker' in navigator)) {
        console.log('PWA Update: Service workers not supported');
        return;
    }
    
    // Get the current registration
    navigator.serviceWorker.ready.then((registration) => {
        // Check if there's already a waiting worker
        if (registration.waiting) {
            waitingWorker = registration.waiting;
            notifyUpdate();
        }
        
        // Listen for new service workers
        registration.addEventListener('updatefound', () => {
            const newWorker = registration.installing;
            if (!newWorker) return;
            
            newWorker.addEventListener('statechange', () => {
                // When the new worker is installed and waiting
                if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                    waitingWorker = newWorker;
                    notifyUpdate();
                }
            });
        });
    });
    
    // Handle controller change (new SW activated)
    // This fires after skipWaiting is called
    let refreshing = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (refreshing) return;
        refreshing = true;
        window.location.reload();
    });
}

function notifyUpdate() {
    console.log('PWA Update: New version available');
    if (dotNetHelper) {
        dotNetHelper.invokeMethodAsync('ShowUpdateBanner');
    }
}

export function applyUpdate() {
    if (!waitingWorker) {
        // No waiting worker, just reload
        window.location.reload();
        return;
    }
    
    // Tell the waiting service worker to skip waiting
    // This will trigger the 'controllerchange' event
    waitingWorker.postMessage({ type: 'SKIP_WAITING' });
}
