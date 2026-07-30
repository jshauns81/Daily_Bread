import * as React from 'react';

export interface ScreenTimePool {
  /** Preformatted, e.g. "2h 30m". */
  minutes: string;
  /** Preformatted guaranteed floor. Shown BEFORE the risk line, with a lock. */
  floor: string;
  /** Minutes at risk. Omit or 0 to hide the line entirely. */
  atRisk?: number;
}

/**
 * The house voice in one component. Order is the design: what you have →
 * what's **guaranteed** (a lock, not a warning) → what's at risk → exactly what
 * one miss costs. Renders nothing at all for a user with no child profile.
 */
export interface ScreenTimeCardProps {
  weekday?: ScreenTimePool;
  weekend?: ScreenTimePool;
  /** Per-chore cost of a single miss. */
  prices?: Array<{ name: string; minutes: number }>;
  /** Shows the "History ›" link — only when there are ledger entries. */
  onHistory?: () => void;
  /** Shows the slider icon — parents only, and only on a child's meter. */
  onSettings?: () => void;
  style?: React.CSSProperties;
}
export declare function ScreenTimeCard(props: ScreenTimeCardProps): JSX.Element;
