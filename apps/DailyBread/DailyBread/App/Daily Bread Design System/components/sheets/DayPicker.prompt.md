One-line: pick a chore's fixed days; always Sunday-first, always full names.

```jsx
<DayPicker selected={days} onChange={setDays} />
{days.length === 0 && <InlineError icon="exclamationmark.circle" style={{color:'var(--ds-help)'}}>Pick at least one day.</InlineError>}
```

Emits full names ("Monday") because that's the wire format — never indices.
