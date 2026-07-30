import React from 'react';
import { GlassCard } from './GlassCard.jsx';

export function StatTile({ value, label, color = 'var(--ds-label)', style, ...rest }) {
  return (
    <GlassCard {...rest} padding="var(--ds-card-padding-tight)" style={{ flex: 1, minWidth: 0, ...style }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 3 }}>
        <span style={{
          fontSize: 'var(--ds-text-title3)', fontWeight: 'var(--ds-weight-bold)',
          color, lineHeight: 1.15, whiteSpace: 'nowrap', maxWidth: '100%',
          overflow: 'hidden', textOverflow: 'ellipsis'
        }}>{value}</span>
        <span style={{
          fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)',
          textAlign: 'center', lineHeight: 1.25
        }}>{label}</span>
      </div>
    </GlassCard>
  );
}
