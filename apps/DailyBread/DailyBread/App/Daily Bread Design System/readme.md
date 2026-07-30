# Daily Bread — Design System

Daily Bread is a **self-hosted family responsibility app for Apple platforms** —
SwiftUI, one codebase for iOS, iPadOS and macOS, talking to one family's own server.
It turns chores into money, screen time, savings goals and badges. No feeds, no ads,
no social layer, no multi-tenant anything.

The name is liturgical. The login screen reads **🍞 Daily Bread / *give us this day***.
Approving a chore is a **Blessing**. Excusing one is **granting dispensation**. The six
colour themes are named for rooms and weather, not for hues — Sunroom, Sky, Rosewater,
Meadow, Mulberry, Harbor.

## Two audiences, one system

|  | Kid | Parent |
|---|---|---|
| Tabs | Home · Today · Earnings · Awards · Settings | Home · Planner · Approvals · Settings |
| Home is | **the game** — a level that lights up in its rarity colour, an XP bar, quests remaining, streak fire, gold, latest badge, next quest | **at-a-glance** — greeting, three chips, stat tiles, each kid's ring, week strip, balances, a year of history |
| Second screen | **Today** — the daily driver: tap to complete, swipe to raise Help | **Planner** — every chore in the family, weekly grid or list, drag to reorder |
| Shares | glass cards, sheets, fields, list rows, progress rings, the whole theme system, every semantic colour | ← identical |

The parent screens **reuse the kid's Today screen** via `TodayView(userId:)` — one
component, two audiences, with Help hidden because raising Help is the kid's own act.
That is the clearest proof the system holds: the shared surface isn't a coincidence,
it's the architecture.

## Design intent (what the audit measures against)

1. **Warm, not cold.** From the source's own first line: *"Color is warmth, and warmth
   is the point."* The primary parent user is grieving; "sterile utility" is a failure
   state, not a neutral one. There is no grey in this system.
2. **Themeable to its core.** Every theme owns the **whole** surface — background
   gradient, card colour, accent, secondary — not a tint over grey. Six complete looks:
   four warm lights, two cozy darks.
3. **Semantic invariants.** **Gold = money. Red = the Help alert.** Help is deliberately
   kept distinct from every accent so the alert never hides in a pink or berry palette.
4. **One design language, two audiences.** See above.
5. **Grows with the child.** `KidVoice` swaps wording by age tier (`younger` / `teen`,
   13+) — *"a grown-up"* becomes *"your parents"*. The visual system never forks.
6. **Single-child sensitivity.** `SessionStore.isSingleChild` / `onlyChild` is the single
   source of truth, and roughly a dozen call sites branch on it. No child pickers,
   filters, switchers, or "which child" affordances when there is one child.
7. **Native-first.** SF Pro, SF Symbols, `List`, `Form`, `TabView`, `NavigationSplitView`,
   swipe actions, haptics, Dynamic Type. The custom warmth layer — themes, glass cards,
   the gold/help semantics — sits *on top of* Apple's conventions, never against them.

## Sources

- **`DailyBread/`** — the SwiftUI app. **This is the ground truth for everything here.**
  - **`Packages/DailyBreadKit/Sources/DailyBreadKit/Design/DesignSystem.swift`** — the
    entire design system in 241 lines: `DBTheme` (6 themes), `DB` (semantic
    invariants), `GlassCard`, `GraphiteBackground`, `Haptics`. Every token in `tokens/`
    comes from this file.
  - `Components/` — `SheetKit.swift` (SheetHeader / SheetCard / SheetField /
    SheetActionBar / DayPicker), `ScreenTimeCard.swift`, `AtRiskCard.swift`,
    `YearHeatmapCard.swift`, `ConfettiView.swift`
  - `Features/` — Home (Kid + Parent), Today, Earnings, Goals, Awards, Approvals,
    Planner (+ grid + chore editor), Calendar, Ledger, Rewards, Driving, Settings
  - `App/` — `RootView` (server setup → login → main), `MainView` (role-based tabs /
    sidebar), `DailyBreadApp`
  - Voice: `Core/AgeTier.swift` (`KidVoice`), `Auth/SessionStore.swift`
    (`isSingleChild`, `onlyChild`, `voice`)
  - `Assets.xcassets/AppIcon.appiconset/` — the app icon (copied into `assets/`)
- **`Daily_Bread/`** — the **older Blazor Server web app** this replaces (C#/Razor,
  EF Core, SignalR, Bootstrap). Useful only as history: the SwiftUI app's comments
  repeatedly say "ported from the web dashboard" / "same rules as the web dashboard".
  Its own CSS was a dark graphite "Nord" look — **explicitly abandoned**. Do not take
  visual cues from it. (One vestige survives: the SwiftUI background modifier is still
  named `GraphiteBackground`, with a comment noting the name is kept for call-site
  stability. It paints a warm gradient.)
- **No Figma file, no slide deck, no brand guidelines, no screenshots** were provided.

