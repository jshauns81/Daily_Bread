import * as React from 'react';

/**
 * The three-up tile row on both Homes and in the trophy case. `title3.bold`
 * value over a `caption2` `.secondary` label, in a 12px-padded card.
 * Long values get truncated rather than shrunk to a smaller font.
 */
export interface StatTileProps {
  value: React.ReactNode;
  /** Sentence case, lowercase in practice: "earned this week", "points". */
  label: string;
  /** var(--ds-gold) for money, var(--ds-accent) for counts. Default .primary. */
  color?: string;
  style?: React.CSSProperties;
}
export declare function StatTile(props: StatTileProps): JSX.Element;
