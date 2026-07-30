import * as React from 'react';

/**
 * "12 more help requests ⌄" — the app previews three of anything long and
 * expands **inline** rather than pushing to another screen. Tap again to fold.
 * (Intentional addition — a private helper in ApprovalsView.)
 */
export interface ExpandRowProps {
  expanded?: boolean;
  moreCount?: number;
  /** e.g. "more waiting", "more help requests". */
  label?: string;
  /** Gold for approvals, help red for help requests. */
  color?: string;
  onToggle?: () => void;
  style?: React.CSSProperties;
}
export declare function ExpandRow(props: ExpandRowProps): JSX.Element;

/**
 * The row a destructive action **morphs into**, in place. This app has no
 * system alerts and no confirmation dialogs — the row itself asks.
 * (Intentional addition — DeleteConfirmRow, plus the approve-all confirm.)
 */
export interface ConfirmRowProps {
  /** State the consequence: "Delete “Dishes”? This can't be undone." */
  message: string;
  confirmTitle?: string;
  /** The safe option, and it comes FIRST. Default "Keep". */
  keepTitle?: string;
  /** Help red for destructive, gold for approve-all. */
  tone?: string;
  onConfirm?: () => void;
  onKeep?: () => void;
  style?: React.CSSProperties;
}
export declare function ConfirmRow(props: ConfirmRowProps): JSX.Element;
