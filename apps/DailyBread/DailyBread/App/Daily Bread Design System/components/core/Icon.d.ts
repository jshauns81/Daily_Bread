import * as React from 'react';

/**
 * SF Symbols stand-in backed by Lucide (SF Symbols cannot ship to the web).
 * Accepts either an SF Symbol name — `"checkmark.circle.fill"`, `"flame.fill"` —
 * which is mapped, or a raw Lucide name.
 */
export interface IconProps {
  /** SF Symbol name (mapped) or a Lucide name. */
  name: string;
  /** Pixel size. Default 17 (matches .body). */
  size?: number;
  color?: string;
  /** Lucide stroke width. Default 2. */
  strokeWidth?: number;
  style?: React.CSSProperties;
}
export declare function Icon(props: IconProps): JSX.Element;
export declare const SF_TO_LUCIDE: Record<string, string>;
