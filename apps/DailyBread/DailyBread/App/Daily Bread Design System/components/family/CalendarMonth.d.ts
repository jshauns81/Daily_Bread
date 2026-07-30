/**
 * Month overview: one status dot per day, plus a fixed three-item legend.
 */
export interface CalendarDay {
  /** Day of month, 1-31. */
  day: number;
  /** Omit for a day with nothing scheduled (renders a neutral dot). */
  status?: 'allDone' | 'inProgress' | 'missed';
  /** Future or padding day — dimmed and non-committal. */
  outside?: boolean;
}
export interface CalendarMonthProps {
  /** e.g. "July 2026". */
  monthLabel: string;
  /** Days 1..n in order. */
  days?: CalendarDay[];
  /** Day number to ring with the accent. */
  selected?: number;
  /** Weekday index the 1st falls on (0 = Sunday) — pads the grid. */
  firstWeekday?: number;
  onSelect?: (day: number) => void;
  onPrev?: () => void;
  onNext?: () => void;
  style?: React.CSSProperties;
}
export declare function CalendarMonth(props: CalendarMonthProps): JSX.Element;
