import React from 'react';
import { Icon } from '../core/Icon.jsx';

export function ExpandRow({ expanded, moreCount, label, color = 'var(--ds-accent)', onToggle, style, ...rest }) {
  return (
    <button {...rest} type="button" onClick={onToggle} style={{
      display: 'flex', alignItems: 'center', gap: 8, width: '100%',
      padding: '10px 0', background: 'transparent', border: 'none',
      cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
      minHeight: 'var(--ds-touch-target)', color, ...style
    }}>
      <span style={{ flex: 1, fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)' }}>
        {expanded ? 'Show fewer' : moreCount + ' ' + label}
      </span>
      <Icon name={expanded ? 'chevron.up' : 'chevron.down'} size={12} strokeWidth={3} />
    </button>
  );
}

export function ConfirmRow({
  message, confirmTitle = 'Delete', keepTitle = 'Keep',
  tone = 'var(--ds-help)', onConfirm, onKeep, style, ...rest
}) {
  return (
    <div {...rest} style={{
      display: 'flex', flexDirection: 'column', gap: 10,
      padding: '10px 12px', margin: '0 -12px',
      borderRadius: 'var(--ds-radius-swatch)',
      background: 'color-mix(in srgb, ' + tone + ' 8%, transparent)', ...style
    }}>
      <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-medium)' }}>{message}</span>
      <div style={{ display: 'flex', gap: 10 }}>
        <button type="button" onClick={onKeep} style={{
          padding: '7px 14px', borderRadius: 'var(--ds-radius-field)', border: 'none',
          background: 'var(--ds-fill-strong)', color: 'var(--ds-label)', cursor: 'pointer',
          fontFamily: 'inherit', fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)'
        }}>{keepTitle}</button>
        <button type="button" onClick={onConfirm} style={{
          padding: '7px 14px', borderRadius: 'var(--ds-radius-field)', border: 'none',
          background: tone, color: '#fff', cursor: 'pointer',
          fontFamily: 'inherit', fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)'
        }}>{confirmTitle}</button>
      </div>
    </div>
  );
}
