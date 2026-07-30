import * as React from 'react';

/**
 * The Earnings screen's hero. The **only** place a different face appears:
 * `.system(size: 42, weight: .bold, design: .rounded)` — SF Pro Rounded — in
 * gold, with `.contentTransition(.numericText())` so the figure animates when
 * money lands. 18px card padding.
 */
export interface BalanceHeroProps {
  /** Preformatted, e.g. "$48.75". Renders "—" while loading. */
  balance?: string;
  /** Default "Balance". */
  label?: string;
  style?: React.CSSProperties;
}
export declare function BalanceHero(props: BalanceHeroProps): JSX.Element;
