One-line: the week's earnings at a glance on the parent's Home.

```jsx
<WeekStrip days={[
  {letter:'S'}, {letter:'M', amount:'$3.50'}, {letter:'T', amount:'$2.00'},
  {letter:'W'}, {letter:'T', amount:'$4.25'}, {letter:'F', amount:'$2.75'}, {letter:'S'}
]} />
```

Day letters come from the locale's narrow weekday format — don't hardcode English.