---

# CONTENT FUNDAMENTALS

## Voice

**Second person, warm, and specific.** The app talks *to* you. `"You always keeps"` —
sorry, `"Always keeps 1h 30m"` · `"Nothing at risk today ✌️"` · `"Every quest done
today!"` · `"Set a savings goal!"`. First-person plural appears only in apology:
`"Couldn't find a Daily Bread server there."`

**Warm-plain, never cute-plain, never corporate.** Sentences are short and concrete.
Numbers are exact (`$12.50`, `−25 min`, `4 of 7 this week`, `70% there`). No
onboarding-speak, no "Oops!", no "Let's get started".

**Stakes are stated kindly, and in this order.** The screen-time meter is the house
style, and the order is the design:

> **Weekdays** · Mon–Fri
> **2h 30m** in your pool
> 🔒 Always keeps **1h 30m**
> Up to **60 min** on the line
> *What each chore is worth* — Take out trash · Miss once: −15 min

What you have → what's **guaranteed** → what's at risk → exactly what one miss costs.
The guarantee comes before the threat, every time, and it wears a lock, not a warning
triangle. `AtRiskCard` is even stricter: it *"only renders true states, never re-sorts,
and never nags"*, and when there's nothing on the line it says one quiet line —
`"Nothing at risk today ✌️"` — and stops.

**The family's own vocabulary.** Not "Submit" / "Approve task":

| In the app | Means |
|---|---|
| **Blessing** | the gold moment when a parent approves |
| **Raise Help** | the kid's own act — a first-class action, not an error path |
| **Fulfill for them** | *"You did it — Ada receives full credit"* |
| **Grant dispensation** | *"Excused for today. No penalty, no earning"* |
| **They must try again** | *"Back to pending — Ada does it themselves"* |
| **Task** vs **Routine** | *"Tasks earn money; Routines are just expected"* |
| **Earns** / **Expected** | the same distinction, as the editor's segmented control |
| **quest** | a chore, on the kid's Home only |
| **Perfect day** | a day with every chore done |
| **Time Machine** | a retroactive screen-time correction |
| **Mercy on the record** | the comment describing the screen-time ledger |

**Help is red because red is the alert semantic — not because asking for help is
failure.** `"Help raised — protected"` is the status line on a kid's chore row. The
word is *protected*.

**Age tiers change words, not layout.** `KidVoice` has exactly two properties:
`parents` (`"your parents"` for a teen, `"a grown-up"` for younger) and `parent`
(`"a parent"` / `"a grown-up"`). They're written to read naturally after specific
phrases — *"waiting on \_\_\_"* and *"until \_\_\_ responds"*. A teen is never spoken
to like a little kid.

**Single-child language is enforced in copy too.** Settings says *"Show goals to Ada"*
instead of *"Show savings goals"* when there's one child. The approvals row hides
whose chore it is. The section header switches between `"Balance"` and `"Balances"`.

## Casing

- **Sentence case** for everything readable: `"Waiting for approval"`, `"Help raised"`,
  `"Still out there"`, `"Grant dispensation"`, `"Approve all (5) — $12.50"`.
- **UPPERCASE + kerning** for section headers and eyebrows, always
  `caption`/`caption2` + `bold` + `.secondary`: `SCREEN TIME THIS WEEK`, `AT RISK TODAY`,
  `TODAY'S PROGRESS`, `BALANCE`, `LAST 14 DAYS`, `BADGES`, `NEXT UP`, `LATEST`, `GOAL`,
  `HELP RAISED`. Kerning is `0.8` for section headers, `1.0` for eyebrows, `1.6` for the
  level tier word (`LEGENDARY`).
- Tab and screen titles are single words: `Home`, `Today`, `Earnings`, `Awards`,
  `Planner`, `Approvals`, `Settings`.

## Punctuation & numbers

Money is `$` + two decimals via `Money.display`; ledger amounts use `signedDisplay`.
Durations are `2h 30m` / `45m`. Deductions use a real minus: `−25 min`. Counts read
`5/7` in a ring, `"4 of 7 this week"` in prose, `"up to 3× a week"` in the editor.
A zero-earning day in the week strip renders `—`, never `$0.00`. `·` separates
metadata (`"Ada · Friday, July 24 · raised 2h ago"`). Kid quotes are curly and
italic: `"“I need a hand because…”"`.

## Emoji — load-bearing, and rationed

Emoji are **identity**, never decoration:
- **Every chore has one**, chosen by the parent in a 2-character text field. Default
  fallback is `🧺`; the planner grid falls back to `💰` for a Task and `✅` for a Routine.
- **Level tiers**: `🌱 ⭐ ⚡ 🔥 💎 👑` — these are the level *icons*, paired with fixed
  tier colours.
- **Milestones** on the XP bar at 25/50/75/100%: `⚡ 🔥 💎 👑`.
- **Money and streak**: `💰` `🔥` `✨` in the kid's stat row. `📺` marks screen-time
  weight in the planner.
