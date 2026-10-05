# Patterns

How screens are composed from the composites (see [README.md](README.md)). Examples use
synthetic data only.

## Page templates

Every signed-in page renders inside `AppLayout`, whose content uses the width up to 1680 px
(`max-w-page`) with fixed side margins (`px-gutter`), left-aligned beside the sidebar. Routes
declare their breadcrumb with a `handle: { breadcrumb: '<common key>' } satisfies RouteHandle`.

| Template      | Structure                                                                                                                                                                                                                                                                                                                                                                                                                  |
| ------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **List**      | `PageHeader` (title, description, the primary action such as "Register arquebusier") → optional `StatFilter` counters → `FilterBar` (`SearchField` first, then filters, announced result count) → `DataTable` (two-line cells, whole-row link, stacked rows on phones), or `EmptyState` when there is nothing yet and `NoMatches` when the filters leave nothing                                                                      |
| **Detail**    | `RecordHeader` (photo or mark, context line, the name as `h1`, `StatusBadge`s, frequent actions, "More actions") → `KeyFacts` → `Tabs` or a `SectionGrid` of read-only `SectionCard`s (`DescriptionList` inside) whose "Edit" opens an `EditSheet`                                                                                                                                                                         |
| **Form**      | `PageHeader` with back link → `Form` → `FormLayout` (sections, index from 1280 px, help from 1700 px) → `ActionBar` fixed at the bottom, primary action last                                                                                                                                                                                                                                                               |
| **Dashboard** | `PageHeader` → `StatCard`s (each linking to its list) → `AlertBanner`s for what needs attention → short tables. The start page is the alerts dashboard: a figure per compliance warning, each linking to the arquebusier list filtered by it. Statistics pages add a `FilterBar` (filters kept in the address) and a `SectionGrid` of `SectionCard`s, each holding a `Breakdown`; per-comparsa figures go in a `DataTable`. Money goes in an `AmountTable` inside a `SectionCard` (the billing summary), never in a `Breakdown` |
| **Settings**  | `PageHeader` → a `SectionGrid` of `SectionCard`s, each holding one small form with its own secondary submit (the account page: profile, password, recovery codes, sessions)                                                                                                                                                                                                                                                |
| **Public**    | `PublicLayout` card: `PageHeader` → notices → `Form` → links ("Forgot your password?"). The card spaces its children; pages add no margins of their own                                                                                                                                                                                                                                                                    |

### Detail pages in read mode

- The record is read-only. Each `SectionCard` has its own "Edit" (`EditSheet`): a side panel on
  wide screens, a bottom sheet on phones, holding only that section's fields. Its form uses the
  section's slice of the feature schema (`schema.pick`), so a stored value outside the section
  never blocks the save; the page merges the section into the record and sends the full update
  with its `version` (the arquebusier). The comparsa, weapon model and user APIs have no version:
  the last save wins.
- Saving closes the panel, the section shows the new values, "Changes saved" is announced by
  `SaveNotice` without taking focus, and focus returns to "Edit". Cancel, Escape or closing discard
  the unsaved values without asking. A version conflict keeps the panel open with the reason in the
  error summary and the current values to review.
- Empty values read "Not given". Identifiers use `mono` (Geist Mono).

### Row selection

- A list offers row selection only where there is a bulk action (the registry list's "Print
  badges", for Admins). The screen owns the selection (`rowSelection`): it survives paging, sorting
  and filtering, and is cleared when the user leaves the page.
- Each row (and each stacked item on phones) has a checkbox labelled "Select <name>"; the header
  box (above the list on phones) selects or clears the current page only and shows a dash when
  partial. Choosing a box, or missing it slightly within its cell, never opens the record, and the
  count is announced. The screen drops the ids of rows that no longer exist.
- While rows are selected, a bar above the table says how many and offers "Clear" and the bulk
  action; a limit (e.g. 200 badges) disables the action and says why.

### Badge sheet

- "Print badges" opens a side panel (`DetailSheet`, a bottom sheet on phones) from the registry
  list's selection bar, from the list header when it is filtered by one comparsa and nothing is
  selected, and from the comparsa page's header (Admins only). It says what will print (the
  comparsa or the number selected), offers the labels' language as `RadioCards` (the user's by
  default), warns in one `AlertBanner` about badges without a photo or an issued license and a
  missing Federation logo, reminds to print at 100 %, and downloads the PDF.
