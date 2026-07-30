import * as React from 'react';

/**
 * Cancel + primary action, side by side, pinned at the sheet's foot — **one
 * action zone**, deliberately not a Save-in-the-form plus Cancel-in-the-toolbar
 * split. Both buttons are 44px tall and equal width.
 */
export interface SheetActionBarProps {
  /** "Save" when editing, "Create" when new. */
  saveTitle?: string;
  saving?: boolean;
  /** False greys the primary button; it stays visible. */
  canSave?: boolean;
  cancelTitle?: string;
  onCancel?: () => void;
  onSave?: () => void;
  style?: React.CSSProperties;
}
export declare function SheetActionBar(props: SheetActionBarProps): JSX.Element;