- **Sentence-final sparkle**, sparingly and always at the end:
  `"Day complete — every chore done ✨"` · `"All done ✨"` · `"Perfect day ✨"` ·
  `"3 perfect days this year ✨"` · `"Nothing at risk today ✌️"` ·
  `"😎 Rest day — no quests"` · `"No chores are priced this week — nothing at risk. 😎"` ·
  `"🎉"` for a cleared day.
- **`🍞`** is the brand, on the login and server-setup screens at 56pt.

**Do not** put emoji in buttons that already have an SF Symbol, in section headers, or
mid-sentence. The check/undo marks that *are* used inline are typographic, not emoji:
`✓ Fulfill for them`, `↺ They must try again`, `✦`, `!`.

## Copy to imitate

| Situation | Copy |
|---|---|
| Nothing at risk | Nothing at risk today ✌️ |
| Nothing priced | No chores are priced this week — nothing at risk. 😎 |
| Day cleared (Today) | Day complete — every chore done ✨ |
| Day cleared (Kid Home) | 🎉 Every quest done today! |
| Rest day | 😎 Rest day — no quests |
| Queue empty | **All caught up** / Nothing needs your approval right now. |
| No badges | No badges yet — go earn one! |
| No goal | **Set a savings goal!** / Something to work toward → |
| No chores in planner | **No chores yet** / Add the first one. Tasks earn money; Routines are just expected. |
| Nothing earned recently | Nothing earned yet in the last two weeks. |
| Locked badges section | Still out there |
| Batch approve | Approve all (5) — $12.50 · then *Approve 5 chores?* |
| Delete a chore | Delete “Dishes”? This can't be undone. → **Keep** / **Delete** |
| Reorder disabled | Show Everyone to reorder. |
| Raise Help | Raising Help on **Dishes** protects it from tonight's penalty until a grown-up responds. |
| Theme footer | Pick the look you like. It changes everywhere, on every device — switch whenever you feel like a change. |
| Empty family | **Quiet around here** / No family activity yet… |
| Network error | *(inline, footnote, `.secondary`, with `wifi.exclamationmark`)* |

Note what's absent: no modal alerts, no "Are you sure?" system dialogs, no toasts.
Errors are **inline footnotes**. Confirmations are **rows that morph in place**.

---

# VISUAL FOUNDATIONS

## The one-sentence version

Warm gradient paper with white cards floating on close, soft shadows; one saturated
accent per theme; gold money and red Help that never move; SF Pro and SF Symbols
throughout — Apple's material language with a devotional warmth layer on top.

## Colour

**A theme is a whole look, not an accent.** Six of them, each owning four things:

| Theme | Mood | Background | Card | Accent | Secondary |
|---|---|---|---|---|---|
| **Sunroom** *(default)* | warm · light | `#FFFDF9 → #FBF1E2` | `#FFFFFF` | `#C7284F` raspberry | `#2E8C86` teal |
| **Sky** | calm · light | `#FBFCFF → #EAF1FE` | `#FFFFFF` | `#3D7BE0` | `#4BA39C` sea-teal |
| **Rosewater** | soft · light | `#FFF9FB → #FBEAF1` | `#FFFFFF` | `#D24E86` | `#6BA3C6` soft blue |
| **Meadow** | fresh · light | `#F8FBF6 → #E9F4E7` | `#FFFFFF` | `#3E9E6B` | `#8AA83E` leaf |
| **Mulberry** | cozy · dark | `#3E1B30 → #2A1220` | `#4A2237` | `#EA6E92` | `#2E8C86` |
| **Harbor** | quiet · dark | `#223049 → #161E2C` | `#2A3852` | `#5B9BE0` | `#4BA39C` |

There is **no separate light/dark toggle** — each theme forces its own appearance, and
`isDark` is a property of the theme. The light themes' backgrounds are all *warm white
into a tinted cream*; the dark themes are *plum* and *deep evening*, not black. **There
is not one grey in this system.**

**Semantic invariants**, mode-scoped and theme-independent:

| Token | Light | Dark | Means |
|---|---|---|---|
| `--ds-gold` | `#C98A1E` | `#E7B44A` | **money.** Balances, earnings, values, the Approve button, gold heatmap cells, chart bars |
| `--ds-glow` | `#E0A21E` | `#F0C868` | the **Blessing** — an 18% wash on the row that was just approved |
| `--ds-help` | `#D1363B` | `#F06B6B` | **Help / alert.** Also the destructive tint |
| `--ds-success` | `#2E9E63` | `#86C08F` | done, perfect day, earn-back, cash-out-ready |

**Label and fill steps come from SwiftUI, not from a custom ramp.** `.primary` /
`.secondary` / `.tertiary` are the label colour at 100% / 60% / 30%; `.quaternary` is the
fill used for icon tiles, inline field backgrounds, week-strip cells, unselected day
pills and progress tracks. Everything is expressed as an opacity of the label colour, so
it inverts correctly on the dark themes for free. **This is why the system is
theme-safe: there are almost no absolute colours below the theme layer.**

