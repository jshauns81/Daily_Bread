# Daily Bread Design System

> **Version:** 1.0  
> **Last Updated:** Phase 5B Complete  
> **Status:** Active Migration (Phases 1–5B complete)

## Overview

Daily Bread uses a custom design system built on the **Nord color palette**. All design tokens are defined in `design-system.css` and serve as the single source of truth for colors, spacing, typography, shadows, and other visual properties.

A **legacy compatibility layer** exists in `app.css` that maps old `--nord*` tokens to `--ds-*` tokens. This layer will be removed in a future phase once all components are fully migrated.

---

## Source of Truth

### 1. `wwwroot/css/design-system.css`
The **single source of truth** for all design tokens:
- Colors
- Spacing
- Radii
- Shadows
- Typography tokens
- Z-index scale
- Interaction timing tokens (durations, easing)

### 2. `wwwroot/app.css`
The **composition layer**:
- Shared primitives (buttons, inputs, cards, tables, modals)
- App-wide utilities and patterns
- Legacy compatibility alias layer (temporary)
- **MUST** consume tokens from `design-system.css`

### 3. Component Scoped CSS (`*.razor.css`)
For **component-specific** layout and states:
- Internal grid/flex layout
- Component-only responsive tweaks
- Component-only hover/active states
- **MUST** consume tokens from `design-system.css`
- **MUST NOT** define global primitives or token values

---

## Token Architecture

### File Structure

```
wwwroot/
├── css/
│   └── design-system.css    # Source of truth for all --ds-* tokens
└── app.css                   # Legacy compatibility layer + component styles

Components/Layout/
├── MainLayout.razor.css      # Page shell, header, sidebar, bottom nav
├── NavMenu.razor.css         # Navigation links, brand, section labels
└── LoginDisplay.razor.css    # User menu, auth buttons
```

### Token Naming Convention

All design system tokens use the `--ds-` prefix:

| Category | Pattern | Example |
|----------|---------|---------|
| Backgrounds | `--ds-bg-*` | `--ds-bg-base`, `--ds-bg-elevated`, `--ds-bg-hover`, `--ds-bg-muted` |
| Text | `--ds-text-*` | `--ds-text-primary`, `--ds-text-secondary`, `--ds-text-muted` |
| Accents | `--ds-accent-*` | `--ds-accent-primary`, `--ds-accent-cyan`, `--ds-accent-teal`, `--ds-accent-blue` |
| Semantic | `--ds-semantic-*` | `--ds-semantic-success`, `--ds-semantic-error`, `--ds-semantic-warning`, `--ds-semantic-highlight`, `--ds-semantic-special` |
| Spacing | `--ds-space-*` | `--ds-space-1` (4px) through `--ds-space-10` (64px) |
| Typography | `--ds-text-*`, `--ds-font-*`, `--ds-leading-*` | `--ds-text-base`, `--ds-font-medium`, `--ds-leading-normal` |
| Radius | `--ds-radius-*` | `--ds-radius-sm`, `--ds-radius-md`, `--ds-radius-lg`, `--ds-radius-full` |
| Shadows | `--ds-shadow-*`, `--ds-glow-*` | `--ds-shadow-md`, `--ds-glow-success` |
| Duration | `--ds-duration-*` | `--ds-duration-fast`, `--ds-duration-normal`, `--ds-duration-slow` |
| Z-Index | `--ds-z-*` | `--ds-z-dropdown`, `--ds-z-modal`, `--ds-z-toast` |
| Touch | `--ds-touch-target*` | `--ds-touch-target` (44px), `--ds-touch-target-lg` (48px) |

### Token Rules

- No raw hex colors outside `design-system.css`
- No "one-off" pixel values for spacing, radius, shadow outside `design-system.css` unless documented
- New UI work must use `--ds-*` tokens directly
- Legacy token names exist only for backward compatibility—new code must not introduce new usage

---

## Color Palette (Nord-Based)

### Backgrounds (Polar Night)
| Token | Hex | Usage |
|-------|-----|-------|
| `--ds-bg-base` | `#2E3440` | Main page background |
| `--ds-bg-elevated` | `#3B4252` | Cards, modals, sidebar |
| `--ds-bg-hover` | `#434C5E` | Hover states, headers |
| `--ds-bg-muted` | `#4C566A` | Borders, disabled elements |

