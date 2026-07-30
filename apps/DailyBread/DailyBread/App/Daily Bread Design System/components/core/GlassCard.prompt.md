One-line: reach for this before anything else — it is the app's only card.

```jsx
<GlassCard><SectionHeader>Screen time this week</SectionHeader>…</GlassCard>
<GlassCard padding="var(--ds-card-padding-tight)">…</GlassCard>
<GlassCard tone="var(--ds-gold)">…</GlassCard>   {/* earned badge */}
```

Never add `backdrop-filter`. Never add a hover shadow. The kid's Home hero is the one
card that opts out (24px radius + a tier gradient + a tier border).
