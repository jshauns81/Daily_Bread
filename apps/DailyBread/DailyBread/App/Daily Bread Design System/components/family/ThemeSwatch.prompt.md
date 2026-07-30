One-line: the theme picker — show the look, don't name the colour.

```jsx
{DB_THEMES.map(t => (
  <ThemeRow key={t.key} theme={t} selected={t.key === theme}
    onSelect={(k) => document.documentElement.dataset.theme = k} />
))}
```

Footer copy is exact: *"Pick the look you like. It changes everywhere, on every device —
switch whenever you feel like a change."* The mood line ("warm · light") does the work a
hex value can't.
