import React from 'react';
import { Icon } from '../core/Icon.jsx';

export const DB_THEMES = [
  { key: 'sunroom', name: 'Sunroom', mood: 'warm · light', top: '#FFFDF9', bottom: '#FBF1E2', card: '#FFFFFF', accent: '#C7284F', gold: '#C98A1E', dark: false },
  { key: 'sky', name: 'Sky', mood: 'calm · light', top: '#FBFCFF', bottom: '#EAF1FE', card: '#FFFFFF', accent: '#3D7BE0', gold: '#C98A1E', dark: false },
  { key: 'rosewater', name: 'Rosewater', mood: 'soft · light', top: '#FFF9FB', bottom: '#FBEAF1', card: '#FFFFFF', accent: '#D24E86', gold: '#C98A1E', dark: false },
  { key: 'meadow', name: 'Meadow', mood: 'fresh · light', top: '#F8FBF6', bottom: '#E9F4E7', card: '#FFFFFF', accent: '#3E9E6B', gold: '#C98A1E', dark: false },
  { key: 'mulberry', name: 'Mulberry', mood: 'cozy · dark', top: '#3E1B30', bottom: '#2A1220', card: '#4A2237', accent: '#EA6E92', gold: '#E7B44A', dark: true },
  { key: 'harbor', name: 'Harbor', mood: 'quiet · dark', top: '#223049', bottom: '#161E2C', card: '#2A3852', accent: '#5B9BE0', gold: '#E7B44A', dark: true }
];

/** A live preview of a theme: its real background, a floating card with the
 *  accent and a gold coin, and the progress glow. What she taps is what the app
 *  becomes. */
export function ThemeSwatch({ theme, width = 76, height = 52, style, ...rest }) {
  const t = typeof theme === 'string' ? DB_THEMES.find((x) => x.key === theme) : theme;
  if (!t) return null;
  const stroke = t.dark ? 'rgb(255 255 255 / .07)' : 'rgb(0 0 0 / .05)';
  return (
    <div {...rest} style={{
      width, height, flex: '0 0 auto', borderRadius: 'var(--ds-radius-swatch)',
      background: 'linear-gradient(to bottom, ' + t.top + ', ' + t.bottom + (t.dark ? ' 60%' : '') + ')',
      boxShadow: 'inset 0 0 0 .5px ' + stroke, position: 'relative',
      padding: 7, boxSizing: 'border-box',
      display: 'flex', flexDirection: 'column', justifyContent: 'space-between', gap: 4, ...style
    }}>
      <div style={{
        height: 20, borderRadius: 6, background: t.card,
        boxShadow: 'inset 0 0 0 .5px ' + stroke,
        display: 'flex', alignItems: 'center', gap: 4, padding: '0 5px'
      }}>
        <span style={{ width: 8, height: 8, borderRadius: '50%', background: t.accent }} />
        <span style={{ width: 8, height: 8, borderRadius: '50%', background: t.gold }} />
      </div>
      <div style={{
        height: 5, borderRadius: 999,
        background: 'linear-gradient(to right, ' + t.accent + ', #E7A83C)'
      }} />
    </div>
  );
}

export function ThemeRow({ theme, selected, onSelect, style, ...rest }) {
  const t = typeof theme === 'string' ? DB_THEMES.find((x) => x.key === theme) : theme;
  if (!t) return null;
  return (
    <button {...rest} type="button" onClick={() => onSelect && onSelect(t.key)} style={{
      display: 'flex', alignItems: 'center', gap: 14, width: '100%',
      padding: '4px 0', background: 'transparent', border: 'none',
      cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
      minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <ThemeSwatch theme={t} />
      <span style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-label)' }}>{t.name}</span>
        <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{t.mood}</span>
      </span>
      <span style={{
        width: 22, height: 22, flex: '0 0 auto', borderRadius: '50%',
        display: 'grid', placeItems: 'center', boxSizing: 'border-box',
        border: selected ? '6px solid ' + t.accent : '1.5px solid var(--ds-label-3)'
      }}>
        {selected ? <Icon name="checkmark" size={10} color="#fff" strokeWidth={4} /> : null}
      </span>
    </button>
  );
}
