# Patterns

How screens are composed from the composites (see [README.md](README.md)). Examples use
synthetic data only.

## Page templates

Every page renders inside `AppLayout` and starts with a `PageHeader` (the page's only `h1`).
Routes declare their breadcrumb with a `handle: { breadcrumb: '<common key>' } satisfies RouteHandle`.

| Template | Structure |
|---|---|
| **List** | `PageHeader` (title, count in the description, primary action such as "Add arquebusier") → optional filters → `DataTable` (or `EmptyState` when there is nothing yet) |
| **Detail** | `PageHeader` with back link, status (`StatusBadge`) and actions → cards with the record's sections → related tables |
| **Form** | `PageHeader` with back link → `Form` with one `FormSection` per group of fields → submit and cancel at the end |
| **Dashboard** | `PageHeader` → a grid of `StatCard`s (each linking to its list) → `AlertBanner`s for what needs attention → short tables |

## Forms and validation

- Build forms with `Form` + `FormField` (React Hook Form + Zod). Zod messages are keys of the `ui`
  namespace (e.g. `validation.required`) and are translated by `FieldError`.
- Group fields with `FormSection` (a `fieldset` with a `legend` and optional description).
- Mark required fields with `required` (visible asterisk + `aria-required`); the form shows the
  "fields marked with * are required" note once.
- Help text goes in `description` (linked with `aria-describedby`), never in the placeholder.
- Validate on submit, then on change of the invalid field. The first invalid field receives focus;
  errors are announced and linked to their field.
- Server errors for the whole form go in an `AlertBanner severity="error"` above the submit button.
- **Compliance checks (license, course, age) are warnings, never blocking errors**
  (BR-04, `docs/data-model.md`): show them with the warning tone and let the user save.
  Data-integrity rules are blocking errors.

## Tables

- `DataTable` takes a `caption` (its accessible name and the pagination label), `columns`
  (`id`, `header`, `cell`, optional `sortValue` and `align: 'end'` for numbers) and `getRowId`.
- Define `columns` outside the component or memoise them, and pass stable `data`: new arrays on
  every render reset sorting and paging work.
- Sorting uses a locale-aware collator; the sort state is announced to screen readers.
- Pages of 10, 20 or 50 rows; the current page is clamped when the data shrinks.
- Wide tables scroll horizontally inside their own focusable region; the page never scrolls
  sideways.
- Loading shows skeleton rows with `aria-busy`; an empty result shows "No results" in the table.
  When a list has never had data, render `EmptyState` instead of the table.
- Statuses in cells use `StatusBadge`; numbers are right-aligned and formatted for the active
  language.

## Destructive and irreversible actions

- Always go through `ConfirmDialog`: the title states the action and its object, the description
  its consequence, and the confirm button repeats the verb ("Delete", "Cancel order").
- Focus starts on Cancel; Escape and Cancel change nothing.
- `onConfirm` may be async: the dialog stays open and busy while it runs, closes on success
  and shows a translated error (`AlertBanner`) if it fails, so the user can retry. Reject with
  `ConfirmFailure(translatedReason)` to show the specific reason (e.g. "last active Admin").
- After a confirmed action succeeds, announce the outcome in an `AlertBanner` with `focusOnMount`:
  the trigger may be gone (e.g. "Deactivate" becomes "Reactivate").
- Every write and export is audit-logged on the server; the UI does not need to say so.

## Empty and loading states

- `EmptyState`: what is missing, why it matters, and the action that fills it.
- Loading: skeletons in the place of the content (tables, cards), never a blocking spinner for the
  whole page. Buttons that submit show a pending state and ignore clicks while pending (they stay
  focusable, so focus is not lost).
- A view that replaces the page after an action ("Check your email", "Password changed") uses
  `PageHeader focusOnMount`, so its title is read and focus is not lost.
- Errors loading a page: the route's error page (inside the shell) with a way back.

## Responsive rules

- Usable from **360 px** wide (NFR-01). Below the sidebar breakpoint the sidebar becomes a drawer
  opened from the top bar.
- The top bar wraps rather than clipping; long labels (Valencian is the longest) wrap.
- Use the Tailwind breakpoints (`sm`, `md`, `lg`) and the default scale; no pixel widths.
- Touch targets are at least 24×24 px; primary controls 36 px or more.
