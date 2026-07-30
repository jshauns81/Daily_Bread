One-line: the rainbow year — the app's best moment. Hue is the day of the year, not the score.

```jsx
<YearHeatmapCard title="Your year" />                             {/* card: last 12 weeks */}
<YearHeatmapCard title="Ada's year" size="full" onDay={openDay} /> {/* the year, scrollable */}
<YearHeatmapCard size="wall" days={days} />                        {/* iPad / macOS */}
```

**Hue = day-of-year**, so a given day is the same colour on the card, the full year, the
wall and every widget. **Intensity = `requiredDone / requiredTotal`** on a linear ramp
from a 34% saturation floor — a bad day is a *drained* colour, never a grey hole, so the
rainbow survives regardless of performance. **Optionals only add bloom**; they never
withhold colour.

Sizes: `card` 18px cells / no scroll / fits an iPhone · `full` 11px / scrolls to today ·
`wall` 14px. No less→more legend — a rainbow has nothing to explain.
