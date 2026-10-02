# Tasks

## 1. Research, decision record and dependencies

- [x] 1.1 Check with Context7 (and gh for real-world usage) the APIs of design D2, D5, D7, D8 and D11:
  - Tailwind v4 `@theme` namespaces for text, spacing, shadow, container, ease and animate, and how to expose durations;
  - tw-animate-css enter and exit variables;
  - Radix Tabs, RadioGroup and Dialog (`side="bottom"` sheet) in the `radix-ui` package;
  - shadcn/ui `radio-group`;
  - React Hook Form `shouldFocusError`, `reValidateMode` and `setFocus`;
  - CSS `appearance: base-select`, `::picker(select)`, `option::checkmark` and `@starting-style`;
  - axe-core `wcag22aa` and `target-size`;
  - the Storybook a11y `runOnly` options.

  Record versions and any workaround in design.md. Verify: design.md updated, no open question left.
- [x] 1.2 Accept `docs/adr/0013-polvora-visual-identity.md`, drafted as Proposed with this change: set its status to Accepted, mark ADR-0012 "Superseded by 0013", and update both rows in `docs/adr/README.md` and the ADR references in `docs/design/` and the design-system spec Purpose. Verify: the links resolve and `design-docs.test.ts` still passes.
- [x] 1.3 Add `@fontsource-variable/bricolage-grotesque`, `@fontsource-variable/geist` and `@fontsource-variable/geist-mono` (5.3.0, exact pins) and remove `@fontsource-variable/inter`. Add the rows to `docs/third-party-licenses.md` and remove Inter's. Add the `radio-group` primitive with the shadcn CLI. Verify: `npm ci`, `npm run build` and the dependency review job pass.
- [x] 1.4 Insert change #6a `redesign-design-system` in `docs/mvp.md`, and raise NFR-07 to "WCAG 2.2 AA" in `docs/nfr.md`. Verify: `docs/mvp.md` lists #6a before #6b, and `docs/nfr.md` says 2.2 AA.

## 2. Foundation: tokens, fonts, accessibility fixes, WCAG 2.2 and motion

- [x] 2.1 Extend `contrast.test.ts` first. Add the new palette pairs (ADR-0013), the night sidebar pairs, and the hover and pressed pairs of the primary and destructive buttons in both themes. Then rewrite the colour tokens in `tokens.css`. Verify: the contrast tests pass, and a deliberately broken pair fails naming the theme, the pair and the ratio.
- [x] 2.2 Add the type, spacing, size, radius, elevation, width and motion tokens (design D2), the 1700 px type step and the fonts in `globals.css`. Regenerate `docs/design/tokens.md` with `npm run docs:design`, extending its generator for the new groups. Verify: `design-docs.test.ts` passes, and ESLint still rejects arbitrary values.
- [x] 2.3 Write `primitives-guard.test.ts` (design D3) and see it fail on today's `ring-ring/50`, `bg-primary/90` and `bg-destructive/60`. Then fix the primitives:
  - full-opacity focus outline;
  - a visible focus on menu and select items;
  - solid hover and pressed colours;
  - the dark destructive button pair and transparent dark inputs;
  - 24 px close buttons;
  - a bordered `AlertBanner` in forced colours.

  Mark each edit `Local edit`. Verify: the guard test passes, and the component tests and stories still pass.
- [x] 2.4 Switch the axe tags to include `wcag22aa` in `src/test/axe.ts`, the Storybook a11y parameters and `e2e/fixtures.ts`. Fix any new violation, `target-size` included. Verify: `npm test`, the Storybook test run and the Playwright shell and theme specs pass with the 2.2 tags.
- [x] 2.5 Write tests for the motion rules: the sheet, dialog and dropdown classes use the duration tokens, the sidebar has no width transition, table rows have no transition, and `Button` has no `transition-all`. Then apply design D5 in the primitives, the tooltip delay, the 150 ms skeleton delay and the new reduced-motion block in `globals.css`. Verify: the tests pass, and a Playwright check with `reducedMotion: 'reduce'` sees no transform on an opening sheet.
- [x] 2.6 Style the select per design D11 in the `native-select` primitive. Add a `SelectInput` story with the list open (Chromium) and a test that the placeholder option is not offered as a choice. Verify: the story passes axe in both themes, and Firefox and WebKit Playwright smoke runs open the native list.
- [x] 2.7 Update `docs/design/README.md` (identity, WCAG 2.2 AA, the guard test, local edits) and the design-system spec references. Verify: no "2.1" remains in `docs/design/` (grep).
- [x] 2.8 Review group 2 in parallel with `react-reviewer`, `typescript-reviewer`, `a11y-architect` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 3. Form composites

