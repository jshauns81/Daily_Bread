One-line: the kid's Home hero — the game, not the spreadsheet.

```jsx
const pct = Math.round(done / total * 100);
<LevelBadge percent={pct} />
<XPBar done={done} total={total} />
```

Level colours never follow the theme. The bar fills with `.spring(0.5)`; the badge and
milestones pop with `.spring(0.4)`. Use `TierFor(pct)` to colour the hero's border and
gradient to match.