### Text (Snow Storm)
| Token | Hex | Usage |
|-------|-----|-------|
| `--ds-text-primary` | `#ECEFF4` | Headings, primary content |
| `--ds-text-secondary` | `#E5E9F0` | Secondary content |
| `--ds-text-muted` | `#D8DEE9` | Captions, placeholders |

### Accents (Frost)
| Token | Hex | Usage |
|-------|-----|-------|
| `--ds-accent-primary` | `#5E81AC` | Primary buttons, links |
| `--ds-accent-blue` | `#81A1C1` | Secondary actions |
| `--ds-accent-cyan` | `#88C0D0` | Active states, info |
| `--ds-accent-teal` | `#8FBCBB` | Decorative accents |

### Semantic (Aurora)
| Token | Hex | Usage |
|-------|-----|-------|
| `--ds-semantic-success` | `#A3BE8C` | Success, approved, money |
| `--ds-semantic-error` | `#BF616A` | Errors, danger, delete |
| `--ds-semantic-warning` | `#D08770` | Warnings, pending |
| `--ds-semantic-highlight` | `#EBCB8B` | Highlights, stars, badges |
| `--ds-semantic-special` | `#B48EAD` | Premium, special features |

---

## Primitives

These primitives are defined in `app.css` and **MUST** use `--ds-*` tokens directly:

| Primitive | Location | Status |
|-----------|----------|--------|
| Buttons | `app.css` | ✅ Migrated |
| Inputs & Validation | `app.css` | ✅ Migrated |
| Cards & Surfaces | `app.css` | ✅ Migrated |
| Tables | `app.css` | ✅ Migrated |
| Modals | `app.css` | ✅ Migrated |

Navigation chrome uses component-scoped CSS and **MUST** use `--ds-*` tokens directly:

| Component | Location | Status |
|-----------|----------|--------|
| Sidebar & Nav Links | `NavMenu.razor.css` | ✅ Migrated |
| Page Shell & Header | `MainLayout.razor.css` | ✅ Migrated |
| User Menu | `LoginDisplay.razor.css` | ✅ Migrated |

---

## Bootstrap Policy

Bootstrap may be used for **layout and utilities only**.

### ✅ Allowed
- Grid and layout helpers (`row`, `col-*`, `d-flex`, `justify-content-*`)
- Visibility helpers (`d-none`, `d-md-block`)
- Spacing helpers if they map cleanly to design tokens (`mb-3`, `p-2`)

### ❌ Forbidden
- Relying on Bootstrap component styling as the final appearance for buttons, inputs, cards, alerts, tables
- Adding new Bootstrap theme color dependencies
- Using Bootstrap's color utilities (`bg-primary`, `text-danger`) as final styling

---

## Legacy Token Mapping

The following legacy tokens are aliased in `app.css` and should **not** be used in new code:

| Legacy Token | Maps To |
|--------------|---------|
| `--nord0` | `--ds-bg-base` |
| `--nord1` | `--ds-bg-elevated` |
| `--nord2` | `--ds-bg-hover` |
| `--nord3` | `--ds-bg-muted` |
| `--nord4` | `--ds-text-muted` |
| `--nord5` | `--ds-text-secondary` |
| `--nord6` | `--ds-text-primary` |
| `--nord7` | `--ds-accent-teal` |
| `--nord8` | `--ds-accent-cyan` |
| `--nord9` | `--ds-accent-blue` |
| `--nord10` | `--ds-accent-primary` |
| `--nord11` | `--ds-semantic-error` |
| `--nord12` | `--ds-semantic-warning` |
| `--nord13` | `--ds-semantic-highlight` |
| `--nord14` | `--ds-semantic-success` |
| `--nord15` | `--ds-semantic-special` |
| `--space-*` | `--ds-space-*` |
| `--radius-*` | `--ds-radius-*` |
| `--shadow-*` | `--ds-shadow-*` |
| `--transition-*` | `--ds-duration-*` + ease |

### Legacy Compatibility Layer

