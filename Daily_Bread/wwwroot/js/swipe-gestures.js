/**
 * SwipeGestures - Touch gesture handling for swipeable cards
 * Provides iOS-style swipe-to-action functionality
 */
window.SwipeGestures = (function () {
    'use strict';

    const SWIPE_THRESHOLD = 80; // Pixels needed to trigger action
    const VELOCITY_THRESHOLD = 0.5; // Pixels per ms for quick swipe
    const MAX_SWIPE_DISTANCE = 150; // Max visual swipe distance

    const instances = new WeakMap();

    // Haptic feedback patterns (vibration in ms)
    const HAPTICS = {
        thresholdReached: [15],           // Quick tick when threshold crossed
        complete: [0, 30, 20, 40],         // Success: pause, buzz, pause, stronger buzz
        help: [20, 30, 20],                // Help: three quick pulses
        reset: [10]                        // Subtle feedback on reset
    };

    /**
     * Trigger haptic feedback if available
     * @param {string} type - Type of haptic pattern
     */
    function haptic(type) {
        if ('vibrate' in navigator) {
            const pattern = HAPTICS[type] || [10];
            navigator.vibrate(pattern);
        }
    }

    /**
     * Initialize swipe handling on a container element
     * @param {HTMLElement} container - The swipeable-card-container element
     * @param {Object} dotNetRef - DotNet object reference for callbacks
     */
    function init(container, dotNetRef) {
        // Guard: null container or dotNetRef
        if (!container || !dotNetRef) return;
        
        // Guard: already initialized - destroy first to prevent duplicate handlers
        if (instances.has(container)) {
            destroy(container);
        }

        const card = container.querySelector('.swipeable-card');
        if (!card) return;

        const state = {
            startX: 0,
            startY: 0,
            currentX: 0,
            startTime: 0,
            isDragging: false,
            isHorizontal: null,
            dotNetRef: dotNetRef,
            disposed: false,
            thresholdTriggered: false  // Track if we've already buzzed for threshold
        };

        const handlers = {
            touchStart: (e) => handleTouchStart(e, container, card, state),
            touchMove: (e) => handleTouchMove(e, container, card, state),
            touchEnd: (e) => handleTouchEnd(e, container, card, state),
            touchCancel: () => resetCard(container, card, state)
        };

        // Add touch event listeners
        container.addEventListener('touchstart', handlers.touchStart, { passive: true });
        container.addEventListener('touchmove', handlers.touchMove, { passive: false });
        container.addEventListener('touchend', handlers.touchEnd, { passive: true });
        container.addEventListener('touchcancel', handlers.touchCancel, { passive: true });

        // Store for cleanup
        instances.set(container, { handlers, state, card });
    }

    /**
     * Clean up swipe handling
     * @param {HTMLElement} container
     */
    function destroy(container) {
        // Guard: null container or not in WeakMap
        if (!container) return;
        
        const instance = instances.get(container);
        if (!instance) return;

        const { handlers, state } = instance;
        
        // Mark as disposed to prevent any pending callbacks
        if (state) {
            state.disposed = true;
            state.dotNetRef = null;
        }

        // Remove event listeners (safe even if container is disconnected from DOM)
        if (handlers) {
            try {
                container.removeEventListener('touchstart', handlers.touchStart);
                container.removeEventListener('touchmove', handlers.touchMove);
                container.removeEventListener('touchend', handlers.touchEnd);
                container.removeEventListener('touchcancel', handlers.touchCancel);
            } catch (e) {
                // Container may be disconnected, ignore errors
            }
        }

        instances.delete(container);
    }

    function handleTouchStart(e, container, card, state) {
        if (state.disposed || e.touches.length !== 1) return;

        const touch = e.touches[0];
        state.startX = touch.clientX;
        state.startY = touch.clientY;
        state.currentX = 0;
        state.startTime = Date.now();
        state.isDragging = false;
        state.isHorizontal = null;
        state.thresholdTriggered = false;
    }

    function handleTouchMove(e, container, card, state) {
        if (state.disposed || e.touches.length !== 1) return;

        const touch = e.touches[0];
        const deltaX = touch.clientX - state.startX;
        const deltaY = touch.clientY - state.startY;

        // Determine direction on first significant move
        if (state.isHorizontal === null) {
            if (Math.abs(deltaX) > 10 || Math.abs(deltaY) > 10) {
                state.isHorizontal = Math.abs(deltaX) > Math.abs(deltaY);
            }
        }

        // If vertical scroll, don't interfere
        if (state.isHorizontal === false) {
            return;
        }

        // Horizontal swipe - prevent scroll and handle
        if (state.isHorizontal === true) {
            e.preventDefault();
            state.isDragging = true;
            state.currentX = deltaX;

            container.classList.add('dragging');

            // Apply resistance at edges
            const resistedX = applyResistance(deltaX);
            card.style.transform = `translateX(${resistedX}px)`;

            // Update visual state and haptics
            updateSwipeVisuals(container, deltaX, state);
        }
    }

    function handleTouchEnd(e, container, card, state) {
        if (state.disposed || !state.isDragging) return;

        const deltaX = state.currentX;
        const deltaTime = Date.now() - state.startTime;
        const velocity = Math.abs(deltaX) / deltaTime;

        // Check if threshold reached (by distance or velocity)
        const thresholdReached = Math.abs(deltaX) >= SWIPE_THRESHOLD || velocity >= VELOCITY_THRESHOLD;

        if (thresholdReached && Math.abs(deltaX) > 30) {
            const direction = deltaX > 0 ? 'right' : 'left';
            triggerAction(container, card, state, direction);
        } else {
            resetCard(container, card, state);
        }
    }

    function applyResistance(delta) {
        // Apply rubber-band resistance past max distance
        const sign = delta >= 0 ? 1 : -1;
        const abs = Math.abs(delta);
        
        if (abs <= MAX_SWIPE_DISTANCE) {
            return delta;
        }
        
        // Logarithmic resistance beyond max
        const excess = abs - MAX_SWIPE_DISTANCE;
        const resisted = MAX_SWIPE_DISTANCE + Math.log10(excess + 1) * 30;
        return sign * resisted;
    }

    function updateSwipeVisuals(container, deltaX, state) {
        const wasThresholdReached = state.thresholdTriggered;
        const isNowThresholdReached = Math.abs(deltaX) >= SWIPE_THRESHOLD;

        // Clear previous classes
        container.classList.remove('swiping-left', 'swiping-right',
            'threshold-reached-left', 'threshold-reached-right');

        if (deltaX > 10) {
            container.classList.add('swiping-right');
            if (deltaX >= SWIPE_THRESHOLD) {
                container.classList.add('threshold-reached-right');
            }
        } else if (deltaX < -10) {
            container.classList.add('swiping-left');
            if (deltaX <= -SWIPE_THRESHOLD) {
                container.classList.add('threshold-reached-left');
            }
        }

        // Haptic feedback when crossing threshold (only once per swipe)
        if (isNowThresholdReached && !wasThresholdReached) {
            haptic('thresholdReached');
            state.thresholdTriggered = true;
        } else if (!isNowThresholdReached && wasThresholdReached) {
            // User pulled back below threshold - reset so they can feel it again
            state.thresholdTriggered = false;
        }
    }

    function triggerAction(container, card, state, direction) {
        // Guard: check if disposed before triggering callback
        if (state.disposed || !state.dotNetRef) {
            resetCard(container, card, state);
            return;
        }
        
        // Add completing animation
        const animClass = direction === 'right' ? 'completing' : 'help-requesting';
        container.classList.add(animClass);

        // Satisfying haptic feedback based on action
        haptic(direction === 'right' ? 'complete' : 'help');

        // Clean up visual state
        container.classList.remove('dragging', 'swiping-left', 'swiping-right',
            'threshold-reached-left', 'threshold-reached-right');

        // Capture dotNetRef before async operation in case state changes
        const dotNetRef = state.dotNetRef;
        
        // Notify Blazor
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('HandleSwipeComplete', direction)
                .catch(err => {
                    // Ignore errors from disposed components or disconnected circuits
                    if (!err.message?.includes('disposed') && 
                        !err.message?.includes('disconnected')) {
                        console.warn('Swipe callback failed:', err);
                    }
                });
        }

        // Reset card position after animation (for help action which doesn't dismiss)
        if (direction === 'left') {
            setTimeout(() => {
                if (!state.disposed) {
                    card.style.transform = '';
                    container.classList.remove(animClass);
                }
            }, 400);
        }
    }

    function resetCard(container, card, state) {
        container.classList.remove('dragging', 'swiping-left', 'swiping-right',
            'threshold-reached-left', 'threshold-reached-right');
        card.style.transform = '';
        state.isDragging = false;
        state.isHorizontal = null;
    }

    // Public API
    return {
        init,
        destroy
    };
})();
