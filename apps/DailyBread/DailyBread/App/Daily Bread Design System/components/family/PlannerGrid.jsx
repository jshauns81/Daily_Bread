import React from 'react';
import { Icon } from '../core/Icon.jsx';

const DAYS = [
  ['Sunday', 'S'], ['Monday', 'M'], ['Tuesday', 'T'], ['Wednesday', 'W'],
  ['Thursday', 'T'], ['Friday', 'F'], ['Saturday', 'S']
];

export function PlannerGrid({ chores = [], showAssignee, onToggle, onEdit, style, ...rest }) {
  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', ...style }}>
      <div style={{
        display: 'flex', alignItems: 'center', gap: 'var(--ds-day-gap)',
        padding: '8px 4px', position: 'sticky', top: 0, zIndex: 2,
        background: 'color-mix(in srgb, var(--db-bg-top) 92%, transparent)',
        backdropFilter: 'blur(12px)', WebkitBackdropFilter: 'blur(12px)'
      }}>
        <span style={{ flex: 1 }} />
        {DAYS.map(([full, letter], i) => (
          <span key={i} style={{
            width: 'var(--ds-day-cell)', textAlign: 'center', flex: '0 0 auto',
            fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
            color: 'var(--ds-label-2)'
          }}>{letter}</span>
        ))}
      </div>

      {chores.map((c, ci) => (
        <React.Fragment key={c.id != null ? c.id : ci}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ds-day-gap)', padding: '7px 4px' }}>
            <button type="button" onClick={() => onEdit && onEdit(c)} style={{
              flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', gap: 8,
              background: 'transparent', border: 'none', cursor: 'pointer',
              fontFamily: 'inherit', textAlign: 'left', padding: 0
            }}>
              <span style={{ fontSize: 17, lineHeight: 1, fontFamily: 'var(--ds-font-emoji)', flex: '0 0 auto' }}>
                {c.icon || (c.isTask === false ? '✅' : '💰')}
              </span>
              <span style={{ minWidth: 0, display: 'flex', flexDirection: 'column', gap: 1 }}>
                <span style={{
                  fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-medium)',
                  color: c.active === false ? 'var(--ds-label-2)' : 'var(--ds-label)',
                  overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
                }}>{c.name}</span>
                {showAssignee && c.assignee ? (
                  <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-label-2)' }}>{c.assignee}</span>
                ) : c.isTask !== false && c.value ? (
                  <span style={{ fontSize: 'var(--ds-text-caption2)', color: 'var(--ds-gold)' }}>{c.value}</span>
                ) : null}
              </span>
            </button>

            {c.weeklyTarget ? (
              <span style={{
                width: 'calc(var(--ds-day-cell) * 7 + var(--ds-day-gap) * 6)', flex: '0 0 auto',
                textAlign: 'center', fontSize: 'var(--ds-text-caption2)',
                fontWeight: 'var(--ds-weight-semibold)', color: 'var(--ds-label-2)'
              }}>{c.weeklyTarget}×/wk</span>
            ) : DAYS.map(([full], i) => {
              const on = (c.days || []).includes(full);
              return (
                <button key={i} type="button" onClick={() => onToggle && onToggle(c, full)}
                  aria-pressed={on} aria-label={c.name + ' ' + full} style={{
                    width: 'var(--ds-day-cell)', height: 'var(--ds-day-cell)', flex: '0 0 auto',
                    borderRadius: 'var(--ds-radius-day-cell)', cursor: 'pointer',
                    border: on ? 'none' : '.5px solid color-mix(in srgb, var(--ds-label) 6%, transparent)',
                    background: on ? 'var(--ds-accent)' : 'color-mix(in srgb, var(--ds-label) 12%, transparent)',
                    display: 'grid', placeItems: 'center', padding: 0,
                    transition: 'background var(--ds-snappy)'
                  }}>
                  {on ? <Icon name="checkmark" size={12} color="#fff" strokeWidth={3} /> : null}
                </button>
              );
            })}
          </div>
          {ci < chores.length - 1 ? (
            <div style={{ height: '.5px', background: 'var(--ds-hairline)', marginLeft: 4 }} />
          ) : null}
        </React.Fragment>
      ))}
    </div>
  );
}
