Picker for whose data a shared card shows — and the enforcement point for the single-child rule.

\`\`\`jsx
<ChildPicker children={['Emma','Liam','Noah']} value={who} onChange={setWho} />
<YearHeatmapCard title={who + "'s year"} />
\`\`\`

**Returns \`null\` when given one name or none.** Callers should render it unconditionally
and let the component decide, so no screen can accidentally show a one-item picker that
implies siblings who don't exist. Pair it with a possessive title on the card below; with
one child, drop the picker and title the card with that child's name directly.
