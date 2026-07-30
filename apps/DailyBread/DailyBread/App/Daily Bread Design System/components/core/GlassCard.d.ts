import * as React from 'react';

/**
 * Every surface in Daily Bread. Opaque card colour on the theme's gradient
 * background, 20px continuous radius, 0.5px stroke, `shadow(radius: 10, y: 3)`.
 * Cards do NOT lift on hover — this is a touch-first app.
 */
export interface GlassCardProps {
  /** 14 default · 12 tile · 16 greeting · 18 hero. Pass a CSS length. */
  padding?: string;
  /** Adds a 1px inset ring at 35% of this colour — the earned-badge treatment. */
  tone?: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function GlassCard(props: GlassCardProps): JSX.Element;