**Opacity percentages are conventions.** `0.05` card stroke (light) · `0.07` card
stroke (dark) and card shadow (light) · `0.08` hairline divider and light-theme heatmap
"nothing due" · `0.12`–`0.14` unselected fills · `0.13` chip backgrounds ·
`0.16` the Help button's tint · `0.18` the Blessing wash · `0.25` dark card shadow ·
`0.28` the kid hero's tier border · `0.35` earned-badge gold border ·
`0.45` "missed" heatmap red · `0.5` the level-badge glow. Never introduce a raw hex
below the theme layer.

**The one deliberate multi-hue gradient** is the progress glow, and its comment
explains itself: *"kept within ONE warm family so it never muddies (the fix to the
two-hue bar): the accent deepening into gold-warm"* — `accent → #E7A83C`.

## Type

**SF Pro, the system font, at Apple's text styles.** No custom face, no display font,
no serif. Weights: regular / medium / semibold / bold / heavy.

The scale in use, top to bottom: `largeTitle.bold` (the iOS "Home" header) ·
`title.heavy` (the level number) · `title2.bold` (greetings, sheet titles) ·
`title3.bold` (pool minutes, stat tile values) · `headline` (sheet headers) ·
`body` (chore names, at `.medium` or `.semibold`) · `subheadline` (most secondary text,
often `.semibold`) · `footnote` (inline errors) · `caption` (metadata, section headers
at `.bold`) · `caption2` (the smallest labels).

**Two deliberate exceptions:**
- The **balance hero** is `.system(size: 42, weight: .bold, design: .rounded)` — SF Pro
  **Rounded**, the only place a different face appears, and it carries
  `.contentTransition(.numericText())` so the number animates when it changes.
- **`.monospacedDigit()`** on anything that ticks: the XP counter, the weekly-goal
  stepper, batch-approval progress.

Section headers are the one uppercase treatment: `caption.weight(.bold)` +
`.foregroundStyle(.secondary)` + `.kerning(0.8)`.

**Dynamic Type is respected everywhere** — the app never hardcodes a point size except
the two above and the emoji glyph sizes. Long values get
`.lineLimit(1).minimumScaleFactor(0.6)` rather than a smaller font.

## Space

SwiftUI stack spacings, and they are **not** a strict 4pt grid — `14` and `18` appear as
often as `12` and `16`. Copy the value; don't snap it.

`glassCard()` pads **14** by default, **12** for compact tiles, **16** for the parent
greeting, **18** for the balance hero. Screens pad **16**. Stacked cards sit **14**
apart on the kid's Home, **16** on the parent's. Chip padding is **9 × 5**. Pool tiles
pad **10**.

## Radius

Everything is `RoundedRectangle(style: .continuous)` — a **squircle**, not a circular
arc. That's an Apple-platform shape; CSS can only approximate it, so use a slightly
generous plain radius and don't try to fake a superellipse.

**20** the glass card (and the earned-badge overlay ring) · **24** the kid hero, the one
larger card · **12** theme swatch, pool tile, action-bar buttons, Next-Up icon tile ·
**10** inline field backgrounds and the 40px chore icon tile · **7** a planner grid cell ·
**2.5** one day in the year heatmap · **Capsule** chips, the level badge, the XP bar,
day pills, the Help/Done buttons, the cash-out-ready tag.

## Elevation

**One shadow, close and soft.** `.shadow(color:, radius: 10, y: 3)` — black at **7%**
on light themes, **25%** on dark. Plus a **0.5px** stroke border: black at 5% (light) /
white at 7% (dark). That's it. Cards do not lift on hover — this is a touch-first app.

The **only** other elevation is the kid's level badge:
`.shadow(color: tier.opacity(0.5), radius: 16, y: 5)` — it glows in its own tier colour.

**The Blessing is a wash, not a shadow.** When a chore is approved, the row's background
becomes `glow.opacity(0.18)` for 0.9 seconds with `.easeOut(duration: 0.4)`, then the
row animates out of the queue.

## Borders & dividers

Hairline `0.5px` stroke on every card. `Divider()` between rows inside a card.
`Rectangle().fill(Color.primary.opacity(0.08)).frame(height: 1)` for the kid hero's
internal rule. The kid hero adds a `1px` border in the tier colour at **28%**. An earned
badge adds a `1px` gold ring at **35%**. Nothing is ever thicker.

## Material & blur

Native materials, not hand-rolled glass:
- `.regularMaterial` — the kid hero's base fill (with the tier gradient layered over it)
  and the planner grid's pinned header.
- Everything else is **opaque theme card colour**. The modifier is *named* `GlassCard`
  but the surface is solid; the "glass" is the soft stroke + close shadow, not blur.

