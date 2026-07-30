import React from 'react';
import { Button } from '../core/Button.jsx';

export function ApprovalRow({
  choreName, childName, value, approved, busy, onApprove, style, ...rest
}) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'center', gap: 12,
      padding: '10px 12px', margin: '0 -12px',
      borderRadius: 'var(--ds-radius-swatch)',
      background: approved ? 'var(--ds-blessing-wash)' : 'transparent',
      transition: 'background var(--ds-ease-out)', ...style
    }}>
      <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{
          fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-medium)',
          color: 'var(--ds-label)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap'
        }}>{choreName}</span>
        {childName ? (
          <span style={{ fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)' }}>{childName}</span>
        ) : null}
      </div>
      <span style={{
        fontSize: 'var(--ds-text-subheadline)', fontWeight: 'var(--ds-weight-semibold)',
        color: 'var(--ds-gold)', flex: '0 0 auto'
      }}>{value}</span>
      <Button variant="gold" size="sm" busy={busy} disabled={approved}
        icon={approved ? 'checkmark' : 'hand.thumbsup'} onClick={onApprove}>
        {approved ? 'Approved' : 'Approve'}
      </Button>
    </div>
  );
}
