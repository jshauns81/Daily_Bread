# Handoff: Daily Bread — Phase 0 + Phase 1

**For:** a developer (or Claude Code) working in `DailyBread/` — SwiftUI, iOS/iPadOS/macOS, `DailyBreadKit` local package.
**Scope:** Phase 0 and Phase 1 only. This is the minimum cut that changes the app's character. Do not start Phase 2 or 3.

## About the design files

Everything referenced below lives in this design-system project as **HTML design references** — prototypes showing intended look, colour and behaviour. They are not production code and nothing should be copied out of them verbatim. The task is to **rebuild these designs in SwiftUI** using the app's existing patterns: `DB.*` tokens in `DesignSystem.swift`, `SheetKit` for sheet surfaces, `themeBackground()` for grounds, `Color.accentColor` for accent.

**Fidelity: high.** Colours, opacities, radii, durations and easings below are final values, not suggestions. Match them.

Read `IMPLEMENTATION.md` at the project root first — it carries the reasoning for every decision here, including the four that were argued and settled. This README is the ordered work order; that document is the why.

---

## Order of work

Phase 0 is mechanical and must land first — Phase 1 depends on its tokens. Phase 0 should be a single reviewable commit per task with no behaviour change.

| # | Task | Type | Blocks |
|---|---|---|---|
| 0.1 | Fill tokens | refactor | 1.1, 1.2 |
| 0.2 | Rarity + night invariants | refactor | — |
| 0.3 | Accent rule + `graphiteBackground` rename | refactor | — |
| 0.4 | `DBIcon` wrapper | new | — |
| 1.1 | `ChoreCheck` four states | new | — |
| 1.2 | Rainbow year (Card / Full / Wall) | replace | 1.3 |
| 1.3 | Celebration ladder | replace | — |
| 1.4 | `List` → `ScrollView` where no swipe actions | refactor | — |

---

# Phase 0

## 0.1 — Fill tokens

Seven different opacities currently mean "off". Call sites:

```
CalendarView.swift:142            PlannerGridView.swift:126
PlannerView.swift:480             SheetKit.swift:164
ScreenTimeSettingsSheet.swift:115 ApprovalsView.swift:419
```

Add to `DesignSystem.swift`:

```swift
public extension DB {
    static func fillSubtle(_ s: ColorScheme) -> Color   // 0.06 — large surfaces
    static func fillOff(_ s: ColorScheme) -> Color      // 0.12 — control backgrounds, unselected
    static func fillStrong(_ s: ColorScheme) -> Color   // 0.32 — pressed / prominent
}
```

Base each on `Color.secondary` at the stated opacity, resolved per `ColorScheme`. Then replace every call site with the nearest token — `.06`→subtle, `.12/.13/.14/.16`→off, `.30/.35`→strong.

**Acceptance:** `rg '\.opacity\(' --glob '!DesignSystem.swift'` returns zero hits on `Color.secondary`. No visual diff beyond the four collapsed mid-range values.

Reference: `guidelines/color-fills.html`

## 0.2 — Rarity and night as invariants

`AchievementsView.swift:180` returns `.accentColor` for `"rare"` — so rarity means something different in every theme, and in Sky it is indistinguishable from ordinary accent chrome. `DrivingLogView.swift:147` hardcodes `Color.indigo` for night driving, the only unowned hue in the app.

```swift
public enum DBRarity: String, Codable {
    case common, rare, epic, legendary
    func color(_ s: ColorScheme) -> Color
}
// common #8A8F98 · rare #3B82D6 · epic #7A5AF8 · legendary #E7B44A

public extension DB {
    static func night(_ s: ColorScheme) -> Color   // replaces Color.indigo
}
```

