One-line: one labelled control inside a SheetCard.

```jsx
<SheetField label="Amount"><TextField prefix="$" value={amt} onChange={setAmt} /></SheetField>
<SheetField label="Importance" value="7 of 10" valueColor="var(--ds-accent)">
  <Slider value={7} max={10} onChange={setImp} />
</SheetField>
```

When a value is zero the label itself changes rather than showing "0" —
`label={imp === 0 ? "No screen-time impact" : "Importance"}`.
