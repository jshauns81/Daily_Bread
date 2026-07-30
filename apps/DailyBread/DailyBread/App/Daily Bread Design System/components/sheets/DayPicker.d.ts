import * as React from 'react';

/**
 * Compact Sunday-first day toggles (S M T W T F S) — 36px circles, accent-filled
 * when on. Reads as a day selector, not a row of big buttons. The two repeated
 * letters (Tue/Thu, Sun/Sat) are the standard calendar header, by design.
 * Values are full day names, so they match the wire format.
 */
export interface DayPickerProps {
  /** Full day names, e.g. ["Monday","Wednesday"]. */
  selected?: string[];
  /** Always emits Sunday-first order. */
  onChange?: (days: string[]) => void;
  style?: React.CSSProperties;
}
export declare function DayPicker(props: DayPickerProps): JSX.Element;
/** ["Sunday" … "Saturday"] — the canonical order. */
export declare const ALL_DAYS: string[];
