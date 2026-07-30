import React from 'react';

export function SectionHeader({ kerning = 'section', color, children, style, ...rest }) {
  return (
    <div {...rest} style={{
      fontSize: 'var(--ds-text-caption)',
      fontWeight: 'var(--ds-weight-bold)',
      textTransform: 'uppercase',
      letterSpacing: kerning === 'eyebrow' ? 'var(--ds-kerning-eyebrow)' : 'var(--ds-kerning-section)',
      color: color || 'var(--ds-label-2)',
      ...style
    }}>{children}</div>
  );
}
