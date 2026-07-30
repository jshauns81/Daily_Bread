# Daily Bread — build spec

Handoff document for implementation. Written against the SwiftUI source at `DailyBread/`.
Companion artifacts: `guidelines/recommendations/index.html` (the reasoning), `guidelines/rainbow-year/index.html` (visual decisions on the heatmap).

---

## Standing principle

**Stock controls are fine. Badly laid-out controls are not.**

This spec does not replace `DatePicker`, `Slider`, `Picker` or `Toggle` on principle. It replaces them in the specific places where (a) the control sits at an emotional centre of the product, or (b) the control's shape misrepresents the data. Everywhere else, keep the system control and fix its container, spacing and grouping.

The rule for a reviewer: *if a stock control is in the right place and laid out well, leave it.*

---

## Phase 0 — Foundations (do first; everything else depends on it)

### 0.1 Fill tokens — R5

Seven opacities currently mean "off": `.06 .12 .13 .14 .16 .30 .35` across `CalendarView:142`, `PlannerGridView:126`, `PlannerView:480`, `SheetKit:164`, `ScreenTimeSettingsSheet:115`, `ApprovalsView:419`.

Add to `DesignSystem.swift`:

```swift
public extension DB {
    static func fillSubtle(_ s: ColorScheme) -> Color   // ~0.06 — large surfaces
    static func fillOff(_ s: ColorScheme) -> Color      // ~0.12 — control backgrounds, unselected
    static func fillStrong(_ s: ColorScheme) -> Color   // ~0.32 — pressed / prominent
}
```

Replace all `Color.secondary.opacity(_)` call sites. **Acceptance:** zero `.opacity(` literals on `Color.secondary` remain outside `DesignSystem.swift`.

### 0.2 Rarity + night as invariants — R6, R7

`AchievementsView.swift:180` returns `.accentColor` for `"rare"`, so rarity changes meaning per theme — in Sky it is indistinguishable from every ordinary accent control. `DrivingLogView.swift:147` hardcodes `Color.indigo` for night driving, the only unowned hue in the app.

Move both into the invariant set beside gold and help. Invariants are **not** themeable and **not** YAML-overridable by default (see 3.4):

```swift
public enum DBRarity { case common, rare, epic, legendary
    func color(_ s: ColorScheme) -> Color }   // #8A8F98 / #3B82D6 / #7A5AF8 / #E7B44A
public extension DB { static func night(_ s: ColorScheme) -> Color }  // replaces Color.indigo
```

✅ **`night` decided: `#5560A8` light / `#8D97D8` dark** ("dusk slate"). Candidates were
compared in context in `guidelines/color-night.html`. Rationale: night is a *fact about a
drive*, not a reward or a brand colour, so it is the quietest hue in the app — deliberately
duller than every theme accent and every rarity, which is what guarantees it can never be
mistaken for one. A saturated indigo sat between Sky's blue and rarity-epic's violet.

✅ **Rarity hexes corrected in `tokens/themes.css` too.** They were `epic:#9B7BE0` and
`rare: var(--db-accent)` — the second being R6's exact bug living in the tokens as well as
in the Swift. Now fixed hex, matching the values above.

### 0.3 Accent rule — R12

`DailyBreadApp.swift:34` already does `content.tint(theme.accent(scheme))`. This is correct and load-bearing — it is why system controls are at least coloured right. Document it so nobody "fixes" it:

> **Use bare `Color.accentColor` for accent.** Reach for `DB.*` only for invariants: gold, help, success, rarity, night.

Rename `graphiteBackground()` → `themeBackground()` (30+ call sites, mechanical; the in-source comment already admits the name is a fossil).

---

## Phase 1 — The daily surfaces

### 1.1 `ChoreCheck` — four states — R1

`TodayView.swift:354`. Currently a stock `circle` / `checkmark.circle.fill` in grey→accent. Grey reads as *disabled* rather than *empty and waiting*, and there is no per-chore feedback at all.

New component, four states:

