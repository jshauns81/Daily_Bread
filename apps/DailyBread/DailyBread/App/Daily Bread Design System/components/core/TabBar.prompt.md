One-line: the iPhone tab bar; never more than five tabs.

```jsx
<TabBar tabs={PARENT_TABS} active="home" badges={{approvals: 4}} onSelect={setTab} />
<TabBar tabs={KID_TABS} active="kidHome" onSelect={setTab} />
```

On macOS the same sections become a `NavigationSplitView` sidebar titled "Daily Bread".
