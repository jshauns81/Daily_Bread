import React from 'react';
import { Icon } from './Icon.jsx';

export function EmptyState({ icon = 'house', title, description, action, style, ...rest }) {
  return (
    <div {...rest} style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center',
      textAlign: 'center', gap: 8, padding: '40px 24px', ...style
    }}>
      <Icon name={icon} size={44} color="var(--ds-label-3)" strokeWidth={1.5} />
      <div style={{
        fontSize: 'var(--ds-text-headline)', fontWeight: 'var(--ds-weight-semibold)',
        color: 'var(--ds-label)', marginTop: 4
      }}>{title}</div>
      {description ? (
        <div style={{
          fontSize: 'var(--ds-text-subheadline)', color: 'var(--ds-label-2)',
          maxWidth: 340, lineHeight: 1.45, textWrap: 'pretty'
        }}>{description}</div>
      ) : null}
      {action ? <div style={{ marginTop: 10 }}>{action}</div> : null}
    </div>
  );
}
