One-line: the importance slider — pair it with a SheetField that shows the live value.

```jsx
<SheetField label={imp === 0 ? 'No screen-time impact' : 'Importance'}
  value={imp === 0 ? null : imp + ' of 10'} valueColor="var(--ds-accent)">
  <Slider value={imp} max={10} onChange={setImp} />
</SheetField>
```
