import React from 'react';
import { GlassCard } from '../core/GlassCard.jsx';
import { Icon } from '../core/Icon.jsx';

const DAY_LETTERS = ['S', 'M', 'T', 'W', 'T', 'F', 'S'];
const STATUS = {
  allDone: { tint: 'var(--ds-success)', label: 'All done' },
  inProgress: { tint: 'var(--ds-accent)', label: 'In progress' },
  missed: { tint: 'var(--ds-help)', label: 'Missed' }
};

/**
 * Month grid where each day carries a single status dot. Three states only —
 * the calendar answers "how did that day go", not "what happened".
 */
export function CalendarMonth({
  monthLabel, days = [], selected, firstWeekday = 0,
  onSelect, onPrev, onNext, style, ...rest
}) {
  const cells = [];
  for (let i = 0; i < firstWeekday; i += 1) cells.push(null);
  days.forEach((d) => cells.push(d));

  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', gap: 14, ...style }}>
      <GlassCard padding="10px 14px">
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <button type="button" aria-label="Previous month" onClick={onPrev} style={{
            background: 'transparent', border: 'none', cursor: 'pointer', padding: 6,
            color: 'var(--ds-accent)', display: 'grid', placeItems: 'center'
          }}><Icon name="chevron.left" size={15} /></button>
          <span style={{ flex: 1, textAlign: 'center', fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)' }}>
            {monthLabel}
          </span>
          <button type="button" aria-label="Next month" onClick={onNext} style={{
            background: 'transparent', border: 'none', cursor: 'pointer', padding: 6,
            color: 'var(--ds-accent)', display: 'grid', placeItems: 'center'
          }}><Icon name="chevron.right" size={15} /></button>
        </div>
      </GlassCard>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(7, 1fr)', gap: 6 }}>
        {DAY_LETTERS.map((l, i) => (
          <span key={i} style={{
            textAlign: 'center', fontSize: 'var(--ds-text-caption2)',
            fontWeight: 'var(--ds-weight-bold)', letterSpacing: '0.6px',
            color: 'var(--ds-label-3)', paddingBottom: 2
          }}>{l}</span>
        ))}
        {cells.map((d, i) => {
          if (!d) return <span key={'pad' + i} />;
          const st = STATUS[d.status];
          const isSel = selected === d.day;
          return (
            <button key={d.day} type="button" onClick={() => onSelect && onSelect(d.day)} style={{
              display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
              gap: 4, padding: '9px 0 7px', minHeight: 52, cursor: 'pointer',
              fontFamily: 'inherit', borderRadius: 'var(--ds-radius-swatch)',
              background: d.outside ? 'transparent' : 'color-mix(in srgb, var(--ds-fill) 55%, transparent)',
              border: isSel ? '1.5px solid var(--ds-accent)' : '1.5px solid transparent',
              opacity: d.outside ? 0.4 : 1,
              transition: 'border-color var(--ds-snappy)'
            }}>
              <span style={{
                fontSize: 'var(--ds-text-subheadline)',
                fontWeight: isSel ? 'var(--ds-weight-bold)' : 'var(--ds-weight-medium)',
                color: 'var(--ds-label)', fontVariantNumeric: 'tabular-nums'
              }}>{d.day}</span>
              <span style={{
                width: 6, height: 6, borderRadius: '50%',
                background: st ? st.tint : 'color-mix(in srgb, var(--ds-label) 18%, transparent)'
              }} />
            </button>
          );
        })}
      </div>

      <div style={{ display: 'flex', justifyContent: 'center', gap: 18, flexWrap: 'wrap', paddingTop: 2 }}>
        {Object.keys(STATUS).map((k) => (
          <span key={k} style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span style={{ width: 7, height: 7, borderRadius: '50%', background: STATUS[k].tint }} />
            <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{STATUS[k].label}</span>
          </span>
        ))}
      </div>
    </div>
  );
}
