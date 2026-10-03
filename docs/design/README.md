# PolvorApp — UI design guide

Required reading for any UI change. It explains how screens are built with the design system
decided in [ADR-0009](../adr/0009-design-system-shadcn-tailwind.md) and the visual identity of
[ADR-0013](../adr/0013-polvora-visual-identity.md).

| Page                       | Content                                                                                                                    |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| [tokens.md](tokens.md)     | Colour tokens (light and dark), type roles, spacing, sizes, radius, elevation, widths and motion — generated from the code |
| [status.md](status.md)     | Domain statuses → tone, icon and label in the three languages — generated from the code                                    |
| [patterns.md](patterns.md) | Page templates, forms and validation, tables, destructive actions, empty and loading states, responsive rules              |
| [copy.md](copy.md)         | Microcopy tone in es-ES, ca-ES-valencia and en                                                                             |

The component catalogue is Storybook: `npm run storybook` in `frontend/` (toolbar: language and
theme). Every composite has stories there.

## Identity

- **PolvorApp's own identity ("Pólvora", ADR-0013)**, not the Federation's. Structure in the
  spirit of Stripe Dashboard and Atlassian, with its own character: a dark **night sidebar** in
  both themes, cool lavender-grey surfaces, cards and tables with hairline borders and two
  elevation levels, coloured status pills, one accent colour.
- **Accent**: ember (`primary`, "pólvora"; brighter on the night sidebar and in the dark theme).
  It marks the primary action, links, the focus indicator, the current navigation item and
  selections — nothing else. Status colours are separate tones.
- **Type**: Bricolage Grotesque for page titles, record names and key figures (`font-display`),
  Geist for the interface (`font-sans`), Geist Mono for identifiers (`font-mono`). All OFL-1.1
  and self-hosted. Sizes are roles (`text-page`, `text-label`…) that step up from 1700 px.
- **Restraint**: one display face for titles and figures only, at most one accent, no gradients
  except the mark, motion only through the motion tokens.
