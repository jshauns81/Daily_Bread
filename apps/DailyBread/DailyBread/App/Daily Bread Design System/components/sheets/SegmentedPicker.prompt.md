One-line: a two- or three-way choice; label the options so no caption is needed.

```jsx
<SegmentedPicker value={kind} onChange={setKind}
  options={[{value:'task',label:'Earns'},{value:'routine',label:'Expected'}]} />
```

The app *does* add a caption under this one, because the money rule matters:
"Earns money when it's done and approved." / "Just expected — no money attached."