- [x] 3.1 Write tests for `FormField`:
  - the "(optional)" suffix in the three locales;
  - no asterisk;
  - the label → help → error → control order;
  - `aria-describedby` and `aria-invalid`;
  - the width classes.

  Then change `FormField` (`optional`, `width`) and the controls (`TextInput`, `SelectInput`, `DateInput`, `PasswordInput`). Verify: the tests and the updated stories pass.
- [x] 3.2 Write tests for `ErrorSummary` and `Form`:
  - the summary appears on an invalid submit, in field order, and receives focus;
  - its links focus and scroll to the field (the first radio of a group);
  - the field error clears on change after submit;
  - `requiredNote` is rendered once.

  Then implement it per design D8. Verify: the tests pass, and the axe story passes in both themes.
- [x] 3.3 Write tests for `RadioCards`: arrow-key selection, the label and hint, the error state, a 20 px dot that is round at any zoom (width equals height), and conditional content revealed under the chosen option. Then implement it on the `radio-group` primitive. Verify: the tests and stories pass, including the 360 px Valencian story.
- [x] 3.4 Write tests for `FormLayout` and `ActionBar`:
  - the section index from 1280 px and the help column from 1700 px (container queries in stories);
  - the primary action is last;
  - the scroll padding equals the bar height.

  Then implement them. Verify: the tests pass, and a Playwright check tabs through a long story form with no focused field under the bar (SC 2.4.11). (Done on the register form in 6.6: the E2E stack does not serve Storybook.)
- [x] 3.5 Add the `ui` keys of design D12 for forms in es-ES, ca-ES-valencia and en, and update `docs/design/patterns.md` (form rules, error summary, widths, radio cards, conditional fields). Verify: `npm run check-i18n` passes, and the catalogue test lists every new composite.
- [x] 3.6 Review group 3 in parallel with `react-reviewer`, `a11y-architect` and `typescript-reviewer`. Fix CRITICAL/HIGH findings.

## 4. List, detail and feedback composites

- [x] 4.1 Write tests for `DataTable`:
  - secondary cell lines;
  - a whole-row click opens `getRowHref` while the name link stays the only tab stop;
  - stacked `mobileRow` items below `md` without page scroll;
  - no row transition.

  Then implement them. Verify: the tests and stories pass.
- [x] 4.2 Write tests for `FilterBar` and `StatFilter`: `aria-pressed` toggling, combined filters, the live result count, and the empty state with "clear filters". Then implement them. Verify: the tests and stories pass.
- [x] 4.3 Write tests for `RecordHeader`, `KeyFacts` (with `Meter` and its accessible label), `Tabs` (arrow keys, the indicator, the panel labelling), `SectionGrid`, `SectionCard` and `DescriptionList` ("Not given" for empty values). Then implement them. Verify: the tests pass, and the stories pass axe at 360, 1440 and 1920 px.
- [x] 4.4 Write tests for `EditSheet`:
  - focus moves in and returns to the trigger;
  - Escape discards;
  - a bottom sheet on phones;
  - an invalid save keeps it open with the summary;
  - a conflict callback shows the reason and resets the fields.

  Then implement it on the `sheet` primitive. Verify: the tests and stories pass.
