import * as React from 'react';

/**
 * A short title row at the top of a designed sheet. Padding is exactly
 * 18 top / 6 bottom / 16 sides.
 */
export interface SheetHeaderProps { title: string; style?: React.CSSProperties }
export declare function SheetHeader(props: SheetHeaderProps): JSX.Element;
