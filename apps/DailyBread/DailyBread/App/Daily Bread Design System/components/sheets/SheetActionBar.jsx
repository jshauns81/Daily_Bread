import React from 'react';

export function SheetActionBar({
  saveTitle = 'Save', saving, canSave = true, cancelTitle = 'Cancel',
  onCancel, onSave, style, ...rest
}) {
  const base = {
    flex: 1, minHeight: 'var(--ds-touch-target)', border: 'none',
    borderRadius: 'var(--ds-radius-swatch)', cursor: 'pointer',
    fontFamily: 'inherit', fontSize: 'var(--ds-text-body)'
  };
  return (
    <div {...rest} style={{ display: 'flex', gap: 12, ...style }}>
      <button type="button" onClick={onCancel} disabled={saving} style={{
        ...base, background: 'color-mix(in srgb, var(--ds-fill-strong) 50%, transparent)',
        color: 'var(--ds-label)', fontWeight: 'var(--ds-weight-medium)'
      }}>{cancelTitle}</button>
      <button type="button" onClick={onSave} disabled={saving || !canSave} style={{
        ...base,
        background: canSave ? 'var(--ds-accent)' : 'color-mix(in srgb, var(--ds-label-2) 30%, transparent)',
        color: '#fff', fontWeight: 'var(--ds-weight-semibold)',
        cursor: canSave && !saving ? 'pointer' : 'default'
      }}>{saving ? '…' : saveTitle}</button>
    </div>
  );
}
