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
| **Chore check** | Four states: unchecked / done / awaiting approval / help raised | §1.1 — absorbs the HELP capsule |
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
2. **`ChoreCheck` removes the HELP capsule** from every Today row. Confirm you're happy
   losing it as a visible affordance.
3. **Reduce Motion behaviour** for tier 3 — currently collapses to the bloom alone.
   Confirm that's the degradation you want rather than nothing.
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
