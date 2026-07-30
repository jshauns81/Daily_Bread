import * as React from 'react';

/**
 * A badge in the trophy case. Earned badges show their rarity + points and gain
 * a 1px gold ring at 35%; locked ones are greyscaled at 55% and show either
 * their progress bar or just their point value. Hidden achievements stay
 * mysterious — pass no description.
 *
 * Note: legendary rarity is gold, which is the one documented overload of the
 * money signal (see readme AUDIT FINDINGS).
 */
export interface AchievementCardProps {
  /** The badge's emoji. */
  icon?: string;
  name: string;
  description?: string;
  points?: number;
  rarity?: 'common' | 'uncommon' | 'rare' | 'epic' | 'legendary';
  earned?: boolean;
  /** Locked + showProgress: current value. */
  progress?: number;
  target?: number;
  style?: React.CSSProperties;
}
export declare function AchievementCard(props: AchievementCardProps): JSX.Element;