| State | Ring | Fill | Glyph | Notes |
|---|---|---|---|---|
| `unchecked` | 2.5px `label.opacity(0.22)` | none | none | warm, empty, clearly tappable |
| `done` | accent | accent | white check | spring + halo bloom, rigid haptic |
| `awaitingApproval` | 2.5px gold, dashed | gold @ 12% | small gold check | "he says it's done, you haven't looked" |
| `helpRaised` | 2.5px help-red | help @ 12% | `questionmark` | shows the help-raised *state*; the row's Help affordance stays (see below) |

- Target 44×44 via `contentShape`; visual 29pt.
- Transition `.spring(response: 0.28, dampingFraction: 0.55)`, halo blooms to 1.35× and fades over ~320ms. `.symbolEffect(.bounce)` on the checkmark is free here — one of the reasons staying on SF Symbols was the right call.
- Haptic `.impact(.rigid)` on check only — undo is silent, don't reward it.
- **Earning chores:** flash the ring gold for ~180ms before settling to accent. The one moment where money and action are the same event.
- `awaitingApproval` and `helpRaised` are terminal from the kid's side — tapping shows the reason, doesn't toggle.

~~Absorbing HELP into the check removes the row's trailing capsule and gives every row one consistent control position.~~ **AMENDED 2026-07-30 by Shaun — overruled. Help stays visible.** Help/forgive is a core mechanic; a safety valve the kid can't see before he needs it isn't one. The row keeps a distinct, always-visible Help affordance; the check's `helpRaised` state only *displays* the result. Do not relitigate.

### 1.2 The rainbow year — replaces `YearHeatmapCard`

Your original Blazor idea, restored. See `guidelines/rainbow-year/index.html` for the live version. **Both open decisions are now settled — recorded below.**

- **Hue = day-of-year.** `hue = (dayOfYear / 365) * 360`. Day 365 lands at 359°, next day 1 at 0° — the year boundary is seamless, so multi-year scroll is one unbroken wheel. Rainbow-start to rainbow-start.
- **Intensity = `requiredDone / requiredTotal`.** Saturation 34→86%, lightness ramps opposite by scheme. A bad day is a *drained* colour, never a grey hole — the rainbow survives regardless of performance.
- **Optionals add bloom, never withhold colour** — ✅ **decided (option B).** Meeting the requirement is full brightness; each optional completed adds a hue-matched glow. **Rationale: under the alternative, declining an optional he was never obliged to do leaves a permanently dimmer square on his year — the calendar punishing him for exercising a choice you deliberately gave him.** Implement B; do not make optionals subtract.
- **A day's `isComplete` is therefore `requiredDone == requiredTotal`**, independent of optionals. Optionals only ever raise `bloomLevel` (0…n). Make this explicit in the model so it can't drift.
- One renderer, three sizes — ✅ **decided:**
  - **Card** — last 12 weeks. **This is what sits on Today**, and is the default the kid sees daily. Cell ~18pt, no horizontal scroll, fits an iPhone width.
  - **Full** — scrollable year, tap a day to open it. Reached by tapping the card.
  - **Wall** — iPad/macOS, whole year plus multi-year scroll.

  Same cell renderer, same maths, one component, `size` enum only.
- **Multi-year continuity is a hard requirement of Full and Wall**, not a nice-to-have: load a year at a time, keep hue continuous across the boundary, no gap or divider between year columns. The seam being invisible is the whole idea — a visible gap there defeats it.

- Cell payload: `{ date, requiredDone, requiredTotal, optionalDone, optionalTotal, isFuture }`.
- Intensity ramp is **linear** — ✅ **decided as `forgiving`** after testing three curves against the real widget set (`ui_kits/widgets/index.html`, Shortfall tweak). Exponent **1.0**, saturation floor **34%**. The earlier `pow(ratio, 1.6)` from a 18% floor (`honest`) and a steeper 2.6 from 8% (`stark`) were both rejected: they read as punishment on a wall of 365, and a half-done day should still be a colour you'd want to look at. A shortfall reads as a shortfall from the lightness ramp alone.
- Perfect days bloom — **`glow`**: a hue-matched halo at `cell × 0.55`, scaled to the cell so it never smears into its neighbours at year-wall density. (`ember` — hot core plus bright rim — was tried and is too loud at 365.) On a wall of 365 this reads as scattered embers and a streak becomes a visible burning line — this is where R11's celebration lands permanently.

