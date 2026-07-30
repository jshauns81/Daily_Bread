import * as React from 'react';

/** One day's real payload. Optionals only ever add bloom — never withhold colour. */
export interface HeatmapDay {
  requiredDone: number;
  requiredTotal: number;
  optionalDone?: number;
  optionalTotal?: number;
  /** Future days render as an empty outline. */
  isFuture?: boolean;
}

/**
 * The rainbow year. Hue = real day-of-year, so a day is the same colour on every
 * surface; intensity = required-chore ratio on a linear ramp from a 34% saturation
 * floor. A perfect day with optionals done blooms. One renderer, three sizes.
 */
export interface YearHeatmapCardProps {
  /** Sentence case. "Your year" for the kid, "Ada's year" for a parent. */
  title?: string;
  /** `card` = last 12 weeks, no scroll, sits on Today. `full` = the year, scrollable. `wall` = iPad/macOS. */
  size?: 'card' | 'full' | 'wall';
  /** Days ending today. Legacy status strings are still accepted and mapped. */
  days?: (HeatmapDay | 'AllComplete' | 'PartialComplete' | 'NoneComplete' | 'none')[];
  /** Override the computed count in the footer. */
  perfectDays?: number;
  /** Tap a day to open it. Never fires on a future day. */
  onDay?: (day: HeatmapDay & { dayOfYear: number }) => void;
  /** Override the size preset's cell edge, in px. */
  cell?: number;
  /** Override the size preset's column count. */
  weeks?: number;
  style?: React.CSSProperties;
}
export declare function YearHeatmapCard(props: YearHeatmapCardProps): JSX.Element;
