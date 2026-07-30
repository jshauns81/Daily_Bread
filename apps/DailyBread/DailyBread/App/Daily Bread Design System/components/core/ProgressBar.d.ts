import * as React from 'react';

/**
 * SwiftUI `ProgressView(value:)` tinted accent — goal progress, badge
 * progress, batch-approval progress. `tone="progress"` uses the theme's
 * one-warm-family glow gradient (accent → gold-warm), reserved for the XP bar.
 */
export interface ProgressBarProps {
  value?: number;
  total?: number;
  /** Track height. Default 6; the XP bar is 10. */
  height?: number;
  tone?: 'accent' | 'gold' | 'success' | 'progress';
  style?: React.CSSProperties;
}
export declare function ProgressBar(props: ProgressBarProps): JSX.Element;
