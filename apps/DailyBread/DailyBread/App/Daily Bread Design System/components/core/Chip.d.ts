import * as React from 'react';

/**
 * The parent greeting's three chips: 13% tinted capsule, coloured text.
 * `emphasized: false` greys it out — the app dims a chip whose count is zero
 * instead of hiding it, so the row never reflows.
 */
export interface ChipProps {
  /** e.g. var(--ds-gold) for approvals, var(--ds-help) for help. */
  tone?: string;
  /** False renders it in .secondary regardless of tone. Default true. */
  emphasized?: boolean;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function Chip(props: ChipProps): JSX.Element;

/** The smaller heavy capsule: HELP, Off, Routine, Cash out ready. */
export interface TagProps {
  tone?: string;
  /** Solid fill with white text (the HELP badge). Default tinted at 15%. */
  solid?: boolean;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function Tag(props: TagProps): JSX.Element;
