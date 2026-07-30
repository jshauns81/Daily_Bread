import * as React from 'react';

export interface LevelTier { level: number; title: string; icon: string; color: string }

/**
 * The kid's level: a capsule in the tier's own colour, glowing in that colour
 * at 50%. Tier colours are FIXED, not theme-derived — a level's colour is its
 * identity. Level is % of today's chores done: 1/25/50/75/100 → 1…5.
 */
export interface LevelBadgeProps {
  /** 0–100. */
  percent?: number;
  style?: React.CSSProperties;
}
export declare function LevelBadge(props: LevelBadgeProps): JSX.Element;

/**
 * The XP bar: "TODAY'S PROGRESS" / "5 / 7 XP" in tabular figures, a 10px
 * capsule filling accent → tier colour, and four milestone emoji that ungrey
 * and scale up as they're passed.
 */
export interface XPBarProps {
  done?: number;
  total?: number;
  /** Override the computed percentage. */
  percent?: number;
  style?: React.CSSProperties;
}
export declare function XPBar(props: XPBarProps): JSX.Element;

export declare const LEVEL_TIERS: LevelTier[];
/** % done → tier. Mirrors KidHomeView.tier(for:). Capitalised so it reaches
 *  the window namespace — consumers need `tier.color` for the hero gradient. */
export declare function TierFor(percent: number): LevelTier;
