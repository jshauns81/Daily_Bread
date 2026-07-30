import React from 'react';

export function SheetHeader({ title, style, ...rest }) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'center',
      padding: '18px 16px 6px',
      fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)',
      color: 'var(--ds-label)', ...style
    }}>{title}</div>
  );
}
