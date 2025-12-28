/**
 * Biometric Authentication JavaScript Interop
 * Provides WebAuthn API integration for Face ID, Touch ID, Windows Hello
 * 
 * Browser Quirks:
 * - iOS Safari: Requires user gesture to prompt for Touch ID/Face ID
 * - Chrome Android: May require HTTPS and user gesture
 * - Windows Hello: Works best in Edge, may require user gesture in Chrome
 * - PublicKeyCredential is not supported in older browsers
 */

window.biometricAuth = {
    /**
     * Check if WebAuthn is available in the browser
     */
    isAvailable: function() {
        return window.PublicKeyCredential !== undefined &&
               typeof window.PublicKeyCredential === 'function';
    },
    
    /**
     * Check if platform authenticator (Face ID, Touch ID, Windows Hello) is available
     */
    isPlatformAuthenticatorAvailable: async function() {
        try {
            if (!this.isAvailable()) {
                return false;
            }
            
            // Check for platform authenticator support
            // This is the modern way to check for biometric support
            const available = await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            return available;
        } catch (error) {
            console.error('Error checking platform authenticator availability:', error);
            return false;
        }
    },
    
    /**
     * Get platform-specific information for debugging/display
     */
    getPlatformInfo: function() {
        const userAgent = navigator.userAgent;
        
        // Detect platform
        if (/iPhone|iPad|iPod/.test(userAgent)) {
            return 'iOS (Face ID/Touch ID)';
        } else if (/Android/.test(userAgent)) {
            return 'Android (Fingerprint/Face)';
        } else if (/Windows/.test(userAgent)) {
            return 'Windows (Windows Hello)';
        } else if (/Mac/.test(userAgent)) {
            return 'macOS (Touch ID)';
        } else {
            return 'Unknown platform';
        }
    },
    
    /**
     * Register a new biometric credential
     * This is scaffolding - full implementation requires server integration
     * 
     * @param {string} userId - User identifier
     * @param {string} userName - User display name
     * @param {string} challenge - Server-provided challenge (base64)
     * @returns {Promise<Object>} Registration result
     */
    register: async function(userId, userName, challenge) {
        try {
            if (!this.isAvailable()) {
                throw new Error('WebAuthn is not supported');
            }
            
            // Convert challenge from base64 to ArrayBuffer
            const challengeBuffer = Uint8Array.from(atob(challenge), c => c.charCodeAt(0));
            const userIdBuffer = Uint8Array.from(userId, c => c.charCodeAt(0));
            
            // Create credential options
            const publicKeyCredentialCreationOptions = {
                challenge: challengeBuffer,
                rp: {
                    name: "Daily Bread",
                    id: window.location.hostname
                },
                user: {
                    id: userIdBuffer,
                    name: userName,
                    displayName: userName
                },
                pubKeyCredParams: [
                    { alg: -7, type: "public-key" },  // ES256
                    { alg: -257, type: "public-key" } // RS256
                ],
                authenticatorSelection: {
                    // Require platform authenticator (Face ID, Touch ID, Windows Hello)
                    authenticatorAttachment: "platform",
                    // Require user verification (biometric or PIN)
                    userVerification: "required"
                },
                timeout: 60000,
                attestation: "direct"
            };
            
            // Request credential creation
            const credential = await navigator.credentials.create({
                publicKey: publicKeyCredentialCreationOptions
            });
            
            // Convert credential to a format that can be sent to server
            // This is scaffolding - full implementation would send to server
            const credentialData = {
                id: credential.id,
                rawId: btoa(String.fromCharCode(...new Uint8Array(credential.rawId))),
                type: credential.type,
                response: {
                    attestationObject: btoa(String.fromCharCode(...new Uint8Array(credential.response.attestationObject))),
                    clientDataJSON: btoa(String.fromCharCode(...new Uint8Array(credential.response.clientDataJSON)))
                }
            };
            
            return {
                success: true,
                credential: credentialData
            };
        } catch (error) {
            console.error('Error during biometric registration:', error);
            return {
                success: false,
                error: error.message
            };
        }
    },
    
    /**
     * Authenticate using a registered biometric credential
     * This is scaffolding - full implementation requires server integration
     * 
     * @param {string} challenge - Server-provided challenge (base64)
     * @returns {Promise<Object>} Authentication result
     */
    authenticate: async function(challenge) {
        try {
            if (!this.isAvailable()) {
                throw new Error('WebAuthn is not supported');
            }
            
            // Convert challenge from base64 to ArrayBuffer
            const challengeBuffer = Uint8Array.from(atob(challenge), c => c.charCodeAt(0));
            
            // Create credential request options
            const publicKeyCredentialRequestOptions = {
                challenge: challengeBuffer,
                timeout: 60000,
                userVerification: "required",
                rpId: window.location.hostname
            };
            
            // Request credential
            const assertion = await navigator.credentials.get({
                publicKey: publicKeyCredentialRequestOptions
            });
            
            // Convert assertion to a format that can be sent to server
            // This is scaffolding - full implementation would send to server for verification
            const assertionData = {
                id: assertion.id,
                rawId: btoa(String.fromCharCode(...new Uint8Array(assertion.rawId))),
                type: assertion.type,
                response: {
                    authenticatorData: btoa(String.fromCharCode(...new Uint8Array(assertion.response.authenticatorData))),
                    clientDataJSON: btoa(String.fromCharCode(...new Uint8Array(assertion.response.clientDataJSON))),
                    signature: btoa(String.fromCharCode(...new Uint8Array(assertion.response.signature))),
                    userHandle: assertion.response.userHandle ? btoa(String.fromCharCode(...new Uint8Array(assertion.response.userHandle))) : null
                }
            };
            
            return {
                success: true,
                assertion: assertionData
            };
        } catch (error) {
            console.error('Error during biometric authentication:', error);
            return {
                success: false,
                error: error.message
            };
        }
    }
};
