# Widgets

Every WidgetKit family for Daily Bread. Built against `IMPLEMENTATION.md` §1.2a.

## Files

- `index.html` — interactive preview. Sliders for done/total, Kid/Parent toggle, all six themes.
- `Widgets.jsx` — the widget bodies, composing `GlassCard`, `ProgressRing`, `ChoreRow`, `Tag` from the bundle.

## The rainbow maths

`RainbowGrid` takes a `weeks` count and renders the **real trailing window** ending today.
Hue is always `dayOfYear / 365 × 360` — never position within the displayed window.
That is what makes a March day a March colour on every surface, and it means the small
widget shows a genuine 4-week slice of the wheel rather than a compressed full spectrum.

Intensity is `pow(requiredDone / requiredTotal, 1.6)`; optionals add bloom, never
withhold colour (decision B, §1.2).

## Families

| Family | Kid | Parent |
|---|---|---|
| Small | 4-week rainbow + today's ring | Approvals waiting |
| Medium · Year | 12 weeks, matching the Today card | — |
| Medium · Today | chore list, interactive | At risk today |
| Large | full year + balance + streak | week rings + 12-week rainbow |
| Lock circular | completion ring | — |
| Lock rectangular | count + pip bar | "2 waiting on you" |
| Lock inline | count | — |
| StandBy | 26 weeks, full colour | — |

**Two medium kinds is deliberate.** The year widget answers "how am I doing"; the Today
widget answers "what's left" and is checkable inline via App Intents. They are separate
`Widget` kinds, both offered; the user picks.

## Lock Screen constraint

Accessory widgets are monochrome — iOS applies its own tint, so colour carries no
meaning there and the rainbow cannot appear. Those three read from shape and fill only.
StandBy is not an accessory family and does get full colour.

## Notes for implementation

Numbered notes ①–⑦ are in `index.html`. The two that bite hardest:

- Read the active theme from the shared **App Group** container — widgets run in a
  separate process and will otherwise render in default colours beside a themed app.
- **No money on the Lock Screen by default.** Opt-in setting; ship count-only.
