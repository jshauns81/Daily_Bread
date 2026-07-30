import * as React from 'react';

export interface ChildProgress {
  name: string;
  done: number;
  total: number;
  pending?: number;
  helpRequests?: number;
}

/**
 * A kid on the parent's Home. **The layout responds to family size, not screen
 * size:** 1–2 children get roomy `ChildRow`s, 3+ get a two-column grid of
 * `ChildTile`s. Tapping either drills into that kid's day.
 *
 * Status line priority: help requests (red) → "N left today" (secondary) →
 * "All done ✨" (gold).
 */
export interface ChildRowProps {
  child: ChildProgress;
  onTap?: () => void;
  style?: React.CSSProperties;
}
export declare function ChildRow(props: ChildRowProps): JSX.Element;
/** The compact grid variant for 3+ children. */
export declare function ChildTile(props: ChildRowProps): JSX.Element;
