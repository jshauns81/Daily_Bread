import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { SectionHeader } from '../core/SectionHeader.jsx';
import { Icon } from '../core/Icon.jsx';

const URGENCY = {
  DueTonight: { icon: 'exclamationmark.circle.fill', tint: 'var(--ds-help)' },
  MustDoDaily: { icon: 'flame.fill', tint: 'var(--ds-help)' },
  GettingTight: { icon: 'clock.badge.exclamationmark', tint: 'var(--ds-gold)' }
};

export function AtRiskCard({ items = [], previewLine, totalMoney, totalMinutes, style, ...rest }) {
  if (items.length === 0) {
    return (
      <GlassCard {...rest} style={style}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>Nothing at risk today ✌️</span>
          {previewLine ? (
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-3)' }}>{previewLine}</span>
          ) : null}
        </div>
      </GlassCard>
    );
  }
  const parts = [];
  if (totalMoney) parts.push(totalMoney);
  if (totalMinutes) parts.push(totalMinutes + ' min');
  return (
    <GlassCard {...rest} style={style}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <SectionHeader>At risk today</SectionHeader>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {items.map((item, i) => {
            const u = URGENCY[item.urgency] || URGENCY.GettingTight;
            return (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <span style={{ width: 24, display: 'grid', placeItems: 'center', flex: '0 0 auto' }}>
                  <Icon name={u.icon} size={15} color={u.tint} />
                </span>
                <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
                  <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-medium)' }}>{item.name}</span>
                  <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{item.detail}</span>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 2, flex: '0 0 auto' }}>
                  {item.money ? (
                    <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)', color: 'var(--ds-gold)' }}>{item.money}</span>
                  ) : null}
                  {item.minutes ? (
                    <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)', color: 'var(--ds-help)' }}>−{item.minutes} min</span>
                  ) : null}
                </div>
              </div>
            );
          })}
        </div>
        {items.length >= 2 && parts.length ? (
          <div style={{ fontSize: 'var(--ds-text-footnote)', fontWeight: 'var(--ds-weight-bold)', paddingTop: 2 }}>
            On the line today: {parts.join(' + ')}
          </div>
        ) : null}
      </div>
    </GlassCard>
  );
}
