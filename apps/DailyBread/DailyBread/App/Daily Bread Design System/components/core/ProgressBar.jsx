import React from 'react';

export function ProgressBar({
  value = 0, total = 100, height = 6, tone = 'accent', style, ...rest
}) {
  const pct = total > 0 ? Math.max(0, Math.min(100, (value / total) * 100)) : 0;
  const fill = tone === 'progress' ? 'var(--ds-progress)'
    : tone === 'gold' ? 'var(--ds-gold)'
    : tone === 'success' ? 'var(--ds-success)'
    : 'var(--ds-accent)';
  return (
    <div {...rest} style={{
      height, borderRadius: 'var(--ds-radius-capsule)',
      background: 'var(--ds-fill)', overflow: 'hidden', ...style
    }}>
      <div style={{
        width: pct + '%', height: '100%', background: fill,
        borderRadius: 'var(--ds-radius-capsule)',
        transition: 'width var(--ds-spring-slow)'
      }} />
    </div>
  );
}
