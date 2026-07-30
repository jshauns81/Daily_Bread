import * as React from 'react';

/**
 * The app's only uppercase treatment: `caption.weight(.bold)` + `.secondary`
 * + kerning. Write the text in sentence case; the component uppercases it.
 * (Intentional addition — four private `sectionHeader(_:)` funcs in the source.)
 */
export interface SectionHeaderProps {
  /** 0.8px section header (default) or 1.0px eyebrow. */
  kerning?: 'section' | 'eyebrow';
  /** Override the colour — e.g. accent for NEXT UP, help for HELP RAISED. */
  color?: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function SectionHeader(props: SectionHeaderProps): JSX.Element;
