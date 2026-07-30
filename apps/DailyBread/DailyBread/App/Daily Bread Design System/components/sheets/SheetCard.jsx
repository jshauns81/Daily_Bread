import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { SectionHeader } from '../core/SectionHeader.jsx';

export function SheetCard({ title, children, style, ...rest }) {
  return (
    <GlassCard {...rest} style={style}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'stretch', gap: 14 }}>
        {title ? <SectionHeader>{title}</SectionHeader> : null}
        {children}
      </div>
    </GlassCard>
  );
}
