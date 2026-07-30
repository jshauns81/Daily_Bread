One-line: the only text input in the app; the `$` lives inside it.

```jsx
<TextField value={name} onChange={setName} placeholder="What's the chore?" />
<TextField prefix="$" value={amt} onChange={setAmt} placeholder="1.00" />
<TextField value={icon} onChange={setIcon} placeholder="🧺" width={46} align="center" />
<TextField multiline rows={2} placeholder="e.g. don't forget under the bed" />
```

The emoji field is capped at 2 characters in the source. No focus ring — don't add one.
