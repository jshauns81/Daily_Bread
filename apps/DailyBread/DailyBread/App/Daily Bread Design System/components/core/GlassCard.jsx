import React from 'react';

/**
 * The `.glassCard()` modifier. Opaque theme card colour, 20px continuous
 * radius, 0.5px stroke, one close soft shadow. There is no blur — the "glass"
 * is the stroke plus the shadow.
 */
export function GlassCard({ padding = 'var(--ds-card-padding)', tone, children, style, ...rest }) {
  return (
    <div {...rest} style={{
      background: 'var(--ds-card)',
      borderRadius: 'var(--ds-radius-card)',
      boxShadow: 'var(--ds-card-shadow), 0 0 0 .5px var(--ds-card-stroke)'
        + (tone ? ', inset 0 0 0 1px color-mix(in srgb, ' + tone + ' 35%, transparent)' : ''),
      padding, boxSizing: 'border-box', position: 'relative',
      ...style
    }}>{children}</div>
  );
}
