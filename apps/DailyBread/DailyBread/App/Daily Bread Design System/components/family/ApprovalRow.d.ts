import * as React from 'react';

/**
 * One chore waiting for the gold moment. Approving washes the row in
 * `glow.opacity(0.18)` for 0.9s with `.easeOut(0.4)`, fires a success haptic,
 * then the row animates out of the queue.
 *
 * **Single-child rule:** omit `childName` when the family has one child —
 * whose chore it is only matters when there's more than one.
 */
export interface ApprovalRowProps {
  choreName: string;
  /** Omit in a single-child household. */
  childName?: string;
  /** Preformatted money, e.g. "$2.00". */
  value: string;
  /** The 0.9s Blessing state: gold wash + "Approved" + disabled. */
  approved?: boolean;
  busy?: boolean;
  onApprove?: () => void;
  style?: React.CSSProperties;
}
export declare function ApprovalRow(props: ApprovalRowProps): JSX.Element;
