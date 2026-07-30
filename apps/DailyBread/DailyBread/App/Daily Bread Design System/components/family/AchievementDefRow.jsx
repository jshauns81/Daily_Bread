import React from 'react';
import { RARITY } from './AchievementCard.jsx';

/**
 * Parent-side achievement definition row: what the badge is, how it's earned,
 * and whether it's active. Kids never see this surface.
 */
export function AchievementDefRow({
  icon, name, rarity = 'common', points, condition,
  enabled = true, onToggle, style, ...rest
}) {
  const tint = RARITY[String(rarity).toLowerCase()] || RARITY.common;
  return (
    <div {...rest} style={{ display: 'flex', alignItems: 'center', gap: 12, padding: '10px 0', minHeight: 44, ...style }}>
      <span style={{
        width: 'var(--ds-icon-tile-lg)', flex: '0 0 auto', textAlign: 'center',
        fontSize: 22, lineHeight: 1, fontFamily: 'var(--ds-font-emoji)',
        opacity: enabled ? 1 : 0.4
      }}>{icon}</span>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{
          fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)',
          color: enabled ? 'var(--ds-label)' : 'var(--ds-label-2)'
        }}>{name}</span>
        <span style={{ display: 'flex', alignItems: 'baseline', gap: 6, fontSize: 'var(--ds-text-caption)' }}>
          <span style={{ color: tint, fontWeight: 'var(--ds-weight-semibold)', textTransform: 'capitalize' }}>{rarity}</span>
          <span style={{ color: 'var(--ds-label-3)' }}>·</span>
          <span style={{ color: 'var(--ds-gold)', fontWeight: 'var(--ds-weight-semibold)' }}>{points} pts</span>
        </span>
        {condition ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-3)' }}>{condition}</span>
        ) : null}
      </div>
      <button type="button" role="switch" aria-checked={enabled} aria-label={name}
        onClick={() => onToggle && onToggle(!enabled)} style={{
          width: 51, height: 31, flex: '0 0 auto', padding: 2, cursor: 'pointer',
          borderRadius: 'var(--ds-radius-capsule)', border: 'none',
          background: enabled ? 'var(--ds-accent)' : 'color-mix(in srgb, var(--ds-label) 20%, transparent)',
          transition: 'background var(--ds-snappy)'
        }}>
        <span style={{
          display: 'block', width: 27, height: 27, borderRadius: '50%', background: '#fff',
          boxShadow: '0 1px 3px rgb(0 0 0 / .2)',
          transform: enabled ? 'translateX(20px)' : 'translateX(0)',
          transition: 'transform var(--ds-snappy)'
        }} />
      </button>
    </div>
  );
}
