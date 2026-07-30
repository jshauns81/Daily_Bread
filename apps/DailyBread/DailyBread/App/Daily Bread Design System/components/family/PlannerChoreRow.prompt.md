One-line: a chore in the planner list; the right side says whether it pays.

```jsx
<PlannerChoreRow icon="🗑" name="Take out the trash" value="$1.50" schedule="Wed, Sat" importance={3} onTap={edit} />
<PlannerChoreRow icon="🛏" name="Make your bed" isTask={false} schedule="Every day" />
<PlannerChoreRow icon="🦷" name="Brush teeth" isTask={false} schedule="Every day" active={false} />
```

**Single-child rule:** never pass `assignee` in a one-child household.
