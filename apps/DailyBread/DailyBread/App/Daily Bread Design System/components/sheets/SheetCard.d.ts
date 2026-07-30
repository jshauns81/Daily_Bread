import * as React from 'react';

/**
 * A titled group of fields on a glass surface — the sheet equivalent of a
 * `Form` section. The app builds sheets from these instead of SwiftUI `Form`
 * because `Form` renders cramped and misaligned on macOS.
 * Children are stacked with a 14px gap.
 */
export interface SheetCardProps {
  /** Sentence case; rendered uppercase by SectionHeader. */
  title?: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function SheetCard(props: SheetCardProps): JSX.Element;
