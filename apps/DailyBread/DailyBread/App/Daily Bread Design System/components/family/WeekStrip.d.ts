import * as React from 'react';

/**
 * "Quick view — this week": seven day cells on a `.quaternary`-at-50% fill.
 * A zero-earning day renders an em dash, **never** "$0.00".
 */
export interface WeekStripProps {
  /** Seven entries, in the server's order. Amount is preformatted or omitted. */
  days?: Array<{ letter: string; amount?: string }>;
  style?: React.CSSProperties;
}
export declare function WeekStrip(props: WeekStripProps): JSX.Element;
