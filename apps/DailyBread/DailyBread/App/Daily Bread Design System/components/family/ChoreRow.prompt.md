One-line: one chore on Today; tap the circle to complete, optimistically.

```jsx
<ChoreRow icon="🗑" name="Take out the trash" value="$1.50" onToggle={t} onHelp={h} />
<ChoreRow icon="🍽" name="Dishes" value="$2.00" done approvedBy="Dad" />
<ChoreRow icon="🐕" name="Walk the dog" help />
<ChoreRow icon="📚" name="Read 20 minutes" value="$1.00" weekly={{done:4,target:7}} />
```

Set `allowHelp={false}` when a parent is viewing a kid's day. Help is red because red
is the alert semantic — the status word is *protected*.