- A refused download explains itself inside the panel: a photo that cannot be read names the
  arquebusiers; arquebusiers no longer in the registry leave the selection, and the panel says so.

### Comparsa logos

- Wherever a comparsa is named, `ComparsaLogo` can go before the name: `sm` in list rows (desktop
  and `mobileRow`), `lg` as the `RecordHeader` media, `md` in the sidebar cards. A comparsa without
  a logo shows the placeholder, so names stay aligned. Next to the name the logo is decorative
  (empty `alt`); give it an `alt` only where it stands alone.
- The image URL is `/api/comparsas/{id}/logo?v={logo.version}`: the version changes with the logo,
  so the browser reloads it. Build it only when `logo` is not null.
- The tile is light in both themes and on the night sidebar, so a black emblem on transparency
  stays visible. Never place a logo directly on a dark surface.
- Admins manage the logo in a "Logo" `SectionCard` with `PhotoUpload` in PNG mode (`output="png"`,
  free crop starting with the whole image, `maxSideRatio` 3, `minLongSide` 256, at most 1024 px)
  and `variant="picture"` (see "Picture actions"). FiringChiefs see the logo without that section.
- `AppLayout`'s `sidebarCards` show a FiringChief's active comparsas above the navigation (logo
  and name, linking to the comparsa, the open one marked as current). Names wrap, never clipped.
  Admins get no cards.
- The Federation's own logo (printed on documents, never in the repository) is managed the same way
  in a "Federation logo" `SectionCard` at the end of the comparsas page, for Admins only. Its URL is
  `/api/federation-logo?v={logo.version}` (`federationLogoUrl`). A failed load offers a retry, and a
  failed refresh after a change says the logo shown may be outdated.

### Check, report, confirm (spreadsheet import)

For an upload that changes many records at once (`add-registry-import`), the page uses the form
template in three steps, and nothing is stored until the last one:

