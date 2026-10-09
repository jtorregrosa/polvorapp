<!-- Generated from the code by frontend/src/styles/design-docs.test.ts: run `npm run docs:design` in `frontend/`. Do not edit by hand. -->

# Design tokens

Colour tokens of `frontend/src/styles/tokens.css` (ADR-0009 §1, ADR-0013). Use them through the
semantic utilities (`bg-primary`, `text-muted-foreground`, `border-input`, `bg-success-soft`…);
raw palette colours, arbitrary values and opacity on semantic tones are rejected by ESLint. The
contrast of every text, state and UI pair is verified by `src/styles/contrast.test.ts` (WCAG 2.2 AA).

| Token | Light | Dark | Use |
|---|---|---|---|
| `background` | `#f3f3f6` | `#0e0c13` | Page background (cool lavender grey / near-black). |
| `foreground` | `#16131f` | `#efedf5` | Body text on `background`. |
| `card` | `#ffffff` | `#17141f` | Cards, tables and panels. |
| `card-foreground` | `#16131f` | `#efedf5` | Text on `card`. |
| `popover` | `#ffffff` | `#1f1b2a` | Menus, dialogs and popovers. |
| `popover-foreground` | `#16131f` | `#efedf5` | Text on `popover`. |
| `surface-2` | `#f7f6fa` | `#1f1b2a` | Second surface: hover of rows and secondary controls, inset panels. |
| `primary` | `#b8430b` | `#ff8a4a` | The single brand accent (ember orange): primary buttons, links, focus ring, current item. |
| `primary-foreground` | `#ffffff` | `#1a0b03` | Text and icons on `primary`. |
| `primary-hover` | `#9e3909` | `#ffa06c` | Hover and pressed state of `primary` (solid, never an opacity). |
| `primary-soft` | `#fff1e8` | `#2d170b` | Tinted surface for non-status UI (selected row, highlighted panel). Never for statuses. |
| `primary-soft-foreground` | `#9a3412` | `#ffb48a` | Text on `primary-soft`. |
| `secondary` | `#ffffff` | `#17141f` | Secondary buttons (outlined surface). |
| `secondary-foreground` | `#16131f` | `#efedf5` | Text on `secondary`. |
| `secondary-hover` | `#efeef4` | `#2a2636` | Hover and pressed state of secondary and ghost buttons. |
| `muted` | `#f4f3f7` | `#221e2d` | Subtle surfaces (skeletons, table headers, neutral pills). |
| `muted-foreground` | `#5e5970` | `#a7a1b8` | Secondary text, help text and placeholders (no opacity). |
| `accent` | `#efeef4` | `#2a2636` | Neutral hover surface of menus and ghost buttons (not the brand accent). |
| `accent-foreground` | `#16131f` | `#efedf5` | Text on `accent`. |
| `success` | `#166534` | `#6ee7a0` | Positive state (valid, validated, active). |
| `success-foreground` | `#ffffff` | `#052e16` | Text on `success`. |
| `success-soft` | `#e7f7ee` | `#0e2a1a` | Status pill background. |
| `success-soft-foreground` | `#166534` | `#6ee7a0` | Status pill text and icon. |
| `warning` | `#854d0e` | `#fcd34d` | Needs attention; compliance warnings (BR-04) are always warnings. |
| `warning-foreground` | `#ffffff` | `#422006` | Text on `warning`. |
| `warning-soft` | `#fdf3d6` | `#2d2208` | Status pill background. |
| `warning-soft-foreground` | `#854d0e` | `#fcd34d` | Status pill text and icon. |
| `destructive` | `#b42318` | `#fca5a5` | Errors and destructive actions (delete, cancel an order). |
| `destructive-foreground` | `#ffffff` | `#450a0a` | Text on `destructive`. |
| `destructive-hover` | `#951c12` | `#fecaca` | Hover and pressed state of `destructive`. |
| `destructive-soft` | `#fdeceb` | `#331213` | Status pill and error banner background. |
| `destructive-soft-foreground` | `#9b1c13` | `#fca5a5` | Status pill and error banner text. |
| `info` | `#1e40af` | `#a5bffd` | Neutral information and in-progress states (submitted, pending). |
| `info-foreground` | `#ffffff` | `#172554` | Text on `info`. |
| `info-soft` | `#eaf0fe` | `#141f3d` | Status pill background. |
| `info-soft-foreground` | `#1e40af` | `#a5bffd` | Status pill text and icon. |
| `tag-1` | `#f1ebfd` | `#251a3d` | Tag background, first categorical tone (violet), e.g. Christian side, trabuco. |
| `tag-1-foreground` | `#5b21b6` | `#c4b5fd` | Tag text on `tag-1`. |
| `tag-2` | `#e1f5f2` | `#0f2a28` | Tag background, second categorical tone (teal), e.g. Admin, arcabuz, rentable. |
| `tag-2-foreground` | `#115e59` | `#5eead4` | Tag text on `tag-2`. |
| `tag-3` | `#fce8f1` | `#331425` | Tag background, third categorical tone (rose), e.g. Moorish side. |
| `tag-3-foreground` | `#9d174d` | `#f9a8d4` | Tag text on `tag-3`. |
| `tag-4` | `#e8eaf2` | `#1e2236` | Tag background, fourth categorical tone (slate-indigo), e.g. FiringChief, pistol. |
| `tag-4-foreground` | `#3f4670` | `#b4bce0` | Tag text on `tag-4`. |
| `tag-neutral` | `#f4f3f7` | `#221e2d` | Tag background for "no", "none" and unknown values (same as `muted`). |
| `tag-neutral-foreground` | `#5e5970` | `#a7a1b8` | Tag text on `tag-neutral`. |
| `chart-1` | `#b8430b` | `#ff8a4a` | First chart series (ember), with its own pattern; 3:1 on `card` and `background`. |
| `chart-2` | `#1f6fb2` | `#6cb4f5` | Second chart series (blue), with its own pattern; 3:1 on `card` and `background`. |
| `chart-3` | `#0f766e` | `#4fd1b5` | Third chart series (teal), with its own pattern; 3:1 on `card` and `background`. |
| `chart-4` | `#7c3aed` | `#b79cfa` | Fourth chart series (violet), with its own pattern; 3:1 on `card` and `background`. |
| `chart-5` | `#a16207` | `#f2c14e` | Fifth chart series (ochre), with its own pattern; 3:1 on `card` and `background`. |
| `border` | `#e4e2ea` | `#2a2636` | Hairline borders of cards, tables and separators (decorative). |
| `input` | `#8b8799` | `#7a748c` | Borders of form controls (3:1 against the background). |
| `ring` | `#b8430b` | `#ff8a4a` | Focus indicator (3:1 against every surface). |
| `sidebar` | `#191524` | `#0a0910` | Night sidebar background, dark in both themes (ADR-0013). |
| `sidebar-foreground` | `#ebe8f3` | `#ebe8f3` | Sidebar text of the current and hovered items, and the brand. |
| `sidebar-muted-foreground` | `#a9a3bb` | `#a9a3bb` | Sidebar text of the other items, group titles and the user line. |
| `sidebar-primary` | `#ff8a4a` | `#ff8a4a` | Ember on the night surface: current-page bar and icon. |
| `sidebar-primary-foreground` | `#1a0b03` | `#1a0b03` | Text on `sidebar-primary`. |
| `sidebar-accent` | `#262038` | `#1d1928` | Hovered and current navigation item. |
| `sidebar-accent-foreground` | `#ebe8f3` | `#ebe8f3` | Text on `sidebar-accent`. |
| `sidebar-border` | `#2e2842` | `#221d30` | Sidebar separators. |
| `sidebar-ring` | `#ff8a4a` | `#ff8a4a` | Focus indicator inside the sidebar. |
| `sidebar-armband` | `#f5c518` | `#f5c518` | The yellow of the FiringChief armband at the bottom of the sidebar. Insignia only: never an accent or a status. |
| `sidebar-armband-foreground` | `#1f1a05` | `#1f1a05` | Armband text on `sidebar-armband`. Insignia only. |
| `sidebar-armband-edge` | `#b8890a` | `#b8890a` | The armband's hemmed top and bottom edges. Insignia only. |
| `logo-tile` | `#f7f6fa` | `#f7f6fa` | Light tile behind comparsa logos, in both themes, so dark logos stay visible (`ComparsaLogo`). |
| `logo-tile-foreground` | `#5e5970` | `#5e5970` | Placeholder icon on `logo-tile` when a comparsa has no logo. |

