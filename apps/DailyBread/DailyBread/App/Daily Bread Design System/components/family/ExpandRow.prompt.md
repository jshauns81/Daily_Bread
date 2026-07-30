One-line: expand a long list inline (ExpandRow); confirm in place (ConfirmRow). Never a dialog.

```jsx
<ExpandRow expanded={open} moreCount={12} label="more waiting"
  color="var(--ds-gold)" onToggle={() => setOpen(!open)} />

<ConfirmRow message={'Delete “Dishes”? This can’t be undone.'}
  onKeep={cancel} onConfirm={del} />

<ConfirmRow message="Approve 5 chores?" confirmTitle="Approve — $12.50"
  keepTitle="Cancel" tone="var(--ds-gold)" onKeep={cancel} onConfirm={approveAll} />
```

The safe option is always on the left and named for what it does ("Keep", not "Cancel",
when the alternative is deletion).
