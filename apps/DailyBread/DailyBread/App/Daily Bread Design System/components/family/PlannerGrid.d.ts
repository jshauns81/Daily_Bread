import * as React from 'react';

export interface PlannerGridChore {
  id?: number | string;
  name: string;
  icon?: string;
  /** Preformatted money, shown under the name. */
  value?: string;
  isTask?: boolean;
  /** Full day names currently scheduled. */
  days?: string[];
  /** For a weekly-goal chore: shows "3×/wk" across the row instead of cells. */
  weeklyTarget?: number;
  assignee?: string;
  active?: boolean;
}

/**
 * The weekly chore grid — chores down the side, seven days across the top,
 * a tap-to-toggle 32px cell at each crossing. The schedule at a glance, and
 * editable in place. Sunday-first; the header pins while the list scrolls.
 * A weekly-goal chore shows its target spanning the week instead of cells.
 */
export interface PlannerGridProps {
  chores?: PlannerGridChore[];
  /** Show the assignee under each name — only with 2+ children on "Everyone". */
  showAssignee?: boolean;
  onToggle?: (chore: PlannerGridChore, dayFullName: string) => void;
  onEdit?: (chore: PlannerGridChore) => void;
  style?: React.CSSProperties;
}
export declare function PlannerGrid(props: PlannerGridProps): JSX.Element;
