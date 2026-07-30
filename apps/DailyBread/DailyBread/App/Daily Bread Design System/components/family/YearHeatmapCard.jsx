import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { SectionHeader } from '../core/SectionHeader.jsx';

/* The rainbow year — IMPLEMENTATION.md §1.2.
   Hue is the REAL day-of-year, never position within the window, so a given day is
   the same colour on every surface (card, full year, wall, widget). Intensity is the
   required-chore ratio on a LINEAR ramp from a 34% saturation floor (the `forgiving`
   curve, decided after testing three against a full year). A bad day is a drained
   colour, never a grey hole. Optionals only ever ADD bloom — they never withhold
   colour, because dimming a square for declining an optional punishes a kid for
   taking a choice you gave him. */

const SIZES = {
  card: { weeks: 12, cell: 18, gap: 3, scroll: false },
  full: { weeks: 52, cell: 11, gap: 2.5, scroll: true },
  wall: { weeks: 52, cell: 14, gap: 3, scroll: true }
};

const LEGACY = {
  AllComplete: { requiredDone: 1, requiredTotal: 1, optionalDone: 1, optionalTotal: 1 },
  PartialComplete: { requiredDone: 2, requiredTotal: 3, optionalDone: 0, optionalTotal: 0 },
  NoneComplete: { requiredDone: 0, requiredTotal: 3, optionalDone: 0, optionalTotal: 0 },
  none: { isFuture: true }
};

function dayOfYear(d) {
  return Math.floor((d - new Date(d.getFullYear(), 0, 0)) / 86400000);
}

/** Which way the lightness ramp runs. Read from the live theme's card surface rather
    than a `dark` prop, so every one of the six themes gets it right unasked. */
function isDarkSurface() {
  if (typeof window === 'undefined') return false;
  const v = getComputedStyle(document.documentElement).getPropertyValue('--ds-card').trim();
  let r, g, b;
  const hex = v.match(/^#([0-9a-f]{3,8})$/i);
  if (hex) {
    const h = hex[1].length < 6 ? hex[1].split('').map((c) => c + c).join('') : hex[1];
    r = parseInt(h.slice(0, 2), 16); g = parseInt(h.slice(2, 4), 16); b = parseInt(h.slice(4, 6), 16);
  } else {
    const n = v.match(/[\d.]+/g);
    if (!n || n.length < 3) return false;
    [r, g, b] = n.map(Number);
  }
  return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 < 0.5;
}

/** Deterministic sample so specimens render identically every time. */
function sample(n) {
  const out = [];
  for (let i = 0; i < n; i++) {
    const h = (i * 2654435761) % 100;
    const requiredTotal = 3 + (i % 3);
    const requiredDone = h > 62 ? requiredTotal : h > 34 ? Math.max(1, requiredTotal - 1) : h > 22 ? 1 : 0;
    out.push({ requiredDone, requiredTotal, optionalDone: h > 88 ? 2 : 0, optionalTotal: 2 });
  }
  // The current week isn't over: the rest of it renders as future outlines.
  for (let i = Math.max(0, n - 3); i < n; i++) out[i] = { isFuture: true };
  return out;
}

function normalise(d) {
  if (typeof d === 'string') return LEGACY[d] || LEGACY.none;
  return d || LEGACY.none;
}

export function YearHeatmapCard({
  title = 'Your year', size = 'card', days, perfectDays, onDay, cell, weeks, style, ...rest
}) {
  const s = SIZES[size] || SIZES.card;
  const edge = cell != null ? cell : s.cell;
  const cols = weeks != null ? weeks : s.weeks;
  const raw = days && days.length ? days : sample(cols * 7);
  const cells = raw.slice(-cols * 7).map(normalise);
  const perfect = perfectDays != null
    ? perfectDays
    : cells.filter((d) => !d.isFuture && d.requiredTotal > 0 && d.requiredDone >= d.requiredTotal).length;

  const todayDoy = dayOfYear(new Date());
  const dark = isDarkSurface();
  const columns = [];
  for (let i = 0; i < cells.length; i += 7) columns.push(cells.slice(i, i + 7));

  const grid = (
    <div style={{ display: 'flex', gap: s.gap, direction: 'ltr' }}>
      {columns.map((week, wi) => (
        <div key={wi} style={{ display: 'flex', flexDirection: 'column', gap: s.gap }}>
          {week.map((d, di) => {
            const index = wi * 7 + di;
            const doy = todayDoy - (cells.length - 1 - index);
            const hue = ((((doy % 365) + 365) % 365) / 365) * 360;
            const ratio = d.requiredTotal > 0 ? Math.min(1, d.requiredDone / d.requiredTotal) : 0;
            const complete = !d.isFuture && d.requiredTotal > 0 && d.requiredDone >= d.requiredTotal;
            const bloom = complete && d.optionalTotal > 0 && d.optionalDone > 0;
            const box = {
              width: edge, height: edge, borderRadius: 'var(--ds-radius-heatmap)',
              cursor: onDay && !d.isFuture ? 'pointer' : 'default'
            };
            if (d.isFuture) {
              box.background = 'transparent';
              box.boxShadow = 'inset 0 0 0 1px color-mix(in srgb, var(--ds-label) 9%, transparent)';
            } else {
              // Opaque by construction. Mixing toward a translucent fill let the card show
              // through and dropped drained days to ~15% saturation — the grey hole the
              // spec forbids — and made the same day a different colour on every surface.
              const sat = 34 + ratio * 52;                       // 34% → 86%
              const light = dark ? 16 + ratio * 40 : 84 - ratio * 40;
              box.background = `hsl(${hue.toFixed(1)} ${sat.toFixed(0)}% ${light.toFixed(0)}%)`;
              // bloom scales with the cell or it smears into its neighbours on a year wall
              if (bloom) box.boxShadow = `0 0 ${(edge * 0.55).toFixed(1)}px hsl(${hue.toFixed(1)} 90% 55% / .7)`;
            }
            return <div key={di} onClick={() => onDay && !d.isFuture && onDay({ ...d, dayOfYear: doy })} style={box} />;
          })}
        </div>
      ))}
    </div>
  );

  return (
    <GlassCard {...rest} style={style}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <SectionHeader>{title}</SectionHeader>
        {s.scroll ? (
          <div style={{ display: 'flex', alignItems: 'flex-start', overflowX: 'auto', direction: 'rtl' }}>{grid}</div>
        ) : grid}
        {/* No less→more legend: a rainbow has nothing to explain, and the old legend
            described three status colours that no longer exist. */}
        {perfect > 0 ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            <strong style={{ color: 'var(--ds-label)' }}>{perfect}</strong> perfect day{perfect === 1 ? '' : 's'} this year ✨
          </span>
        ) : null}
      </div>
    </GlassCard>
  );
}
