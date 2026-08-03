# START HERE

You're tired. Read this page only. It tells you what to do now, what's decided, and how to
pick this back up without losing anything.

---

## Do these three things now

1. **Download the project zip** (the download card in chat) and put it somewhere that
   backs up — Dropbox, iCloud, a git repo, anywhere that isn't just this browser tab.
2. **Drop that folder next to your `DailyBread/` Xcode project.** It's the design source
   of truth; the Swift repo is the code source of truth.
3. **Stop for today.** Nothing is half-finished. Every open item below is a *decision*
   waiting on you, not a broken file.

That's it. Everything below is reference for when you come back.

---

## What this project is

Two things living together:

- **A design system** — 6 themes, 192 tokens, 50+ components, 3 UI kits (kid, parent,
  macOS), a widget set, and guideline cards explaining every decision.
- **A build spec** — `IMPLEMENTATION.md`, which turns the design work into an ordered
  list of code changes for `DailyBread/`.

The design system exists to make the spec unambiguous. If they ever disagree, the
guideline card wins for *look*, and `DesignSystem.swift` wins for *Harbor's surfaces*.

---

## Where to start next time — pick one

| If you want to… | Open this |
|---|---|
| **Write code** | `design_handoff_phase0_phase1/README.md` — the ordered work order. Hand this and the project folder to Claude Code. |
| **See the whole system** | The Design System tab (34 cards, grouped Brand / Colors / Type / Spacing / Themes / Components / Kid app / Parent app / macOS app / Widgets / Guidelines). |
| **Remember why something is the way it is** | `guidelines/recommendations/index.html` — the 12 recommendations with the argument for each. |
| **Understand the app as it exists today** | `readme.md` — the audit of the real Swift source. |
| **Get all of this into Figma** | `figma/README.md` — two routes, tokens first. |
| **Change the rainbow year** | `components/family/YearHeatmapCard.jsx` — one component, used everywhere. |

---

## Decided — do not relitigate

Each of these took real argument. The reasoning is recorded next to the value so a future
you (or a future me) can't quietly undo it.

| Decision | Value | Recorded in |
|---|---|---|
| **Rainbow year** replaces the status heatmap | Hue = day-of-year, `(doy/365)×360` | `IMPLEMENTATION.md` §1.2 |
| Intensity ramp | **Linear**, saturation 34→86% (`forgiving`) | §1.2 — reversed an earlier `pow(1.6)` after seeing a full year |
| Optional chores | **Add bloom, never withhold colour** | §1.2 — dimming a square punishes a kid for a choice you gave him |
| Where it lives | 12-week card on **Today**, full year one tap away, wall on iPad/macOS | §1.2 |
| Cell shape | **Always square.** Size the grid from the cell edge, never from the box | learned twice the hard way |
| Cell fill | **Opaque.** Never mix toward a translucent token | same day must be the same colour on every surface |
| **Chore check** | Four states: unchecked / done / awaiting approval / help raised | §1.1 — as amended: check shows the help-raised *state* only |
| **Help stays visible** (2026-07-30) | The row keeps its own always-visible Help affordance — §1.1's "absorbs the HELP capsule" is overruled | Help is core to the app; a safety valve the kid can't see isn't one |
| **Celebrations** | Three tiers: per-chore bloom → coin arc to balance → perfect-day particles | §1.3, built in `ui_kits/kid_app/Celebration.jsx` |
| **Icon set** | **SF Symbols stays.** Phosphor/Iconoir/Lucide evaluated and rejected | "Icon set" section |
| **App icon** | Stable identity mark (monstrance). The *living* rainbow goes in widgets + macOS dock | §1.2a |
| **Night** | `#5560A8` / `#8D97D8` — the quietest hue in the app | `guidelines/color-night.html` |
| **Rarity** | `#8A8F98` / `#3B82D6` / `#7A5AF8` / `#E7B44A`, theme-independent | §0.2 |
| **Themes** | Built-ins compiled in Swift; user themes are YAML that **cannot brick the app** | §3.3 |
| **Harbor surfaces** | `#223049 → #161E2C` bg, `#2A3852` card — Swift source wins | §"Decisions still needed" #4 |
| Lock Screen | Monochrome, so the rainbow **cannot** appear there. StandBy gets full colour | §1.2a |

