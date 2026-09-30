<!-- Generated from the code by frontend/src/styles/design-docs.test.ts: run `npm run docs:design` in `frontend/`. Do not edit by hand. -->

# Design tokens

Colour tokens of `frontend/src/styles/tokens.css` (ADR-0009 §1, ADR-0012). Use them through the
semantic utilities (`bg-primary`, `text-muted-foreground`, `border-input`, `bg-success-soft`…);
raw palette colours, arbitrary values and opacity on semantic tones are rejected by ESLint. The
contrast of every text and UI pair is verified by `src/styles/contrast.test.ts` (WCAG 2.1 AA).

| Token | Light | Dark | Use |
|---|---|---|---|
| `background` | `#faf9f7` | `#1c1917` | Page background (warm off-white / warm near-black). |
| `foreground` | `#1c1917` | `#fafaf9` | Body text on `background`. |
| `card` | `#ffffff` | `#292524` | Cards, tables and panels. |
| `card-foreground` | `#1c1917` | `#fafaf9` | Text on `card`. |
| `popover` | `#ffffff` | `#292524` | Menus, dialogs and popovers. |
| `popover-foreground` | `#1c1917` | `#fafaf9` | Text on `popover`. |
| `primary` | `#c2410c` | `#fb923c` | The single brand accent (ember orange): primary buttons, links, focus ring, current item. |
| `primary-foreground` | `#ffffff` | `#1c1917` | Text and icons on `primary`. |
| `primary-soft` | `#ffedd5` | `#431407` | Tinted surface for non-status UI (selected row, highlighted panel). Never for statuses. |
| `primary-soft-foreground` | `#9a3412` | `#fdba74` | Text on `primary-soft`. |
| `secondary` | `#f5f5f4` | `#292524` | Secondary buttons. |
| `secondary-foreground` | `#1c1917` | `#fafaf9` | Text on `secondary`. |
| `muted` | `#f5f5f4` | `#292524` | Subtle surfaces (skeletons, table headers, neutral pills). |
| `muted-foreground` | `#57534e` | `#b8b2ad` | Secondary text, help text and placeholders (no opacity). |
| `accent` | `#f5f5f4` | `#44403c` | Neutral hover surface of menus and ghost buttons (not the brand accent). |
| `accent-foreground` | `#1c1917` | `#fafaf9` | Text on `accent`. |
| `success` | `#15803d` | `#4ade80` | Positive state (valid, validated, active). |
| `success-foreground` | `#ffffff` | `#052e16` | Text on `success`. |
| `success-soft` | `#dcfce7` | `#052e16` | Status pill background. |
| `success-soft-foreground` | `#166534` | `#86efac` | Status pill text and icon. |
| `warning` | `#a16207` | `#facc15` | Needs attention; compliance warnings (BR-04) are always warnings. |
| `warning-foreground` | `#ffffff` | `#422006` | Text on `warning`. |
| `warning-soft` | `#fef9c3` | `#422006` | Status pill background. |
| `warning-soft-foreground` | `#854d0e` | `#fde047` | Status pill text and icon. |
| `destructive` | `#b91c1c` | `#f87171` | Errors and destructive actions (delete, cancel an order). |
| `destructive-foreground` | `#ffffff` | `#450a0a` | Text on `destructive`. |
| `destructive-soft` | `#fee2e2` | `#450a0a` | Status pill and error banner background. |
| `destructive-soft-foreground` | `#991b1b` | `#fca5a5` | Status pill and error banner text. |
| `info` | `#1d4ed8` | `#60a5fa` | Neutral information and in-progress states (submitted, pending). |
| `info-foreground` | `#ffffff` | `#172554` | Text on `info`. |
| `info-soft` | `#dbeafe` | `#172554` | Status pill background. |
| `info-soft-foreground` | `#1e40af` | `#93c5fd` | Status pill text and icon. |
| `border` | `#e7e5e4` | `#44403c` | Hairline borders of cards, tables and separators (decorative). |
| `input` | `#8a827c` | `#8a827c` | Borders of form controls (3:1 against the background). |
| `ring` | `#c2410c` | `#fb923c` | Focus indicator (3:1 against every surface). |
| `sidebar` | `#f5f5f4` | `#1c1917` | Sidebar background. |
| `sidebar-foreground` | `#1c1917` | `#fafaf9` | Sidebar text. |
| `sidebar-primary` | `#c2410c` | `#fb923c` | Current-page bar in the navigation. |
| `sidebar-primary-foreground` | `#ffffff` | `#1c1917` | Text on `sidebar-primary`. |
| `sidebar-accent` | `#e7e5e4` | `#292524` | Hovered and current navigation item. |
| `sidebar-accent-foreground` | `#1c1917` | `#fafaf9` | Text on `sidebar-accent`. |
| `sidebar-border` | `#e7e5e4` | `#44403c` | Sidebar separators. |
| `sidebar-ring` | `#c2410c` | `#fb923c` | Focus indicator inside the sidebar. |

## Type, radius and spacing

- Font: **Inter Variable**, self-hosted (`@fontsource-variable/inter`, OFL-1.1); `font-sans`.
- Radius: `--radius` = 0.5rem; `rounded-sm`, `rounded-md`, `rounded-lg`, `rounded-xl` derive from it.
- Spacing, sizes and type scale: the default Tailwind scale; no arbitrary values.
