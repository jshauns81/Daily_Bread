import * as React from 'react';

/**
 * SwiftUI `Stepper`. One use: a weekly-goal chore's target,
 * `"up to 3× a week"`, 1…7, with `.monospacedDigit()` so the label doesn't
 * jitter as it changes.
 */
export interface StepperProps {
  /** The whole sentence, e.g. "up to 3× a week". */
  label: React.ReactNode;
  value?: number;
  min?: number;
  max?: number;
  onChange?: (value: number) => void;
  style?: React.CSSProperties;
}
export declare function Stepper(props: StepperProps): JSX.Element;
