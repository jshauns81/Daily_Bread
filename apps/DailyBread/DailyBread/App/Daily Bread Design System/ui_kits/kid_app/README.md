# UI kit — Kid app (iPhone)

Recreated from `DailyBread/DailyBread/Features/Home/KidHomeView.swift`,
`Features/Today/TodayView.swift`, `Features/Earnings/{EarningsView,GoalsView}.swift`,
`Features/Awards/AchievementsView.swift`, `Features/Settings/SettingsView.swift`,
and `Components/{AtRiskCard,ScreenTimeCard,YearHeatmapCard,ConfettiView}.swift`.

## Files
- `index.html` — the interactive kit. Five tabs, real state, a working Help sheet.
- `KidScreens.jsx` — `KidHome`, `KidToday`, `KidEarnings`, `KidAwards`,
  `AppSettings` (shared with the parent kit), `HelpSheet`.

## What you can do
- **Tap a chore's circle** on Today or **Done** on Home → it completes optimistically,
  the ring and XP bar climb, the balance ticks up, and the level badge changes tier and
  colour as you cross 25/50/75/100%.
- **Clear the day** → confetti for 2.8s, and the "Day complete — every chore done ✨"
  banner appears.
- **Raise Help** → the sheet explains what Help *does* ("protects it from tonight's
  penalty until a grown-up responds"), takes the kid's own words, and the row becomes
  "Help raised — protected" with a solid HELP tag.
- **Settings → Theme** → switch any of the six themes live. Every surface follows,
  because nothing in the kit holds a hardcoded colour.

## Layout honoured
- 393px iPhone width (`--ds-iphone-width`). Screens pad 16, cards stack 14 apart.
- The Home hero is the one card that **opts out of `glassCard()`**: 24px radius,
  `.regularMaterial` plus a tier gradient (20% → 2%) and a 28% tier border.
- Five tabs. Tap targets never below 44px.

## Voice
Kid copy uses the app's own strings, including the `KidVoice` age-tier phrasing
("waiting on **a grown-up**" — a teen would read "your parents"). The kit is pinned to
the `younger` tier.

## Not recreated
The Calendar month view, the Goals management screen, Reward Claims, the Driving log,
and the screen-time History sheet. Confetti is a simplified CSS version of
`ConfettiView`.
