import * as React from 'react';

export interface AtRiskItem {
  name: string;
  /** Server-written explanation, e.g. "Due tonight · Mon–Fri pool". */
  detail: string;
  urgency: 'DueTonight' | 'MustDoDaily' | 'GettingTight';
  /** Preformatted money at risk, e.g. "$1.50". */
  money?: string;
  minutes?: number;
}

/**
 * "At risk today" — what is actually on the line right now, and nothing else.
 * The **server owns the ordering** (DueTonight → MustDoDaily → GettingTight);
 * this card renders only true states, never re-sorts, and never nags. With
 * nothing at risk it says one quiet line and stops.
 */
export interface AtRiskCardProps {
  items?: AtRiskItem[];
  /** The one optional look-ahead line shown in the calm state. */
  previewLine?: string;
  /** Server-summed totals. Only shown with 2+ items. */
  totalMoney?: string;
  totalMinutes?: number;
  style?: React.CSSProperties;
}
export declare function AtRiskCard(props: AtRiskCardProps): JSX.Element;
