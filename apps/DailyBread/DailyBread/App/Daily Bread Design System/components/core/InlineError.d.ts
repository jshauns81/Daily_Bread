import * as React from 'react';

/**
 * How this app reports failure: a `footnote` `.secondary` label with a symbol,
 * inline where the content would have been. **Never** an alert, never a toast,
 * never red unless it is a validation error inside a sheet.
 * (Intentional addition — the same Label is repeated in a dozen views.)
 */
export interface InlineErrorProps {
  /** "wifi.exclamationmark" for network, "exclamationmark.circle" in sheets. */
  icon?: string;
  children?: React.ReactNode;
  style?: React.CSSProperties;
}
export declare function InlineError(props: InlineErrorProps): JSX.Element;
