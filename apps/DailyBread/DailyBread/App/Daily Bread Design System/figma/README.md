# Pushing Daily Bread into Figma

Two routes. They do different jobs — **do both**, tokens first.

---

## Route 1 · Variables and styles → `daily-bread.tokens.json`

Every token in this design system, with all six themes as separate sets, in
[Tokens Studio](https://tokens.studio) / W3C DTCG format.

**Import:**

1. Figma → Plugins → **Tokens Studio for Figma** (free tier is enough)
2. Plugins pane → ⚙️ Settings → **Load from file/folder** → pick `daily-bread.tokens.json`
3. The `$themes` block is already configured — go to the **Themes** dropdown and you'll
   see Sunroom, Sky, Rosewater, Meadow, Mulberry, Harbor
4. Hit **Export to Figma** → *Variables* → select all six themes

You get **one variable collection with six modes**. Switch a frame between Harbor and
Sunroom from the Figma layers panel, exactly like the app does.

### What's in it

| Set | Contents |
|---|---|
| `global` | spacing, radius, type scale, weights, kerning, motion curves, fixed component sizes, frame sizes, level-tier colours |
| `theme/sunroom` … `theme/harbor` | backgrounds, card, label steps, fill steps, accent, secondary, and the five invariants |

**The invariants are duplicated into every theme set on purpose.** `gold`, `glow`, `help`,
`success` and `night` shift between the light and dark palettes (gold is `#C98A1E` on light,
`#E7B44A` on dark) but always *mean* the same thing. Keeping them per-theme is what makes
mode-switching correct; their descriptions carry the rule.

**`rarity` lives in `global`, not per theme** — a badge's rarity must read the same in every
palette. `rare` is a fixed `#3B82D6`, deliberately **not** the theme accent (that was R6).

---

## Route 2 · Screens → `html.to.design`

Tokens give you variables, not layouts. For the actual screens:

1. Figma → Plugins → **html.to.design** (free tier: ~10 imports/day)
2. Choose **Import from URL**
3. Paste a URL for the screen you want. **Ask me for it while you're sat at your computer**
   — the URLs I mint are single-use and expire in about ten minutes, so there's no point
   generating them in advance. Say *"mint me the kid app URL"* and paste it straight in.

Every import lands as **real editable Figma layers** — auto-layout frames, text nodes,
vectors. Not a screenshot.

### Worth importing

| Surface | File |
|---|---|
| Kid app | `ui_kits/kid_app/index.html` |
| Parent app | `ui_kits/parent_app/index.html` |
| Widgets — all families | `ui_kits/widgets/index.html` |
| Component gallery | `components/core/core.card.html` |
| Sheets | `components/sheets/sheets.card.html` |
| Family components | `components/family/family.card.html` |

**Import each screen once per theme you care about.** Switch `data-theme` on `<html>`
before I mint the URL and you get a Harbor version and a Sunroom version as separate
frames — useful for checking that a design holds in both.

### After importing

The layers arrive with hardcoded hexes, not bound to variables. Two options:

- **Quick:** leave them. Fine for exploration.
- **Proper:** select a frame and use Tokens Studio's **Apply to selection**, or Figma's
  native *Selection colors* panel, to rebind fills to the variables from Route 1. Do this
  for the component gallery frames only — that's what you'll actually reuse.

---

## What I could not do

**There is no Figma MCP connected to this environment.** I searched for Figma tools twice
and found none, so I cannot create the file, push frames, or authenticate on your behalf —
authentication happens in your Claude client's connector settings, not from inside a
project.

If you do connect one, tell me and I'll check what it exposes. Be aware that most Figma
MCP servers — including Figma's official one — are **read-only by design**: they exist so
an agent can *read* your designs to generate code. Writing frames into a file generally
needs the Plugin API, which is what Route 2 uses. So even with the MCP connected, the two
routes above are likely still the way to get this system into Figma.

---

## Suggested file structure once imported

```
Daily Bread (Figma file)
├── 📄 Foundations      ← Route 1 variables; a swatch page per theme
├── 📄 Components       ← Route 2, the three component galleries, rebound to variables
├── 📄 Kid app          ← Route 2 screens
├── 📄 Parent app       ← Route 2 screens
└── 📄 Widgets          ← Route 2, all families
```

Keep `IMPLEMENTATION.md` as the source of truth for *decisions*. Figma is for exploring
layout; the Swift source and this design system remain truth for values.
