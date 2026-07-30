import React from 'react';

export function SheetField({ label, value, valueColor, children, style, ...rest }) {
  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', gap: 8, ...style }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
        <span style={{
          fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-medium)',
          color: 'var(--ds-label)'
        }}>{label}</span>
        <span style={{ flex: 1 }} />
        {value != null ? (
          <span style={{
            fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
            color: valueColor || 'var(--ds-label-2)', fontVariantNumeric: 'tabular-nums'
          }}>{value}</span>
        ) : null}
      </div>
      {children}
    </div>
  );
}
