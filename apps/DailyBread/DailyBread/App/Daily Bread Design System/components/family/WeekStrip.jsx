import React from 'react';

export function WeekStrip({ days = [], style, ...rest }) {
  return (
    <div {...rest} style={{ display: 'flex', gap: 6, ...style }}>
      {days.map((d, i) => (
        <div key={i} style={{
          flex: 1, minWidth: 0, textAlign: 'center', padding: '8px 2px',
          background: 'color-mix(in srgb, var(--ds-fill) 50%, transparent)',
          borderRadius: 'var(--ds-radius-field)',
          display: 'flex', flexDirection: 'column', gap: 3
        }}>
          <span style={{
            fontSize: 'var(--ds-text-caption2)', fontWeight: 'var(--ds-weight-semibold)',
            color: 'var(--ds-label-2)'
          }}>{d.letter}</span>
          {d.amount ? (
            <span style={{
              fontSize: 'var(--ds-text-caption2)', fontWeight: 'var(--ds-weight-bold)',
              color: 'var(--ds-gold)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
            }}>{d.amount}</span>
          ) : (
            <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-3)' }}>—</span>
          )}
        </div>
      ))}
    </div>
  );
}
