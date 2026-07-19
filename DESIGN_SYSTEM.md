# Daily Bread Design System — "Graphite & Glass"

> **Version:** 2.0
> **Status:** Active — replaces the retired v1 (Nord) system entirely.
> This document describes the shipped theme system in `wwwroot/css/design-system.css`.

## Principle

**Surfaces are neutral graphite and never change per theme. Themes are accent tints only.**

The web app shares one design language with the native iOS/macOS app: neutral dark/light
surfaces, with color appearing only where it carries meaning —

| Color | Meaning | Rule |
|---|---|---|
| **Accent** (`--db-accent`) | Interactive: buttons, links, active states, progress | The *only* thing a theme changes |
| **Gold** (`--db-gold` / `--db-glow`) | Money / the Approve ("Blessing") moment | Constant across every theme. Never themed away. |
| **Red** (`--db-error`) | Help alert / errors | Reserved. Nothing else in the app is red. |

## Source of truth

1. **`wwwroot/css/design-system.css`** — all tokens. Section 1b is the theme system.
2. **`wwwroot/app.css`** — shared primitives (buttons, inputs, cards, tables, modals) built
   from tokens, plus the legacy alias layer.
3. **`*.razor.css`** — component-scoped layout/states; must consume tokens only.

No raw hex colors outside `design-system.css`. Tinted surfaces use
`color-mix(in srgb, var(--db-accent) N%, transparent)` — never a hardcoded rgba.

## Theme system (section 1b)

`data-theme` × `data-mode` are set on `<html>` (persisted in `localStorage` by `DBTheme`,
no-flash bootstrap in `App.razor`). Fallback: `guadalupe` + `dark`.

### Per-mode surfaces (identical across all themes)

| Token | Dark | Light | Role |
|---|---|---|---|
| `--db-bg` | `#0E0E10` | `#F2F2F6` | Page background |
| `--db-bg2` | `#1A1A1E` | `#FFFFFF` | Cards, elevated surfaces |
| `--db-bg3` | `#26262B` | `#E8E8ED` | Hover, borders, muted fills |
| `--db-text` | `#F5F5F7` | `#1D1D1F` | Primary text |
| `--db-text2` | `#A3A3AA` | `#6E6E73` | Secondary text |
| `--db-text3` | `#7A7A82` | `#98989F` | Muted text |

### Per-mode semantic constants

| Token | Dark | Light |
|---|---|---|
| `--db-gold` | `#D7A23F` | `#D99514` |
| `--db-glow` | `#EAC468` | `#F2B705` |
| `--db-error` | `#C25563` | `#D33B4E` |
| `--db-warning` | `#D8956A` | `#D27D3C` |
| `--db-success` | `#84B98F` | `#3E9E63` |
| `--db-special` | `#B48EAD` | `#9B6FB0` |
| `--db-on-accent` | `#0C0F11` | `#FFFFFF` |

### Themes = accent only

| Theme | Dark accent | Light accent |
|---|---|---|
| `guadalupe` (default) | `#4DA8C6` | `#0E8FC4` |
| `marian` ("Sea") | `#6A82E6` | `#3A5BD0` |
| `sion` ("Garden") | `#5AAE7B` | `#2E9E63` |
| `advent` ("Violet") | `#9B7BE0` | `#7A4FD0` |
| `rosa` ("Rose") | `#E08AA6` | `#D14E7E` |

`rosa` additionally deepens `--db-error` (dark `#9E3350`, light `#A8123F`) so the Help
signal stays distinct from the pink accent.

### CSS ordering constraint (do not break)

The `:root` safety-defaults block **must precede** the `[data-mode]` blocks in
section 1b: `:root` and `[data-mode="…"]` tie on specificity, so the per-mode
surfaces win by source order. Moving the safety block below them silently breaks
light mode.

## Mapping layer

The `--db-*` palette maps onto the app-wide `--ds-*` tokens (`--ds-bg-base`,
`--ds-text-primary`, `--ds-accent-primary`, `--ds-semantic-*`, `--ds-bread`,
`--ds-blessing`, glows) in one `:root` block. All components consume `--ds-*`;
nothing outside section 1b needs to know the theme system exists.

Legacy `--nord*` aliases still resolve via `app.css` for old component CSS.
They are frozen: no new usage.

## Usage rules

- New UI consumes `--ds-*` tokens directly.
- Accent-tinted fills: `color-mix(in srgb, var(--db-accent) N%, transparent)` (N ≈ 15–25).
- Gold marks money and the Approve moment — nowhere else. Red marks Help/errors — nowhere else.
- Glass utilities (`.ds-glass`, `.ds-bottom-bar`) are graphite-neutral and mode-aware; never re-tint them per theme.
- Touch targets ≥ 44px; visible focus rings; respect `prefers-reduced-motion`.
- Validate changes on: Home dashboard, one modal, login inputs, mobile width, both modes, at least two themes.

## Changing things safely

1. Surface/neutral tweaks → the two `[data-mode]` blocks (one place, all themes follow).
2. A new theme → one pair of `[data-theme][data-mode]` accent rules + a `ThemePicker` entry + the `THEMES` list in `App.razor`.
3. Semantic color changes → per-mode constants; remember `rosa`'s error override.
4. Never add surface definitions to theme selectors — that reintroduces the v1 architecture this system replaced.
