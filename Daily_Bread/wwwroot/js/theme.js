// Daily Bread theme controller — persists the chosen palette + light/dark mode
// per device (localStorage) and applies it instantly to <html>.
// The no-flash bootstrap lives inline in App.razor <head>; this drives changes
// from the in-app theme picker and the quick top-bar toggle.
window.DBTheme = (function () {
    var BG = {
        ultraviolet: { dark: '#0D0A14', light: '#FAF9FD' },
        voltage:     { dark: '#0E1116', light: '#FAFBFA' },
        cobalt:      { dark: '#0B1220', light: '#F5F8FF' },
        tangerine:   { dark: '#131114', light: '#FAF8F7' }
    };

    function current() {
        var t = localStorage.getItem('db-theme');
        var m = localStorage.getItem('db-mode');
        if (!BG[t]) t = 'ultraviolet';
        if (m !== 'light' && m !== 'dark') m = 'dark';
        return [t, m];
    }

    function apply(t, m) {
        var de = document.documentElement;
        de.setAttribute('data-theme', t);
        de.setAttribute('data-mode', m);
        de.style.backgroundColor = BG[t][m];
        var mc = document.querySelector('meta[name=theme-color]');
        if (mc) mc.setAttribute('content', BG[t][m]);
        syncToggles();
    }

    // Keep every quick-toggle button's icon in sync with the current mode.
    // Mono Lucide stroke icon from the app sprite (design #14 shell).
    function toggleIcon(name) {
        return '<svg width="15" height="15" fill="none" stroke="currentColor" stroke-width="1.8" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
            '<use href="/images/lucide-sprite.svg#lucide-' + name + '"/></svg>';
    }
    function syncToggles() {
        var m = current()[1];
        document.querySelectorAll('[data-theme-toggle]').forEach(function (el) {
            el.innerHTML = toggleIcon(m === 'light' ? 'sun' : 'moon');
            el.setAttribute('aria-label', m === 'light' ? 'Switch to dark mode' : 'Switch to light mode');
        });
    }

    return {
        get: function () { return current(); },
        setTheme: function (t) {
            if (!BG[t]) return;
            localStorage.setItem('db-theme', t);
            apply(t, current()[1]);
        },
        setMode: function (m) {
            if (m !== 'light' && m !== 'dark') return;
            localStorage.setItem('db-mode', m);
            apply(current()[0], m);
        },
        toggleMode: function () {
            this.setMode(current()[1] === 'light' ? 'dark' : 'light');
        },
        sync: syncToggles
    };
})();

// Self-wiring quick toggle: any element with [data-theme-toggle] flips light/dark.
// Uses event delegation so it survives Blazor enhanced navigation.
(function () {
    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-theme-toggle]');
        if (btn) {
            e.preventDefault();
            window.DBTheme.toggleMode();
        }
    });
    function sync() { try { window.DBTheme.sync(); } catch (e) {} }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', sync);
    } else {
        sync();
    }
    // Re-sync after Blazor enhanced navigation swaps the DOM
    var iv = setInterval(function () {
        if (window.Blazor && window.Blazor.addEventListener) {
            clearInterval(iv);
            window.Blazor.addEventListener('enhancedload', sync);
        }
    }, 200);
    setTimeout(function () { clearInterval(iv); }, 10000);
})();
