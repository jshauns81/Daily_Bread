import React from 'react';

export function ProgressRing({ progress = 0, label, size = 64, stroke = 7, color, style, ...rest }) {
  const p = Math.max(0, Math.min(1, progress));
  const r = (size - stroke) / 2;
  const circ = 2 * Math.PI * r;
  return (
    <div {...rest} style={{ position: 'relative', width: size, height: size, flex: '0 0 auto', ...style }}>
      <svg width={size} height={size} style={{ transform: 'rotate(-90deg)', display: 'block' }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--ds-fill)" strokeWidth={stroke} />
        <circle
          cx={size / 2} cy={size / 2} r={r} fill="none"
          stroke={color || 'var(--ds-accent)'} strokeWidth={stroke} strokeLinecap="round"
          strokeDasharray={circ} strokeDashoffset={circ - circ * p}
          style={{ transition: 'stroke-dashoffset var(--ds-snappy)' }}
        />
      </svg>
      <div style={{
        position: 'absolute', inset: 0, display: 'grid', placeItems: 'center',
        fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-bold)',
        color: 'var(--ds-label)'
      }}>{label}</div>
    </div>
  );
}