- **Mark**: an original "P" monogram with an ember spark (`PolvorAppMark`, `frontend/public/icon.svg`).
- **Third-party brand assets are never committed.** A customer logo (e.g. the Federation's) can
  only be supplied at deployment time.
- Light and dark themes, plus "system". The first paint already has the right theme
  (`public/theme-init.js`).

## Building a screen

1. Feature screens (`src/features/**`) and the shell (`src/app/**`) use **composites** from
   `@/components/app` only. Primitives (`@/components/ui`, shadcn/ui on Radix) are building blocks
   for composites; ESLint rejects importing them from features.
2. Style with **semantic token utilities** (`bg-card`, `text-muted-foreground`, `border-input`,
   `bg-warning-soft`…). ESLint rejects raw palette colours (`bg-orange-600`), arbitrary values
   (`w-[13px]`), opacity on semantic tones (`bg-primary/50`) and `style` attributes.
3. Every user-facing text comes from translations (`useTranslation`); composites receive
   already-translated strings. No literal text in JSX.
4. Pick a page template from [patterns.md](patterns.md) and add a story when you add a composite.

### Composites

**Shell and pages**

| Composite                           | Use                                                                                                                                                                                                                                                                                                                                          |
| ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AppLayout`                         | Shell: skip link; the night sidebar (mark, optional link cards under it via `sidebarCards`, e.g. a FiringChief's comparsas, navigation with icons, counters (`count`, with `countLabel` saying what it counts in the link's accessible name, e.g. "5 with warnings") and the ember current-page bar, footer); a sticky top bar (drawer trigger, breadcrumbs, user menu) aligned with the content; main content up to 1680 px; the `SaveNotice` region |
| `PublicLayout`                      | Pages before signing in: mark, language and theme switchers, a centred card and the version                                                                                                                                                                                                                                                  |
| `UserMenu`                          | The signed-in user in the top bar: name, role, the language and theme switchers (radio groups), the account page, sign out                                                                                                                                                                                                                   |
| `LanguageSwitcher`, `ThemeSwitcher` | The switchers of `PublicLayout` (signed in, they are in `UserMenu`)                                                                                                                                                                                                                                                                          |
| `Breadcrumbs`                       | Trail built from the `breadcrumb` handle of the matched routes                                                                                                                                                                                                                                                                               |
| `PageHeader`, `BackLink`            | The page's only `h1` (display face), description, actions, optional back link (`BackLink`, also above a `RecordHeader`); `focusOnMount` for a view that replaces the page after an action                                                                                                                                                    |
| `PolvorAppMark`                     | The mark                                                                                                                                                                                                                                                                                                                                     |

**Lists**

| Composite                | Use                                                                                                                                                                                                                                                     |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `StatFilter`             | Counters above a list that are also toggle filters (`aria-pressed`); a pressed counter shows a check and a ring besides its tint                                                                                                                        |
| `FilterBar`, `NoMatches` | Filters, search and the announced result count in one bar; the empty state when the filters leave nothing, with "Clear filters"                                                                                                                         |
| `FilterSelect`           | Labelled select that filters a list by a category (comparsa, status); also a choice inside a confirmation, with `error` between label and select                                                                                                        |
| `SearchField`            | Labelled search box; filters rows already loaded (the term is never in the URL: it may be personal data)                                                                                                                                                |
| `DataTable`              | Sortable, paginated table with loading and empty states; two-line cells (`secondary`), a whole-row link (`getRowHref`, the name stays the only tab stop) stacked items on phones (`mobileRow`) and a row-header column (`rowHeader`) that names each row for screen readers |
| `StatusBadge`            | A domain status pill (see [status.md](status.md))                                                                                                                                                                                                       |
| `LinkCard`               | A shortcut to a section, e.g. on the start page                                                                                                                                                                                                         |
| `StatCard`               | A key figure on a dashboard, optionally a link: then the whole card shows the focus ring and a chevron marks it as a link                                                                                                                               |
| `Breakdown`              | Counts with each row's share as text and a bar that repeats it visually (hidden from assistive technology) for dashboards and statistics; several count columns (e.g. by gender) and a row total; its table scrolls in its own focusable region                                              |
| `AmountTable`            | Money lines (concept, quantity, unit price, amount) with the total as the table footer, for the billing summary; values come formatted; a missing price shows a dash and "No price" for screen readers; below `sm` quantity and price move under the concept so the table keeps two columns |
| `ComparsaLogo`           | A comparsa's logo, or a flag placeholder, on the light `logo-tile` in both themes (`sm` 32 px rows, `md` 40 px sidebar, `lg` 64 px record header); decorative by default because the name is beside it; a logo that fails to load shows the placeholder |

**Detail pages**

| Composite         | Use                                                                                                                               |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `RecordHeader`    | Photo or mark, context line, the record name as `h1`, statuses, frequent actions and "More actions" (destructive items set apart) |
| `KeyFacts`        | The facts that describe a record at a glance, with an optional meter                                                              |
| `Tabs`            | Tabs of a detail page (arrow keys, labelled panels, bar and weight on the chosen tab)                                             |
| `SectionGrid`     | One column, two from 1024 px, three from 1700 px                                                                                  |
| `SectionCard`     | A titled, read-only section with its "Edit" action                                                                                |
| `DescriptionList` | Read-only values, "Not given" when empty, identifiers in the mono face                                                            |
| `EditSheet`       | "Edit" opens a section's fields in a side panel (bottom sheet on phones); saves, announces and returns focus                      |
| `SaveNotice`      | `useSaveNotice()`'s `notify("Changes saved")`: a polite announcement and a short toast, never taking focus                        |

**Forms**

| Composite                                                                 | Use                                                                                                                                                        |
| ------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Form`, `FormField`, `useAppForm`                                         | Forms with React Hook Form and Zod: required by default, "(optional)" labels, widths, the error summary, one submission at a time                          |
| `FormLayout`                                                              | The form template: sections, an index from 1280 px, a help column from 1700 px                                                                             |
| `ActionBar`                                                               | Form actions fixed at the bottom, primary last; its height is kept as scroll padding                                                                       |
| `ErrorSummary`                                                            | "There is a problem": links to each field; rendered and focused by `Form`                                                                                  |
| `RadioCards`                                                              | Two to four options as cards, with hints and fields revealed under the chosen answer                                                                       |
| `TextInput`, `PasswordInput`, `SelectInput`, `CheckboxField`, `DateInput`, `TimeInput` | Controls for `FormField` (password with a show/hide toggle; a select with a placeholder that is never offered; the date and time pickers show the browser's locale) |
| `TextArea`                                                                | Multi-line free text for `FormField`, with a required `maxLength` (GOV.UK character count): the limit is part of its description, the characters left (or over) show under it and are announced politely once typing pauses. Nothing is cut: the form's schema refuses a longer text. Stories: `Default`, `WithText`, `NearTheLimit`, `OverTheLimit`, `Invalid`, `Disabled` |
| `FileField`                                                               | A file to upload, for `FormField`: a button opens the system chooser (`accept`), the chosen file shows with its size (part of the button's description, announced) and can be cleared (focus returns to the button); check type and size in the form's schema with `fileProblem` (`file-rules.ts`) so the problem shows before uploading. Stories: `Empty`, `Chosen`, `Disabled`, `InAFormWithAnError` |
| `MoneyInput`                                                              | An amount in euros, for `FormField` with `width="short"`: the decimal keypad on phones, a `€` suffix read as "in euros", and the typed text as its value (never reformatted while typing); read it in the form's schema with `parseMoney` (`lib/money.ts`), which takes a comma or a point and refuses more than two decimals, and show amounts with `useFormatters().currency`. Stories: `Default`, `Empty`, `Invalid`, `Disabled` |

**Feedback and other**

| Composite                     | Use                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Button`                      | Every button: `primary`, `secondary`, `destructive`, `quiet`, `link`; `pending` keeps focus; `asChild` for links                                                                                                                                                                                                                                                                                                                                          |
| `AlertBanner`, `NoticeBanner` | Inline info, success, warning or error message (`NoticeBanner` shows a page's `useNotice()` outcome, focused); `focusOnMount` for the outcome of an action whose control went away; `live={false}` for a lasting state shown with the page ("this comparsa is inactive")                                                                                                                                                                                  |
| `EmptyState`                  | Replaces an empty list, table or panel                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `ConfirmDialog`               | Confirmation of destructive or irreversible actions (`tone="primary"` for a reversible one such as a transfer); reject with `ConfirmFailure` to show why it failed, return `false` to stay open when a field inside already says what is missing; `initialFocus` for a field inside, `returnFocus` when it is opened from a menu                                                                                                                          |
| `PhotoUpload`                 | A photo with add/replace/remove: choose a file or take a picture (phones), crop in a dialog (fixed shape or free; arrow keys, dragging or the move and resize buttons), rotate, then hand the cropped JPEG to the page (`output="png"` keeps transparency, shows it on a checkerboard and hands a PNG, for logos); checks size and format first; removal through `ConfirmDialog`. Reject `onUpload` with `PhotoUploadFailure` to show a translated reason |
| `QrCode`                      | A QR code as an image with an accessible name (e.g. authenticator setup)                                                                                                                                                                                                                                                                                                                                                                                  |
| `RecoveryCodeList`            | One-time codes shown once: copy (success or failure announced) and print                                                                                                                                                                                                                                                                                                                                                                                  |

## Accessibility rules (WCAG 2.2 AA, NFR-07)

- **Never by colour alone.** Statuses carry icon + label; alert banners speak their severity; the
  current navigation item has a bar and weight, not only a background. Selected states (tabs,
  segmented controls) use a border, underline or weight too.
- `primary-soft` is for non-status UI only (selected row, highlighted panel).
- Placeholders use `muted-foreground` without opacity, and never replace a label.
- Contrast of every declared text/UI pair is tested in both themes (`contrast.test.ts`),
  including the hover and pressed states and the night sidebar; do not add a new colour without
  adding its pairs there. Hover and pressed states use the solid `*-hover` tokens, never an
  opacity.
- Focus is always visible: a 2 px `ring` outline at full opacity, also inside menu and select
  items (not a background change alone). Dialogs, side panels and the navigation drawer trap
  focus, close with Escape and return focus to their trigger.
- Pointer targets are at least 24 × 24 px (close buttons 36 px), and controls are 44 px high on
  touch screens (`h-control`). Sticky bars never cover the focused control (SC 2.4.11): the form
  action bar reserves its height as scroll padding.
- Dragging always has a single-pointer alternative (SC 2.5.7): the photo crop has move and resize
  buttons. Sign-in fields keep `autocomplete`, paste and the show-password toggle (SC 3.3.8).
- `AlertBanner` errors interrupt (`role="alert"`); other severities are polite (`role="status"`)
  and are only announced reliably when their content changes after they are rendered — render the
  banner where the message will appear, then set its content. A lasting state shown with the page
  is not a live region (`live={false}`), so it never competes with the announcement of an action.
- Landmark names are unique: a section card is named by its heading, the table inside it by a
  distinct caption ("Firing chiefs of Comparsa Norte").
- `globals.css` honours `prefers-reduced-motion` (no movement or scaling, fades of at most
  100 ms, no looping animations) and `forced-colors` (borders on pills, buttons and banners).
- Automated checks run the axe rules tagged WCAG 2.2 A/AA (`wcag22aa` included, so `target-size`
  too): on every story in both themes and three languages (`catalogue.test.tsx`; jsdom cannot
  measure contrast or target size), in the Storybook panel, and in a real browser (Playwright,
  light and dark).

## Content Security Policy (design D10)

Radix primitives (dialog and sheet scroll lock) inject `<style>` elements at runtime, so the UI's
CSP allows `style-src 'self' 'unsafe-inline'`. Scripts stay strict: `script-src 'self'`, no
`unsafe-eval`, and the theme bootstrap is an external file. The residual risk — injected CSS used
to leak data — is bounded because no directive allows another origin: `img-src` is `'self' data:
blob:`, `connect-src` is `'self'` and fonts fall back to `default-src 'self'`. The Playwright
fixture fails on any CSP violation.

## Local edits to primitives

`src/components/ui/` stays close to upstream shadcn/ui; local edits are limited to these and
marked with a `Local edit` comment:

- **Translations**: texts inside primitives (close buttons, breadcrumb "more", sidebar toggle and
  mobile title) come from the `ui` namespace.
- **Sidebar**: the open state is not persisted (no cookie); a collapsed sidebar is `inert`
  (not focusable); Ctrl/Cmd+B does not toggle it inside editable fields; navigation labels wrap
  instead of truncating and leave room for a counter badge.
- **Table**: `container` passes attributes to the scroll container, so `DataTable` makes it the
  focusable, named region that scrolls on narrow screens; rows change at once on hover.
- **Focus, states and targets** (design D3 of `redesign-design-system`): a full-opacity 2 px focus
  outline on every control and inside menu and select items; solid hover tokens; the destructive
  token pair in both themes; inputs on the card surface; 20 px checkboxes and radios in a fixed
  square box (the radio dot never turns oval); 36 px close buttons; token heights.
- **Motion** (design D5): sheets 250 ms in and 200 ms out with the drawer easing; dialogs 200 ms
  in (fade and a slight scale) and 150 ms out; menus, selects and tooltips 150 ms in with a fade
  and a 4 px slide; a 500 ms tooltip delay; skeletons fade in after 150 ms without pulsing; the
  sidebar never animates its width or position.
- **Select** (design D11): the `native-select` list follows the menu style where the browser
  supports a customizable select (`globals.css`); elsewhere the native list opens.

`src/components/ui/primitives-guard.test.ts` and `src/styles/motion.test.ts` fail if a primitive
goes back to a translucent focus ring or hover colour, white text on the destructive colour, a
small close button or untokenised motion (ESLint exempts this folder). Re-apply these edits when
updating a primitive from upstream.

## Arquebusier badge

The printed badge is specified in [data-model.md §4](../data-model.md#4-arquebusier-badge-uc-30).
It uses the same identity; any Federation marks on it are supplied at deployment, never committed.

## Do / don't

| Do                                                             | Don't                                                                  |
| -------------------------------------------------------------- | ---------------------------------------------------------------------- |
| Use a composite; add one (with stories) when a pattern repeats | Import `@/components/ui` from a feature                                |
| `text-muted-foreground` for secondary text                     | `text-stone-500`, `opacity-60` on text                                 |
| `StatusBadge kind="license" value={status}`                    | A coloured `<span>` with a hand-written label                          |
| `ConfirmDialog` before deleting or cancelling                  | Delete on a single click                                               |
| `EmptyState` with the action that fills it                     | An empty table body                                                    |
| Let long Valencian labels wrap                                 | Truncate labels or fix widths in pixels                                |
| Show a record read-only and edit each section in `EditSheet`   | Make the whole detail page one editable form                           |
| Put rare and destructive actions in "More actions"             | Red buttons in the page body                                           |
| Announce a save with `useSaveNotice()`                         | Move focus to a success banner                                         |
| `RadioCards` for two to four options                           | A select for "Yes / No" or for three short options                     |
| Mark the few optional fields "(optional)"                      | Asterisks on required fields                                           |
| Validate when the form is sent, with the error summary         | Errors that appear while the person is still typing or leaving a field |
| A table plus `mobileRow` for phones                            | A table that scrolls sideways at 360 px                                |