Do not add `backdrop-filter` anywhere the source doesn't. There is far less blur in this
app than the name suggests.

## Backgrounds

`GraphiteBackground` (legacy name) sets `.scrollContentBackground(.hidden)` and paints
the theme's gradient `.ignoresSafeArea()`. So: **the gradient is the whole screen,
edge to edge, behind everything, always.** Light themes run `top → bottom`; dark themes
stop at `y: 0.6` so the lower half settles into the deeper tone.

The kid hero is the one place two fills stack: `.regularMaterial`, then a
`tier.opacity(0.20) → tier.opacity(0.02)` top-to-bottom gradient, then a 28% tier
border.

**No photography, no illustration, no pattern, no texture, no grain.** The warmth
budget is spent entirely on colour, gradient, radius and emoji.

## Motion

SwiftUI's own curves, and the choice of curve is meaningful:

- **`.snappy`** is the workhorse. Every optimistic mutation uses it — toggling a chore,
  reordering the planner, deactivating, deleting, filter changes, morphing a row into
  its confirm state. The UI answers the finger *before* the network does, and rolls back
  with the same curve if the server disagrees.
- **`.spring(duration: 0.4)`** — the level badge and the milestone pops.
- **`.spring(duration: 0.5)`** — the XP bar filling.
- **`.easeOut(duration: 0.4)`** — the Blessing gold wash.
- **`.easeInOut(duration: 0.2)`** — theme switch, planner grid/list toggle.
- **`.contentTransition(.numericText())`** on the balance, `.symbolEffect(.replace)` on
  the chore checkmark.

**Timed beats** the app actually waits: the `+$2.50` earn pop lives **1.4s**; an
approved row glows **0.9s** before leaving; confetti runs **2.8s**.

**Haptics are part of the motion system**, not an afterthought — `Haptics.tick()` on
every toggle and tap, `.success()` on approve / save / a cleared day, `.warning()` on
every rollback. They no-op on macOS so call sites stay clean.

## Interaction states

This is a **touch-first** app, so hover is barely present:
- **Tap** — haptic tick, optimistic state change with `.snappy`.
- **Swipe** — the Today row's leading edge is `Done`/`Undo` (accent, full-swipe
  allowed); trailing is `Help` (help red). The planner row's leading edge is
  `Deactivate`/`Activate` (secondary); trailing is `Delete` (help red, **never**
  full-swipe — it routes through an inline confirm).
- **Selected** — accent-filled: day pills, planner grid cells, filter chips all go
  solid accent with white content. Unselected is `secondary.opacity(0.12–0.14)`.
- **Disabled** — the primary sheet button's background becomes
  `secondary.opacity(0.3)`; buttons disable rather than hide.
- **Destructive** — `role: .destructive` on "Change server" and "Sign Out";
  `tint(DB.help)` on Delete. Red is the only destructive signal, and it's the same red
  as Help.
- **No hover states, no press-scale, no focus rings** are defined. Don't invent them.

## Layout rules

- **iPhone: `TabView`** with a badge on Approvals (`pendingApprovals + helpRequests`).
  **macOS: `NavigationSplitView`** with the same sections in a sidebar, titled
  "Daily Bread". One `Section` enum drives both.
- **The parent's iOS Home hides the navigation bar** and draws its own header row —
  `"Home"` in `largeTitle.bold` with the calendar and gift icons baseline-aligned
  beside it (`alignmentGuide(.firstTextBaseline) { $0[.bottom] - 6 }`).
- **Screens are `List` or `ScrollView`.** Cards inside a `List` get
  `.listRowBackground(Color.clear)` and explicit `listRowInsets` of
  `(8, 16, 8, 16)` so the gradient shows through.
- **Responsive to family size, not to screen size:** 1–2 kids get roomy rows, 3+ get a
  two-column `LazyVGrid`. The heatmap child picker only appears with more than one child.
- **`.refreshable`** and **`.refreshOnForeground`** on every data screen.
- Sheets use `.presentationDetents` — `.medium` for Help, `.large` for the chore
  editor, `.height(260)` for a day's detail. macOS sheets get explicit
  `minWidth: 460, idealWidth: 500`.

---

# ICONOGRAPHY

Two systems, each with a defined job — plus one typographic layer.

### 1. SF Symbols — every piece of UI

Monochrome, weight-matched to the text they sit beside, inheriting `currentColor` or a
semantic tint. **No icon assets exist in the project because none are needed.**
The full set in use:

