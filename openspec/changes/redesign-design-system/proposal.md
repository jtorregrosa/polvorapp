# Proposal

## Why

The maintainer finds the UI plain and unattractive, and the audit in
`docs/design/layout-audit.md` explains why: stock shadcn/ui with no page-width system, two section
styles, a detail page that is one 3000 px edit form, a flat type scale, a submit button stretched
across the form, and a layout that floats in a fixed 1152 px column on wide monitors. The second
pass also found real accessibility defects: the focus ring of every primitive fails 3:1 (SC 1.4.11),
the primary hover text is 4.45:1, keyboard focus in menus is nearly invisible, and WCAG 2.2 is never
tested. Motion is the slow, untokenised shadcn default (a 500 ms drawer).

The maintainer compared three directions and chose **"Pólvora"** (Stripe Dashboard and Atlassian
structure with its own character) **with the form rules of "Registro"** (GOV.UK Design System). It is
done now, before #6b and #7 add more screens, so every later screen is built on the new templates
instead of being redone.

Capability (from `docs/mvp.md`): **`design-system`**, inserted as change **#6a**. It also changes
the shell (`platform`) and the registry screens (`arquebusier-registry`). It raises **NFR-07** to
WCAG 2.2 AA and keeps **NFR-01** (360 px upwards), **BR-04** (compliance checks are warnings) and
**BR-12** (FiringChief scope) as they are. It follows **ADR-0009** (shadcn/ui + Tailwind with
guardrails). A new **ADR-0013** supersedes **ADR-0012** and keeps its rule against committing
third-party brand assets.

## What Changes

- **New identity** (ADR-0013, superseding ADR-0012):
  - fonts: Bricolage Grotesque for titles and key figures, Geist for the interface, Geist Mono for
    identifiers (nationalId, federationId, guide numbers). All OFL-1.1 and self-hosted.
  - palette: cool lavender greys, a dark "night" sidebar in both themes, and ember as the only
    accent. The mark and the "no third-party brand assets" rule stay.
- **Tokens beyond colour**: a semantic type scale with a step up at 1700 px, a spacing rhythm
  (field < group < section < page), control and row heights, radii, three elevation levels, page
  widths, and motion durations and easings. All are checked in both themes.
- **Accessibility fixes**:
  - focus indicators at full opacity, at least 2 px and 3:1;
  - solid hover colours, so hover text keeps 4.5:1;
  - visible keyboard focus in menus and selects;
  - the dark destructive button uses its own token pair;
  - 24 px targets for dialog and sheet close buttons;
  - borders on banners in forced-colours mode;
  - a single-pointer alternative to dragging in the photo crop (SC 2.5.7).
- **WCAG 2.2 AA** (NFR-07, from 2.1 AA): automated checks include the `wcag22aa` rules. Sticky
  bars never hide the focused control (SC 2.4.11). Specs and docs say 2.2.
- **Motion system**: duration and easing tokens. The 250 ms drawer and side panel replace the
  500 ms drawer. There is no layout animation, no transition on table rows or keyboard-triggered
  changes and no route transitions. Under reduced motion only short fades remain.
- **Shell** (`platform`):
  - the night sidebar;
  - the top bar keeps the breadcrumbs and the user menu, and the language and theme switchers move
    into the user menu;
  - the content uses the width up to 1680 px instead of a centred 1152 px column.
- **Page templates and composites**, still used only through `components/app/` (ADR-0009):
  - list, detail and form templates;
  - `RecordHeader` + `KeyFacts`, `Tabs`, `SectionCard` + `DescriptionList` (one section style;
    `PageSection` and the notched `FormSection` legend go away), `EditSheet`, `FilterBar` +
    `StatFilter`, `FormLayout` + `ActionBar`, `ErrorSummary`, `RadioCards` and `SaveNotice`;
  - changed `AppLayout`, `PageHeader`, `DataTable` (two-line cells, whole-row link, stacked rows
    on phones), `FormField` (`width`, `optional`), `Button` and the select (styled picker where
    the browser supports it).
- **Form rules from "Registro"**: no asterisks. Optional fields say "(optional)" and one sentence
  explains it. Field widths follow the expected content, radio cards replace selects for two to
  four options, and conditional fields appear under the answer they depend on. An error summary
  takes focus after a failed submission and links to each field, and every error is repeated at
  its field. **BREAKING** for the "Form fields and validation messages" requirement: the required
  marker and the "focus the first invalid field" rule are replaced.
- **Detail pages in read mode**: each record shows a summary header and read-only sections. Each
  section has an "Edit" action that opens a side panel (a bottom sheet on phones). Destructive and
  secondary actions go into a "More actions" menu. **BREAKING** for "Registry screens": the detail
  page no longer is a single edit form. The same pattern is applied to comparsas, weapon models and
  users.
- **Arquebusier list**: counters that also filter the list (active, reserve, expired license,
  pending license, no license), comparsa segments, search and a result count in one bar. Rows show
  the license expiry. All of this comes from the existing list response, so the API does not
  change.
- **Every feature page** moves to the new templates: start page (better interim state until #7),
  arquebusiers, owned weapons, comparsas, weapon models, users, account, the sign-in and other
  public pages, and the not-found and error pages.

## Non-goals

- **Global search** in the top bar (shown in the demo) and a command palette: a later change.
- **"Expiring soon" counter and the "needs attention" sidebar group**: they need a threshold that no
  business rule defines yet. They belong to #7 `add-compliance-insights`.
- **Audit history tab** on the detail page: #15 `add-audit-privacy`.
- No change to APIs, data, permissions, business rules or validation rules: only how they are
  shown. Server-side scoping (BR-12) and audit logging are untouched.
- No dashboard content on the start page (#7) and no comparsa logos (#6b).
- No new UI framework: still shadcn/ui on Radix with Tailwind v4 (ADR-0009, ADR-0011).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `design-system`: identity (fonts, palette), tokens beyond colour (type, spacing, elevation, widths,
  motion), WCAG 2.2 AA, focus and state contrast, motion, page templates, read-mode detail with
  section editing, actions, form fields and the error summary, data tables on phones and wide screens,
  the styled select, and the crop alternative to dragging.
- `platform`: application shell (night sidebar, switchers in the user menu, full-width content,
  drawer motion, WCAG 2.2 AA).
- `arquebusier-registry`: registry screens (read-mode detail with section editing, "More actions")
  and the list (counters and filters by license state, license expiry in the row).

## Impact

- **Frontend**:
  - `src/styles/` (tokens, globals, contrast tests, generated `tokens.md`);
  - `src/components/ui/` (local edits for focus, hover, menu focus, motion and the select);
  - `src/components/app/` (new and changed composites, with stories);
  - every page under `src/features/` and `src/app/`;
  - translations in the three locales;
  - `@fontsource-variable/bricolage-grotesque`, `geist` and `geist-mono` replace
    `@fontsource-variable/inter`.
- **Tests**: unit and component tests of the composites and pages, axe with WCAG 2.2 tags in
  Vitest, Storybook and Playwright, and updated Playwright suites for the new detail and form flows.
- **Docs**:
  - ADR-0013 (drafted as Proposed, accepted in task 1.2), with ADR-0012 marked as superseded;
  - `docs/design/` (README, patterns, tokens, layout audit);
  - `docs/nfr.md` (NFR-07), `docs/mvp.md` (#6a) and `docs/third-party-licenses.md`.
- **Backend, API contract, data and infrastructure**: no change.
