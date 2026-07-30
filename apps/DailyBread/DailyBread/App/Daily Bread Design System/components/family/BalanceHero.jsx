import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { SectionHeader } from '../core/SectionHeader.jsx';

export function BalanceHero({ balance = '—', label = 'Balance', style, ...rest }) {
  return (
    <GlassCard {...rest} padding="var(--ds-card-padding-roomy)" style={style}>
      <SectionHeader kerning="eyebrow">{label}</SectionHeader>
      <div style={{
        fontFamily: 'var(--ds-font-rounded)', fontSize: 'var(--ds-text-hero)',
        fontWeight: 'var(--ds-weight-bold)', color: 'var(--ds-gold)',
        lineHeight: 1.08, marginTop: 2, fontVariantNumeric: 'tabular-nums'
      }}>{balance}</div>
    </GlassCard>
  );
}