---

## Open — waiting on you, not on me

Nothing here blocks Phase 0 or Phase 1.

1. **Vector monstrance artwork.** Concept is locked (host constant, 8 fixed rays lighting
   proportionally). Your uploaded SVG reads as a *sunrise* rather than a monstrance and has
   baked glow filters that fuzz at small sizes. `guidelines/app-icon/round-8-review.html`
   compares it against a redraw; `guidelines/app-icon/round-8-render-check.html` proves the
   render is faithful. **Not a blocker for shipping.**
2. ~~**`ChoreCheck` removes the HELP capsule**~~ **DECIDED 2026-07-30: it doesn't.** Help
   stays a visible affordance on the row — see the Decided table. Overrules §1.1.
3. ~~**Reduce Motion behaviour** for tier 3~~ **DECIDED 2026-07-30: the default stands** —
   collapses to the bloom alone, never to nothing. (One-line change if it ever feels wrong
   in practice.)
4. **Phase 2 and Phase 3 are specced but not started.** Ship Phase 1 first; your family
   doesn't need either to start using the app.

---

## The build order — the only sequencing that matters

**Phase 0** (mechanical, no behaviour change) → **Phase 1** (the app changes character) →
stop and ship → Phase 2 → Phase 3.

Phase 0 is fill tokens, the rarity/night invariants, the accent rule, the
`graphiteBackground` → `themeBackground` rename, and the `DBIcon` wrapper. Phase 1 is the
four-state check, the rainbow year, the celebration ladder, and the `List` cleanup.

**Ship after Phase 1.** It's the smallest cut that makes the app feel like the thing you
described.

---

## Three mistakes already made here — don't repeat them in Swift

These cost real time. They're also recorded in the handoff README.

1. **Blur per particle.** Blurring 1200 particles individually ran at 1.3 fps and froze the
   page. Blur the *depth layer*, not the particle. In SwiftUI the equivalent is `.blur()`
   on each particle view.
2. **Non-square day cells.** Letting a grid stretch both axes turns a calendar into bar
   charts and stripes.
3. **Translucent cell fills.** Dropped drained days to ~15% saturation — the grey hole the
   spec forbids — and made the same day a different colour on a card than on a sheet.

---

## Figma

You've paid for a month, so use it. `figma/README.md` has the full steps; the short version:

- **Tokens → do this yourself, now.** `figma/daily-bread.tokens.json` imports through the
  free **Tokens Studio** plugin and gives you one variable collection with **six modes** —
  switch a frame between Harbor and Sunroom from the layers panel, exactly like the app.
  No help from me needed; the file is in the zip.
- **Screens → ask me when you're at your computer.** The **html.to.design** plugin imports
  a URL as real editable Figma layers (not screenshots). I mint those URLs on demand and
  they expire in ~10 minutes, so there's no point pre-generating them. Say *"mint me the
  kid app URL"*.

There is no Figma write API in this environment, so I can't create the file or push frames
myself — those two routes are the way in.

---

## To resume with me

Paste one of these:

> Pick up Daily Bread. Read `START-HERE.md`, then continue with Phase 0.

> Pick up Daily Bread. Read `START-HERE.md`. I want to work on the app icon.

> Pick up Daily Bread. Read `START-HERE.md`. I've built Phase 0 — here's the code.

To resume with **Claude Code**, in your Xcode project:

> Read `design_handoff_phase0_phase1/README.md` in the attached design system folder, then
> implement Phase 0 as separate commits, one per numbered task. Don't start Phase 1 until
> I've reviewed Phase 0.