**Tabs** `house` · `sun.max` · `dollarsign.circle` · `trophy` · `checklist` ·
`checkmark.circle` · `gearshape`
**State** `checkmark.circle.fill` / `circle` (the chore toggle, with
`.symbolEffect(.replace)`) · `checkmark` · `checkmark.seal` · `checkmark.seal.fill` ·
`hand.thumbsup` · `questionmark.circle` · `exclamationmark.circle` /
`exclamationmark.circle.fill` · `flame.fill` · `clock.badge.exclamationmark` ·
`lock.fill` · `minus.circle` · `arrow.uturn.up` · `clock.arrow.circlepath`
**Navigation** `chevron.right` / `.up` / `.down` · `chevron.up.chevron.down` ·
`plus` · `ellipsis.circle` · `list.bullet` · `square.grid.2x2` ·
`slider.horizontal.3` · `party.popper`
**Objects** `calendar` · `gift` · `target` · `car` · `person.2` · `wifi.exclamationmark`

Urgency mapping in `AtRiskCard` is worth copying verbatim:
`DueTonight → exclamationmark.circle.fill` (help red) ·
`MustDoDaily → flame.fill` (help red) ·
`GettingTight → clock.badge.exclamationmark` (gold).

> **Recreating this on the web:** SF Symbols is not licensed for web use. Use a CDN
> line-icon set with matching weight and rounded caps — **Lucide** or
> **Bootstrap Icons** — and flag the substitution. The UI kits in this project use
> Lucide from CDN for exactly this reason. Never hand-draw an SVG.

### 2. Emoji — identity

Chore icons (parent-chosen, 2 characters max, default `🧺`), level tier icons
(`🌱 ⭐ ⚡ 🔥 💎 👑`), XP milestones, the money/streak/sparkle marks, `📺` for
screen-time weight, `🍞` for the brand. See CONTENT FUNDAMENTALS for the rules.

### 3. Typographic marks — inline meaning

`✦` awaiting approval · `✓` done / fulfil · `!` needs help · `↺` try again ·
`—` an empty value · `·` separator · `×` as in `3×/wk` · `→` a soft call to action.

### Brand mark

**`assets/app-icon-1024.png` / `-512` / `-128` — the real app icon, and it is not a
loaf of bread.** It's the **year heatmap**: a 4×4 grid of squircles on a dark
teal-to-black ground, fifteen of them in shades of teal and **one in gold**. The app's
own comment calls the heatmap *"the app's emotional high point"*, and gold is money —
so the icon is one good day. It's the best asset in the project; use it as the mark.

There is **no wordmark and no vector logo.** Where a mark is needed, use the app icon,
or set `🍞 Daily Bread` above *give us this day* in italic secondary, the way the login
screen does. **Do not draw or reconstruct a logo** — none exists to reconstruct.

---

# INDEX

## Root
- `styles.css` — the single entry point; `@import`s every token file. Link this.
- `readme.md` — this document.
- `SKILL.md` — Agent-Skills front matter for use outside this project.
- `thumbnail.html` — the homepage tile.

## `tokens/`
`themes.css` (**the six themes** + semantic invariants + level/rarity hues) ·
`semantic.css` (`--ds-*` aliases — **consume these**) · `typography.css` ·
`spacing.css` · `radius.css` · `shadows.css` · `motion.css` · `layout.css` ·
`fonts.css` (a note: there are no font files, by design)

## `guidelines/` — foundation specimen cards
Themes (all six as real swatches, light set, dark set, anatomy), Colour (semantic
invariants, label steps, fill steps, opacity conventions, level tiers, rarity),
Type (scale, section headers, the rounded hero, monospaced digits), Spacing (stack
scale, card paddings), Radius, Elevation (card + level glow + Blessing wash), Motion,
Brand (app icon, login lockup), Iconography (SF Symbols set, emoji layer).

## `components/` — reusable primitives (React)
- `core/` — **GlassCard**, **SectionHeader**, **Chip**, **StatTile**, **ProgressRing**,
  **ProgressBar**, **Button**, **EmptyState**, **TabBar**, **InlineError**
- `sheets/` — **SheetHeader**, **SheetCard**, **SheetField**, **SheetActionBar**,
  **TextField**, **SegmentedPicker**, **ToggleRow**, **Slider**, **Stepper**,
  **DayPicker**
- `family/` — **ChoreRow**, **AtRiskCard**, **ScreenTimeCard**, **YearHeatmapCard**,
  **LevelBadge**, **XPBar**, **AchievementCard**, **ApprovalRow**, **HelpRow**,
  **PlannerChoreRow**, **PlannerGrid**, **WeekStrip**, **BalanceHero**, **GoalCard**,
  **TransactionRow**, **ChildRow**, **ThemeSwatch**, **ExpandRow**, **ConfirmRow**

Each directory has one `@dsCard` HTML showing states and variants. Each component has a
`.d.ts` props contract and a `.prompt.md` usage note.

- `family/` (admin, added after the running app was seen) — **FamilyMemberRow**,
  **DrivingTotals** + **DrivingLogRow**, **CalendarMonth**, **AchievementDefRow**,
  **ChildPicker**

### Intentional additions
- **`SectionHeader`**, **`InlineError`**, **`ExpandRow`**, **`ConfirmRow`** — these are
  private helper funcs repeated across several SwiftUI views (`sectionHeader(_:)`,
  the `wifi.exclamationmark` label, `expandRow(...)`, `DeleteConfirmRow`) rather than
  shared types. They're the app's most-repeated patterns, so they're extracted here.
