import * as React from 'react';

/**
 * The primary savings goal. Eyebrow with a target symbol, the goal's name, an
 * accent progress bar, and "X of Y" — never a percentage on the kid's Home
 * (the Earnings screen adds "70% there" as a third line).
 * Hidden entirely when the family has `enableGoals` off.
 */
export interface GoalCardProps {
  name?: string;
  /** Preformatted, e.g. "$17.50". */
  current?: string;
  target?: string;
  /** 0–100. */
  percent?: number;
  /** Renders the invitation state instead. */
  empty?: boolean;
  onTap?: () => void;
  style?: React.CSSProperties;
}
export declare function GoalCard(props: GoalCardProps): JSX.Element;