Invariants are **not** themeable and not YAML-overridable (Phase 3 adds an explicit `invariants.unlock` escape hatch; don't build it now).

✅ **`night` decided: `#5560A8` light / `#8D97D8` dark** ("dusk slate"). Candidates were compared in context in `guidelines/color-night.html`. Night is a *fact about a drive*, not a reward or a brand colour, so it is the quietest hue in the app — deliberately duller than every theme accent and every rarity, which is what guarantees it can never be mistaken for one. Live as `--ds-night`.

✅ **Rarity hexes were also wrong in the tokens** and are now corrected: `tokens/themes.css` had `--db-rarity-epic:#9B7BE0` and `--db-rarity-rare: var(--db-accent)` — the second being R6's exact bug living in the design tokens as well as in the Swift. Both are fixed hex now, matching the values above.

The full invariant set, for reference — all six are already decided:

| Meaning | Deep | Light | Used for |
|---|---|---|---|
| Money | `#C98A1E` | `#E7B44A` | balances, earn values, Approve, chart bars |
| Blessing | `#E0A21E` | `#F0C868` | the 18% wash on a just-approved row |
| Help / alert | `#D1363B` | `#F06B6B` | Help, destructive, at-risk minutes |
| Done | `#2E9E63` | `#86C08F` | perfect day, earn-back, cash-out ready |

Reference: `guidelines/color-invariants.html`

## 0.3 — Accent rule and the rename

`DailyBreadApp.swift:34` already does `content.tint(theme.accent(scheme))`. This is correct and load-bearing — it's why stock controls are at least coloured right. Add a doc comment at that line so nobody "fixes" it:

> Use bare `Color.accentColor` for accent. Reach for `DB.*` only for invariants: money, blessing, help, done, rarity, night.

Then rename `graphiteBackground()` → `themeBackground()`. ~30 call sites, purely mechanical — the in-source comment already admits the name is a fossil.

**Acceptance:** no `graphiteBackground` remains; build clean on all three platforms.

## 0.4 — `DBIcon`

**Icon set decision: SF Symbols stays.** Phosphor, Iconoir, Remix and Lucide were evaluated and rejected — `.symbolEffect`, Dynamic Type coupling and optical alignment inside `Label` all come free, and iOS draws nav-bar backs, share sheets and swipe defaults with SF Symbols regardless, so it's the only choice with no seam.

Wrap it anyway. One view every icon goes through:

```swift
struct DBIcon: View {
    let name: String            // SF Symbol name
    var weight: Font.Weight = .medium
    var tint: Color? = nil      // nil = inherit
    var size: DBIconSize = .body
}
```

Two reasons, neither of them "enable a swap": per-call-site sizing has already drifted, and Phase 3's `icons.set` YAML key needs somewhere to land. Cheap now, expensive to retrofit.

**Note for whoever reads the HTML references:** SF Symbols is not licensed for web, so the design-system cards substitute Lucide from CDN. That substitution is cosmetic and has no bearing on the app.

---

# Phase 1

## 1.1 — `ChoreCheck`, four states

`TodayView.swift:354`. Currently a stock `circle` / `checkmark.circle.fill` in grey→accent. Grey reads as *disabled* rather than *empty and waiting*, and there is no per-chore feedback at all.

| State | Ring | Fill | Glyph |
|---|---|---|---|
| `unchecked` | 2.5px `label.opacity(0.22)` | none | none |
| `done` | accent | accent | white checkmark |
| `awaitingApproval` | 2.5px money, **dashed** | money @ 12% | small money-tinted check |
| `helpRaised` | 2.5px help | help @ 12% | `questionmark` |

- Visual 29pt, hit target 44×44 via `contentShape`.
- Transition `.spring(response: 0.28, dampingFraction: 0.55)`. Halo blooms to 1.35× and fades over ~320ms.
- `.symbolEffect(.bounce)` on the checkmark.
- Haptic `.impact(.rigid)` on check **only** — undo is silent. Don't reward undo.
- **Earning chores:** flash the ring money-gold for ~180ms before settling to accent. The one moment where money and action are the same event.
- `awaitingApproval` and `helpRaised` are terminal from the kid's side — tapping shows the reason, does not toggle.

~~**This absorbs the HELP capsule.**~~ **AMENDED 2026-07-30 by Shaun — Help stays visible.**
Help/forgive is a core mechanic of the app, and a safety valve only works if the kid can see
it before he needs it. The row KEEPS a distinct, always-visible Help affordance. The check's
`helpRaised` state still renders as specced above (it shows the *result*), but raising Help
must never be hidden behind the check control. Do not relitigate.

**Acceptance:** all four states reachable in a preview; VoiceOver labels distinguish "not done", "done", "waiting for approval", "help raised".

## 1.2 — The rainbow year

Replaces `Components/YearHeatmapCard.swift` entirely. **Live reference: `guidelines/rainbow-year/index.html` — build against it, it's the spec.**

```
hue        = (dayOfYear / 365) * 360
intensity  = requiredDone / requiredTotal          // linear — the `forgiving` curve, decided
saturation = 34% → 86%   mapped from intensity
lightness  = ramps opposite by ColorScheme
bloom      = cell × 0.55 halo, hue-matched, on perfect days only
```

Non-negotiables:

1. **Hue is day-of-year, never position-within-window.** Otherwise every widget renders a full spectrum regardless of range and the year stops being legible.
2. **Day 365 lands at 359°, day 1 at 0°** — the year boundary is seamless. Multi-year scroll is one unbroken wheel.
3. **A bad day is a drained colour, never a grey hole.** The rainbow survives regardless of performance.
4. **Optionals add bloom, never withhold colour.** `isComplete == (requiredDone == requiredTotal)`, independent of optionals; optionals only raise `bloomLevel` (0…n). Make this explicit in the model so it can't drift. *Rationale: the alternative punishes a kid for declining an optional he was never obliged to do — a permanently dimmer square for exercising a choice you deliberately gave him.*
5. **The ramp is linear, from a 34% saturation floor.** Three curves were tested against the real widget set; steeper ones read as punishment on a wall of 365. A half-done day should still be a colour you'd want to look at — the lightness ramp already makes a shortfall legible.
6. **Perfect days bloom** with a hue-matched halo at `cell × 0.55`. **Scale the bloom to the cell** — a fixed radius smears into neighbouring days at year-wall density. On a wall of 365 this reads as scattered embers, and a streak becomes a visible burning line.

Cell payload: `{ date, requiredDone, requiredTotal, optionalDone, optionalTotal, isFuture }`.

One renderer, one component, a `size` enum only:

| Size | Range | Where |
|---|---|---|
| `.card` | last 12 weeks, cell ~18pt, no h-scroll, fits iPhone width | **Today — the default the kid sees daily** |
| `.full` | scrollable year, tap a day to open it | reached by tapping the card |
| `.wall` | whole year + multi-year scroll | iPad / macOS |

**Multi-year continuity is a hard requirement of `.full` and `.wall`**, not a nice-to-have. Load a year at a time, keep hue continuous across the boundary, **no gap or divider between year columns.** The invisible seam is the whole idea.

### Where it lives outside the app

- **macOS dock — do this, it's fully live.** `NSApp.applicationIconImage = renderedImage` (or `NSApp.dockTile.contentView` + `display()`) replaces the icon at runtime. Render the last 12 weeks each morning and on every completion.
- **iOS home screen — not the icon.** iOS cannot render an icon at runtime; `setAlternateIconName(_:)` only switches build-time variants and raises a system alert every time. Use **WidgetKit**: Small (4 weeks + today's ring), Medium (Year = 12 weeks matching the Today card, or Today = inline-checkable chores via App Intents), Large (full year). Reload via `WidgetCenter.shared.reloadTimelines(ofKind:)` each morning and on completion.
- **Lock Screen accessories are monochrome** — iOS tints them, the rainbow cannot appear. Circular ring, rectangular count + pip bar, inline count. **StandBy is not an accessory family** and does get full colour: 26 weeks.

Widget reference: `ui_kits/widgets/` (built, interactive, theme-aware).

**Consequence for the app icon:** this settles it. The icon stays a **stable identity mark** — it must say "Daily Bread" from six feet away, and a mark that changes daily can never build recognition. The living rainbow goes in the widget and the macOS dock. You get both, each where it works.

## 1.3 — Celebration ladder

Today: one `ConfettiView` gated on `store.allDone`, ~40 flat rectangles. A kid who does five of six chores gets the same feedback as one who does none.

1. **Per-chore** — the 1.1 bloom + haptic. Every completion.
2. **Threshold** — last *earning* chore lands, or a streak extends. A money-gold coin arcs from the check into the header balance, which pulses. ~600ms, no full-screen takeover.
3. **Perfect day** — replace the flakes. `SpriteKit` or `TimelineView`+`Canvas` particle system: gravity and drag, varied geometry (coins, ribbons, stars), depth via scale+blur layers, theme-tinted plus money-gold, ~1200 particles, 2.5s. Brief radial bloom behind them, rising chime.

Tier 3 now requires a genuinely perfect day, so it fires *less* often than today's — which is what makes it worth something. All three respect the existing `enableConfetti` setting, and all three respect Reduce Motion.

## 1.4 — List cleanup

`TodayView.swift:165–210` neutralises `List` row by row (`.listRowBackground(.clear)` + custom insets) so authored cards can sit inside it.

- **No swipe actions → `ScrollView` + `LazyVStack`:** Earnings, Awards, the heatmap and screen-time stacks.
- **Has swipe actions → keep `List`.** Today's swipe-to-Done and swipe-to-Help are real, native and worth keeping. Approvals and Planner likewise.

This is the standing principle in action: *if a stock control is in the right place and laid out well, leave it.*

---

## Design tokens

Live values are in `tokens/` as CSS custom properties — `themes.css` (6 themes × light/dark), `spacing.css`, `radius.css`, `motion.css`, `typography.css`, `shadows.css`. `figma/daily-bread.tokens.json` is the same set in Tokens Studio format.

The app's own `DesignSystem.swift` remains the source of truth for Swift. Where the two disagree, **Swift wins** — Harbor's surfaces were already reverted to the Swift values (`#223049 → #161E2C` background, `#2A3852` card) for exactly this reason.

## Reference implementations to build against

These are working, tuned and settled. Read them before writing the Swift — the values in
them are the decisions, not suggestions.

| Path | What it settles |
|---|---|
| `components/family/YearHeatmapCard.jsx` | **The rainbow year, complete.** Hue from day-of-year, linear 34→86% ramp, per-scheme lightness, cell-scaled bloom, future outlines, `card`/`full`/`wall` sizes. Port this maths directly. |
| `ui_kits/widgets/Widgets.jsx` | The same maths across every WidgetKit family, plus the square-cell rule and the three expressive modes. |
| `ui_kits/kid_app/Celebration.jsx` | The full tier-1/2/3 celebration ladder (§1.3) with the tuned config. |
| `ui_kits/kid_app/index.html` | Tweaks panel → **SwiftUI** section renders a ready-to-paste `CelebrationConfig`. |

### Three mistakes already made here — don't repeat them in Swift

1. **Blur per particle.** Canvas2D applies a filter per draw call; blurring 1200 particles
   individually cost ~1 fps and froze the page. Render depth bands into three layers and
   blur each layer once. In SwiftUI the equivalent is `.blur()` on each particle view
   instead of on the depth layer — same 30× cost.
2. **Non-square day cells.** Letting the grid stretch both axes turned the calendar into
   bar charts and stripes. A day cell is square, always; size the grid from the cell edge,
   never from the available box.
3. **Translucent cell fills.** Mixing the hue toward a translucent fill token dropped
   drained days to ~15% saturation (the grey hole §1.2 forbids) and made the same day a
   different colour on a card than on a sheet. Cell fills are opaque.

## Files to read

| Path | What it is |
|---|---|
| `IMPLEMENTATION.md` | Full spec incl. Phases 2–3 and the reasoning |
| `guidelines/recommendations/index.html` | The 12 recommendations, with the argument for each |
| `guidelines/rainbow-year/index.html` | **Live heatmap — the spec for 1.2** |
| `guidelines/color-invariants.html` | The six invariant hexes |
| `guidelines/color-fills.html` | The three fill levels |
| `guidelines/motion.html` | Durations and easings |
| `ui_kits/kid_app/` `parent_app/` `mac_app/` | Screen-level layout references |
| `ui_kits/widgets/` | Widget family reference for 1.2 |

## Not in this handoff

Phase 2 (drive logging, frequency pips, named stakes, pickers, emoji grid) and Phase 3 (YAML theming and the two-mode editor) are fully specced in `IMPLEMENTATION.md` but out of scope. Ship Phase 1 first — your family doesn't need 2 or 3 to start using it.

The app icon needs vector artwork. Concept is locked (monstrance; host constant, 8 fixed rays lighting proportionally) but it is **not a blocker** — see `guidelines/app-icon/`.
