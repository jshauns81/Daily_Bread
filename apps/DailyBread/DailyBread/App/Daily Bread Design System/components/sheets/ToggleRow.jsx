import React from 'react';

export function ToggleRow({ label, description, checked, onChange, style, ...rest }) {
  return (
    <label {...rest} style={{
      display: 'flex', alignItems: 'center', gap: 12, cursor: 'pointer',
      minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <span style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{ fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)' }}>{label}</span>
        {description ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{description}</span>
        ) : null}
      </span>
      <span
        onClick={(e) => { e.preventDefault(); onChange && onChange(!checked); }}
        style={{
          width: 51, height: 31, flex: '0 0 auto', borderRadius: 999,
          background: checked ? 'var(--ds-success)' : 'var(--ds-fill-strong)',
          transition: 'background var(--ds-ease-in-out)', position: 'relative'
        }}>
        <span style={{
          position: 'absolute', top: 2, left: checked ? 22 : 2,
          width: 27, height: 27, borderRadius: '50%', background: '#fff',
          boxShadow: '0 2px 5px rgb(0 0 0 / .18)',
          transition: 'left var(--ds-ease-in-out)'
        }} />
      </span>
    </label>
  );
}