- [x] 4.5 Write tests for `SaveNotice`: a polite region mounted once, a repeated message announced again, and no focus change. Then implement it and its `notify` context. Verify: the tests pass.
- [x] 4.6 Write tests for the crop buttons in `PhotoUpload` (move and resize by 5 %, keeping the 3:4 shape, without dragging). Then add them per design D4. Verify: the tests pass, and a Playwright photo spec crops with buttons only (added to the photo spec in 6.6).
- [x] 4.7 Add the `ui` keys for detail, tabs and the crop buttons in the three locales, and update `docs/design/patterns.md` (list, detail and action templates, responsive rules 360–2560 px, motion). Verify: `npm run check-i18n` passes.
- [x] 4.8 Review group 4 in parallel with `react-reviewer`, `a11y-architect` and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 5. Shell and platform pages

- [x] 5.1 Write tests for the shell:
  - the night sidebar;
  - the breadcrumbs and `UserMenu` in the top bar;
  - the language and theme radio groups inside `UserMenu` change the UI at once;
  - the top bar aligned with the 1680 px content;
  - the 250 ms drawer, which returns focus to its trigger on Escape.

  Then change `AppLayout`, `UserMenu` and the app shell (design D6). Verify: `AppLayout.test.tsx` and `SessionShell.test.tsx` pass.
- [x] 5.2 Restyle `PublicLayout` and move the start, not-found, error and "not allowed" pages to the templates. Give the start page a better interim state with links to the user's sections until #7. Verify: the component tests and the Playwright `layout`, `shell`, `theme` and `language` specs pass, the last ones updated for the switchers in the user menu.
- [x] 5.3 Update `docs/design/README.md` (shell, composites table) and the platform docs that mention the switchers in the top bar. Verify: the docs match the shell stories.
- [x] 5.4 Review group 5 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 6. Arquebusier registry pages

- [x] 6.1 Write tests for the arquebusiers list:
  - the counters count only rows in scope;
  - each counter filters and toggles;
  - the filters combine with the comparsa and the search;
  - the result count is announced;
  - a FiringChief without assignments sees no counters;
  - the rows show the license expiry, "No ID photo" and the stacked rows on a phone.

  Then rebuild `ArquebusiersPage` with `FilterBar`, `StatFilter` and `DataTable`. Verify: `ArquebusiersPage.test.tsx` passes.
- [x] 6.2 Write a unit test that every field of the arquebusier schema belongs to exactly one section schema (design D9). Then split the schema into `pick`s for personal data, license and course. Verify: the test passes.
- [x] 6.3 Write tests for the detail page:
  - read-only sections with "Not given";
  - the header (ID photo, comparsa and side, statuses) and the key facts;
  - the BR-04 warning without blocking;
  - editing each section in `EditSheet`, with a merged `PUT` that carries `version`, the saved notice and focus back on "Edit";
  - the version conflict;
  - "More actions" (status change, delete with confirmation) and transfer for Admins only.

  Then rebuild `ArquebusierDetailPage`. Verify: the page tests and `ArquebusierActions.test.tsx` pass.
- [x] 6.4 Write tests for the register form:
  - the "(optional)" labels and no asterisks;
  - radio cards for gender, status and license type, with the license dates revealed;
  - the field widths;
  - the error summary with focus;
  - the comparsa pre-selected when there is only one;
  - the ID photo.

  Then rebuild `ArquebusierFormPage` and `OwnedWeaponFormPage` on `FormLayout`. Verify: `ArquebusierForm.test.tsx`, `OwnedWeapons.test.tsx` and `Photos.test.tsx` pass.
- [x] 6.5 Add the `registry` keys of design D12 in the three locales. Verify: `npm run check-i18n` passes.
- [x] 6.6 Update the Playwright `registry`, `photos` and `dates` specs for counters, read-mode detail, edit sheets, "More actions" and the error summary, with axe (2.2 tags) on the list, the detail and the form in both themes. Verify: they pass in `desktop-chromium` and `mobile-360`, and the date and photo specs pass in Firefox and WebKit.
- [x] 6.7 Review group 6 in parallel with `react-reviewer`, `a11y-architect`, `security-reviewer` (personal data on screen and in notices) and `silent-failure-hunter`. Fix CRITICAL/HIGH findings.

