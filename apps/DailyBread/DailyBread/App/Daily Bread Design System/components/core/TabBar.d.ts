import * as React from 'react';

export interface TabItem { id: string; label: string; icon: string }

/**
 * The iOS tab bar from `MainView`. Kids get five tabs, parents four, driven by
 * one `Section` enum that also builds the macOS sidebar. Approvals carries a
 * badge = pendingApprovals + helpRequests.
 * (Intentional addition — SwiftUI's TabView, made renderable on the web.)
 */
export interface TabBarProps {
  /** KID_TABS or PARENT_TABS, both exported. */
  tabs?: TabItem[];
  active?: string;
  /** Badge counts keyed by tab id, e.g. { approvals: 4 }. */
  badges?: Record<string, number>;
  onSelect?: (id: string) => void;
  style?: React.CSSProperties;
}
export declare function TabBar(props: TabBarProps): JSX.Element;
export declare const KID_TABS: TabItem[];
export declare const PARENT_TABS: TabItem[];
