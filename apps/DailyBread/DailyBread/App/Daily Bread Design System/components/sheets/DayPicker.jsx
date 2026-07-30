import React from 'react';

export const ALL_DAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const LETTERS = ['S', 'M', 'T', 'W', 'T', 'F', 'S'];

export function DayPicker({ selected = [], onChange, style, ...rest }) {
  const set = new Set(selected);
  const toggle = (day) => {
    const next = new Set(set);
    if (next.has(day)) next.delete(day); else next.add(day);
    onChange && onChange(ALL_DAYS.filter((d) => next.has(d)));
  };
  return (
    <div {...rest} style={{ display: 'flex', gap: 6, ...style }}>
      {ALL_DAYS.map((day, i) => {
        const on = set.has(day);
        return (
          <button key={day} type="button" onClick={() => toggle(day)}
            aria-pressed={on} aria-label={day} style={{
              flex: 1, display: 'grid', placeItems: 'center',
              width: 'var(--ds-day-pill)', height: 'var(--ds-day-pill)',
              maxWidth: 'var(--ds-day-pill)', margin: '0 auto',
              borderRadius: '50%', border: 'none', cursor: 'pointer',
              fontFamily: 'inherit', fontSize: 'var(--ds-text-subheadline)',
              fontWeight: 'var(--ds-weight-semibold)',
              background: on ? 'var(--ds-accent)' : 'color-mix(in srgb, var(--ds-label) 14%, transparent)',
              color: on ? '#fff' : 'var(--ds-label)',
              transition: 'background var(--ds-snappy), color var(--ds-snappy)'
            }}>{LETTERS[i]}</button>
        );
      })}
    </div>
  );
}
