import React from 'react';

export function Chip({ tone, emphasized = true, children, style, ...rest }) {
  const c = emphasized && tone ? tone : 'var(--ds-label-2)';
  return (
    <span {...rest} style={{
      display: 'inline-flex', alignItems: 'center', gap: 4,
      fontSize: 'var(--ds-text-caption)', fontWeight: 'var(--ds-weight-semibold)',
      padding: '5px 9px', borderRadius: 'var(--ds-radius-capsule)',
      background: 'color-mix(in srgb, ' + c + ' 13%, transparent)',
      color: c, whiteSpace: 'nowrap', ...style
    }}>{children}</span>
  );
}

/** The small solid capsule used for HELP, Off, Routine, Cash out ready. */
export function Tag({ tone = 'var(--ds-label-2)', solid, children, style, ...rest }) {
  return (
    <span {...rest} style={{
      display: 'inline-flex', alignItems: 'center',
      fontSize: 'var(--ds-text-caption2)', fontWeight: 'var(--ds-weight-heavy)',
      padding: '3px 7px', borderRadius: 'var(--ds-radius-capsule)',
      background: solid ? tone : 'color-mix(in srgb, ' + tone + ' 15%, transparent)',
      color: solid ? '#fff' : tone, whiteSpace: 'nowrap', ...style
    }}>{children}</span>
  );
}