**Status 2026-07-30: Phase 0 is DONE** — four commits on master (fill tokens, DBRarity +
night, themeBackground rename, DBIcon), iOS + macOS builds green. Every open decision above
is settled.

**Status 2026-07-30 (later): Phase 1 is DONE** — four commits on master, one per task,
iOS + macOS builds green, 205 backend tests green:

- **1.1 `ChoreCheck`** — four states as specced, Help kept as an always-visible
  `questionmark.circle` affordance on the row (the amendment, honored). `AutoApprove` now
  crosses the wire so the optimistic tap predicts done vs. awaiting-approval correctly.
- **1.2 Rainbow year** — one Canvas renderer, card/full/wall; hue from real day-of-year,
  linear 34→86% ramp, opaque square cells, cell-scaled bloom; Today card opens the full
  year (wall density on iPad/macOS) with year-at-a-time history and a seamless boundary;
  the **macOS dock icon is live**. `RainbowDay.bloomLevel` encodes "complete blooms,
  optionals only add" — both README readings honored (the backend has no optionals yet).
- **1.3 Celebration ladder** — coin arc → balance pulse on the last earning chore;
  perfect-day particle system (1200, depth-band blur, radial bloom, synthesized rising
  chime). Tiers 2–3 respect `enableConfetti`; all tiers respect Reduce Motion (tier 3 →
  bloom alone, as decided).
- **1.4 List cleanup** — Earnings → ScrollView+LazyVStack; swipe-bearing screens keep List.

**Ship it.** Remaining from §1.2's "outside the app", deliberately not in Phase 1:
the **WidgetKit family** (Small/Medium/Large + monochrome Lock Screen + StandBy) is a new
extension target — build it next, before or alongside Phase 2. App icon artwork still open
(not a blocker).

**Status 2026-08-02: the WidgetKit family is BUILT** — new `DailyBreadWidgets` iOS
extension target (`org.dailybread.app.widgets`), both platform builds green:

- **One renderer, literally** — `RainbowDay` / `RainbowMath` / `RainbowYearGrid` moved
  into DailyBreadKit; the app's cards, the macOS dock, and the widget draw with the
  same code. Forgiving ramp everywhere (the widget JSX's `honest` default is
  superseded by the §1.2 decision).
- **The widget never talks to the server.** The app writes a `WidgetBridge` snapshot
  (app group `group.org.dailybread.shared`) from Kid Home, Today, and the rainbow
  stores, then pokes WidgetKit. The widget's own timeline only rolls at midnight so
  yesterday's ring can't pose as this morning's.
- **Kid Home feeds the bridge** — it's the daily driver; a widget fed only from the
  Today tab would go stale for a kid who checks chores from Home's Next Up card.
  The streak fetch now reaches back to Jan 1 so one call answers streak *and* rainbow.
- **Family** — Small: 4 weeks + today's ring · Medium Year: 12 weeks + streak /
  balance / ring · Large: two 26-week bands + perfect days / balance / streak ·
  Lock Screen circular / rectangular / inline (monochrome by decree) · StandBy:
  26 weeks, full colour on black. Cells stay square at every size — grids fit by
  dropping weeks, never by stretching.
- **Deferred, additive per §1.2a:** the interactive Medium *Today* kind (needs App
  Intents + shared-Keychain auth inside the extension process). Parent-flavoured
  widgets (approvals hero) likewise wait for a later pass.

Verified end-to-end in the kid simulator: launch → Home writes the snapshot (real
year, counts, balance, theme) → extension embedded with the right extension point.
To see it: long-press the home screen → **+** → Daily Bread → add. Same on the Lock
Screen for the accessories.

**Status 2026-08-02 (later): Phase 2 is DONE** — five commits, one per task, iOS +
macOS builds green:

- **2.1 Drive logging** — duration is the hero (44pt value, preset chips, drag to
  fine-tune, live "brings you to X of 50 hours"); date = Today / Yesterday / Pick…
  chips with a themed month grid; exact times behind a disclosure as themed fields;
  **parents log too** — + on Driving approvals, auto-approved server-side (already
  supported by the API), supervising adult defaults to the logging parent, child
  chips only in a genuinely multi-child household, row stamped "logged by".
