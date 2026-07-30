import React from 'react';

export const LEVEL_TIERS = [
  { level: 0, title: 'STARTER', icon: '🌱', color: 'var(--db-level-0)' },
  { level: 1, title: 'COMMON', icon: '⭐', color: 'var(--db-level-1)' },
  { level: 2, title: 'UNCOMMON', icon: '⚡', color: 'var(--db-level-2)' },
  { level: 3, title: 'RARE', icon: '🔥', color: 'var(--db-level-3)' },
  { level: 4, title: 'EPIC', icon: '💎', color: 'var(--db-level-4)' },
  { level: 5, title: 'LEGENDARY', icon: '👑', color: 'var(--db-level-5)' }
];

/** % of today's chores done → tier. Mirrors KidHomeView.tier(for:) exactly.
 *  Capitalised so the compiler exposes it on the window namespace. */
export function TierFor(percent) {
  if (percent >= 100) return LEVEL_TIERS[5];
  if (percent >= 75) return LEVEL_TIERS[4];
  if (percent >= 50) return LEVEL_TIERS[3];
  if (percent >= 25) return LEVEL_TIERS[2];
  if (percent >= 1) return LEVEL_TIERS[1];
  return LEVEL_TIERS[0];
}

export function LevelBadge({ percent = 0, style, ...rest }) {
  const t = TierFor(percent);
  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, ...style }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10,
        padding: '12px 24px', borderRadius: 'var(--ds-radius-capsule)',
        background: 'linear-gradient(135deg, ' + t.color + ', color-mix(in srgb, ' + t.color + ' 65%, transparent))',
        boxShadow: 'var(--ds-level-glow) color-mix(in srgb, ' + t.color + ' 50%, transparent)',
        transition: 'background var(--ds-spring), box-shadow var(--ds-spring)'
      }}>
        <span style={{ fontSize: 30, lineHeight: 1, fontFamily: 'var(--ds-font-emoji)' }}>{t.icon}</span>
        <span style={{ display: 'flex', alignItems: 'baseline', gap: 4 }}>
          <span style={{ fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-bold)', color: 'rgb(255 255 255 / .9)' }}>LVL</span>
          <span style={{ fontSize: 'var(--ds-text-title)', fontWeight: 'var(--ds-weight-heavy)', color: '#fff', lineHeight: 1 }}>{t.level}</span>
        </span>
      </div>
      <span style={{
        fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
        color: t.color, letterSpacing: 'var(--ds-kerning-level)'
      }}>{t.title}</span>
    </div>
  );
}

export function XPBar({ done = 0, total = 0, percent, style, ...rest }) {
  const pct = percent != null ? percent : (total ? Math.round((done / total) * 100) : 0);
  const t = TierFor(pct);
  const milestones = [['⚡', 25], ['🔥', 50], ['💎', 75], ['👑', 100]];
  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', gap: 10, ...style }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span style={{
          fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
          textTransform: 'uppercase', letterSpacing: '0.6px', color: 'var(--ds-label-2)'
        }}>Today's progress</span>
        <span style={{ flex: 1 }} />
        <span style={{
          fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-bold)',
          color: 'var(--ds-accent)', fontVariantNumeric: 'tabular-nums'
        }}>{done} / {total} XP</span>
      </div>
      <div style={{
        height: 'var(--ds-xp-bar-height)', borderRadius: 'var(--ds-radius-capsule)',
        background: 'color-mix(in srgb, var(--ds-label) 10%, transparent)', overflow: 'hidden'
      }}>
        <div style={{
          width: 'max(12px, ' + pct + '%)', height: '100%',
          borderRadius: 'var(--ds-radius-capsule)',
          background: 'linear-gradient(to right, var(--ds-accent), ' + t.color + ')',
          transition: 'width var(--ds-spring-slow)'
        }} />
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
        {milestones.map(([ic, at]) => {
          const reached = pct >= at;
          return (
            <span key={at} style={{
              fontSize: 15, lineHeight: 1, fontFamily: 'var(--ds-font-emoji)',
              opacity: reached ? 1 : 0.28, filter: reached ? 'none' : 'grayscale(1)',
              transform: reached ? 'none' : 'scale(0.85)',
              transition: 'all var(--ds-spring)'
            }}>{ic}</span>
          );
        })}
      </div>
    </div>
  );
}
