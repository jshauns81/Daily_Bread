import React from 'react';
import { Tag } from '../core/Chip.jsx';

export function PlannerChoreRow({
  name, icon, value, isTask = true, schedule, assignee, importance,
  active = true, onTap, style, ...rest
}) {
  const glyph = icon || (isTask ? '💰' : '✅');
  const caption = [schedule, assignee].filter(Boolean).join(' · ');
  return (
    <button {...rest} type="button" onClick={onTap} style={{
      display: 'flex', alignItems: 'center', gap: 12, width: '100%',
      padding: '10px 0', background: 'transparent', border: 'none',
      cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
      minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <span aria-hidden="true" style={{
        width: 'var(--ds-icon-tile)', height: 'var(--ds-icon-tile)', flex: '0 0 auto',
        borderRadius: 'var(--ds-radius-field)', background: 'var(--ds-fill)',
        display: 'grid', placeItems: 'center', fontSize: 20, lineHeight: 1,
        fontFamily: 'var(--ds-font-emoji)', opacity: active ? 1 : 0.45
      }}>{glyph}</span>

      <span style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
          <span style={{
            fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-medium)',
            color: active ? 'var(--ds-label)' : 'var(--ds-label-2)',
            overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
          }}>{name}</span>
          {!active ? <Tag>Off</Tag> : null}
        </span>
        {caption ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{caption}</span>
        ) : null}
      </span>

      <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 3, flex: '0 0 auto' }}>
        {isTask ? (
          <span style={{
            fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
            color: 'var(--ds-gold)', opacity: active ? 1 : 0.5
          }}>{value}</span>
        ) : <Tag>Routine</Tag>}
        {importance > 0 ? (
          <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)' }}>
            <span style={{ fontFamily: 'var(--ds-font-emoji)' }}>📺</span> {importance}
          </span>
        ) : null}
      </span>
    </button>
  );
}