- **2.2 Frequency pips** — six numbered pips in DayPicker's exact geometry; capped
  at 6 as decided (7 is daily — the caption points at Fixed days, all seven on).
- **2.3 Named stakes** — Nice to have / Normal / Matters / Big deal / Critical →
  0/2/5/7/10, storage unchanged; per-tier meaning lines echo the kid's urgency copy.
- **2.4 Chip pickers** — rarity as five DBRarity-tinted chips; weather as four
  inline SF-Symbol chips. Category / Which-chore / Before-hour stay menus (long
  lists are what menus are for). `SheetChip` joins SheetKit as the shared vocabulary.
- **2.5 Emoji picker** — the grouped grid from `guidelines/icons-emoji.html` (53
  glyphs, 7 groups), per-set Recents, free entry validated to one grapheme; same
  component for achievements with a trophy/streak set.

**Also 2026-08-02, out of band:** bundle ids are `com.jshauns.dailybread(.widgets)`
(org.dailybread.app is registered to a stranger's team globally — old builds only
ever worked via the wildcard profile); chore-name uniqueness is now scoped to the
assignee (siblings share "Empty Dishwasher" by design — the global check made
common names uneditable); dev server gets a `lan` profile (`0.0.0.0:5100`) so real
phones can reach it.

**Status 2026-08-02 (evening): Phase 3 theming — engine and sync are IN.**

- **3.A engine** — `ThemeManifest` (§3.2, lenient: only meta.id/meta.name required,
  typos degrade to the scheme base, malformed YAML reports its line), `ThemeLoader`
  (never throws; invalid files listed-not-selectable; exports `example.yaml` + the
  six `builtin-*.yaml` references the app never reads back), `AppTheme` as the one
  resolved surface, last-known-good fallback with banner, the always-Sunroom Reset
  row, invariants locked behind `unlock: true`, widgets themed via snapshot palette,
  Themes folder visible in Files. 8 loader tests pin the can't-brick rules; verified
  live in the sim (ultraviolet.yaml end-to-end). Typography/icons/motion/radius keys
  parse but are inert this pass.
- **3.B server sync** — `ThemeFiles` table (household + slug unique), GET/PUT/DELETE
  `api/v1/themes` storing YAML verbatim (server never parses); children write by
  design, overwrite/delete is author-or-parent (5 service tests); `ThemeSync` pushes
  valid local files (local wins — it's the authoring surface) and pulls server themes
  into the folder, on bootstrap and picker refresh.

- **3.C the editor and the rest** — the §3.6 sheet: `Simple | YAML`, both editing one
  `ThemeDraft`, so switching is lossless. **A round-trip test caught a real bug**:
  re-opening a saved theme read the *derived* gradient stop, so the background
  ratcheted lighter every edit-save; `CustomPalette` now carries the authored
  background beside the two computed stops. YAML mode bridges UITextView/NSTextView
  — required by §3.6's own two notes (smart quote/dash substitution silently breaks
  YAML and SwiftUI can't disable it; the iOS accessory bar must insert at the
  cursor). Colour gutter, 400ms lint that suggests the key you meant, live preview
  (split on macOS, pinned strip on iPhone), SF Mono. Plus preview-before-apply
  (Keep/Undo; Reset commits instantly — the escape hatch never asks you to confirm),
  advisory contrast badge, swipe edit/delete with server propagation, macOS hot
  reload, and the hidden legendary **Make It Your Own**, awarded server-side when a
  *child* saves a theme.

**Phase 3 is DONE.** 214 backend + 32 kit tests green, iOS + macOS builds green.

**Deferred within §3, deliberately:** `typography` / `icons` / `motion` / `radius`
parse but are inert — the schema accepts them so files don't break when they land.
App icon artwork still open (not a blocker).

**The whole handoff (Phase 0 → 3) is now built.**
