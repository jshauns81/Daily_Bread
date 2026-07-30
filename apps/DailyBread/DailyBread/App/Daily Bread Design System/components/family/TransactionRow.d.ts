import * as React from 'react';

/**
 * One ledger line. Note the colour rule: a **positive** amount is gold, a
 * negative one is merely `.secondary` — money leaving isn't an alarm, so it is
 * never red. Amount is preformatted with its sign (`Money.signedDisplay`).
 */
export interface TransactionRowProps {
  description: string;
  /** Short date, e.g. "Jul 24". */
  date: string;
  /** Signed and preformatted, e.g. "+$2.00" / "−$25.00". */
  amount: string;
  negative?: boolean;
  style?: React.CSSProperties;
}
export declare function TransactionRow(props: TransactionRowProps): JSX.Element;
