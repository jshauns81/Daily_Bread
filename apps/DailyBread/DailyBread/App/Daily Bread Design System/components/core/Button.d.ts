import * as React from 'react';

/**
 * The app's button treatments. `gold` is Approve — the Blessing — and gold is
 * only ever money, so no other action may use it. `capsuleAccent`/`capsuleHelp`
 * are the Done/Help pair on the kid's Next Up card.
 */
export interface ButtonProps {
  variant?: 'prominent' | 'gold' | 'bordered' | 'destructive' | 'capsuleAccent' | 'capsuleHelp' | 'plain';
  /** SF Symbol name, e.g. "hand.thumbsup", "checkmark.circle.fill". */
  icon?: string;
  block?: boolean;
  disabled?: boolean;
  /** Shows a spinner glyph and disables. */
  busy?: boolean;
  size?: 'sm' | 'md' | 'lg';
  onClick?: (e: React.MouseEvent) => void;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function Button(props: ButtonProps): JSX.Element;
