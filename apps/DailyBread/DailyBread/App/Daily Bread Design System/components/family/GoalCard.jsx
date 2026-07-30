import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { ProgressBar } from '../core/ProgressBar.jsx';
import { Icon } from '../core/Icon.jsx';

export function GoalCard({ name, current, target, percent, empty, onTap, style, ...rest }) {
  return (
    <GlassCard {...rest} style={{ cursor: onTap ? 'pointer' : undefined, ...style }} onClick={onTap}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <Icon name="target" size={12} color="var(--ds-label-2)" />
          <span style={{
            fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
            textTransform: 'uppercase', letterSpacing: 'var(--ds-kerning-section)',
            color: 'var(--ds-label-2)'
          }}>{empty ? 'Goals' : 'Goal'}</span>
        </div>
        {empty ? (
          <React.Fragment>
            <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)' }}>Set a savings goal!</span>
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-accent)' }}>Something to work toward →</span>
          </React.Fragment>
        ) : (
          <React.Fragment>
            <span style={{ fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)' }}>{name}</span>
            <ProgressBar value={percent} total={100} />
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{current} of {target}</span>
          </React.Fragment>
        )}
      </div>
    </GlassCard>
  );
}
