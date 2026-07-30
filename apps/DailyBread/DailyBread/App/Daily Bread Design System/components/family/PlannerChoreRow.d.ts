import * as React from 'react';

/**
 * One chore in the parent's planner list. Icon, name (greyed with an "Off" tag
 * when inactive), a schedule caption, and on the right either the gold earn
 * value (Task) or a quiet "Routine" capsule — plus "📺 N" when it weighs on
 * screen time. Tap anywhere to edit.
 *
 * Swipe in the app: leading = Deactivate/Activate, trailing = Delete (help red,
 * never full-swipe — it routes through an inline confirm row).
 */
export interface PlannerChoreRowProps {
  name: string;
  /** Falls back to 💰 for a Task, ✅ for a Routine. */
  icon?: string;
  /** Preformatted money. Ignored when isTask is false. */
  value?: string;
  /** Task (earns) vs Routine (expected). Default true. */
  isTask?: boolean;
  /** e.g. "Mon, Wed, Fri" or "up to 3× a week". */
  schedule?: string;
  /** Only shown on the "Everyone" filter with 2+ children. */
  assignee?: string;
  /** Screen-time importance 0–10; shows "📺 N" when above zero. */
  importance?: number;
  active?: boolean;
  onTap?: () => void;
  style?: React.CSSProperties;
}
export declare function PlannerChoreRow(props: PlannerChoreRowProps): JSX.Element;
