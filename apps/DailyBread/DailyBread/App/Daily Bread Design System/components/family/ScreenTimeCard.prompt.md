One-line: copy this component's *structure* whenever you state stakes anywhere in the app.

```jsx
<ScreenTimeCard
  weekday={{minutes:'2h 30m', floor:'1h 30m', atRisk:60}}
  weekend={{minutes:'4h', floor:'3h', atRisk:60}}
  prices={[{name:'Take out the trash',minutes:15},{name:'Dishes',minutes:20}]}
  onHistory={openHistory} />
```

Never lead with the threat. "Always keeps **1h 30m**" comes first, with a lock, in
`.secondary`. Empty state: "No chores are priced this week — nothing at risk. 😎"
