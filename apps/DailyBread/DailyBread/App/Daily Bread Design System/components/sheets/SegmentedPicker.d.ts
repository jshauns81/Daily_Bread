import * as React from 'react';

/**
 * SwiftUI `.pickerStyle(.segmented)`. Three appear in the app and all three
 * carry meaning in their labels rather than in an explanation:
 * Tasks/Routines · Earns/Expected · Fixed days/Weekly goal.
 */
export interface SegmentedPickerProps {
  options?: Array<string | { value: string; label: string }>;
  value?: string;
  onChange?: (value: string) => void;
  style?: React.CSSProperties;
}
export declare function SegmentedPicker(props: SegmentedPickerProps): JSX.Element;
