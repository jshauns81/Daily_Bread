import React from 'react';
import { Icon } from './Icon.jsx';

export function InlineError({ icon = 'wifi.exclamationmark', children, style, ...rest }) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'center', gap: 6,
      fontSize: 'var(--ds-text-footnote)', color: 'var(--ds-label-2)', ...style
    }}>
      <Icon name={icon} size={13} />
      <span>{children}</span>
    </div>
  );
}
