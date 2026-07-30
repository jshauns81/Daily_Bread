One-line: the parent greeting chips (Chip) and the small status capsules (Tag).

```jsx
<Chip tone="var(--ds-gold)" emphasized={count > 0}>✦ {count} awaiting approval</Chip>
<Chip emphasized={false}>✓ 4 done today</Chip>
<Tag tone="var(--ds-help)" solid>HELP</Tag>
<Tag tone="var(--ds-success)">Cash out ready</Tag>
<Tag>Routine</Tag>
```

Chips use the typographic marks ✦ ✓ !, not emoji.
