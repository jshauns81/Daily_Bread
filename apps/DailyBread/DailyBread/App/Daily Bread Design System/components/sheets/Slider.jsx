import React from 'react';

export function Slider({ value = 0, min = 0, max = 10, step = 1, onChange, style, ...rest }) {
  const pct = max > min ? ((value - min) / (max - min)) * 100 : 0;
  return (
    <input
      {...rest} type="range" min={min} max={max} step={step} value={value}
      onChange={(e) => onChange && onChange(Number(e.target.value))}
      style={{
        width: '100%', height: 28, margin: 0, appearance: 'none',
        background: 'transparent', cursor: 'pointer',
        '--pct': pct + '%',
        backgroundImage: 'linear-gradient(to right, var(--ds-accent) 0 ' + pct + '%, var(--ds-fill-strong) ' + pct + '% 100%)',
        backgroundSize: '100% 4px', backgroundRepeat: 'no-repeat',
        backgroundPosition: 'center', borderRadius: 999, ...style
      }}
    />
  );
}
