import * as React from 'react';

export interface DBThemeInfo {
  key: string; name: string; mood: string;
  top: string; bottom: string; card: string; accent: string; gold: string; dark: boolean;
}

/**
 * A live preview of a theme — its real background gradient, a floating card
 * carrying the accent dot and a gold coin, and the progress glow beneath.
 * What she taps is what the app becomes.
 *
 * Known bug in the source: the gold coin hardcodes the LIGHT gold, so on
 * Mulberry and Harbor the coin is the wrong gold. This component takes the
 * gold from the theme, which is the fix.
 */
export interface ThemeSwatchProps {
  /** A theme key ("sunroom") or a DBThemeInfo object. */
  theme: string | DBThemeInfo;
  /** Default 76×52. The Settings disclosure label uses 64×44. */
  width?: number;
  height?: number;
  style?: React.CSSProperties;
}
export declare function ThemeSwatch(props: ThemeSwatchProps): JSX.Element | null;

/** Swatch + name + mood + a radio that fills with the theme's own accent. */
export interface ThemeRowProps {
  theme: string | DBThemeInfo;
  selected?: boolean;
  onSelect?: (key: string) => void;
  style?: React.CSSProperties;
}
export declare function ThemeRow(props: ThemeRowProps): JSX.Element | null;

/** All six themes, in the order the picker shows them. */
export declare const DB_THEMES: DBThemeInfo[];
