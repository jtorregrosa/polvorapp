# Design

## Context

See `proposal.md` (Why) and `docs/design/layout-audit.md` (findings 1–12, the three directions and
the chosen one). The private demo of "Pólvora with Registro's forms" is the visual reference. It
was reviewed by the maintainer, who asked for two corrections: radio dots must stay round, and the
select list must follow the menu style.

The current state that shapes the approach:

- **Frontend only.** React 19, Tailwind v4, shadcn/ui primitives on the `radix-ui` package
  (Tabs included, so no new UI dependency), React Hook Form + Zod, react-i18next. Feature screens
  use only `src/components/app/`; ESLint enforces it and rejects arbitrary values, raw palette
  colours and `style` attributes (ADR-0009).
- **24 pages**: 4 registry, 6 catalogue, 11 identity (5 of them public), 3 platform.
- **Primitives** with defects to fix: `button`, `input`, `native-select`, `checkbox` and `select`
  use `ring-ring/50` focus; `dropdown-menu` and `select` show focus with `focus:bg-accent` only;
  `sheet` opens in 500 ms; `sidebar` animates `width`; the dark `button` destructive variant uses
  `text-white` + `bg-destructive/60`.
- **Data**: `GET /api/arquebusiers` returns every row in the user's scope (filters only by
  `comparsaId` and `status`), and each row carries `licenseStatus` and `licenseExpiresOn`. So the
  counters and license filters can be computed on the client. `PUT /api/arquebusiers/{id}` takes the
  whole record plus `version` (optimistic concurrency). The comparsa, weapon model and user updates
  take the whole record without a `version` (the last save wins, as today); this change does not
  add one.
- **Axe** runs with `wcag2a/2aa/21a/21aa` in Vitest (`src/test/axe.ts`), Storybook (`a11y.test:
  'error'`) and Playwright (`e2e/fixtures.ts`).
- **CSP**: `font-src` falls back to `'self'`, `img-src 'self' data: blob:`, `style-src 'self'
  'unsafe-inline'`.

## Goals / Non-Goals

**Goals:**
- One token source that covers type, spacing, elevation, widths and motion as well as colour, with
  the state pairs checked in `contrast.test.ts`.