**Open decision:** none. Build it.

### 1.2a Where the rainbow lives outside the app

The rainbow year should be visible without opening Daily Bread. Three surfaces, three different platform realities:

**macOS dock — fully live. Do this.**
`NSApp.applicationIconImage = renderedImage` (or `NSApp.dockTile.contentView` + `display()`) replaces the dock icon at runtime with anything you can draw. Render the last 12 weeks each morning and on every chore completion. This is the only place the icon itself can genuinely reflect state, and it's the scenario that prompted the question.

**iOS home screen — not the icon. Use a widget.**
iOS cannot render an app icon at runtime. `setAlternateIconName(_:)` only switches between variants **baked into the bundle at build time** (declared in `Info.plist` under `CFBundleIcons ▸ CFBundleAlternateIcons`), and every switch raises a system alert — unacceptable daily. Pre-baking ~5 completion buckets is technically possible but the alert kills it.

The sanctioned surface is **WidgetKit**:
- **Small** — last 4 weeks + today's ring.
- **Medium** — two kinds. *Year*: last 12 weeks, matching the Today card exactly (§1.2). *Today*: the day's chores, checkable inline via App Intents. Additive, not a replacement — one answers "how am I doing", the other "what's left".
- **Large** — the full year.
- Timeline reload each morning and on completion via `WidgetCenter.shared.reloadTimelines(ofKind:)`.
- **Lock Screen accessories are monochrome** — iOS tints them, so the rainbow cannot appear there; circular ring, rectangular count + pip bar, inline count. **StandBy** is not an accessory family and does get full colour: 26 weeks.
- **Hue is always `dayOfYear / 365 × 360`**, never position-within-window — otherwise every widget renders a full spectrum regardless of range and the year stops being legible.
- Built: `ui_kits/widgets/`.

A widget is strictly better than a dynamic icon anyway — bigger, legible, and it sits right beside the icon.

**Consequence for the icon decision.** This resolves the tension between the two icon rounds: the **icon stays a stable identity mark** (the monstrance — it must say "Daily Bread" from six feet away, and a mark that changes daily can never build recognition), while the **living rainbow goes in the widget and the macOS dock**. You get both, each where it works.



Today: one `ConfettiView`, gated on `store.allDone`, ~40 flat rectangles. A kid who does five of six chores gets the same feedback as one who does none.

Three tiers, ascending:

1. **Per-chore** — the 1.1 check bloom + haptic. Every completion.
2. **Threshold** — last *earning* chore lands, or a streak extends. A gold coin arcs from the check into the balance in the header, which pulses. Short, ~600ms, no full-screen takeover.
3. **Perfect day** — the real thing. Replace the current flakes with a `SpriteKit` / `TimelineView`+`Canvas` particle system: physics with gravity and drag, varied geometry (coins, ribbons, stars), depth via scale+blur layers, theme-tinted plus gold, ~1200 particles, 2.5s. Add a brief radial bloom behind them and a rising chime.

Because tier 3 now requires a genuinely perfect day it fires *less* often than today's — which is what makes it worth something. All three respect the existing `enableConfetti` setting; a teen can turn the ladder down.

### 1.4 List cleanup — R9

`TodayView.swift:165–210` neutralises `List` row by row (`.listRowBackground(.clear)` + custom insets) so authored cards can sit inside it.

- Screens whose rows are all clear-backgrounded cards **with no swipe actions** → `ScrollView` + `LazyVStack`: Earnings, Awards, the heatmap/screen-time stacks.
- Screens **with** swipe actions → keep `List`. Today's swipe-to-Done and swipe-to-Help are real, native and worth keeping. Approvals and Planner likewise.

---

