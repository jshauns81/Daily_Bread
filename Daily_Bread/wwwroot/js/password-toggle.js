/**
 * Password Toggle Functionality
 * Handles password visibility toggle for login forms
 * Works in static SSR mode without Blazor interactivity
 */

(function () {
    'use strict';

    function initPasswordToggles() {
        // Find all password toggle buttons
        const toggleButtons = document.querySelectorAll('.password-toggle');

        toggleButtons.forEach(button => {
            // Find the associated input field (previous sibling in the input-group)
            const inputGroup = button.closest('.input-group');
            if (!inputGroup) return;

            const passwordInput = inputGroup.querySelector('input[type="password"], input[type="text"]');
            if (!passwordInput) return;

            // Handle click event
            button.addEventListener('click', function (e) {
                e.preventDefault();

                // Toggle input type
                const isPassword = passwordInput.type === 'password';
                passwordInput.type = isPassword ? 'text' : 'password';

                // Update button aria-label and title
                const newLabel = isPassword ? 'Hide password' : 'Show password';
                button.setAttribute('aria-label', newLabel);
                button.setAttribute('title', newLabel);

                // Update icon
                const icon = button.querySelector('.bi');
                if (icon) {
                    if (isPassword) {
                        icon.classList.remove('bi-eye');
                        icon.classList.add('bi-eye-slash');
                    } else {
                        icon.classList.remove('bi-eye-slash');
                        icon.classList.add('bi-eye');
                    }
                }
            });
        });
    }

    // Initialize on DOM load
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initPasswordToggles);
    } else {
        initPasswordToggles();
    }

    // Re-initialize on Blazor enhanced navigation
    if (window.Blazor) {
        window.Blazor.addEventListener('enhancedload', initPasswordToggles);
    }
})();
