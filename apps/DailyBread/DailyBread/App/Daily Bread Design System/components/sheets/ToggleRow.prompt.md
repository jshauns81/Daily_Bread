One-line: a switch row; use the description line to say what it actually does.

```jsx
<ToggleRow label="Auto-approve completions" description="No parent check needed"
  checked={auto} onChange={setAuto} />
<ToggleRow label="Savings goals" description="Show goals to Ada" checked={g} onChange={setG} />
```

Note the single-child copy: name the child rather than saying "the kids".
