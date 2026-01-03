/**
 * Heatmap utilities for YearActivityCard
 * Provides mobile detection and scroll functionality
 */

/**
 * Check if the current device is mobile (based on viewport width)
 * @returns {boolean} True if viewport width is <= 768px
 */
window.isMobileDevice = function() {
    return window.innerWidth <= 768;
};

/**
 * Scroll the heatmap container to a specific position
 * @param {HTMLElement} element - The scrollable container element
 * @param {number} position - The scroll position in pixels
 */
window.scrollHeatmap = function(element, position) {
    if (element) {
        element.scrollLeft = position;
    }
};