## Phase 2 — Input surfaces

### 2.1 Drive logging — duration-first, and parents can log — R2

`DrivingLogView.swift:247–249` — three `DatePicker`s stacked inside an authored `SheetCard`. The log exists to accumulate hours toward a licence; **duration is the payload and the form never shows it.**

New sheet:

1. **Date** — chips: `Today` / `Yesterday` / `Pick…`
2. **Duration is the hero** — large value, preset chips `15 / 30 / 45 / 1h / 1h30 / 2h`, drag to fine-tune. Live subtitle: *"brings you to 12.8 of 50 hours"*.
3. **`Set exact times`** — disclosure. Start/End only when needed.
4. Supervising adult, weather, night mode, notes as today.

**Both disclosures must be premium — no cheap surfaces.** `Pick…` opens a themed calendar sheet reusing the `CalendarView` month grid (already authored), not a bare system sheet. `Set exact times` expands into themed inline time fields on `DB.fillOff`, matching `SheetKit` field styling. If a surface appears, it's designed.

**Parent entry — you flagged this as an oversight.** Parents must be able to log a drive on the child's behalf:
- Entry point on the child's driving log and from Approvals.
- When a parent logs it, it is **auto-approved** and stamped `Logged by <parent>` on the row.
- Reuse the same sheet; when `isParent`, show the child selector *only if* `!isSingleChild` (existing invariant) and swap the supervising-adult default to the logging parent.

### 2.2 Weekly frequency — pips, max 6 — R3

`ChoreEditorSheet.swift:195` uses a `Stepper` twelve lines from your own `DayPicker` at `:207` — same sheet, same question, two vocabularies.

- Replace with `DayPicker` geometry: tap the nth pip to set the count.
- **Cap at 6.** Per your call: at 7 it isn't optional any more, it's daily — so the control shouldn't offer it. If a parent wants 7, they want "specific days: all", and the UI should say so.
- Show only when the schedule toggle is *frequency*; the day picker shows when it's *specific days*. One vocabulary either way.

### 2.3 Importance → named stakes — R4

`ChoreEditorSheet.swift:242`, a bare `Slider(0...10)`. Nobody knows what 7 means, including the parent who set it — and it feeds the urgency copy on the kid's screen.

Five chips: **Nice to have · Normal · Matters · Big deal · Critical** → stored as 0/2/5/7/10. Storage unchanged, input only. Map each tier to the existing `AtRiskCard` urgency language.

### 2.4 Pickers — show what has a colour or a glyph — R8

*Annotated as agreed.* `AchievementDefinitionsView.swift:249–266`, `DrivingLogView.swift:253`.

**Rule: if the choice has a colour or a glyph, don't hide it in a menu.**

- **Rarity** → four coloured chips using the 0.2 invariants. It has a full visual identity and the editor currently reduces it to a grey row reading "Rare ⌄".
- **Weather** → four SF Symbols inline.
- **Keep `.menu`** for genuinely long lists: Category, Which chore, Before-hour. These are correct as-is — an example of the standing principle.

### 2.5 Emoji picker — R10

`ChoreEditorSheet.swift:133` and `AchievementDefinitionsView.swift:238` are unbounded `TextField`s. They accept `"asdf"`, three emoji, or nothing — then render at 40pt in the kid's list every day.

- Curated grid, **grouped by household area** — the full set is specified in `guidelines/icons-emoji.html` (the Brand card), which is the source of truth:
  - Kitchen · Cleaning · Laundry & bedroom · Outdoors & rubbish · Pets · Self & school · Helping & general
  - ~54 glyphs total. Groups matter: an ungrouped wall of 54 is worse than the text field it replaces.
- Recents row beneath.
- Free entry stays as the escape hatch, but **validate to exactly one grapheme cluster** — that's the bug you noticed.
- Same component for achievements with a trophy/award set.

---

## Phase 3 — YAML theming (R7 + R12)

The feature you actually want. Design goal: **a theme file is a first-class citizen, not a skin.** No half-baked variance — the built-in themes are expressible in exactly the schema a user theme uses, so anything a built-in can do, a user theme can do.

