import React from 'react';
import { Icon } from '../core/Icon.jsx';

/**
 * Scoped picker for whose data a shared card is showing.
 *
 * SINGLE-CHILD RULE: with one child this renders NOTHING. A picker implies a
 * choice, and a choice implies other children. Callers may render it
 * unconditionally — the guard lives here so it cannot be forgotten.
 */
export function ChildPicker({ children: names = [], value, onChange, style, ...rest }) {
  const [open, setOpen] = React.useState(false);
  if (names.length <= 1) return null;
  return (
    <div {...rest} style={{ position: 'relative', display: 'inline-flex', ...style }}>
      <button type="button" onClick={() => setOpen((o) => !o)} style={{
        display: 'inline-flex', alignItems: 'center', gap: 7, minHeight: 44,
        padding: '8px 14px', cursor: 'pointer', fontFamily: 'inherit',
        borderRadius: 'var(--ds-radius-capsule)', border: 'none',
        background: 'color-mix(in srgb, var(--ds-accent) 12%, transparent)',
        color: 'var(--ds-accent)', fontSize: 'var(--ds-text-subheadline)',
        fontWeight: 'var(--ds-weight-semibold)'
      }}>
        <Icon name="chevron.up.chevron.down" size={13} />
        {value}
        <Icon name={open ? 'chevron.up' : 'chevron.down'} size={12} />
      </button>
      {open ? (
        <React.Fragment>
          <span onClick={() => setOpen(false)} style={{ position: 'fixed', inset: 0, zIndex: 40 }} />
          <div style={{
            position: 'absolute', left: 0, top: 'calc(100% + 6px)', zIndex: 41, minWidth: 190,
            padding: 6, borderRadius: 'var(--ds-radius-card)',
            background: 'color-mix(in srgb, var(--ds-card) 88%, transparent)',
            backdropFilter: 'blur(20px)', WebkitBackdropFilter: 'blur(20px)',
            boxShadow: '0 8px 30px var(--ds-card-shadow-color)',
            border: '.5px solid var(--ds-hairline)',
            display: 'flex', flexDirection: 'column'
          }}>
            {names.map((n) => (
              <button key={n} type="button" onClick={() => { setOpen(false); if (onChange) onChange(n); }} style={{
                display: 'flex', alignItems: 'center', gap: 10, width: '100%', minHeight: 44,
                background: 'transparent', border: 'none', cursor: 'pointer', textAlign: 'left',
                fontFamily: 'inherit', padding: '10px 12px', borderRadius: 8,
                fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)'
              }}>
                <span style={{ width: 16, flex: '0 0 auto' }}>
                  {n === value ? <Icon name="checkmark" size={14} color="var(--ds-accent)" /> : null}
                </span>
                {n}
              </button>
            ))}
          </div>
        </React.Fragment>
      ) : null}
    </div>
  );
}
