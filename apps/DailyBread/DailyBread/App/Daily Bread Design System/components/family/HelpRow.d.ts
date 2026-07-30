import * as React from 'react';

/**
 * An open Help request in the parent's queue. An 8px help-red dot, the chore,
 * the kid's own words (or a fallback), and a chevron — the row opens the
 * three-outcome Help sheet, never a dialog.
 */
export interface HelpRowProps {
  choreName: string;
  /** The kid's typed reason. Falls back to "{childName} needs a hand". */
  reason?: string;
  childName?: string;
  onOpen?: () => void;
  style?: React.CSSProperties;
}
export declare function HelpRow(props: HelpRowProps): JSX.Element;
