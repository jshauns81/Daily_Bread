import * as React from 'react';

/**
 * SwiftUI `ContentUnavailableView`: a large light-stroke symbol, a short
 * headline, one calm sentence, optionally one action. Never a sad face,
 * never an apology.
 */
export interface EmptyStateProps {
  /** SF Symbol name — "house", "checklist", "checkmark.seal". */
  icon?: string;
  title: string;
  description?: string;
  action?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function EmptyState(props: EmptyStateProps): JSX.Element;