**Non-negotiable constraint: no theme file can prevent the app from launching or being used.** Customisation that a child can't recover from isn't a feature. See §3.3.

### 3.1 Location and sync

Themes live **on the family's own server** and sync to every device — your son builds a theme on his iPad, you see it on macOS. Local override folder for offline authoring:

- iOS/iPadOS: app container `Themes/`, exposed via Files
- macOS: `~/Library/Application Support/DailyBread/Themes/`
- Server: `GET /api/themes`, `PUT /api/themes/{id}`

### 3.2 Schema

```yaml
meta:
  id: harbor-night          # required, unique, slug
  name: Harbor Night        # required
  mood: quiet · dark
  author: shaun
  scheme: dark              # light | dark — forces appearance

colors:
  background: { top: "#1C2E3A", bottom: "#111C24" }
  card:       "#263E4F"
  accent:     "#5B9BE0"
  secondary:  "#4BA39C"
  onAccent:   "#FFFFFF"
  label:      "#FFFFFF"

typography:
  face: system              # system | rounded | serif | custom
  customFile: MyFont.ttf    # optional, sits beside the yaml
  scale: 1.0                # 0.85–1.3, respects Dynamic Type on top

icons:
  set: sfsymbols            # sfsymbols | phosphor | iconoir
  overrides:
    chore.laundry: "washing-machine"
    money: "coins"

motion:
  scale: 1.0                # 0 disables; respects Reduce Motion regardless
  celebration: full         # full | modest | off

radius: { card: 20, control: 10 }
```

### 3.3 Loading — and why a bad theme cannot brick the app

**Design rule: a theme is presentation-only, and the app must be fully usable with every theme file on the system deleted, corrupt, or malformed.**

```swift
struct ThemeManifest: Codable { /* mirrors the schema */ }
final class ThemeLoader {
    func available() -> [ThemeManifest]      // never throws; skips bad files
    func load(id: String) -> Result<DBTheme, ThemeError>
}
```

**1. Built-ins are compiled in, not read from disk.**
The six shipped themes live in Swift as they do today. They are *also* exported as YAML into the themes folder for reference and copying, but the app never depends on those files. Delete the entire themes directory and Daily Bread still launches in Sunroom.

