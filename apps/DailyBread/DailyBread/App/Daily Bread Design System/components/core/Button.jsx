import React from 'react';
import { Icon } from './Icon.jsx';

/**
 * The four button treatments the app uses:
 *   prominent  → .borderedProminent tinted accent
 *   gold       → .borderedProminent tinted DB.gold (Approve — the Blessing)
 *   bordered   → .bordered, secondary tint
 *   capsule    → the Help/Done pair on the kid's Next Up card
 */
const shapes = {
  prominent: {
    background: 'var(--ds-accent)', color: 'var(--ds-on-accent)',
    borderRadius: 'var(--ds-radius-swatch)', fontWeight: 'var(--ds-weight-semibold)'
  },
  gold: {
    background: 'var(--ds-gold)', color: 'rgb(0 0 0 / .8)',
    borderRadius: 'var(--ds-radius-swatch)', fontWeight: 'var(--ds-weight-bold)'
  },
  bordered: {
    background: 'var(--ds-fill-strong)', color: 'var(--ds-label)',
    borderRadius: 'var(--ds-radius-swatch)', fontWeight: 'var(--ds-weight-medium)'
  },
  destructive: {
    background: 'var(--ds-help)', color: '#fff',
    borderRadius: 'var(--ds-radius-swatch)', fontWeight: 'var(--ds-weight-semibold)'
  },
  capsuleAccent: {
    background: 'var(--ds-accent)', color: '#fff',
    borderRadius: 'var(--ds-radius-capsule)', fontWeight: 'var(--ds-weight-semibold)'
  },
  capsuleHelp: {
    background: 'color-mix(in srgb, var(--ds-help) 16%, transparent)', color: 'var(--ds-help)',
    borderRadius: 'var(--ds-radius-capsule)', fontWeight: 'var(--ds-weight-semibold)'
  },
  plain: {
    background: 'transparent', color: 'var(--ds-accent)',
    borderRadius: 'var(--ds-radius-capsule)', fontWeight: 'var(--ds-weight-semibold)'
  }
};

export function Button({
  variant = 'prominent', icon, block, disabled, busy, size = 'md',
  children, onClick, style, ...rest
}) {
  const pad = size === 'sm' ? '6px 12px' : size === 'lg' ? '13px 20px' : '11px 16px';
  const font = size === 'sm' ? 'var(--ds-text-caption)' : 'var(--ds-text-subheadline)';
  return (
    <button
      {...rest} type="button" disabled={disabled || busy} onClick={onClick}
      style={{
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center', gap: 6,
        border: 'none', cursor: disabled || busy ? 'default' : 'pointer',
        fontFamily: 'inherit', fontSize: font, padding: pad,
        minHeight: size === 'sm' ? 32 : 'var(--ds-touch-target)',
        width: block ? '100%' : undefined, whiteSpace: 'nowrap',
        transition: 'background var(--ds-ease-in-out), opacity var(--ds-ease-in-out)',
        ...shapes[variant],
        ...(disabled ? { background: 'color-mix(in srgb, var(--ds-label-2) 30%, transparent)', color: 'var(--ds-label-2)' } : null),
        ...style
      }}
    >
      {busy ? '…' : icon ? <Icon name={icon} size={size === 'sm' ? 14 : 16} /> : null}
      {busy ? null : children}
    </button>
  );
}