- Fix the accessibility defects at their root: the primitives and the tokens, not each page.
- Templates expressed as composites, so a later change (#6b, #7…) builds a page from them without
  layout code.
- Keep every API, permission, validation rule and audit behaviour as it is.

**Non-Goals:**
- No route changes: the owned-weapon add and edit pages stay pages, restyled to the form template.
- No new state library, animation library or component framework.
- No server-side change: the "expiring soon" threshold, global search and audit history are
  deferred (see proposal Non-goals).

## Decisions

### D1. Fonts: self-hosted variable fonts from `@fontsource-variable`
Bricolage Grotesque (display), Geist (text) and Geist Mono (identifiers), all OFL-1.1, version
5.3.0. They are imported in `globals.css` like Inter today, with the Latin and Latin Extended
subsets (Valencian `à`, `ç`, `l·l`) and the default `font-display: swap`. Inter is removed.

- **Why**: the CSP needs no change, there are no third-party requests (GDPR) and the fonts are
  versioned with the app.
- *Alternatives*: Google Fonts (a third-party request and a CSP change); keeping Inter (rejected in
  the comparison as the stock look); a system font stack (no identity).

### D2. Tokens: CSS variables in `tokens.css`, exposed through Tailwind v4 theme namespaces
- **Colour**: the palette of ADR-0013, plus state tokens (`primary-hover`, `secondary-hover`,
  `destructive-hover`, `surface-2`). The night sidebar uses shadcn's `sidebar-*` tokens with night
  values in both themes, plus `sidebar-muted-foreground`, so the `sidebar` primitive needs no change
  to pick it up.
- **Type**: `--text-page`, `--text-record`, `--text-section`, `--text-figure`, `--text-body`,
  `--text-label`, `--text-help` and `--text-id`, with their line heights and weights. Their sizes
  come from `--type-*` variables that a media query at 1700 px switches to the larger steps.
- **Space**: `field` 6, `group` 20, `section` 24, `region` 32 px; page margins (`gutter`) 28 px, and
  16 px on phones.
- **Sizes**: controls 40 (44 on coarse pointers), rows 52, top bar 60, action bar 72; radius 9 / 14.
- **Elevation**: `e1` and `e2` (no shadow is the base level). **Widths**: `page` 1680, `form` 860,
  `prose` 640.
- **Motion**: durations 100 / 150 / 200 / 250 ms and the `ease-out` and `ease-drawer` curves.

The exact Tailwind v4 namespace for each group (`--text-*`, `--spacing-*`, `--shadow-*`,
`--container-*`, `--ease-*`, `--animate-*`; durations as plain variables) is checked in Context7
before writing it. The ESLint rule that rejects arbitrary values stays: tokens are the only way to
get these values.

- *Alternative*: a TS token module shared with JS. Rejected, because CSS variables are already the
  source and `design-docs.test.ts` generates `tokens.md` from them.

### D3. Accessibility fixes live in the primitives, guarded by tests
These are local edits in `components/ui/`, each marked `Local edit` as the guide requires:

- Focus is `focus-visible:outline-2 outline-offset-2 outline-ring` at full opacity on every
  control. Menu and select items get the same ring inside the item, not only a background.
- Hover and pressed states use the solid `*-hover` tokens instead of `/90`.
- The dark destructive button uses the `destructive` and `destructive-foreground` pair; dark inputs
  keep the light inputs' transparent background.
- The dialog and sheet close buttons become at least 24 × 24 px (size-9 with the icon centred).
- `AlertBanner` gets a border, visible in forced colours.

A new `primitives-guard.test.ts` reads `components/ui/*.tsx` and fails on opacity modifiers on
`ring`, `primary`, `destructive` or `input` (for example `ring-ring/50`), because ESLint exempts that
folder. `contrast.test.ts` gains the state pairs and the night sidebar pairs.

### D4. WCAG 2.2 AA in every automated check
- Axe tags become `['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']` in `src/test/axe.ts`,
  the Storybook a11y parameters (`options.runOnly`) and `e2e/fixtures.ts`. This brings in
  `target-size`.
- SC 2.4.11 (focus not obscured) with the sticky `ActionBar`: the scroll container gets
  `scroll-padding-block-end` equal to the bar height (a token). A Playwright check tabs through
  the register form and asserts that each focused field's box is above the bar.
- SC 2.5.7 (dragging): `PhotoUpload`'s crop dialog adds buttons that move the crop by 5 % of the
  image and resize it by 5 %, keeping the aspect ratio. The existing arrow-key support stays.
- SC 3.3.8 (accessible authentication): already compliant. A Playwright check keeps paste and
  `autocomplete` on the sign-in fields.

### D5. Motion through tokens, set in the primitives
- The `sheet` primitive enters in 250 ms with `ease-drawer` and leaves in 200 ms. The dialog and
  alert dialog enter in 200 ms (fade + `scale(.96)`) and leave in 150 ms (fade). Dropdown, select
  and tooltip enter in 150 ms (fade + 4 px slide, no zoom).
- The sidebar no longer animates `width` or `left`. Table rows lose `transition-colors`, and
  `Button` animates only colour, background, border, shadow and transform (`scale(.98)` on press).
- The tooltip delay becomes 500 ms. Skeleton rows appear after 150 ms.
- `globals.css` reduced-motion block: transforms are removed, fades are capped at 100 ms, and
  `animate-pulse` and `animate-spin` stop (`aria-busy` and the button text still say it is busy).

The tw-animate-css variables (`--tw-enter-*`) carry the reduced-motion overrides. This follows the
motion audit (findings M1–M6).

### D6. Shell
- `AppLayout`:
  - the sidebar gets the `night` tokens;
  - the top bar holds the drawer trigger, the breadcrumbs and `UserMenu`;
  - `main` holds a `PageContainer` with `max-w-page` (1680), with the top bar's inner row aligned
    to the same edges;
  - `LanguageSwitcher` and `ThemeSwitcher` become menu groups inside `UserMenu` (radio items).
- `PublicLayout` keeps its visible switchers and gets the new card and typography.
- The breadcrumbs stay, because the platform spec requires them. The demo's top-bar search is
  deferred.

### D7. Templates as composites
New composites, each with stories and tests:

- **List**:
  - `PageHeader` (unchanged API, no required note);
  - `FilterBar` (segments, search and a live result count);
  - `StatFilter` (a group of toggle buttons with `aria-pressed`, a count and a hint);
  - `DataTable` gains `secondary` cell lines, `getRowHref` (whole-row click, the name stays the
    only link and the only tab stop) and `mobileRow` (stacked item below `md`).
- **Detail**:
  - `RecordHeader` (photo slot, context line, name, status slot, actions, `moreActions` menu items);
  - `KeyFacts` (with an optional `Meter`);
  - `Tabs` (Radix Tabs with the sliding indicator);
  - `SectionGrid` (12-column grid, from 1700 px in three columns);
  - `SectionCard` (title, description, edit action, body);
  - `DescriptionList` (shows "Not given" for empty values);
  - `EditSheet` (see D9).
- **Form**:
  - `FormLayout` (section index at ≥1280 px, help column at ≥1700 px, `ActionBar` slot);
  - `ActionBar` (sticky, primary at the end);
  - `ErrorSummary`;
  - `RadioCards`;
  - `FormField` gains `optional` and `width: 'id' | 'short' | 'name' | 'long' | 'full'` and loses
    the required asterisk.
- **Feedback**: `SaveNotice`, a polite `role="status"` region mounted once by the shell, with an
  imperative `notify(text)` via context. `AlertBanner` stays for persistent messages.

`PageSection` and `FormSection` are removed at the end of the change, once no page uses them.
`StatCard` stays for the future dashboard (#7).

- *Alternative*: page-level layout components owning data fetching. Rejected, because composites
  stay presentational and pages keep their hooks.

### D8. Form behaviour with React Hook Form
- Mode `onSubmit`, `reValidateMode: 'onChange'` (the current rule) and `shouldFocusError: false`.
- On an invalid submit, `Form` renders `ErrorSummary` from `formState.errors`, in field order, and
  focuses it.
- Summary links point at `#<fieldId>`. On activation they focus the control (the first radio for
  radio groups) and scroll it into view, which respects the scroll padding of D4.
- Field errors keep `aria-describedby` and `aria-invalid`. The bar is a `box-shadow` inset plus the
  message, so it is not colour alone.
- The "all fields are required except optional ones" sentence comes from `ui.form.requiredNote`
  (new text) and is rendered once by `Form`.
- `RadioCards` uses Radix RadioGroup through the `radio-group` primitive. If the primitive is not
  yet in `components/ui`, it is added with the shadcn CLI. The card is the label, the dot is drawn
  with an inset shadow and its box has a fixed `aspect-ratio`, so it never turns oval (reviewed in
  the demo).

### D9. Section editing in `EditSheet`
- An `EditSheet` hosts a `Form` whose schema is the section's slice of the feature schema
  (`schema.pick(...)`). So a stored value outside the section can never block the save.
- On submit, the page merges the section values into the loaded record and sends the full
  `PUT ... { ..., version }` request, as the detail form does today (without `version` for the
  comparsa, weapon model and user, whose APIs have none).
- On success: close, invalidate the query, `notify(t('…saved'))` and return focus to the trigger
  (Radix Dialog does it).
- On `409`/version conflict: keep the sheet open, show the existing `form.modified` reason in the
  summary area, and reset the section's fields to the server values.
- Escape and Cancel discard the values. With unsaved changes, closing asks nothing: the values are
  short and the record is unchanged, so no data is lost.
- On phones the sheet is `side="bottom"`, at most 90 svh, and scrolls inside.
- Sections:
  - **Arquebusier**: personal data, license (type, pending, dates; the photos stay managed in place
    with `PhotoUpload`), course.
  - **Comparsa**: name and side. Assignments stay inline in their section.
  - **Weapon model**: its attributes.
  - **User**: name, role and email language.
  - **Account**: no sheet. Its existing flows (password, two-step verification, language) are
    restyled as sections.

### D10. Arquebusier list counters on the client
`StatFilter` counts from the rows already loaded for the user's scope (BR-12 holds, because the
server only returns rows in scope). The filters combine (AND) with the comparsa segment and the
search. The result count is a polite live region in `FilterBar`. No API change.

### D11. Styled select as progressive enhancement
In the `native-select` primitive:
- `@supports (appearance: base-select)` gives `select` and `::picker(select)` the menu tokens;
- `option::checkmark` uses the accent;
- `select::picker-icon` turns on `:open`;
- `@starting-style` adds a 150 ms fade.

The placeholder option is hidden from the list only when `base-select` applies. The native picker
remains elsewhere, so phones keep the system picker.

- *Alternative*: Radix Select. Rejected, because it loses native mobile pickers and form autofill
  and adds JS for an equal result where it matters.

### D12. i18n
New keys in the three locales (es-ES, ca-ES-valencia, en):
- `ui`: `form.requiredNote` (replaces the asterisk note), `form.errorSummaryTitle`,
  `detail.edit`, `detail.moreActions`, `detail.notGiven`, `detail.saved`, `tabs.*`, `userMenu.*`
  (theme and language groups) and the crop button labels;
- `registry`: `counters.*`, `keyFacts.*`, `detail.sections.*` and `list.resultCount_*`.

`npm run check-i18n` guards completeness. Valencian copy needs no native review (maintainer
decision); a story at 360 px with Valencian labels checks the wrapping.

### D13. Security, privacy and audit
No API, data or permission changes. Scoping stays on the server (BR-12), and every write still
goes through the same endpoints, so audit logging is unchanged (SEC-05). Personal data shown in
`SaveNotice` and toasts stays in the DOM of an authorised user and is never logged. Fonts are
self-hosted (no third-party request), and the checkbox tick SVG is a `data:` URI already allowed
by `img-src`. The CSP is unchanged (design D10 of `add-design-system`).

## Research notes (task 1.1)

Checked with Context7 and the installed packages (`package.json` versions):

- **Tailwind CSS 4.3.3** `@theme` namespaces:
  - `--text-<name>` with `--text-<name>--line-height`, `--letter-spacing` and `--font-weight`
    sub-tokens gives one `text-<name>` utility per step;
  - `--spacing-<name>` gives named spacing and sizing utilities (`gap-field`, `p-page`, `h-control`);
  - `--shadow-<name>`, `--radius-<name>` and `--container-<name>` (`max-w-page`, `@page:`) work the
    same way, and `--ease-<name>` gives `ease-<name>`;
  - there is no duration namespace: `duration-<number>` is a bare-value utility (`duration-250`), so
    the duration tokens are documented values used through those utilities, and plain `:root`
    variables (`--duration-*`) for the hand-written CSS (select picker, reduced motion).
- **tw-animate-css 1.4.0**: `animate-in`/`animate-out` read `--tw-duration` and `--tw-ease`, which
  Tailwind's `duration-*` and `ease-*` utilities set, so `duration-250 ease-drawer` drives the
  enter and exit keyframes. Transforms come from `--tw-enter-translate-*`, `--tw-enter-scale` and
  the `--tw-exit-*` pair; the reduced-motion block resets these variables.
- **radix-ui 1.6.7** exports `Tabs` and `RadioGroup`; `Dialog` is already used by the `sheet`
  primitive, which takes `side="bottom"`. The shadcn/ui `radio-group` primitive wraps
  `RadioGroup.Root/Item/Indicator`.
- **React Hook Form 7.89**: `useForm({ mode: 'onSubmit', reValidateMode: 'onChange',
  shouldFocusError: false })`, `handleSubmit(onValid, onInvalid)` and `setFocus(name)`.
- **CSS customizable select** (MDN): `appearance: base-select` on `select` and `::picker(select)`,
  `::picker-icon`, `option::checkmark`, the `:open` pseudo-class and `@starting-style`; Chromium
  only today, behind `@supports (appearance: base-select)`.
- **axe-core 4.13.0**: the `wcag22aa` tag includes `target-size`. jsdom has no layout, so
  `target-size` and contrast are only measured by the Playwright checks; the Vitest run
  (`src/test/axe.ts`, used by `catalogue.test.tsx` for every story) still gets the other 2.2 rules.
- **Storybook 10.6 addon-a11y**: `parameters.a11y.options` is passed to `axe.run`, so
  `options.runOnly = { type: 'tag', values: [...] }` sets the tags for the Storybook panel.

No workaround is needed.

## Risks / Trade-offs

- **[Large diff touching every page]** → Task groups go primitives → composites → shell → one
  capability's pages at a time, keeping the build and E2E green at the end of each group. The old
  `PageSection` and `FormSection` live until the last page moves.
- **[E2E suites written against the edit-form detail]** → Each capability group updates its
  Playwright specs in the same group. Selectors stay role- and name-based.
- **[`base-select` only in Chromium today]** → Progressive enhancement: other browsers get the
  native list. Covered by a Firefox/WebKit Playwright smoke run on the register form.
- **[Section schemas drift from the register schema]** → Sections are `pick`s of the one feature
  schema. A unit test checks that every field of the full schema belongs to exactly one section.
- **[Font weight on slow phones]** → Variable fonts at about 3 × 40 KB (Latin + Latin Extended
  WOFF2) replace Inter's about 40 KB. `font-display: swap` keeps text visible meanwhile.
- **[Night sidebar contrast in both themes]** → The night pairs are added to `contrast.test.ts`, and
  axe checks the shell in both themes.
- **[Users used to the old detail form]** → Small user base, rolled out before go-live; the manual
  regression before go-live (`docs/mvp.md`) covers the new flows.

## Migration Plan

Frontend-only release, shipped as one pull request per the workflow (`feat/redesign-design-system`).
No data migration. Rollback is reverting the merge. Storybook and the generated `tokens.md` and
`status.md` are rebuilt in CI.
