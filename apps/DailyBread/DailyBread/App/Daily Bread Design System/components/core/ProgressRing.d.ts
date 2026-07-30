import * as React from 'react';

/**
 * Today's progress, as a ring with a "5/7" label. Accent stroke, round cap,
 * 7px wide, on a `.quaternary` track. Animates with `.snappy`.
 * Three sizes in the app: 64 (Today header), 48 (parent child tile),
 * 44 (parent child row).
 */
export interface ProgressRingProps {
  /** 0–1. */
  progress?: number;
  /** Centre text — always "done/total", never a percentage. */
  label?: React.ReactNode;
  /** 64 | 48 | 44. Default 64. */
  size?: number;
  /** Stroke width. Default 7. */
  stroke?: number;
  color?: string;
  style?: React.CSSProperties;
}
export declare function ProgressRing(props: ProgressRingProps): JSX.Element;
