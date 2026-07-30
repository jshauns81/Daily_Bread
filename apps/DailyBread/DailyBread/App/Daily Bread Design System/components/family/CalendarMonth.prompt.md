Month grid with a single status dot per day, for reviewing how the weeks have gone.

\`\`\`jsx
<CalendarMonth monthLabel="July 2026" firstWeekday={3} selected={25}
  days={[{day:1,status:'missed'}, {day:5,status:'allDone'}, {day:6,status:'inProgress'}]}
  onSelect={setDay} onPrev={prev} onNext={next} />
\`\`\`

Exactly **three** statuses — All done (success), In progress (accent), Missed (Help red) —
and the legend is always shown, because a bare coloured dot is not self-explanatory.
Days with nothing scheduled get a neutral dot, never a red one: an unscheduled day is not
a failure. Future days pass \`outside\` and dim rather than implying a verdict.
