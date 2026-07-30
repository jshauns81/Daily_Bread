import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { ProgressBar } from '../core/ProgressBar.jsx';

/** Rarity → hue. Exported so AchievementDefRow reads a badge identically. */
export const RARITY = {
  legendary: 'var(--ds-gold)', epic: 'var(--db-rarity-epic)',
  rare: 'var(--ds-accent)', uncommon: 'var(--ds-success)', common: 'var(--ds-label-2)'
};

export function AchievementCard({
  icon = '🏆', name, description, points, rarity = 'common',
  earned, progress, target, style, ...rest
}) {
  const c = RARITY[String(rarity).toLowerCase()] || RARITY.common;
  return (
    <GlassCard {...rest} padding="var(--ds-card-padding-tight)"
      tone={earned ? 'var(--ds-gold)' : undefined}
      style={{ minHeight: 128, ...style }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, textAlign: 'center' }}>
        <span style={{
          fontSize: 34, lineHeight: 1, fontFamily: 'var(--ds-font-emoji)',
          filter: earned ? 'none' : 'grayscale(1)', opacity: earned ? 1 : 0.55
        }}>{icon}</span>
        <span style={{
          fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
          color: 'var(--ds-label)', maxWidth: '100%', overflow: 'hidden',
          textOverflow: 'ellipsis', whiteSpace: 'nowrap'
        }}>{name}</span>
        {description ? (
          <span style={{
            fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)',
            lineHeight: 1.35, display: '-webkit-box', WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical', overflow: 'hidden'
          }}>{description}</span>
        ) : null}
        {earned ? (
          <span style={{ display: 'flex', alignItems: 'baseline', gap: 4, fontSize: 'var(--ds-text-caption2)' }}>
            <span style={{ fontWeight: 'var(--ds-weight-bold)', color: c, textTransform: 'capitalize' }}>{rarity}</span>
            <span style={{ color: 'var(--ds-gold)' }}>· {points} pts</span>
          </span>
        ) : progress != null && target > 0 ? (
          <span style={{ width: '100%', display: 'flex', flexDirection: 'column', gap: 3 }}>
            <ProgressBar value={progress} total={target} height={4} />
            <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-3)' }}>{progress}/{target}</span>
          </span>
        ) : (
          <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-3)' }}>{points} pts</span>
        )}
      </div>
    </GlassCard>
  );
}
