import React from 'react';

export function TransactionRow({ description, date, amount, negative, style, ...rest }) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'center', gap: 12, padding: '9px 0',
      minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{
          fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)',
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
        }}>{description}</span>
        <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{date}</span>
      </div>
      <span style={{
        fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
        color: negative ? 'var(--ds-label-2)' : 'var(--ds-gold)',
        flex: '0 0 auto', fontVariantNumeric: 'tabular-nums'
      }}>{amount}</span>
    </div>
  );
}
