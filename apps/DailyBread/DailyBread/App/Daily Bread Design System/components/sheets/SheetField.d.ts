import * as React from 'react';

/**
 * Label **above** its control, with an optional live value on the right —
 * deliberately not SwiftUI's leading-label `Form` row, which breaks on macOS.
 * The live value is how the importance slider reads "7 of 10".
 */
export interface SheetFieldProps {
  label: string;
  /** Live value shown right-aligned, in tabular figures. */
  value?: React.ReactNode;
  /** Usually var(--ds-accent). */
  valueColor?: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function SheetField(props: SheetFieldProps): JSX.Element;
