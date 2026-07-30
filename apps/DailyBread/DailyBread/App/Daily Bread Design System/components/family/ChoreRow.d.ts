import * as React from 'react';

/**
 * The Today list row — the daily driver. Emoji tile, name, status line, gold
 * value, and a tap-target circle that fills with the accent when done.
 * Completion is **optimistic**: flip it on tap with `.snappy` and roll back if
 * the server disagrees.
 *
 * In the app: leading swipe = Done/Undo, trailing swipe = Help. On the web the
 * Help affordance is an inline symbol button instead.
 */
export interface ChoreRowProps {
  name: string;
  /** The chore's emoji. Default "🧺". */
  icon?: string;
  /** Preformatted money, e.g. "$1.50". Omit for a Routine. */
  value?: string;
  done?: boolean;
  /** Help raised — shows "Help raised — protected" and the solid HELP tag. */
  help?: boolean;
  /** Shows "Approved by Dad" in gold. */
  approvedBy?: string;
  /** Weekly-frequency chores show "4 of 7 this week". */
  weekly?: { done: number; target: number };
  /** False on a parent drill-in — raising Help is the kid's own act. */
  allowHelp?: boolean;
  onToggle?: () => void;
  onHelp?: () => void;
  style?: React.CSSProperties;
}
export declare function ChoreRow(props: ChoreRowProps): JSX.Element;
