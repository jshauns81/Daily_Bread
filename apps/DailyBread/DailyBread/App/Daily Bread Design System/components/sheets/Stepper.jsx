import React from 'react';
import { Icon } from '../core/Icon.jsx';

export function Stepper({ label, value = 1, min = 1, max = 7, onChange, style, ...rest }) {
  const btn = (delta, icon, disabled) => (
    <button type="button" disabled={disabled}
      onClick={() => onChange && onChange(Math.max(min, Math.min(max, value + delta)))}
      style={{
        width: 40, height: 30, border: 'none', cursor: disabled ? 'default' : 'pointer',
        background: 'transparent', color: disabled ? 'var(--ds-label-3)' : 'var(--ds-label)',
        display: 'grid', placeItems: 'center'
      }}>
      <Icon name={icon} size={15} />
    </button>
  );
  return (
    <div {...rest} style={{ display: 'flex', alignItems: 'center', gap: 12, minHeight: 'var(--ds-touch-target)', ...style }}>
      <span style={{
        flex: 1, minWidth: 0, fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)',
        fontVariantNumeric: 'tabular-nums'
      }}>{label}</span>
      <span style={{
        display: 'flex', alignItems: 'center', borderRadius: 'var(--ds-radius-field)',
        background: 'var(--ds-fill-strong)', overflow: 'hidden'
      }}>
        {btn(-1, 'minus', value <= min)}
        <span style={{ width: '.5px', height: 18, background: 'var(--ds-label-3)' }} />
        {btn(1, 'plus', value >= max)}
      </span>
    </div>
  );
}
