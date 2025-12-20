// PWA Install Prompt JavaScript Module
// Handles the beforeinstallprompt event and install flow

let deferredPrompt = null;
let dotNetHelper = null;

export function initialize(helper) {
    dotNetHelper = helper;
    
    // Check if already installed
    if (window.matchMedia('(display-mode: standalone)').matches) {
        console.log('PWA: Already installed');
        return;
    }
    
    // Check if user has dismissed before (within last 7 days)
    const dismissedAt = localStorage.getItem('pwa-install-dismissed');
    if (dismissedAt) {
        const dismissedDate = new Date(parseInt(dismissedAt));
        const daysSinceDismissed = (Date.now() - dismissedDate) / (1000 * 60 * 60 * 24);
        if (daysSinceDismissed < 7) {
            console.log('PWA: User dismissed recently, not showing prompt');
            return;
        }
    }
    
    // Listen for the beforeinstallprompt event
    window.addEventListener('beforeinstallprompt', (e) => {
        // Prevent Chrome 67 and earlier from automatically showing the prompt
        e.preventDefault();
        
        // Stash the event so it can be triggered later
        deferredPrompt = e;
        
        console.log('PWA: Install prompt ready');
        
        // Show our custom prompt after a delay (let the user see the app first)
        setTimeout(() => {
            if (deferredPrompt && dotNetHelper) {
                dotNetHelper.invokeMethodAsync('ShowInstallPrompt');
            }
        }, 5000); // 5 second delay
    });
    
    // Listen for successful installation
    window.addEventListener('appinstalled', () => {
        console.log('PWA: App installed successfully');
        deferredPrompt = null;
        
        // Track installation (could send to analytics)
        localStorage.setItem('pwa-installed', Date.now().toString());
    });
}

export async function promptInstall() {
    if (!deferredPrompt) {
        console.log('PWA: No deferred prompt available');
        return false;
    }
    
    // Show the browser's install prompt
    deferredPrompt.prompt();
    
    // Wait for the user's response
    const { outcome } = await deferredPrompt.userChoice;
    console.log(`PWA: User choice: ${outcome}`);
    
    // Clear the deferred prompt
    deferredPrompt = null;
    
    return outcome === 'accepted';
}

export function dismissPrompt() {
    // Remember that user dismissed, don't show again for a week
    localStorage.setItem('pwa-install-dismissed', Date.now().toString());
    deferredPrompt = null;
}

// For iOS, show instructions since beforeinstallprompt isn't supported
export function isIOS() {
    return /iPad|iPhone|iPod/.test(navigator.userAgent) && !window.MSStream;
}

export function isInStandaloneMode() {
    return window.matchMedia('(display-mode: standalone)').matches || 
           window.navigator.standalone === true;
}
