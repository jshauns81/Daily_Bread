import React from 'react';

/**
 * SF Symbols stand-in. The real app uses SF Symbols, which cannot ship to the
 * web — this renders the closest Lucide glyph instead (a flagged substitution;
 * see readme ICONOGRAPHY). Load Lucide from CDN before the bundle:
 *   <script src="https://unpkg.com/lucide@0.454.0/dist/umd/lucide.min.js"></script>
 */
export const SF_TO_LUCIDE = {
  house: 'house', 'sun.max': 'sun', 'dollarsign.circle': 'circle-dollar-sign',
  trophy: 'trophy', checklist: 'list-checks', 'checkmark.circle': 'circle-check',
  gearshape: 'settings', 'checkmark.circle.fill': 'circle-check-big',
  circle: 'circle', checkmark: 'check', 'checkmark.seal': 'badge-check',
  'checkmark.seal.fill': 'badge-check', 'hand.thumbsup': 'thumbs-up',
  'questionmark.circle': 'circle-help', 'exclamationmark.circle': 'circle-alert',
  'exclamationmark.circle.fill': 'circle-alert', 'flame.fill': 'flame',
  'clock.badge.exclamationmark': 'clock-alert', 'lock.fill': 'lock',
  'minus.circle': 'circle-minus', 'arrow.uturn.up': 'redo-2',
  'clock.arrow.circlepath': 'history', 'chevron.right': 'chevron-right',
  'chevron.up': 'chevron-up', 'chevron.down': 'chevron-down',
  'chevron.up.chevron.down': 'chevrons-up-down', plus: 'plus',
  'chevron.left': 'chevron-left', 'moon.stars': 'moon-star', key: 'key',
  'ellipsis.circle': 'ellipsis', 'list.bullet': 'list',
  'square.grid.2x2': 'grid-2x2', 'slider.horizontal.3': 'sliders-horizontal',
  'party.popper': 'party-popper', calendar: 'calendar', gift: 'gift',
  target: 'target', car: 'car', 'person.2': 'users',
  'wifi.exclamationmark': 'wifi-off', 'exclamationmark.triangle': 'triangle-alert'
};

/**
 * Paint one glyph into `host` using Lucide's documented createIcons() scan.
 * The host's children are managed imperatively and never by React, so letting
 * Lucide replace the placeholder <i> is safe.
 *
 * Returns 'ok' | 'unknown' (glyph absent from this build — stop trying) |
 * 'waiting' (Lucide not loaded yet — worth a retry).
 */
function paint(host, glyph, size, strokeWidth) {
  const L = window.lucide;
  if (!L || typeof L.createIcons !== 'function') return 'waiting';
  try {
    host.innerHTML = '<i data-lucide="' + glyph + '"></i>';
    L.createIcons();
    const svg = host.querySelector('svg');
    if (!svg) { host.innerHTML = ''; return 'unknown'; }
    svg.setAttribute('width', size);
    svg.setAttribute('height', size);
    svg.setAttribute('stroke-width', strokeWidth);
    svg.style.display = 'block';
    return 'ok';
  } catch (e) {
    host.innerHTML = '';
    return 'unknown';
  }
}

export function Icon({ name, size = 17, color, strokeWidth = 2, style, ...rest }) {
  const ref = React.useRef(null);
  const glyph = SF_TO_LUCIDE[name] || name;
  React.useEffect(() => {
    const host = ref.current;
    if (!host) return undefined;
    // 'unknown' stops here too: retrying a glyph this build doesn't have would
    // just spam the console 40 times per icon.
    if (paint(host, glyph, size, strokeWidth) !== 'waiting') return undefined;
    let tries = 0;
    const id = setInterval(() => {
      if (paint(host, glyph, size, strokeWidth) !== 'waiting' || ++tries > 40) clearInterval(id);
    }, 50);
    return () => clearInterval(id);
  }, [glyph, size, strokeWidth]);
  return (
    <span
      {...rest}
      ref={ref}
      aria-hidden="true"
      style={{
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        width: size, height: size, flex: '0 0 auto',
        color: color || 'currentColor', ...style
      }}
    />
  );
}