- **`TabBar`** — `MainView`'s `TabView` config, made renderable on the web.
- **`Button`** — a thin wrapper over the four button treatments the app uses
  (`.borderedProminent` tinted accent / tinted gold / `.bordered` / plain-capsule).

### Built after seeing the running app
The screenshots confirmed five surfaces were real screens, not incidental logic, so they
were promoted from placeholders to components: `FamilyMembersView` → **FamilyMemberRow**
(+ its reset-password / lock-account menu), `DrivingLogView` → **DrivingTotals** +
**DrivingLogRow**, `CalendarView` → **CalendarMonth**, `AchievementDefinitionsView` →
**AchievementDefRow**, and the parent Home's heatmap scope control → **ChildPicker**.

`ChildPicker` is the one place the single-child rule is enforced *in code rather than by
convention*: it returns `null` for one name or none, so callers can render it
unconditionally and no screen can grow a one-item picker implying absent siblings.

### Source families still deliberately NOT authored as components
Surfaces whose value is entirely in their logic, shown inside the UI kits instead:
`GoalsView`, `RewardClaimsView` (an empty state in practice), `AdjustBalanceSheet`,
`ScreenTimeSettingsSheet`, `ScreenTimeHistorySheet`, `DayDetailSheet`, `ConfettiView`,
`ServerSetupView`, and the New-Achievement editor sheet. See "Known gaps".

## `templates/`
- `kid-home/KidHome.dc.html` — the kid's Home: level badge, XP bar, goal, badges, Next Up
- `parent-home/ParentHome.dc.html` — the parent's Home: greeting, chips, stat tiles,
  week strip, balances, heatmap

Both load the system through a sibling `ds-base.js`; point its `base` at the bound
`_ds/<folder>` tree when copied into a consuming project.

## `ui_kits/`
- `kid_app/` — iPhone: Home, Today, Earnings, Awards, Settings (interactive)
- `parent_app/` — iPhone: Home, Planner, Approvals, Settings, child drill-in, two sheets
- `mac_app/` — macOS `NavigationSplitView`: the same screens in a sidebar shell, with
  both accounts switchable and every detail screen reachable. Imports the two kits above
  **unchanged** — the single-codebase claim, made visible.

## `assets/`
`app-icon-1024.png` · `app-icon-512.png` · `app-icon-128.png`

---

# AUDIT FINDINGS

Measured against the design intent, from the SwiftUI source.

## Token coverage — excellent, and unusually disciplined
Colour, type, radius, shadow, motion and spacing all resolve through `DBTheme` / `DB` /
SwiftUI system values. Because label and fill steps are expressed as **opacities of the
label colour** rather than absolute values, the whole system inverts correctly on the
dark themes without a second palette. This is the single best decision in the codebase.

## Theme-safety — strong, with a short list of real leaks
Almost nothing is hardcoded. What is:

| Where | Value | Verdict |
|---|---|---|
| `KidHomeView.tier(for:)` | six fixed hexes for level colours | **Intentional.** A level's colour is its identity. Should still be *named* tokens, not inline literals. |
| `AchievementsView.rarityColor` | `#9B7BE0` for epic | **Intentional**, same reason — but it's a second, unrelated copy of the same hex as level 4. Extract one. |
| `KidHomeView.streakColor` | `#E8894A` | Duplicate of level-3 orange. Extract. |
| `DesignSystem.progressGradient` | `#E7A83C` gold-warm | Fine — documented as the deliberate one-family fix. |
| `SettingsView.ThemeSwatch` | `#C98A1E` gold dot | **Leak.** Hardcodes the *light* gold, so on Mulberry/Harbor the swatch's coin is the wrong gold. |
| `ThemeSwatch` outer stroke | `Color.black.opacity(0.08)` | **Leak.** Should be `theme.cardStroke`. |
| `LoginView` error text | `.foregroundStyle(.red)` | **Leak.** System red, not `DB.help` — the one place the alert colour escapes the system. |
| `HelpRespondSheet`, approve-all | `Color.black.opacity(0.8)` on gold | Works on both, but should be an `onGold` token. |

None of these break a theme outright. The `ThemeSwatch` gold and the `LoginView` red are
the two worth fixing.

## Consistency of shared components — very good, two seams
- **`glassCard()` is used with genuine consistency** — every card in the app, at four
  padding values. `SheetKit` gives sheets their own consistent chrome, explicitly
  because macOS `Form` renders badly. Cards, rows, empty states, chips and section
  headers all match across both audiences.
- **Seam 1 — the kid hero opts out of `glassCard()`.** It hand-rolls
  `.regularMaterial` + a tier gradient + a 24px radius + a tier border. That's a
  deliberate "this is the game" statement, and it's the only card that does it. Worth a
  named modifier (`heroCard(tier:)`) so it can't drift.