## 7. Federation catalogue pages

- [x] 7.1 Write tests and rebuild `ComparsasPage` and `WeaponModelsPage` on the list template. Verify: their tests pass.
- [x] 7.2 Write tests and rebuild `ComparsaDetailPage`:
  - a read-mode header;
  - name and side in `EditSheet` (the comparsa API has no `version`; the last save wins);
  - the FiringChief assignments section, inline add and remove with confirmation;
  - deactivate and delete in "More actions";
  - Admin-only actions.

  Then rebuild `ComparsaFormPage` on `FormLayout`. Verify: the tests pass.
- [x] 7.3 Write tests and rebuild `WeaponModelDetailPage` (read mode, `EditSheet`, deactivate and delete in "More actions") and `WeaponModelFormPage` (radio cards for type, side, hand and size, revealed under the chosen type: required for trabucos and arcabuces, optional with "not set" for pistols, which the catalogue spec keeps optional, BR-07). Verify: the tests pass.
- [x] 7.4 Add any new `catalog` keys in the three locales, and update the Playwright `catalog` spec, with axe (2.2) in both themes. Verify: `npm run check-i18n` and the spec pass in `desktop-chromium` and `mobile-360`.
- [x] 7.5 Review group 7 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH findings.

## 8. Identity pages

- [x] 8.1 Write tests and rebuild `UsersPage` (list template) and `UserDetailPage`:
  - read mode;
  - name, role and email language in `EditSheet`;
  - the comparsas section;
  - "Reset two-step verification" as a normal item and "Deactivate user" in the destructive colour in "More actions", both confirmed.

  Then rebuild `InviteUserPage` on `FormLayout`. Verify: the tests pass, including the last-active-Admin refusal.
- [x] 8.2 Write tests and rebuild `AccountPage` as sections (profile, language, password, two-step verification) with its existing flows. Verify: the tests pass.
- [x] 8.3 Restyle the public pages (sign-in, second factor, recovery code, enrolment, recovery codes, accept invitation, forgot and reset password) on `PublicLayout` with the form rules. Keep `autocomplete`, paste and the show-password toggle (SC 3.3.8). Verify: their tests pass.
- [x] 8.4 Add any new `identity` keys in the three locales, and update the Playwright `identity` spec, including a check that paste works in the sign-in fields, with axe (2.2) in both themes. Verify: `npm run check-i18n` and the spec pass.
- [x] 8.5 Review group 8 in parallel with `react-reviewer`, `a11y-architect` and `security-reviewer` (authentication pages). Fix CRITICAL/HIGH findings.

## 9. Cleanup and documentation

- [x] 9.1 Remove `PageSection`, `FormSection` and their stories, now unused. Verify: grep finds no import, and the catalogue test and the build pass.
- [x] 9.2 Finish the design guide:
  - `README.md`: the composites table and do/don't;
  - `patterns.md`: all templates;
  - `copy.md`: optional labels, error summary wording and notices.

  Mark `layout-audit.md` as resolved by this change. Verify: `design-docs.test.ts` passes, and the guide describes every composite in the catalogue.
- [x] 9.3 Review the whole diff with `doc-updater` and `code-reviewer`. Fix CRITICAL/HIGH findings.

## 10. Verification

- [x] 10.1 Run `verification-loop`: build, typecheck, lint, unit tests with coverage (≥ 80 % for the new composites), the Storybook a11y run, `check-i18n` and the security grep. Verify: a PASS report.
- [x] 10.2 Run the full Playwright suite against the compose stack with the synthetic seed (all projects), then capture the 17 routes at 360, 1280, 1920 and 2560 px in both themes and compare them with the demo. Verify: the suite is green and the captures show no horizontal scroll and no clipped Valencian label.
- [x] 10.3 Run `pr-test-analyzer` and `e2e-runner` (Playwright only) over the change's critical flows: list filters, editing a section, registering with errors and then success, and deletion from "More actions". Fix the gaps they report. Verify: the agents report no critical gap.