*(This corrects the earlier note that built-ins would load through the identical file path. Schema parity is the goal — anything a built-in expresses, a custom theme can express — but sharing the on-disk **loader** would make the app's ability to render depend on files a child can delete. Parity of schema, not of load path.)*

**2. Unknown and misspelled keys are ignored, never fatal.**
Every key is optional except `meta.id` and `meta.name`. Missing keys inherit from the base theme for that `scheme`. So `acent: "#FF0000"` is not an error — it's a warning, and the accent stays default. **This is the main reason a typo can't do damage: the common failure mode degrades instead of failing.**

**3. The only true parse failure is malformed YAML** (bad indentation, unclosed quote). That file is listed in the picker as invalid, is **not selectable**, and shows the error with line number and expected key. Never a crash, never a blank screen.

**4. Validation happens at save/select, not at render.**
An invalid theme never becomes the active theme. There is no code path where a broken file reaches the render layer.

**5. Last-known-good is persisted separately from selection.**
If the active theme fails to resolve at launch for any reason, fall back to the built-in default and show a dismissible banner naming the file and the problem.

**6. The escape hatch is always rendered in built-in colours.**
Settings ▸ Appearance ▸ **Reset to Sunroom** is drawn with hardcoded built-in values, never the active theme. Even a theme that sets every colour to the same hex leaves that row legible and tappable. This is the one intentional exception to "nothing hardcoded outside the theme system" — document it as such.

**7. Preview before apply.** Selecting a theme in the picker previews it live; it isn't persisted until confirmed. Backing out restores the previous theme.

### 3.3a Make the safe path the default path

Raw YAML should be the *advanced* route, not the only one:

- **In-app theme editor** — colour wells, a scheme toggle, live preview, Save As. Produces valid YAML by construction. This is how your wife and son will actually make themes.
- **Export / import** — the editor writes the YAML file; the file can be hand-edited and re-imported by anyone who wants to.
- **Error messages teach.** "Line 7: `acent` isn't a key — did you mean `accent`?" A twelve-year-old debugging his first config file should learn something, not just be told no.

With the editor as the front door and unknown keys non-fatal behind it, the realistic worst case is *a theme that looks wrong*, recoverable in two taps — not an app he can't open.

- **Validate contrast on load.** If `label` on `card` falls below 4.5:1, show a warning badge in the picker — don't block it, it's their app. The warning is advisory; an unreadable theme is still recoverable via 3.3 point 6.
- **Hot reload** on file change (macOS) and on pull-to-refresh in the theme picker (iOS).

### 3.4 Invariants

Gold, help, success, rarity and night are **not** in the schema by default — they carry meaning that must survive every theme. Add an explicit opt-in escape hatch for people who know what they're doing:

```yaml
invariants:
  unlock: true              # required, or the block is ignored
  gold: "#E7B44A"
  help: "#F06B6B"
```

Without `unlock: true` the block is silently ignored. This keeps the guarantee intact by default while making the app genuinely yours if you insist.

### 3.6 The theme editor — one sheet, two modes

`Add a theme` at the bottom of the theme list opens an editor sheet. **It has a segmented control at the top: `Simple` | `YAML`.** Both edit the same in-memory `ThemeManifest`, so switching modes is lossless and live in both directions — drag a colour well in Simple and the YAML text updates; fix a hex in YAML and the well moves.

This is the important decision. A text editor alone serves the person who enjoys config files and fails the person who just wants a purple app; a form alone removes the thing worth *discovering*. One sheet with two views serves both, and costs little extra because the model is shared.

**Seeded from the current theme.** Opening it while Harbor is active pre-fills every field with Harbor's values, `meta.id` blank and `meta.name` as "Harbor copy". You start from something that already works and looks right.

**The template lists every key**, including ones left at default, each with a trailing comment naming what it affects:

```yaml
colors:
  background: { top: "#1C2E3A", bottom: "#111C24" }  # the app's ground
  card:       "#263E4F"   # every card, sheet and row surface
  accent:     "#5B9BE0"   # buttons, links, selected states
  secondary:  "#4BA39C"   # progress fills, secondary chips
```

Self-documenting beats a separate reference nobody opens.

#### Simple mode

Colour wells, a scheme toggle, font face picker, motion slider. Produces valid YAML by construction. This is the default mode and the front door.

#### YAML mode

- **Colour gutter, not inline swatches.** A narrow column beside the text showing a swatch for every line containing a hex. Tap it → system colour picker → the hex is rewritten in place. You almost never type a hex by hand. (Inline text attachments in `TextEditor` are painful; a gutter gets the same benefit for a fraction of the work.)
- **Lint as you type**, debounced ~400ms, not only on save. Bad line gets a red gutter marker and a one-line message underneath: *"Line 7: `acent` isn't a key — did you mean `accent`?"* Save is disabled while invalid, with the reason visible.
- **Live preview.** macOS: split view, app preview on the right, updating as you type. iPhone: a preview strip pinned above the keyboard showing a card, a button and a row in the theme being written.

#### Two implementation notes that will otherwise bite

1. **Disable every smart-text feature.** iOS and macOS will substitute curly quotes and en-dashes into YAML and silently break parsing — this is the single most likely source of "it says line 7 is broken and it looks fine to me". Set `.autocorrectionDisabled()`, `.textInputAutocapitalization(.never)`, and on AppKit `isAutomaticQuoteSubstitutionEnabled = false`, `isAutomaticDashSubstitutionEnabled = false`, `isAutomaticTextReplacementEnabled = false`.
2. **Add a keyboard accessory bar on iOS** with `:` `-` `#` `"` and an indent key. YAML is indentation-sensitive and the stock iPhone keyboard buries all of those behind modifier taps.

**Don't build a code editor.** `TextEditor` in **SF Mono** (`.font(.custom("SF Mono", size: 13))`, falling back to `.system(.body, design: .monospaced)`), an `AttributedString` recoloured on change, and the gutter above is enough for a 40-line file. Syntax highlighting via `AttributedString` recomputed on each edit is fine at this size. Resist WKWebView + CodeMirror — it's a better editor and a worse app.

**Save** writes to the themes folder, syncs to the server (§3.1), and selects the new theme. First successful save by a non-parent account fires the hidden achievement (§3.5).

**Why SF Mono specifically:** it ships with the OS, the zero is slotted, and `l` / `1` / `I` are unambiguous — which matters when the content is hex values a child is retyping from a swatch. Fixed advance also means the colour gutter's rows align to the text rows with no per-line measurement. 13pt on macOS, 15pt on iOS; below that, hex on a phone becomes a squint. It is the only place in Daily Bread where a monospaced face appears — that's correct, and it signals "this is the code surface" without needing a label.

Hidden achievement, **"Make it your own"** — fires when a user-authored theme is validated and selected for the first time. `isHidden: true`, `isVisibleBeforeUnlock: false`, legendary flourish on. Reward: manual (gift card / cash), so set `rewardKind` to the manual/parent-fulfilled option.

Ship one deliberately readable example theme (`example.yaml`) in the themes folder with comments explaining every key — that's the breadcrumb he'll find.

---

## Icon set — decided: stay on SF Symbols

✅ **No change. SF Symbols remains the icon language.** Phosphor, Iconoir, Remix and Lucide were evaluated (`guidelines/icon-sets/index.html`); it was close, and the incumbent won.

Why this is the low-regret answer:

- **`.symbolEffect`, Dynamic Type coupling, and optical alignment inside `Label` all come free.** Every alternative costs a wrapper view to claw those back.
- **iOS draws some chrome with SF Symbols regardless** — nav-bar backs, share sheets, context menus, swipe defaults — with no override. Staying on SF Symbols is the only choice with *no* seam.
- ~6,900 glyphs, vendor-maintained, zero dependencies.

### Still worth doing

Wrap it anyway — **one `DBIcon` view** that every icon goes through, taking a symbol name, a weight and a semantic tint. Not to enable a swap, but so `icons.set` in the YAML schema (§3.2) has somewhere to land later, and so per-call-site sizing stops drifting. Cheap now, expensive to retrofit.

### For the web artifacts only

SF Symbols is not licensed for web, so the HTML cards and kits in this design system substitute **Lucide** from CDN. That substitution is flagged wherever it appears and has no bearing on the app.

---

## Sequence

| Phase | Contents | Why here |
|---|---|---|
| **0** | Fill tokens, invariants, accent rule, rename | Everything downstream depends on it; mechanical, low risk |
| **1** | ChoreCheck, rainbow year, celebrations, list cleanup | The daily-felt surfaces — ship this and the app changes character |
| **2** | Drive logging, pips, stakes, pickers, emoji | Input surfaces; each is independent, ship as they land |
| **3** | YAML theming | Largest, most architectural, and the one with a gift attached |

**Ship after Phase 1.** It is the smallest cut that makes the app feel like the thing you described, and your family does not need Phase 2 or 3 to start using it.

---

## Decisions still needed

1. ~~Rainbow year: A or B on optional chores~~ — ✅ **B.** Optionals add bloom, never withhold colour.
2. ~~Rainbow year: density~~ — ✅ **Card (last 12 weeks) on Today**, Full year one tap away, Wall on iPad/macOS.
3. ~~Icon set~~ — ✅ **SF Symbols, unchanged.** Alternatives evaluated and rejected; wrap in a `DBIcon` view anyway.
4. ~~Harbor surfaces~~ — ✅ **Reverted to `DesignSystem.swift`.** `#223049 → #161E2C` background, `#2A3852` card. The Swift source is the truth for Harbor.

**Nothing blocks Phase 0 or Phase 1. Start building.**
