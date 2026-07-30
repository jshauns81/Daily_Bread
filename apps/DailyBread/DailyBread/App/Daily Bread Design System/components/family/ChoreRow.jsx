import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Tag } from '../core/Chip.jsx';

export function ChoreRow({
  name, icon = '🧺', value, done, help, approvedBy, weekly, allowHelp = true,
  onToggle, onHelp, style, ...rest
}) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'center', gap: 12,
      padding: '10px 0', minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <span aria-hidden="true" style={{
        width: 'var(--ds-icon-tile)', height: 'var(--ds-icon-tile)', flex: '0 0 auto',
        borderRadius: 'var(--ds-radius-field)', background: 'var(--ds-fill)',
        display: 'grid', placeItems: 'center', fontSize: 20, lineHeight: 1,
        fontFamily: 'var(--ds-font-emoji)'
      }}>{icon}</span>

      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{
          fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-medium)',
          color: done ? 'var(--ds-label-2)' : 'var(--ds-label)',
          textDecoration: done ? 'line-through' : 'none',
          textDecorationColor: 'var(--ds-label-2)'
        }}>{name}</span>
        {help ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-help)' }}>Help raised — protected</span>
        ) : approvedBy ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-gold)' }}>Approved by {approvedBy}</span>
        ) : weekly ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            {weekly.done} of {weekly.target} this week
          </span>
        ) : null}
      </div>

      {value != null ? (
        <span style={{
          fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
          color: 'var(--ds-gold)', opacity: done ? 0.5 : 1, flex: '0 0 auto'
        }}>{value}</span>
      ) : null}

      {help ? (
        <Tag tone="var(--ds-help)" solid>HELP</Tag>
      ) : (
        <button type="button" onClick={onToggle} aria-label={done ? 'Undo' : 'Done'} style={{
          background: 'transparent', border: 'none', cursor: 'pointer', padding: 0,
          display: 'grid', placeItems: 'center', flex: '0 0 auto',
          color: done ? 'var(--ds-accent)' : 'var(--ds-label-2)'
        }}>
          <Icon name={done ? 'checkmark.circle.fill' : 'circle'} size={22} />
        </button>
      )}
      {allowHelp && !done && !help && onHelp ? (
        <button type="button" onClick={onHelp} aria-label="Raise Help" style={{
          background: 'transparent', border: 'none', cursor: 'pointer', padding: 0,
          display: 'grid', placeItems: 'center', flex: '0 0 auto', color: 'var(--ds-help)'
        }}>
          <Icon name="questionmark.circle" size={20} />
        </button>
      ) : null}
    </div>
  );
}
