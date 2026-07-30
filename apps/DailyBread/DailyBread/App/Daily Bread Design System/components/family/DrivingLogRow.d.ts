/**
 * Supervised-driving log: a totals header and one row per logged drive.
 * The teen logs; a parent approves; only approved drives count toward the totals.
 */
export interface DrivingTotalsProps {
  /** Approved hours only. */
  totalHours?: number;
  /** Subset of total that counted as night driving. */
  nightHours?: number;
  style?: React.CSSProperties;
}
export declare function DrivingTotals(props: DrivingTotalsProps): JSX.Element;

export interface DrivingLogRowProps {
  /** Short date, e.g. "Jun 25". */
  date: string;
  /** Human duration, e.g. "36m" or "1h 20m". */
  duration: string;
  /** Clock start, e.g. "22:03". */
  from: string;
  /** Clock end, e.g. "22:39". */
  to: string;
  /** Supervising adult, rendered as "with Dad". */
  supervisor?: string;
  /** Moon icon instead of sun; night hours are tracked separately. */
  isNight?: boolean;
  /** Pending drives show a tag and do not count yet. */
  status?: 'Pending' | 'Approved';
  /** Free-text note the teen added. */
  note?: string;
  style?: React.CSSProperties;
}
export declare function DrivingLogRow(props: DrivingLogRowProps): JSX.Element;