1. **Choose**: the target (a `SelectInput`) and the file (`FileField`, its type and size checked by
   the form's schema with `fileProblem`, so a wrong file is refused before uploading). A "Download
   the template" secondary `Button` sits above the file field; a failed download shows an error
   `AlertBanner` next to it.
2. **Check**: the submit `Button` uploads the file for a report only. A problem with the file as a
   whole goes on the file field, in words. The report is a third `FormLayout` section: `StatCard`s
   with the counts, one `AlertBanner` saying whether the file can be imported (success) or not
   (error), info banners for what is ignored or left out, and a `DataTable` of the rows with
   errors or warnings (each problem in words, with its column), narrowed to errors by a
   `CheckboxField`. Changing the target or the file discards the report.
3. **Confirm**: only a report without errors turns the primary action into "Import N …", which
   opens a `ConfirmDialog` (`tone="primary"`) naming the target and the count. Success opens the
   list filtered by the target with a notice; a refusal with a new report replaces the report and
   focuses its outcome; any other refusal stays in the dialog with its reason.

### State actions (festival editions)

When a record moves through states that change what other users can do (`add-festival-editions`),
its detail page follows this pattern:

- **Primary action.** The current state's main action is the `RecordHeader`'s primary `Button`,
  named by its effect: "Start edition" for a draft, "Open orders" or "Close orders" for the
  edition in progress.
- **More actions.** Rarer moves go in "More actions": closing the edition, sending it back to
  preparation, reopening a closed edition, and deleting a draft (destructive, set apart).
- **Disabled moves.** A move that is not possible yet stays in the menu, disabled, and its label
  says why, e.g. "Close edition (Close the orders first.)".
- **Confirmation.** Every move opens a `ConfirmDialog`. Its title names the move and the record,
  and its description says what changes for FiringChiefs. The tone is `primary`; only a deletion
  is `destructive`.
- **Refusals.** A refused move stays in the dialog with the reason in words, e.g. the missing
  fields listed with `Intl.ListFormat`, or the year of the edition already in progress.
- **Success.** A success is announced with `SaveNotice` and does not move focus. The `StatusBadge`s
  update: `edition` for the lifecycle and `orders` for whether orders are open. A deletion leads
  back to the list with its notice.
- **Section editing.** Sections are edited in `EditSheet`s. Money uses `MoneyInput`. A panel that
  adds a row, such as "Add milestone", gives `EditSheet` its own `trigger` label and icon.
- **Emails.** A move that emails other users says so in its dialog, e.g. "They are told by email"
  for opening or closing the orders (`add-notifications`).
- **Reminder marks.** A milestone reminded by email shows a muted text mark under its title ("Email
  reminder", with a `BellRing` icon that is `aria-hidden`), never an icon alone. Its panel has a
  `CheckboxField` whose description says who is reminded and when.

### Notification settings (account page)

The account page's "Email notifications" section (`add-notifications`) lists, in read mode, each kind
of email of the user's role with its state as text ("On"/"Off") and what it covers. "Edit" opens an
`EditSheet` with one `CheckboxField` per kind (label and description). The section is a
`SectionCard` with `anchorId="notifications"`: an email's settings link opens
`/account?section=notifications`, and the page scrolls to the section and moves focus to it. A load
failure stays inside the section (`LoadFailure` with retry) and never hides the rest of the page.

### Settings page (Federation settings)

`/settings` (Admins, "Administration" in the navigation; `add-federation-settings`) is a detail page in
read mode without a record header: a `PageHeader` and a `SectionGrid` with one `SectionCard` per group
(Identity, Federation logo, Emails, Orders, Calendar). Each card's description says in one sentence
where its values are used, the values are a `DescriptionList`, and "Edit" opens an `EditSheet` saved
on its own with the settings version. A stale version reloads the settings and keeps the panel open
with the reason. Lead times are a `SelectInput` of day counts, never free numbers. The Identity panel
says that the contact must be the Federation's, never a person's. A new group is a new card: the
existing ones do not change.

### Order page (comparsa orders)

A record made of many rows that are edited one by one and never removed (`add-comparsa-orders`)
uses the detail template this way:

- **Header.** `RecordHeader` with the comparsa's logo, a context link to the edition's orders, the
  record's status (`order`) and, for the edition in progress, whether orders are open (`orders`).
- **Why not.** When the user may not edit, an info `AlertBanner` says why (orders closed, order
  validated) and no edit, add or submit action is shown, rather than disabled ones.
- **Attention first.** Under the header, in this order: the return reason (warning, kept as typed
  with its line breaks), who submitted it when an Admin did (info), the number of entries with
  compliance warnings as a link to the entries (warning; warnings never block) and the entries that
  block the submission with their reasons in words (error).
- **Totals.** `KeyFacts` with the computed totals; units in words ("5 kg").
- **Rows.** A `DataTable` whose row header is "Last name, First name". The DNI/NIE and ID Unión,
  then the first-year flag or "No longer in the registry", go in the row header's secondary line. Each row has one "Edit" action,
  named after the person; there is never a remove action. On phones, `mobileRow` stacks the values.
- **Related lists.** `SectionCard`s for "Not in the order" (each with "Add") and "Weapons lent to
  others", shown only when they have items.
- **Refusals.** A write refused because the record changed or orders closed reloads the order and
  says why; the page then shows its new state.

### Distribution page (distribution planning)

An edition's distribution (`add-distribution-planning`) uses the detail template with sections
only, one per day and one for the proxies:

- **Header.** `PageHeader` "Distribution of {year}", back to the edition. The "Distribution"
  navigation entry opens the edition in progress, or says there is none with a link to the editions.
- **One section per day.** Powder, then weapons. A day not planned says so; Admins get "Plan". A
  planned day shows `KeyFacts` (date in words, location) and its slots in a `DataTable`: by time,
  then name, the comparsas without a slot last as "No slot" (Admins only; FiringChiefs see their
  own). Admins edit the day in an `EditSheet` (the "Plan" trigger turns into "Edit" in place, so
  focus stays) and delete it with a `ConfirmDialog` that says its slots go.
- **Slots sheet.** One optional `TimeInput` per comparsa, by name, each named after the comparsa
  (its clear button too, `clearSubject`); empty means no slot; saved as one set with the day's
  version. The API's `slots[i]` reasons are mapped back to the comparsa's row.
- **Lists.** For Admins, under the slots: the numbering note, a warning naming the comparsas whose
  orders are not validated (and so not on the list), and `DownloadButtons` for Excel and PDF.
- **Proxies.** A full-width section with a `DataTable` (holder as row header, type, proxy,
  comparsa, problem as a `proxy` status badge). "Print form" is a single PDF download named after
  the holder and the type (a holder may have both), hidden while the proxy has a problem; "Remove"
  is confirmed, and a proxy already removed elsewhere counts as removed. Admins filter by
  comparsa (in the address). When the user may not change proxies, an info banner says why and no
  add or remove action is shown; the form can still be printed.
- **Proxy sheet.** The comparsa only when the user has several, the type as `RadioCards`, then the
  holder and the proxy as selects: people who cannot be chosen stay in the list, disabled, with the
  reason in their label, and are summed up in the field's description too, as browsers skip
  disabled options. The panel says that the reason is written by hand on the form. Conflicts about
  a person land on their field.
- **Not in progress.** A draft or closed edition says so under the header; the panels stay mounted
  with hidden triggers, so one left open still shows why its save was refused. After a conflict a
  panel shows the server's values before the person saves again.

### Audit log (audit and privacy)

A long, append-only record read by Admins (`add-audit-privacy`) uses the list template with
server paging:

- **Filters in the address.** `FilterDate` from and to, then `FilterSelect`s for user (with "No
  user" for the system and anonymous requests), comparsa, area and action. With no period in the
  address the page asks for the last 30 days; an emptied "From" stays as `from=` (no lower bound),
  and an inverted period is said at "To" without asking. The actions offered follow the chosen
  area; a value from the address stays shown while its list loads. A record
  filter ("View history") shows as an info banner naming the record type, with a button that
  removes it.
- **Rows.** A `DataTable` with `paginated={false}`, newest first; the action label is the row
  header. "Show more" under the table appends the next page (cursor paging), and the result text
  says when there are more; focus moves to the first new entry, and a failed page keeps what was
  loaded. The record links to its page (underlined) only while it exists and has one; the
  user is the name, "Erased user", "System" or "Anonymous", never an identifier.
- **Details.** Each row has a `DetailSheet` ("View details" + the action for screen readers): a
  read-only side panel with a `DescriptionList` of the entry (trace id in the monospaced face) and
  a second list of the recorded data fields, as recorded. Use `DetailSheet` for any read-only
  panel; `EditSheet` only when the panel saves something.
- **History.** A record's page offers "View history" to Admins in its header, a `Button asChild`
  link to the audit log filtered by the record.

### GDPR requests (audit and privacy)

Requests that involve a person's DNI/NIE (`add-audit-privacy`) never put it in the address:

- **Lookup.** The settings template: a `SectionCard` with a one-field form (the DNI/NIE, checked as
  the API does before it is sent), then the result: a summary `SectionCard` with the actions, or
  an info banner when nothing is held; focus moves to the result. The lookup and its result live in
  the page's memory only, and editing the DNI/NIE hides the result.
- **Request reference.** Every export and erasure asks for it inside its confirmation: a
  `FormField` whose help says not to type the person's name or DNI/NIE. The dialog checks it before
  sending; a refused reference lands on the field, any other refusal in the dialog.
- **Download.** A primary-toned `ConfirmDialog` that starts on the reference; the file comes from a
  POST (`apiDownloadPost`) and keeps the API's file name.
- **Erasure.** A destructive `ConfirmDialog` that names the person, lists the warnings in `notes`
  (cannot be undone, the lists that will no longer name them, the entry removed while orders are
  open) before the reference, starts on Cancel and repeats the verb ("Erase"). After it, the
  outcome (what was deleted, anonymised and removed, in words) is announced with focus, and the
  lookup is cleared.
- **Erased records.** An erased user shows the `ERASED` status, "Erased user" as the name, no
  edits and only "View history"; an erased entry shows "Erased person", no identity and no "Edit".

## Actions

- At most **one primary action** per page, at its natural width (never stretched across a form).
  On forms it is the last action of the `ActionBar`, with "Cancel" before it.
- Secondary actions use the `secondary` button; quiet actions (`quiet`) are for low-emphasis
  commands inside panels and menus.
- On detail pages, rarely used and destructive actions go in the `RecordHeader`'s "More actions"
  menu. Destructive items are set apart after a separator, in the destructive colour with an icon.
- Only the confirming button of a destructive confirmation is filled with the destructive colour;
  two filled destructive buttons never sit side by side.

## Forms and validation

The form rules follow the GOV.UK Design System (direction "Registro", design D8 of
`redesign-design-system`).

- Create the form with `useAppForm` (React Hook Form with the rules below) and render it with
  `Form` + `FormField`. Zod messages are keys of a translation namespace (e.g.
  `validation.required`) and are translated at the field and in the summary.
- Lay a page form out with `FormLayout`: one section per group of fields (a card with an `h2` and
  an optional description), an index of the sections beside the form from 1280 px, a help column
  from 1700 px, and an `ActionBar` at the bottom with the secondary actions and then the primary
  one. The form keeps a readable width (`max-w-form`) on any screen. A form with a single
  section has no index; below 1700 px the help follows the sections, collapsed in a disclosure.
- **Required by default, no asterisks.** Mark the exceptions with `optional`: the label then ends
  in "(optional)" in the user's language. `Form` states once, at its start, that the other fields
  are required (`requiredNote`).
- **Order inside a field**: label → help text → error → control. Help text goes in `description`
  (linked with `aria-describedby`), never in the placeholder.
- **Widths follow the expected content** (`FormField width`): `id` for nationalId, federationId
  and codes, `short` for dates, phones and numbers, `name` for names and selects of names, `long`
  for email addresses; only free text takes the full width.
- **Choices**: two to four options are `RadioCards` (each with an optional hint); a longer or
  growing list is a `SelectInput`, whose `placeholder` ("Choose a comparsa") is shown but never
  offered. An empty value that is a real choice ("All", "No license") is an option, not a
  placeholder.
- **Conditional fields** appear under the answer they depend on (`RadioCards` option `reveal`),
  instead of being shown disabled.
- **Validation** runs on submit, then again on every change of a field that has an error, so a
  fixed field clears at once. After a failed submission (or server errors set with
  `form.setError` while submitting) an `ErrorSummary` titled "There is a problem" appears at the
  top and takes focus. Each entry links to its field ("Birth date: …"); following it focuses the
  field (the chosen or first option of a radio group) and scrolls it above the action bar. Each
  error is repeated at its field, after the help, with a bar beside the field, and read with the
  field (`aria-describedby`).
- A server error that belongs to no field goes in the summary as
  `form.setError('root.server', …)`; in an `EditSheet`, return `{ status: 'rejected', reason }`.
  An `AlertBanner severity="error"` is only for what is not about the submitted values (e.g. a
  failed load).
- Dates use `DateInput` (the native date picker). Its value is ISO `yyyy-MM-dd` as the API expects;
  the picker shows it in the **browser's** locale (not the app language), while read-only dates are
  formatted in the app language. Form defaults are `''`, never `null` (map `null` to `''` when loading
  and `''` to `null` on submit). A partly typed date is reported as `INCOMPLETE_DATE` so the schema
  can say "enter a complete date" instead of "required". `min`/`max` only guide the picker and are
  not announced: the schema checks the bounds (`isIsoDate`, `todayIso()` in Europe/Madrid for "not
  in the future") and its error states them. Optional dates use `clearable`, because some mobile
  pickers cannot empty a date. Autofill is off: these forms record other people's dates.
- Times of day use `TimeInput` (the native time picker, whole minutes). Its value is `HH:mm`, the
  same rules as `DateInput` apply (`''` defaults, `INCOMPLETE_TIME`, `clearable`), and where several
  times share a form each clear button gets its own name with `clearLabel` (e.g. a distribution
  day's slots).
- **Compliance checks (license, course, age) are warnings, never blocking errors**
  (BR-04, `docs/data-model.md`): show them with the warning tone and let the user save. They never
  appear in the error summary. Data-integrity rules are blocking errors.

### Picture actions (logos)

A single picture that its owner manages, such as a comparsa logo or the Federation logo, is
changed from the picture itself (`PhotoUpload variant="picture"`), not from buttons under it:
- **With a picture**, the picture is a button named "{picture}, options" that opens a menu with
  "Replace" and "Remove". "Remove" asks first with a `ConfirmDialog`.
- **Without one**, the placeholder is the "Add {label}" button and opens the file chooser directly.
- A pencil chip appears on hover and keyboard focus. It is a hint only: the button's name already
  says what it does, and touch works without hover.
- Focus returns to the picture after the menu, the crop dialog or the confirmation; saving and
  removing are announced as in "Photo upload with cropping".
- Arquebusier photos keep the `section` variant (buttons under the photo): several photos share a
  section there, and each action names its photo.

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
- **Tags vs statuses.** A status says how something is doing (a license, an order) and uses
  `StatusBadge`: a semantic tone and an icon. A fixed value that says what something is (a role,
  a side, a weapon kind, a yes/no flag) uses `CategoryTag`: a categorical tone from `tags.ts`, no
  icon, the same tone everywhere. Never show a fixed value as a status, or a status as a tag.
- **Every orderable column is sortable**, by the value it shows (the translated label of a tag or
  status, not its code). Columns of actions are not.
- **Missing values** are shown in the muted text colour. A lone dash gets a screen-reader text that
  says what is missing ("No side").
- **Links in rows.** A row that leads to a record (`getRowHref`) opens it from any cell. A cell may
  hold its own link to a related record, e.g. an arquebusier's comparsa: that link opens its own
  target, and the rest of the row still opens the row's record.
- A list page gives every table a `mobileRow`: the name as the link first, then what the other
  columns say, so nothing is lost on a phone (WCAG 1.4.10).
- A table inside a `SectionCard` gets a caption distinct from the card's title ("Firing chiefs of
  Comparsa Norte"), so no two regions share a name.

## Destructive and irreversible actions

- Always go through `ConfirmDialog`: the title states the action and its object, the description
  its consequence, and the confirm button repeats the verb ("Delete", "Cancel order").
- Focus starts on Cancel; Escape and Cancel change nothing.
- `onConfirm` may be async: the dialog stays open and busy while it runs, closes on success
  and shows a translated error (`AlertBanner`) if it fails, so the user can retry. Reject with
  `ConfirmFailure(translatedReason)` to show the specific reason (e.g. "last active Admin").
- The description never claims what is not known yet: while the people who would lose access are
  still loading, it says "if any" instead of "nobody".
- A confirmation opened from "More actions" is a controlled `ConfirmDialog` with `returnFocus` on
  the "More actions" button. After it succeeds, focus goes back there and the outcome is
  announced with `useSaveNotice()`; a failure stays in the dialog with its reason.
- When the action takes the person to another page (a deletion), that page shows the outcome in
  an `AlertBanner` with `focusOnMount`.
- A choice needed to confirm (the destination of a transfer) goes inside the dialog, which starts
  on it (`initialFocus`); confirming without it keeps the dialog open and says so at the field.
- Every write and export is audit-logged on the server; the UI does not need to say so.

## Empty and loading states

- `EmptyState`: what is missing, why it matters, and the action that fills it.
- Loading: skeletons in the place of the content (tables, cards), never a blocking spinner for the
  whole page. Buttons that submit show a pending state and ignore clicks while pending (they stay
  focusable, so focus is not lost).
- A view that replaces the page after an action ("Check your email", "Password changed") uses
  `PageHeader focusOnMount`, so its title is read and focus is not lost.
- Errors loading a page: the route's error page (inside the shell) with a way back.

## Responsive rules (360 to 2560 px)

| Width                | What changes                                                                                                                                                     |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| < 768 px (`md`)      | The sidebar is a drawer opened from the top bar; lists become stacked items (`DataTable` `mobileRow`); side panels become bottom sheets; side margins are 16 px. |
| ≥ 768 px             | The sidebar is shown; tables; side margins 28 px.                                                                                                                |
| ≥ 1024 px (`lg`)     | Detail sections in two columns.                                                                                                                                  |
| ≥ 1280 px (`xl`)     | Forms get the section index beside them.                                                                                                                         |
| ≥ 1700 px (`wide`)   | The type steps up; detail sections in three columns; forms get the help column.                                                                                  |
| > 1680 px of content | The content stops at 1680 px (`max-w-page`), left-aligned; the top bar is aligned with it.                                                                       |

- Usable from **360 px** wide (NFR-01) with no horizontal page scroll; a table wider than its
  container scrolls inside its own focusable region.
- Long labels (Valencian is the longest) wrap instead of being truncated; forms keep a readable
  width (`max-w-form`) on any screen.
- Use the breakpoints above and the tokens; no pixel widths.
- Pointer targets are at least 24×24 px; controls are 40 px high (`h-control`), 44 px on touch
  screens.

## Motion

- Only opacity and transforms move, never layout. Durations come from the tokens: colour and
  border changes 100 ms; menus, selects and tooltips 150 ms (a fade and a 4 px slide); dialogs
  200 ms (a fade and a slight scale); side panels, bottom sheets and the drawer 250 ms with the
  drawer easing. Leaving is shorter than appearing.
- Not animated: route changes, sorting, filtering, paging, validation messages, theme and language
  changes, the sidebar collapsing (it never animates its width), and table rows on hover.
- No decorative motion: no looping animations (skeletons fade in after 150 ms and do not pulse),
  no staggered lists, no hover scaling of rows. Buttons press to 98 %.
- Under reduced motion, movement and scaling are removed and fades last at most 100 ms.
