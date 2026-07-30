import React from 'react';
import { Icon } from '../core/Icon.jsx';
import { GlassCard } from '../core/GlassCard.jsx';
import { Tag } from '../core/Chip.jsx';

/** Totals header for the driving log — hours toward a learner's permit. */
export function DrivingTotals({ totalHours = 0, nightHours = 0, style, ...rest }) {
  const Row = ({ label, value }) => (
    <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
      <span style={{
        fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-bold)',
        textTransform: 'uppercase', letterSpacing: '0.8px', color: 'var(--ds-label-2)'
      }}>{label}</span>
      <span style={{ flex: 1 }} />
      <span style={{
        fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)',
        color: 'var(--ds-accent)', fontVariantNumeric: 'tabular-nums'
      }}>{value} h</span>
    </div>
  );
  return (
    <GlassCard {...rest} style={style}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <Row label="Total hours" value={totalHours} />
        <Row label="Night hours" value={nightHours} />
      </div>
    </GlassCard>
  );
}

/** One logged supervised drive. Night drives get the moon; pending ones a tag. */
export function DrivingLogRow({
  date, duration, from, to, supervisor, isNight = false,
  status = 'Approved', note, style, ...rest
}) {
  return (
    <div {...rest} style={{ display: 'flex', flexDirection: 'column', gap: 4, padding: '10px 0', ...style }}>
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
        <span style={{ width: 22, flex: '0 0 auto', display: 'grid', placeItems: 'center', paddingTop: 2 }}>
          <Icon name={isNight ? 'moon.stars' : 'sun.max'} size={16} color="var(--ds-accent)" />
        </span>
        <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
          <span style={{ fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)' }}>
            {date} · {duration}
          </span>
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>
            {from}–{to}{supervisor ? ' · with ' + supervisor : ''}
          </span>
        </div>
        {status === 'Pending' ? <Tag>Pending</Tag> : null}
      </div>
      {note ? (
        <div style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)', paddingLeft: 34 }}>{note}</div>
      ) : null}
    </div>
  );
}
