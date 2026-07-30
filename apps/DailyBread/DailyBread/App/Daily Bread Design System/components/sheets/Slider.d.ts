import * as React from 'react';

/**
 * SwiftUI `Slider`. One use in the app: a chore's screen-time **importance**,
 * 0–10 in steps of 1, where 0 means "no screen-time impact" and the SheetField
 * label changes to say so.
 */
export interface SliderProps {
  value?: number;
  min?: number;
  max?: number;
  step?: number;
  onChange?: (value: number) => void;
  style?: React.CSSProperties;
}
export declare function Slider(props: SliderProps): JSX.Element;
