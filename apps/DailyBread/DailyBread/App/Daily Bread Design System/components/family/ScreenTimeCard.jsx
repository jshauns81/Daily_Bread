import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { SectionHeader } from '../core/SectionHeader.jsx';
import { Icon } from '../core/Icon.jsx';

function Pool({ name, tag, minutes, floor, atRisk }) {
  return (
    <div style={{
      flex: 1, minWidth: 0, padding: 10,
      background: 'color-mix(in srgb, var(--ds-fill) 50%, transparent)',
      borderRadius: 'var(--ds-radius-swatch)',
      display: 'flex', flexDirection: 'column', gap: 4
    }}>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: 6 }}>
        <span style={{ fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)' }}>{name}</span>
        <span style={{ flex: 1 }} />
        <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-3)' }}>{tag}</span>
      </div>
      <div style={{
        fontSize: 'var(--ds-text-title3)', fontWeight: 'var(--ds-weight-bold)',
        color: 'var(--ds-accent)', lineHeight: 1.15
      }}>{minutes}</div>
      <div style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)' }}>in your pool</div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 4, paddingTop: 2 }}>
        <Icon name="lock.fill" size={11} color="var(--ds-label-3)" />
        <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)' }}>
          Always keeps <strong style={{ color: 'var(--ds-label)', fontWeight: 'var(--ds-weight-semibold)' }}>{floor}</strong>
        </span>
      </div>
      {atRisk ? (
        <div style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-help)' }}>
          Up to <strong style={{ fontWeight: 'var(--ds-weight-semibold)' }}>{atRisk} min</strong> on the line
        </div>
      ) : null}
    </div>
  );
}

export function ScreenTimeCard({ weekday, weekend, prices = [], onHistory, onSettings, style, ...rest }) {
  return (
    <GlassCard {...rest} style={style}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <SectionHeader>Screen time this week</SectionHeader>
          <span style={{ flex: 1 }} />
          {onHistory ? (
            <button type="button" onClick={onHistory} style={{
              display: 'inline-flex', alignItems: 'center', gap: 3, background: 'transparent',
              border: 'none', cursor: 'pointer', padding: 0, fontFamily: 'inherit',
              fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-semibold)',
              color: 'var(--ds-accent)'
            }}>History<Icon name="chevron.right" size={11} /></button>
          ) : null}
          {onSettings ? (
            <button type="button" onClick={onSettings} aria-label="Adjust screen time settings" style={{
              background: 'transparent', border: 'none', cursor: 'pointer', padding: 0,
              color: 'var(--ds-accent)', display: 'grid', placeItems: 'center'
            }}><Icon name="slider.horizontal.3" size={14} /></button>
          ) : null}
        </div>

        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
          {weekday ? <Pool name="Weekdays" tag="Mon–Fri" {...weekday} /> : null}
          {weekend ? <Pool name="Weekend" tag="Sat–Sun" {...weekend} /> : null}
        </div>

        {prices.length === 0 ? (
          <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            No chores are priced this week — nothing at risk. 😎
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div style={{
              fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-semibold)',
              color: 'var(--ds-label-2)'
            }}>What each chore is worth</div>
            {prices.map((p) => (
              <div key={p.name} style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                <span style={{ fontSize: 'var(--ds-text-subheadline)' }}>{p.name}</span>
                <span style={{ flex: 1 }} />
                <span style={{
                  fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-semibold)',
                  color: 'var(--ds-help)', whiteSpace: 'nowrap'
                }}>Miss once: −{p.minutes} min</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </GlassCard>
  );
}
