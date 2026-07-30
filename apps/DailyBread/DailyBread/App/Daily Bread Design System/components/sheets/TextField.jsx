import React from 'react';

export function TextField({
  value, onChange, placeholder, prefix, prefixColor, multiline, rows = 2,
  align, width, style, ...rest
}) {
  const shell = {
    width: width || '100%', boxSizing: 'border-box',
    background: 'color-mix(in srgb, var(--ds-fill) 50%, transparent)',
    borderRadius: 'var(--ds-radius-field)',
    padding: '10px 12px', display: 'flex', alignItems: 'center', gap: 6
  };
  const input = {
    flex: 1, minWidth: 0, background: 'transparent', border: 'none', outline: 'none',
    fontFamily: 'inherit', fontSize: 'var(--ds-text-body)', color: 'var(--ds-label)',
    textAlign: align || 'left', resize: 'none'
  };
  return (
    <div style={{ ...shell, ...style }}>
      {prefix ? (
        <span style={{
          fontSize: 'var(--ds-text-body)', fontWeight: 'var(--ds-weight-semibold)',
          color: prefixColor || 'var(--ds-gold)'
        }}>{prefix}</span>
      ) : null}
      {multiline ? (
        <textarea {...rest} rows={rows} value={value} placeholder={placeholder}
          onChange={(e) => onChange && onChange(e.target.value)} style={input} />
      ) : (
        <input {...rest} value={value} placeholder={placeholder}
          onChange={(e) => onChange && onChange(e.target.value)} style={input} />
      )}
    </div>
  );
}
