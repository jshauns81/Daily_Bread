/**
 * PWA Utilities for Daily Bread
 * Provides detection and utilities for Progressive Web App functionality
 */

window.dailyBread = window.dailyBread || {};

/**
 * Detects if the app is running in standalone mode (installed PWA)
 * @returns {boolean} True if running as installed PWA, false otherwise
 */
window.dailyBread.isStandalone = function() {
    // Check for display-mode: standalone (Android Chrome, most browsers)
    if (window.matchMedia('(display-mode: standalone)').matches) {
        return true;
    }
    
    // Check for iOS Safari standalone mode
    if (window.navigator.standalone === true) {
        return true;
    }
    
    // Check for Android TWA (Trusted Web Activity)
    if (document.referrer.includes('android-app://')) {
        return true;
    }
    
    return false;
};

/**
 * Performs PIN sign-in via HTTP POST request
 * This ensures the auth cookie is set properly via an HTTP response
 * @param {string} pin - The 4-digit PIN
 * @returns {Promise<{success: boolean, error?: string}>}
 */
window.dailyBread.pinSignIn = async function(pin) {
    try {
        const response = await fetch('/auth/pin', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            credentials: 'include', // Important: include cookies in request/response
            body: JSON.stringify({ pin: pin })
        });
        
        if (response.ok) {
            // Redirect to home page to load with the new auth cookie
            window.location.href = '/';
            return { success: true };
        } else if (response.status === 401) {
            const data = await response.json().catch(() => ({}));
            return { success: false, error: data.message || 'Invalid PIN. Please try again.' };
        } else {
            return { success: false, error: 'An error occurred. Please try again.' };
        }
    } catch (error) {
        console.error('PIN sign-in error:', error);
        return { success: false, error: 'Unable to connect. Please try again.' };
    }
};