- **Seam 2 — `ProgressRing` lives in `TodayView.swift`** but is used by
  `ParentHomeView`. Likewise `Greeting` lives in `YearHeatmapCard.swift`, and `HelpSheet`
  in `TodayView.swift`. Shared types are scattered across feature files; they belong in
  `DailyBreadKit` beside `DesignSystem.swift`.
- Four separate `private func sectionHeader(_:)` and four separate `statTile(...)` /
  `tile(...)` implementations exist across views, each subtly different in padding.
  One shared pair would remove real drift risk.

## One system, two audiences — yes, structurally
The parent literally renders the kid's screen (`TodayView(userId:)`, with
`allowHelp: isSelf`). Both homes use the same chips, tiles, rings, week strip, heatmap
and card treatment. The only divergence is the kid hero (see Seam 1) and the level/XP
family, which exist on one side only — the right things to have diverged.

## Grows with the child — real but thin
`KidVoice` exists, is wired through `SessionStore.voice`, and is used correctly in the
two places it appears (`HelpSheet`, `KidHomeView`'s Help status line). But it only has
**two** properties, and most kid-facing copy still doesn't route through it — "quests",
"Rest day", "go earn one!" are the same for a 7-year-old and a 15-year-old. The
mechanism is right; the coverage is ~5% of what it implies.

## Single-child sensitivity — the best-implemented invariant in the app
`isSingleChild` / `onlyChild` is a documented single source of truth, and every
plural-implying affordance branches on it: the planner's child filter is hidden, the
chore editor's "Who's it for" card is omitted (and the chore auto-assigned), the
approvals row hides the child's name, the balance section header switches
`Balance`/`Balances`, the heatmap child menu is suppressed, `goalsSubtitle` names the
child, and `balanceUserId` returns the only child directly instead of matching by name.
Every one of those has a comment saying why. **No violations found.**

## Semantic invariants — held, with one gap
Gold is money everywhere: balances, earn values, chart bars, the Approve button and its
glow, `GettingTight` urgency, the "all done ✨" line, partial-day heatmap cells. Help red
is Help and destruction, nothing else — and the source explicitly notes it's kept
distinct from every accent so it can't hide in Rosewater or Mulberry. The gap:
**legendary rarity is also gold**, which is a documented-but-real overload of the money
signal in the trophy case.

## Accessibility
`.accessibilityLabel` appears on icon-only buttons. `.lineLimit` +
`.minimumScaleFactor` guard long values. Dynamic Type is respected. But: several
colour-only status signals (heatmap cell status, urgency tint, active/inactive chore)
have no non-colour redundancy, and there's no `prefers-reduced-motion` equivalent —
`.snappy` and the springs always run.

## Verified against the running app
Screenshots of the running iOS and macOS builds (Harbor and Meadow themes, both accounts)
were reviewed after the system was first authored. What they confirmed and changed:

- **Tokens were already correct.** Harbor's `0x5B9BE0` accent, `0x2A3852` card and
  `0x223049 → 0x161E2C` background match the running app; `AtRiskCard`,
  `ScreenTimeCard`, `LevelBadge` and `XPBar` needed **no** changes — including the
  `⚡🔥💎👑` milestone row, the `"0 / 10 XP"` format, the gold-over-red at-risk
  columns and the `"On the line today: $4.50 + 198 min"` footer.
- **macOS is a real second surface**, not a theoretical one: accent-filled sidebar rows,
  a **plain trailing number** for the Approvals badge (not iOS's red pill), a rounded
  toolbar cluster, and a capped content column. Built as `ui_kits/mac_app/`.
- **Five placeholder screens are real** — promoted to components (above).
- **Gold has a second sanctioned use**: child avatars in Family are gold, parents accent.
  A child is the earning party, so this reads as an extension of the money semantic
  rather than a violation, but it is worth a deliberate decision.
- **Section-header casing is genuinely mixed** and the split is meaningful: uppercase
  tracked for dashboard/card eyebrows (`AT RISK TODAY`, `QUICK VIEW — THIS WEEK`),
  sentence case for Settings-style group headers (`Family features`, `Manage`,
  `Recent`, `Help raised`) — the native `Form` convention. Follow it.

## Known gaps in this design system
- Native `List`, `Form` and `.regularMaterial` rendering are approximated in CSS; the
  kits are close but not pixel-identical to a real build.
- The iPad three-column layout, real window/traffic-light behaviour and menu-bar commands
  are not recreated.
- SF Symbols can't ship to the web; the kits substitute Lucide from CDN.
- SF Pro and SF Pro Rounded aren't redistributable; on non-Apple platforms the specimens
  fall back to the platform UI font.
- The screens listed under "Source families deliberately NOT authored" appear in kits
  only, or are left as honest placeholders.
- `Models.swift`, `APIClient.swift` and the reward/driving features were read only
  enough to name them; their screens are not recreated.