- Exists to prevent regressions while migrating older components
- Is **not** the long-term styling API
- Removal is gradual and only happens when repo-wide usage is eliminated

---

## Migration Status

### Completed Phases

| Phase | Scope | Status |
|-------|-------|--------|
| Phase 1 | Legacy compatibility layer | ✅ Complete |
| Phase 2 | Buttons | ✅ Complete |
| Phase 3 | Inputs, validation | ✅ Complete |
| Phase 4 | Cards, tables, modals | ✅ Complete |
| Phase 5A | Navigation chrome (sidebar, header, user menu) | ✅ Complete |
| Phase 5B | Design system governance activation | ✅ Complete |

### Remaining Work

| Phase | Scope | Status |
|-------|-------|--------|
| Phase 6 | Remove legacy compatibility layer | 🔲 Pending |
| Phase 7 | Audit and documentation finalization | 🔲 Pending |

---

## Usage Guidelines

### ✅ Do

- Use `--ds-*` tokens for all new CSS
- Reference `design-system.css` for available tokens
- Use semantic tokens (`--ds-semantic-success`) for meaning-based colors
- Use background tokens (`--ds-bg-*`) for surfaces
- Use text tokens (`--ds-text-*`) for typography colors
- Use spacing tokens (`--ds-space-*`) for margins and padding

### ❌ Don't

- Use raw hex values (e.g., `#2E3440`)
- Use legacy `--nord*` tokens in new code
- Create component-specific color variables
- Override design system tokens at the component level
- Mix legacy and `--ds-*` tokens in the same rule

---

## Component Patterns

### Surfaces

```css
/* Elevated surface (cards, modals) */
background-color: var(--ds-bg-elevated);
border-radius: var(--ds-radius-lg);
box-shadow: var(--ds-shadow-md);

/* Hover state */
background-color: var(--ds-bg-hover);
```

### Interactive Elements

```css
/* Default state */
color: var(--ds-text-muted);
border: 1px solid var(--ds-bg-muted);

/* Hover state */
background-color: var(--ds-bg-hover);
color: var(--ds-text-primary);
border-color: var(--ds-text-muted);

/* Active/selected state */
color: var(--ds-accent-cyan);
background: rgba(136, 192, 208, 0.15);
```

### Buttons

```css
/* Primary */
background: linear-gradient(135deg, var(--ds-accent-primary), var(--ds-accent-blue));
color: var(--ds-text-primary);

/* Success */
background: var(--ds-semantic-success);
color: var(--ds-bg-base);

/* Danger */
background: var(--ds-semantic-error);
color: var(--ds-text-primary);
```

### Focus States

```css
/* Focus ring */
outline: 2px solid var(--ds-accent-primary);
outline-offset: 2px;

/* Focus glow (inputs) */
box-shadow: 0 0 0 3px rgba(94, 129, 172, 0.25);
```

---

## Accessibility

- **Touch targets**: Minimum 44px (`--ds-touch-target`)
- **Color contrast**: All text colors meet WCAG AA against their intended backgrounds
- **Focus indicators**: Visible focus rings on all interactive elements
- **Reduced motion**: Respect `prefers-reduced-motion` media query

---

## File Ownership

| File | Owner | Notes |
|------|-------|-------|
| `design-system.css` | Design System | Source of truth. Changes here affect entire app. |
| `app.css` | Design System | Legacy layer + global component styles. |
| `*.razor.css` | Component | Scoped styles using `--ds-*` tokens. |

---

## How to Make Changes Safely

1. **Prefer changing tokens** in `design-system.css` first
2. **If updating a primitive**, change it in `app.css` (not scattered component CSS)
3. **Keep changes small** and commit per primitive
4. **Validate on**:
   - Home dashboard
   - At least one modal
   - Login inputs
   - Mobile width check
   - Production build or publish output
5. **No visual regressions**: Dev and Production must render identically after changes

---

## Contributing

1. **New tokens**: Add to `design-system.css` with semantic naming
2. **Component styles**: Use scoped `.razor.css` files with `--ds-*` tokens
3. **Migration work**: Update legacy tokens to `--ds-*` equivalents, verify build, test visually
4. **Validation**: Run through the checklist above before committing
