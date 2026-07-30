import React from 'react';
import { Icon } from '../core/Icon.jsx';

export function HelpRow({ choreName, reason, childName, onOpen, style, ...rest }) {
  return (
    <button {...rest} type="button" onClick={onOpen} style={{
      display: 'flex', alignItems: 'center', gap: 12, width: '100%',
      padding: '10px 0', background: 'transparent', border: 'none',
      cursor: 'pointer', fontFamily: 'inherit', textAlign: 'left',
      minHeight: 'var(--ds-touch-target)', ...style
    }}>
      <span style={{
        width: 8, height: 8, flex: '0 0 auto', borderRadius: '50%',
        background: 'var(--ds-help)'
      }} />
      <span style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={{
          fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-medium)',
          color: 'var(--ds-label)'
        }}>{choreName}</span>
        <span style={{
          fontSize: 'var(--ds-text-caption)', color: 'var(--ds-label-2)',
          display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden'
        }}>{reason || (childName + ' needs a hand')}</span>
      </span>
      <Icon name="chevron.right" size={12} color="var(--ds-label-3)" />
    </button>
  );
}
