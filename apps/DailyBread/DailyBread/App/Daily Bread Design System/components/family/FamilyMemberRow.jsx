import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { Tag } from '../core/Chip.jsx';

/** Avatar hue carries the ROLE: accent for parents, gold for children.
 *  (FamilyMembersView.swift — the only place gold is used non-monetarily,
 *  because a child IS the earning party.) */
export function FamilyMemberRow({
  name, role = 'Child', isYou = false, locked = false,
  onResetPassword, onToggleLock, style, ...rest
}) {
  const [open, setOpen] = React.useState(false);
  const isParent = role === 'Parent';
  const hue = isParent ? 'var(--ds-accent)' : 'var(--ds-gold)';
  return (
    <div {...rest} style={{ position: 'relative', display: 'flex', alignItems: 'center', gap: 12, padding: '10px 0', minHeight: 44, ...style }}>
      <span style={{
        width: 'var(--ds-avatar)', height: 'var(--ds-avatar)', flex: '0 0 auto',
        borderRadius: '50%', display: 'grid', placeItems: 'center',
        background: hue, color: 'var(--ds-on-accent)',
        fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)'
      }}>{name.slice(0, 1).toUpperCase()}</span>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{ display: 'flex', alignItems: 'baseline', gap: 7 }}>
          <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>{name}</span>
          {isYou ? (
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-3)', fontWeight: 'var(--ds-weight-semibold)' }}>You</span>
          ) : null}
          {locked ? <Tag tone="var(--ds-help)">Locked</Tag> : null}
        </span>
        <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{role}</span>
      </div>
      <button type="button" aria-label={'Manage ' + name} onClick={() => setOpen((o) => !o)} style={{
        background: 'transparent', border: 'none', cursor: 'pointer', padding: 6,
        color: 'var(--ds-accent)', display: 'grid', placeItems: 'center'
      }}><Icon name="ellipsis.circle" size={17} /></button>

      {open ? (
        <React.Fragment>
          <span onClick={() => setOpen(false)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
          <div style={{
            position: 'absolute', right: 0, top: '76%', zIndex: 41, minWidth: 210,
            padding: 6, borderRadius: 'var(--ds-radius-card)',
            background: 'color-mix(in srgb, var(--ds-card) 88%, transparent)',
            backdropFilter: 'blur(20px)', WebkitBackdropFilter: 'blur(20px)',
            boxShadow: '0 8px 30px var(--ds-card-shadow-color)',
            border: '.5px solid var(--ds-hairline)',
            display: 'flex', flexDirection: 'column'
          }}>
            {[
              { label: 'Reset password', icon: 'key', tint: 'var(--ds-label)', fn: onResetPassword },
              { label: locked ? 'Unlock account' : 'Lock account', icon: 'lock.fill', tint: 'var(--ds-help)', fn: onToggleLock }
            ].map((a) => (
              <button key={a.label} type="button" onClick={() => { setOpen(false); if (a.fn) a.fn(); }} style={{
                display: 'flex', alignItems: 'center', gap: 12, width: '100%', minHeight: 44,
                background: 'transparent', border: 'none', cursor: 'pointer', textAlign: 'left',
                fontFamily: 'inherit', padding: '10px 12px', borderRadius: 8,
                fontSize: 'var(--ds-text-body)', color: a.tint
              }}>
                <Icon name={a.icon} size={16} color="var(--ds-accent)" />{a.label}
              </button>
            ))}
          </div>
        </React.Fragment>
      ) : null}
    </div>
  );
}
