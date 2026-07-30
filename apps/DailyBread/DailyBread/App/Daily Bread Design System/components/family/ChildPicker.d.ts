/**
 * Picker for which child a shared card is scoped to. Renders nothing when the
 * household has one child — the single-child invariant is enforced internally.
 */
export interface ChildPickerProps {
  /** Child display names. One entry (or none) renders nothing at all. */
  children?: string[];
  /** Currently scoped name. */
  value?: string;
  onChange?: (name: string) => void;
  style?: React.CSSProperties;
}
export declare function ChildPicker(props: ChildPickerProps): JSX.Element | null;
