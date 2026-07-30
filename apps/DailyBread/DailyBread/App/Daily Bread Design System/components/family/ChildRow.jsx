import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { ProgressRing } from '../core/ProgressRing.jsx';

function statusLine(child) {
  if (child.helpRequests > 0) {
    return { text: child.helpRequests + ' help request' + (child.helpRequests === 1 ? '' : 's'), color: 'var(--ds-help)' };
  }
  if (child.pending > 0) return { text: child.pending + ' left today', color: 'var(--ds-label-2)' };
  return { text: 'All done ✨', color: 'var(--ds-gold)' };
}

export function ChildRow({ child, onTap, style, ...rest }) {
  const s = statusLine(child);
  const p = child.total ? child.done / child.total : 0;
  return (
    <GlassCard {...rest} style={{ cursor: onTap ? 'pointer' : undefined, ...style }} onClick={onTap}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <ProgressRing progress={p} label={child.done + '/' + child.total} size={44} />
        <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
          <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-medium)' }}>{child.name}</span>
          <span style={{ fontSize: 'var(--ds-text-caption)', color: s.color }}>{s.text}</span>
        </div>
      </div>
    </GlassCard>
  );
}

export function ChildTile({ child, onTap, style, ...rest }) {
  const s = statusLine(child);
  const p = child.total ? child.done / child.total : 0;
  return (
    <GlassCard {...rest} padding="var(--ds-card-padding-tight)"
      style={{ cursor: onTap ? 'pointer' : undefined, ...style }} onClick={onTap}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
        <ProgressRing progress={p} label={child.done + '/' + child.total} size={48} />
        <span style={{
          fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
          whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: '100%'
        }}>{child.name}</span>
        <span style={{ fontSize: 'var(--ds-text-caption)', color: s.color, whiteSpace: 'nowrap' }}>{s.text}</span>
      </div>
    </GlassCard>
  );
}
