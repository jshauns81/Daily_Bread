import React from 'react';

export function SegmentedPicker({ options = [], value, onChange, style, ...rest }) {
  return (
    <div {...rest} role="tablist" style={{
      display: 'flex', padding: 2, gap: 2,
      background: 'color-mix(in srgb, var(--ds-fill) 70%, transparent)',
      borderRadius: 'var(--ds-radius-field)', ...style
    }}>
      {options.map((o) => {
        const v = typeof o === 'string' ? o : o.value;
        const l = typeof o === 'string' ? o : o.label;
        const on = v === value;
        return (
          <button key={v} type="button" role="tab" aria-selected={on}
            onClick={() => onChange && onChange(v)} style={{
              flex: 1, minHeight: 30, border: 'none', cursor: 'pointer',
              fontFamily: 'inherit', fontSize: 'var(--ds-text-subheadline)',
              fontWeight: on ? 'var(--ds-weight-semibold)' : 'var(--ds-weight-regular)',
              borderRadius: 7, color: 'var(--ds-label)',
              background: on ? 'var(--ds-card)' : 'transparent',
              boxShadow: on ? '0 1px 3px rgb(0 0 0 / .12)' : 'none',
              transition: 'all var(--ds-ease-in-out)'
            }}>{l}</button>
        );
      })}
    </div>
  );
}