## Type, spacing, sizes, radius, elevation, widths and easing

Fonts are self-hosted from `@fontsource-variable` (OFL-1.1, ADR-0013). Type sizes are roles, not
steps: use `text-page`, `text-label`… (the utility also sets the line height and weight). Use the
named spacing (`gap-group`, `px-gutter`, `h-control`) for the layout rhythm; the default Tailwind
scale stays available for small inner gaps. The elevation tokens change with the theme.

| Token | Value | Use |
|---|---|---|
| `font-sans` | `'Geist Variable', ui-sans-serif, system-ui, sans-serif` | Interface text (Geist). |
| `font-display` | `'Bricolage Grotesque Variable', ui-sans-serif, system-ui, sans-serif` | Page titles, record names and key figures (Bricolage Grotesque). |
| `font-mono` | `'Geist Mono Variable', ui-monospace, 'Cascadia Mono', Consolas, monospace` | Identifiers: nationalId, federationId, guide numbers (Geist Mono). |
| `text-page` | `1.75rem · 2rem from 1700 px · 1.15 · 700` | Page title (`h1`), with `font-display`. |
| `text-record` | `1.625rem · 1.875rem from 1700 px · 1.15 · 700` | Record name in a record header, with `font-display`. |
| `text-section` | `1rem · 1.0625rem from 1700 px · 1.3 · 600` | Section and card titles. |
| `text-figure` | `1.75rem · 2rem from 1700 px · 1.1 · 700` | Key figures and counters, with `font-display`. |
| `text-body` | `0.9375rem · 1rem from 1700 px · 1.55` | Body text, table cells and controls. |
| `text-label` | `0.875rem · 0.9375rem from 1700 px · 1.35 · 500` | Field labels, buttons and column headers. |
| `text-help` | `0.8125rem · 0.875rem from 1700 px · 1.45` | Help text, errors, secondary lines and captions. |
| `text-id` | `0.8125rem · 0.875rem from 1700 px · 1.45` | Identifiers, with `font-mono`. |
| `spacing-field` | `0.375rem` | Label → help → error → control inside one field. |
| `spacing-group` | `1.25rem` | Between fields, and between items of a group. |
| `spacing-section` | `1.5rem` | Between sections and cards. |
| `spacing-region` | `2rem` | Between the page header and its content, and between page regions. |
| `spacing-gutter` | `1rem` | Page side margins (16 px on phones, 28 px from 768 px). |
| `spacing-control` | `2.5rem` | Height of buttons, inputs and selects (40 px, 44 px on touch screens). |
| `spacing-row` | `3.25rem` | Height of a table row. |
| `spacing-topbar` | `3.75rem` | Height of the top bar. |
| `spacing-action-bar` | `4.5rem` | Height of the sticky form action bar, and the scroll padding that keeps focus above it. |
| `spacing-bottom-bar` | `4rem` | Height of a FiringChief's bottom navigation on phones, the room kept under the page and the scroll padding that keeps focus above it. |
| `radius-sm` | `0.375rem` | Badges, checkboxes and small chips. |
| `radius-md` | `0.5625rem` | Buttons, inputs, menus and items. |
| `radius-lg` | `0.875rem` | Cards, sections, dialogs and panels. |
| `radius-xl` | `1.125rem` | Large surfaces: public card, bottom sheet. |
| `shadow-e1` | `0 1px 2px rgb(22 19 31 / 0.05), 0 1px 1px rgb(22 19 31 / 0.03)` | Elevation 1: cards and buttons (a hairline in the dark theme). |
| `shadow-e2` | `0 12px 32px -10px rgb(22 19 31 / 0.28), 0 2px 6px rgb(22 19 31 / 0.06)` | Elevation 2: menus, pickers, dialogs and panels. |
| `container-page` | `105rem` | Maximum content width (1680 px). |
| `container-form` | `53.75rem` | Maximum form width (860 px). |
| `container-prose` | `40rem` | Maximum width of running text (640 px). |
| `container-field-id` | `12rem` | Width of identifier fields: nationalId, federationId, codes (`FormField width="id"`). |
| `container-field-short` | `14rem` | Width of dates, phone numbers and numbers (`width="short"`). |
| `container-field-name` | `22rem` | Width of names and selects of names (`width="name"`). |
| `container-field-long` | `30rem` | Width of email addresses and long names (`width="long"`). |
| `breakpoint-wide` | `106.25rem` | Wide screens (1700 px, `wide:`): help column of forms, three-column sections, larger type. |
| `ease-out` | `cubic-bezier(0.23, 1, 0.32, 1)` | Entering and state changes: fast start, soft landing. |
| `ease-drawer` | `cubic-bezier(0.32, 0.72, 0, 1)` | Side panels, bottom sheets and the navigation drawer. |

## Motion

Only opacity and transforms are animated, never layout. Leaving is shorter than appearing. Under
reduced motion, movement and scaling are removed and fades last at most `--duration-fast`.

| Token | Value | Use |
|---|---|---|
| `--duration-fast` | `100ms` | Colour, border and press feedback; every fade under reduced motion (at most). |
| `--duration-base` | `150ms` | Menus, pickers and tooltips appearing; dialogs leaving. |
| `--duration-moderate` | `200ms` | Dialogs appearing; panels leaving. |
| `--duration-slow` | `250ms` | Side panels, bottom sheets and the navigation drawer appearing. |
