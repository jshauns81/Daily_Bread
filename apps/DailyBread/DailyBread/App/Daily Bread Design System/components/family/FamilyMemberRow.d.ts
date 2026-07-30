/**
 * One person in the household, with the parent-only management menu.
 * Avatar hue encodes role: accent = parent, gold = child.
 */
export interface FamilyMemberRowProps {
  /** Display name, e.g. "emma_test". First letter becomes the avatar. */
  name: string;
  /** Drives the avatar hue and the subtitle. */
  role?: 'Parent' | 'Child';
  /** Shows the quiet "You" marker after the name. */
  isYou?: boolean;
  /** Paused account — adds a Locked tag and flips the menu verb. */
  locked?: boolean;
  onResetPassword?: () => void;
  onToggleLock?: () => void;
  style?: React.CSSProperties;
}
export declare function FamilyMemberRow(props: FamilyMemberRowProps): JSX.Element;
