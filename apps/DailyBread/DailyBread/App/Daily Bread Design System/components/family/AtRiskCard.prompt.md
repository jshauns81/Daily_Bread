One-line: the stakes, shown before the chore list — and silent when there are none.

```jsx
<AtRiskCard items={[
  {name:'Dishes', detail:'Due tonight', urgency:'DueTonight', money:'$2.00', minutes:20},
  {name:'Walk the dog', detail:'Every day', urgency:'MustDoDaily', minutes:15}
]} totalMoney="$2.00" totalMinutes={35} />

<AtRiskCard previewLine="Trash goes out tomorrow." />
```

**Never re-sort the items** — server order is the truth. **Never add a nag.** The calm
state is one `caption` `.secondary` line: "Nothing at risk today ✌️".
