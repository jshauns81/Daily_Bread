# Theme Ownership — a kid can write his own look

*Design note. Captured while the idea was fresh. Not yet built — this is the shape we build to,
after Friday, as Victor's "back from camp" gift.*

## Why this exists

Every chore app on the market does the same four things: it **defines responsibility**, it
**rewards**, it **punishes**, and it **moves money** — in and out. Not one of them gives the kid
**ownership**. The child is always the subject of the system, never an author of it.

A theme a child writes himself, in his own words, is the smallest true form of ownership we can
give. It costs the parent nothing and takes nothing away from the mechanics. It just changes the
sentence the app is saying — from *"here are your rules"* to *"here are your responsibilities, in
your colors."* That is the whole point, and it is worth more than it looks.

The theme is the first door. If it lands, ownership can grow from here (see *Horizon*). But it
starts small, concrete, and completely his.

## What Victor writes

A theme is already nothing but a handful of values. So the thing he authors is small and human —
a file he can read out loud and understand every line of. YAML, not JSON, precisely because a kid
can write it cleanly without getting lost in braces:

```yaml
# victor's theme
name: Victor's Blue
feel: cool and quiet        # shows up under the name in the picker
mode: dark                  # dark or light

colors:
  main: "#4C8DFF"           # buttons, my name, the important stuff
  extra: "#3FC9B0"          # the little done-checks
  background: "#1B2340"     # behind everything
  card: "#26304F"           # the cards that float on top
```

That is the entire contract. Five colors, a name, a feel, a mode. It maps one-to-one onto the
`DBTheme` fields that already exist: `main → accent`, `extra → secondary`, `background →
backgroundGradient` (we derive the soft two-stop gradient from the one color he gives, so he
never has to think about gradients), `card → cardColor`, `mode → isDark`.

## What he owns, and what he doesn't

He owns the *look*. He does not own the two colors that carry **meaning**:

- **Gold is money.** Always. Balances, earnings, the coin — gold, in every theme, including his.
- **Red is help.** The "needs help" alert stays its reserved red so it can never hide inside a
  blue or a berry palette.

This is not a limitation to apologize for — it *is* part of the lesson. Ownership includes the
freedom to make it yours, and the responsibility not to paint over the things that keep everyone
safe. Gold and red are load-bearing. He gets everything else.

## How it loads

1. A new `CustomTheme: Codable` struct with exactly the fields above, plus a stable `id`.
2. Parse the YAML (add the **Yams** SwiftPM package to `DailyBreadKit` — small, well-known,
   no strings attached).
3. Author it two ways, whichever fits the moment: **paste/edit** the text in-app, or **drop a
   `.yaml` file**. Store the parsed result in `UserDefaults` under `db.customThemes` (a small
   JSON array), the same store the picker already reads.
4. `DBTheme` gains a `.custom(CustomTheme)` path (or the picker composes built-ins + customs into
   one list). Everything downstream — `accent`, `backgroundGradient`, `cardColor`,
   `progressGradient`, the `GlassCard`/`GraphiteBackground` modifiers — already reads through one
   place, so a custom theme renders with zero new plumbing.
5. It appears in **Settings → Theme** beside Sunroom and the rest, shown as *Victor's Blue*, with
   the same live preview swatch and the same instant, whole-app switch.

He writes it, saves it, and the app *becomes* it. That immediacy is the reward — no build, no
grown-up in the loop, no waiting.

## Keeping it safe without taking over

A kid can write a background and a card color that land on top of each other and make text
vanish. We protect legibility without confiscating his choices:

- On save, check contrast between text and `background`, and between text and `card`. If it's too
  low, we don't reject his colors — we **nudge** ("this might be hard to read — want me to darken
  the background a touch?") and offer a one-tap fix he can accept or decline.
- Any missing or malformed field falls back to the Sunroom value for that slot, so a half-written
  file still renders something warm rather than crashing.
- Gold and red are injected after his colors, never from them.

The guiding rule: **his theme should be hard to make ugly and impossible to make broken.**

## The downstream delivery mechanism

This is the same feature at a larger size, and it's why it matters beyond one family. Once a theme
is **data instead of code**, themes can be *delivered*:

- A **theme pack** is just a list of these YAML themes. A downstream family (or a school, or a
  reseller) drops in a pack and their app has new looks — nobody ships a new build, nobody touches
  Swift.
- The app can carry a small set of built-in packs and accept authored/imported ones through the
  exact `CustomTheme` path Victor uses. His one theme and a customer's twenty-theme pack are the
  same code.
- Natural future step: themes shareable as a file or a short code, so a kid can hand his look to a
  cousin. Ownership you can *give away* is ownership that's really yours.

## Horizon — where ownership goes next (not now)

Captured so we don't lose it. The theme is the proof of concept for a larger idea: letting the
child author the parts of the system that are safe to author.

- **His words on his work.** Let him rename a chore in his own language ("tidy my cave" for "clean
  your room") without changing what the parent set or what it pays.
- **His badges / quest names.** He names the milestones he's climbing.
- **His avatar / banner.** A small self-portrait at the top of his Home.

Each one is the same move as the theme: the parent still owns responsibility, reward, and money;
the child owns the *voice*. None of these are on the Friday path. They're here so the theme is
built as a door, not a dead end.

## Build checklist (for later)

- [ ] `CustomTheme: Codable` model in `DailyBreadKit` (name, feel, mode, main, extra, background,
      card, id).
- [ ] Add **Yams** to the `DailyBreadKit` package; YAML → `CustomTheme` decoder with per-field
      fallback to Sunroom.
- [ ] Persist customs to `UserDefaults` (`db.customThemes`, JSON array).
- [ ] Compose built-ins + customs in the picker; render customs through the existing modifiers.
- [ ] Authoring UI: paste/edit sheet **and** `.yaml` file import; live preview updates as he types.
- [ ] Legibility check on save with a friendly one-tap nudge; gold/help injected post-parse.
- [ ] Delete / rename a custom theme; guard against removing the active one (fall back to Sunroom).
- [ ] Optional: export a custom theme as a shareable `.yaml`.
- [ ] Tests: decode a good file; decode a broken file (fallbacks hold); contrast check flags a
      bad pair; gold/help stay reserved regardless of authored colors.

---

*One line to remember why: chore apps tell a kid what he owes. This one starts to tell him what's
his.*
