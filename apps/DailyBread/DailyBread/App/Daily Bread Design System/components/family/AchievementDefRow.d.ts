/**
 * One achievement definition, as the parent manages it (not as the kid sees it).
 */
export interface AchievementDefRowProps {
  /** Emoji badge face. */
  icon: string;
  name: string;
  /** Drives the coloured rarity label; shares RARITY with AchievementCard. */
  rarity?: 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary';
  points: number;
  /** Plain-language trigger, e.g. "Day streak" or "First chore ever". */
  condition?: string;
  /** Disabled definitions dim but are never deleted. */
  enabled?: boolean;
  onToggle?: (next: boolean) => void;
  style?: React.CSSProperties;
}
export declare function AchievementDefRow(props: AchievementDefRowProps): JSX.Element;
