/**
 * AnimatedCounter - Smooth number animation with easing
 * Creates a satisfying "counting up" effect for balance changes
 */

/**
 * Animate a number from one value to another
 * @param {HTMLElement} element - The counter element
 * @param {number} from - Starting value
 * @param {number} to - Ending value
 * @param {number} duration - Animation duration in ms
 * @param {string} format - Number format (e.g., "F2" for 2 decimal places)
 * @param {Object} dotNetRef - .NET object reference for callbacks
 */
export function animate(element, from, to, duration, format, dotNetRef) {
    if (!element) return;

    const startTime = performance.now();
    const decimals = format.match(/\d+/)?.[0] || 2;
    const diff = to - from;
    const isIncrease = diff > 0;

    // Add visual feedback class
    element.classList.add('counting');
    element.classList.add(isIncrease ? 'counting-up' : 'counting-down');

    // Haptic feedback at start if significant change
    if (Math.abs(diff) >= 0.01 && 'vibrate' in navigator) {
        navigator.vibrate(10);
    }

    function easeOutExpo(t) {
        return t === 1 ? 1 : 1 - Math.pow(2, -10 * t);
    }

    function update(currentTime) {
        const elapsed = currentTime - startTime;
        const progress = Math.min(elapsed / duration, 1);
        const easedProgress = easeOutExpo(progress);
        const currentValue = from + (diff * easedProgress);

        // Update display via .NET callback
        const formattedValue = currentValue.toFixed(parseInt(decimals));
        dotNetRef.invokeMethodAsync('UpdateDisplayValue', formattedValue);

        if (progress < 1) {
            requestAnimationFrame(update);
        } else {
            // Animation complete
            element.classList.remove('counting', 'counting-up', 'counting-down');
            element.classList.add('count-complete');

            // Success haptic on increase
            if (isIncrease && 'vibrate' in navigator) {
                navigator.vibrate([0, 20, 10, 30]);
            }

            // Remove complete class after flash
            setTimeout(() => {
                element.classList.remove('count-complete');
            }, 400);

            dotNetRef.invokeMethodAsync('AnimationComplete');
        }
    }

    requestAnimationFrame(update);
}
