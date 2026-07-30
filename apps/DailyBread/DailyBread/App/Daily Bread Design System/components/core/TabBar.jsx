import React from 'react';
import { Icon } from './Icon.jsx';

export const KID_TABS = [
  { id: 'kidHome', label: 'Home', icon: 'house' },
  { id: 'today', label: 'Today', icon: 'sun.max' },
  { id: 'earnings', label: 'Earnings', icon: 'dollarsign.circle' },
  { id: 'awards', label: 'Awards', icon: 'trophy' },
  { id: 'settings', label: 'Settings', icon: 'gearshape' }
];
export const PARENT_TABS = [
  { id: 'home', label: 'Home', icon: 'house' },
  { id: 'planner', label: 'Planner', icon: 'checklist' },
  { id: 'approvals', label: 'Approvals', icon: 'checkmark.circle' },
  { id: 'settings', label: 'Settings', icon: 'gearshape' }
];

export function TabBar({ tabs = PARENT_TABS, active, badges = {}, onSelect, style, ...rest }) {
  return (
    <nav {...rest} style={{
      display: 'flex', alignItems: 'stretch',
      minHeight: 'var(--ds-tabbar-height)',
      borderTop: '.5px solid var(--ds-hairline)',
      background: 'color-mix(in srgb, var(--db-bg-bottom) 88%, transparent)',
      backdropFilter: 'saturate(180%) blur(20px)',
      WebkitBackdropFilter: 'saturate(180%) blur(20px)',
      ...style
    }}>
      {tabs.map((t) => {
        const on = t.id === active;
        const badge = badges[t.id];
        return (
          <button key={t.id} type="button" onClick={() => onSelect && onSelect(t.id)} style={{
            flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center',
            justifyContent: 'center', gap: 2, padding: '5px 2px 4px',
            background: 'transparent', border: 'none', cursor: 'pointer',
            fontFamily: 'inherit', position: 'relative',
            color: on ? 'var(--ds-accent)' : 'var(--ds-label-2)'
          }}>
            <span style={{ position: 'relative' }}>
              <Icon name={t.icon} size={22} strokeWidth={on ? 2.4 : 2} />
              {badge ? (
                <span style={{
                  position: 'absolute', top: -5, left: 12, minWidth: 16, height: 16,
                  padding: '0 4px', borderRadius: 999, background: 'var(--ds-help)',
                  color: '#fff', fontSize: 10, fontWeight: 700,
                  display: 'grid', placeItems: 'center', boxSizing: 'border-box'
                }}>{badge}</span>
              ) : null}
            </span>
            <span style={{ fontSize: 10, fontWeight: 500 }}>{t.label}</span>
          </button>
        );
      })}
    </nav>
  );
}
