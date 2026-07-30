import * as React from 'react';

/**
 * SwiftUI `Toggle` with an optional secondary line — the shape used for
 * "Auto-approve completions / No parent check needed" and every family feature
 * switch. The switch is the iOS green, not the theme accent.
 */
export interface ToggleRowProps {
  label: string;
  /** Second line in caption/.secondary. */
  description?: string;
  checked?: boolean;
  onChange?: (checked: boolean) => void;
  style?: React.CSSProperties;
}
export declare function ToggleRow(props: ToggleRowProps): JSX.Element;
