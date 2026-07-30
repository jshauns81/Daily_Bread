import * as React from 'react';

/**
 * The app's inline field: a soft `.quaternary`-at-50% rounded background, no
 * border, no focus ring. The `$` prefix sits INSIDE the field beside the value
 * (fixing macOS `Form`, which flings prefixes away from their fields).
 */
export interface TextFieldProps {
  value?: string;
  onChange?: (value: string) => void;
  placeholder?: string;
  /** Static leading glyph, e.g. "$". Rendered gold by default. */
  prefix?: string;
  prefixColor?: string;
  multiline?: boolean;
  rows?: number;
  align?: 'left' | 'center';
  /** Fixed width — the 46px emoji field uses this. */
  width?: number | string;
  style?: React.CSSProperties;
}
export declare function TextField(props: TextFieldProps): JSX.Element;
