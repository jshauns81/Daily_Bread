One-line: one group of fields in a sheet. Stack several down the sheet's scroll view.

```jsx
<SheetCard title="Schedule rule">
  <SegmentedPicker options={[{value:'days',label:'Fixed days'},{value:'weekly',label:'Weekly goal'}]} … />
  <DayPicker selected={days} onChange={setDays} />
</SheetCard>
```

The chore editor stacks six: identity, kind, schedule, who, screen time, options.
