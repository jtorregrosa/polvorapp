# Layout and design-system audit (2026-10-01)

Status: **resolved** by the OpenSpec change `redesign-design-system` (ADR-0013, mvp #6a): the
findings below were the input for its design, specs and tasks. Kept as the record of why the
design system changed; the current rules are in [README.md](README.md) and
[patterns.md](patterns.md).

Originally: findings only, input for a future OpenSpec change (working name
`refine-page-layout`) to be proposed after `add-arquebusier-photos` is archived and before #6b.
The [second pass](#second-pass-design-system-motion-and-accessibility) widens the scope to a new
design direction and adds accessibility and motion findings.

## Method

- 26 full-page screenshots of the running app with the synthetic seed: desktop 1280 px and mobile
  360 px, light and dark theme; plus the list and the arquebusier and comparsa details at 1920 and
  2560 px. Screens: start, arquebusier list, detail and register form,
  comparsa list and detail, weapon model list and form, user list and detail, account, sign-in.
  Regenerate them with a throwaway Playwright spec that visits each route and calls
  `page.screenshot({ fullPage: true })`. They are not stored in the repository.
- Code read: `frontend/src/styles/` (tokens, global CSS), `frontend/src/components/app/`
  (`PageHeader`, `PageSection`, `FormSection`, form fields, `DataTable`) and the feature pages.
- Checked against the skills `ui-ux-pro-max` (UX rules, design-system generator) and
  `redesign-existing-projects` (audit checklist).

Ignored, because they do not fit an internal records tool used by volunteers:
- the generator's "Trust & Authority + Conversion" landing pattern and clinical-blue palette (it
  read the product as a marketing site);
- grain, glassmorphism, parallax and other marketing-site effects.

A false positive was ruled out. On full-page screenshots the sidebar seems to stop at the first
viewport, but it is `fixed h-svh`, so in real use it stays in place.

## What works and stays

- **Colour tokens.** One brand accent (ember orange), one warm grey family (stone), and semantic
  status tones. Contrast is verified by `contrast.test.ts` and dark mode is consistent. This fits
  the "minimal / Swiss style for professional tools" direction.
- **Sign-in page and arquebusier list.** These are the most finished screens: clear header and
  primary action, filters, and a readable table with status pills.
- **Accessibility foundations.** Visible focus, skip link, reduced motion, forced colours, and
  every page uses composites only (ADR-0009).

## Findings, by priority

### 1. No page-width system (high)

Each page picks its own width, so one screen mixes several:
- the forms use `max-w-xl` (about 576 px);
- "Jefes de disparo" on the comparsa detail uses `max-w-3xl`;
- the "Acciones" card and the arquebusier sections after the form are full width.

At 1280 px the forms leave roughly half the content area empty on the right, while the sections
below them span it all.

### 2. Two section styles mixed (high)

- `FormSection` is a `fieldset` whose `legend` sits on the border as a notch. `PageSection` puts
  its heading inside the card.
- Some forms have no card at all (comparsa, weapon model, user), while the arquebusier form wraps
  every group in one.
- Tables sit inside a bordered table container inside a card ("card in card").
- The "Acciones" card uses the notched legend style for what is a plain section.

### 3. The detail page is a full edit form (high)

- The arquebusier detail has no read view: every value is an editable field.
- It is 3077 px tall on desktop and 3709 px on mobile.
- The status badge floats alone at the top right, far from the name.
- The note "fields marked with * are required" stands alone between the header and the form, on
  every detail and form page.
- The ID photo sits between "Status" and "License", in the middle of the personal data.

### 4. Forms are one column with no sizing rule (medium-high)

- Every text input is full width whatever its content: a 6-digit federation id is as wide as an
  email.
- Selects take the width of their text, so stacked selects form a ragged edge. On the weapon model
  form, Type, Side, Hand and Size all have different widths.
- Help text under almost every field adds noise, and much of it could be shorter or removed.
- The vertical spacing is `gap-4` everywhere, so field groups and sections have no rhythm.

### 5. Button hierarchy is inconsistent (medium-high)

- The submit button spans the whole form width, a mobile pattern used on desktop. Other actions
  use their natural width.
- On the user detail, "Deactivate user" and "Reset two-step verification" are both filled red,
  next to each other. The second one is not destructive in the usual sense.
- Row actions are bare text ("Quitar", "Editar"), while section actions are secondary buttons.
- Long forms have no sticky action bar: on the arquebusier detail, Save is about 2300 px down on
  desktop.

### 6. Flat type hierarchy (medium)

- The page title is `text-2xl` (24 px) semibold, the section title `text-base` (16 px) semibold,
  and labels and body text 14 px. A section title barely differs from a field label.
- IDs and numbers in tables are not tabular (`tabular-nums`).
- Inter is acceptable for this kind of tool. A font change is optional and should be decided on a
  visual comparison.

### 7. Mobile (medium)

- Tables scroll horizontally inside their region. At 360 px only two columns of the arquebusier
  list are visible, so the status and license pills are off screen. Below `md` the rows should
  become a stacked list.
- At 360 px the top bar is crowded: the breadcrumb wraps onto two lines next to the language
  select, the theme button and the user menu.

### 8. Wide monitors (high)

Checked at 1920 × 1080 and 2560 × 1440. The problems in findings 1 to 5 grow with the screen:

- `AppLayout` centres the content in a `max-w-6xl` column (1152 px), while the top bar spans the
  whole window. At 2560 px:
  - the user menu, theme and language controls sit about 1000 px from the content they belong to;
  - the content floats in the middle with wide empty bands on both sides.
- The forms keep their `max-w-xl` inside that column. On the arquebusier and comparsa details at
  2560 px, the fields use about a quarter of the window.
  - The status badge sits at the far right of the column, about 800 px from the name it
    qualifies.
  - The first viewport shows only half of "Datos personales".
- At 2560 px the type stays at 14 px with no scaling, so the screen reads as small text in a large
  empty canvas.
- The list page holds up best (the table fills the column), but the filters still leave most of
  the row empty.

To consider in A: a layout that uses the width instead of centring a fixed column. For example,
a two-column detail (content + aside) and a form grid that adds columns at `xl`/`2xl`. Lists can
take the full width up to a generous maximum. The top bar's controls should stay aligned with the
content area. Check every template at 1280, 1920 and 2560 px.

### 9. Empty start page (low)

The start page is a heading and one sentence. Its dashboard comes with #7
`add-compliance-insights`, so it only needs a better interim empty state.

## Proposal (to be specified in OpenSpec)

Scope: the `design-system` spec and the composites in `components/app/`. No colour or identity
change unless decided separately: ADR-0009 stays, and ADR-0012 stays unless the font changes. Then
update each feature page to the new templates.

- **A. Page templates as composites.** A page container with defined widths:
  - list pages use the full content width with a sensible maximum;
  - detail pages use two columns at `lg`, with the main content on one side and an aside with the
    photo, statuses and actions;
  - forms use a fixed readable width.

  One section style everywhere, without the notched legend. Cards only where grouping needs them,
  never card in card.
- **B. Detail pages in read mode.** A summary header with photo, name, comparsa, status pills and
  the main actions. Sections are shown as description lists, with an "Edit" action per section
  (inline or in a sheet) instead of one long form.
- **C. Form grid.** Two columns from `md`, with a field width that matches the expected content
  (for example `size: 'sm' | 'md' | 'full'`). Selects get consistent widths. Help text appears only
  where it changes what the user enters. The required-fields note goes once, in the form header.
- **D. Actions.**
  - One primary action per view, with its natural width, aligned to the end.
  - A sticky action bar on long forms.
  - One "danger zone" pattern.
  - Filled destructive buttons only for destructive actions.
  - One style for row actions.
- **E. Type scale as tokens.** Distinct page, section and field sizes and weights, a vertical
  spacing rhythm (field < group < section < page), and `tabular-nums` for ids, numbers and tables.
- **F. Responsive data.** `DataTable` shows a stacked row layout below `md`, and the top bar is
  compact on phones (language and theme move into the user menu).
- **G. Optional font change**, judged on a comparison of the same screens.

Suggested next step: a visual comparison of the arquebusier detail in its current layout and two
variants of A–D, built with the real tokens, to choose a direction before writing the change. A
motion review (`design-motion-principles`) is low priority: the app only uses the shadcn/ui
default transitions.

## Second pass: design system, motion and accessibility

Date: 2026-10-01. The maintainer finds the app plain and unattractive and opened the door to a new
design system (colour, type, density, layout, components and motion), within these constraints:
volunteers as users, WCAG 2.2 AA, es-ES / ca-ES-valencia / en, 360 to 2560 px, light and dark,
ADR-0009 kept, and a new identity or font only through an ADR that supersedes ADR-0012.

### Method

- 132 screens captured from the running stack with the synthetic seed: 17 routes (sign-in, start,
  the arquebusier list, detail, register form and owned-weapon form, comparsa list, detail and form,
  weapon model list, detail and form, user list, detail and invitation, account, not found) at 360,
  1280, 1920 and 2560 px, light and dark, full page and first viewport. Same throwaway Playwright
  spec as the first pass, with `reducedMotion: 'reduce'`; nothing is stored in the repository.
- Skills applied: `design-system` (10-dimension score and AI-slop check), `redesign-existing-projects`
  (audit checklist), `accessibility` (WCAG 2.2 criteria), `design-motion-principles` (motion audit)
  and `ui-ux-pro-max` (style, typography and colour search). As in the first pass, the generator read
  the product as a landing page ("Operations Landing", dark slate with a green CTA, Fira Code
  headings); only its "Minimalism & Swiss" style match was kept. Landing-only guidance (hero
  patterns, scroll reveals, GSAP, conversion) was discarded.

### Scores (design-system skill, 0–10)

| Dimension | Score | Why |
|---|---|---|
| Colour consistency | 8 | Clean tokens; primitives bypass them with opacity variants (A1, D1) |
| Typography hierarchy | 5 | 12/14/16/18/24 px with no tokens; `h2` size differs by page |
| Spacing rhythm | 5 | 9 distinct `gap` values, ad-hoc `mb-4`/`mt-6`, `gap-10` wrappers |
| Component consistency | 6 | Filter bars, headings and statuses built differently per page |
| Responsive | 5 | Findings 7 and 8 above |
| Dark mode | 7 | Complete, but see D1 and the loud primary fill |
| Animation | 6 | shadcn defaults only, no tokens (M1–M6) |
| Accessibility | 7 | Strong foundations; focus and hover states fail contrast (A1–A3) |
| Density | 6 | Every name in a list is an accent link: a column of orange competes with the pills |
| Polish | 6 | Hoverable rows that do nothing on click; two styles of empty state |

AI-slop check: no gradients, glass or purple. The fingerprint is "stock shadcn" (Inter, Lucide,
stone greys, `rounded-lg` bordered cards, `shadow-xs`); the ember accent is the only distinctive
element. That matches the maintainer's impression.

### 10. Accessibility (high)

These are defects against NFR-07 today, independent of any redesign; A1–A3 can be fixed as bug
fixes without an OpenSpec change.

- **A1. Focus indicators fail 3:1 (SC 1.4.11).** Primitives draw focus as
  `focus-visible:ring-[3px] ring-ring/50` with `outline-none` (`ui/button.tsx:7`, `ui/input.tsx:11`,
  `ui/native-select.tsx:20`, `ui/checkbox.tsx:11`, `ui/select.tsx:33`): 2.16:1 on the light
  background, 2.28:1 next to a primary button, 2.74–2.90:1 in dark. `contrast.test.ts` only tests
  `ring` at full opacity, and ESLint exempts `components/ui`, so the rendered state is never checked.
- **A2. Primary button hover text is 4.45:1** (white on `hover:bg-primary/90`), just under 4.5:1.
- **A3. Keyboard focus in menus and selects is background only** (`focus:bg-accent` in
  `ui/dropdown-menu.tsx`, `ui/select.tsx`): 1.09:1 against the popover in light, 1.48:1 in dark.
- **A4. WCAG 2.2 is never tested.** axe runs `wcag2a/2aa/21a/21aa` only (`src/test/axe.ts:6`,
  `e2e/fixtures.ts:36`), so `target-size` (tag `wcag22aa`) never runs. NFR-07, the design guide,
  ADR-0009 and the design-system and platform specs still say 2.1 AA; raising them to 2.2 AA is a
  spec change.
- **A5. The photo crop has no single-pointer alternative to dragging (SC 2.5.7).** Moving or
  resizing the crop needs dragging or arrow keys (`PhotoUpload.tsx:454-490`).
- Low: dialog and sheet close buttons are 16 px targets (pass only through the spacing exception);
  `AlertBanner` has no border in forced colours; `focusOnMount` targets hide their focus outline.
- Compliant and to keep: accessible authentication (SC 3.3.8: autocomplete, show password, no
  paste blocking, no CAPTCHA) and the 32 px row actions.

### 11. Component and token gaps (medium)

- **D1. Dark destructive button bypasses its tokens** (`text-white` + `dark:bg-destructive/60`): the
  muddy salmon "Eliminar comparsa". Dark inputs are filled (`bg-input/30`) while light ones are not.
  In dark, the filled ember primary (`#fb923c` across a 576 px submit button) is the loudest element
  on the page.
- **D2. `PageHeader` has no status or meta slot**: the status badge is passed as `actions`, which is
  why it floats at the far right (finding 3).
- **D3. Each page hand-rolls its filter bar**, with different alignment (`items-start`,
  `items-end`, none). A `FilterBar` composite is missing.
- **D4. `h2` sizes differ** (`text-lg` on the account page, `text-base` in the section composites),
  and `AccountPage` builds its own `<dl>`: the seed of a `DescriptionList` composite (proposal B).
- **D5. Page widths are literal classes**: `max-w-xl` 24 times, `max-w-2xl`/`max-w-3xl` one-offs.
- **D6. No elevation, z-index or wrapping tokens**; no `text-wrap: balance`/`pretty`.
- Every name in the arquebusier list is an orange link, and the row hover suggests the row is
  clickable while only the name is.
- The start page is empty at every width; at 1920 px it is a heading in a blank canvas.

Tokens a redesign must add: a semantic type scale (page, section, subsection, body, label, caption,
numeric with `tabular-nums`, plus large-screen steps); four spacing steps (field < group < section
< page) owned by the templates; three elevation levels; row and control height tokens (comfortable
and compact); page widths (`list`, `detail` main + aside, `form`, `prose`); motion tokens; a
full-opacity focus token of at least 2 px and solid hover and pressed tokens, all added to
`contrast.test.ts`.

### 12. Motion (medium)

Inventory: shadcn/tw-animate defaults only. No Framer Motion, View Transitions or toasts.

- **M1. The mobile navigation drawer opens in 500 ms** with `ease-in-out` (`ui/sheet.tsx:55`). It is
  the slowest and most frequent motion in the app. It also carries a stray `transition` class.
- **M2. The desktop sidebar animates `width` and `left` linearly** (`ui/sidebar.tsx:212,225`), also
  on Ctrl+B, which reflows the data table on every frame. Keyboard-triggered changes should not animate.
- **M3. The reduced-motion rule kills fades too**: opacity changes help orientation and are not
  vestibular triggers. View-transition pseudo-elements are not covered.
- **M4. `transition-all` on `Button`** also animates width and padding (for example when the
  pending spinner appears).
- **M5. No duration or easing tokens**: 150, 200, 300 and 500 ms with the built-in `ease`,
  `ease-in-out` and `ease-linear`.
- **M6. Small ones**: a 150 ms fade on table-row hover makes the table feel slow; tooltips have
  `delayDuration=0`; Select adds zoom and slide to a control often opened from the keyboard;
  skeleton rows flash on fast loads (show them after about 150 ms).

Proposed base, whatever the direction:

| Token | Value | Use |
|---|---|---|
| `--duration-instant` | 0 ms | Row hover, keyboard-triggered changes, theme and language switch |
| `--duration-fast` | 100 ms | Colour, focus, button hover; popover exit |
| `--duration-base` | 150 ms | Dropdown, select and tooltip enter; dialog exit |
| `--duration-moderate` | 200 ms | Dialog enter; sheet exit |
| `--duration-slow` | 250 ms | Sheet enter (ceiling) |
| `--ease-out` | `cubic-bezier(0.23, 1, 0.32, 1)` | Enters, popovers, dialogs |
| `--ease-in-out` | `cubic-bezier(0.77, 0, 0.175, 1)` | Movement on screen |
| `--ease-drawer` | `cubic-bezier(0.32, 0.72, 0, 1)` | Sheets and drawers |

Exits run at about 70 % of their enter. Static: route changes, sorting, filtering and paging,
validation errors, layout and width changes, and anything decorative (no hover scale on rows, no
stagger, no pulsing badges). Under reduced motion, remove movement and scale but keep fades of up
to 100 ms; stop `pulse` and `spin` and rely on `aria-busy` and text.

### Design directions

Three directions were drawn on the same three screens (arquebusier list, detail and register form)
at 1440 and 360 px, light and dark, with synthetic data, in a private comparison page for the
maintainer (not stored in the repository). All three keep ADR-0009, ember as the only accent,
semantic status tones separate from it, AA contrast in both themes (checked), and the motion base
above.

| | A · Consola | B · Registro | C · Pólvora |
|---|---|---|---|
| Inspiration | Linear, Vercel dashboard | GOV.UK Design System, Stripe Dashboard forms | Stripe Dashboard, Atlassian |
| Palette | Near-monochrome graphite, ember on the primary action only | White paper, ink, ember buttons, yellow focus band | Cool lavender greys, a "night" sidebar in both themes, ember |
| Type | Inter Variable (kept) + Geist Mono for ids | Atkinson Hyperlegible Next, 16 px base, 19 px labels | Bricolage Grotesque headings + Geist UI + Geist Mono ids |
| Density | Compact (38 px rows, 28–32 px controls; 44 px on touch) | Comfortable (44 px controls, 48 px rows) | Medium (52 px two-line rows, 38–40 px controls) |
| List | Full width, filter chips, saved views | Filter panel with checkboxes, one-third / two-thirds | Counters that filter, comparsa segments, avatar rows |
| Detail | Read view + properties panel, inline edit per section | Summary cards with "Change" per row, actions aside | Header with photo and statuses, key facts, tabs, edit in a side sheet |
| Form | Settings layout (section title left, fields right), sticky bar | One column, widths by content, radios, "(optional)", error summary | Section index, 6-column grid, sticky bar |
| Motion | Near instant; command palette | Almost none; focus is the signal | Tokens above; sheet, tab indicator, press scale, saved notice |
| ADR | Not for the font | Supersedes ADR-0012 (font) | Supersedes ADR-0012 (fonts, sidebar identity) |

Recommendation given to the maintainer: **C, with B's form rules** ("(optional)" instead of
asterisks, field widths by content, radios for short option sets, an error summary). Pending the
maintainer's choice; the OpenSpec change and the ADR follow that choice.
