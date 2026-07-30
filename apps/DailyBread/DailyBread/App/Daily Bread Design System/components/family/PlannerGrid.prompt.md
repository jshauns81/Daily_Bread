One-line: the planner's default view — the whole week, editable by tapping a cell.

```jsx
<PlannerGrid chores={[
  {id:1, icon:'🛏', name:'Make your bed', isTask:false, days:['Monday','Tuesday','Wednesday','Thursday','Friday']},
  {id:2, icon:'🍽', name:'Dishes', value:'$2.00', days:ALL_DAYS},
  {id:3, icon:'📚', name:'Read 20 minutes', value:'$1.00', weeklyTarget:5}
]} onToggle={toggleDay} onEdit={openEditor} />
```

Toggling is optimistic with `.snappy` and rolls back on failure. The parent can switch
to a list view; both are driven by one server-ordered array.
