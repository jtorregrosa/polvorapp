# Tasks

## 1. Research

- [x] 1.1 Check with Context7, and with `gh search code` for real-world use, what D1–D3 and D7 rely
  on:
  - the shadcn/ui sidebar `collapsible="icon"`;
  - `SidebarMenuButton` `tooltip`;
  - `SidebarGroup` and `SidebarGroupLabel` semantics;
  - the Radix Tooltip open-on-focus behaviour and Escape dismissal;
  - the Radix DropdownMenu focus return to its trigger;
  - Tailwind v4 `motion-reduce:` variants on transitions.

  Record versions and any workaround in design.md. Verify: design.md is updated, with no new open
  question.

## 2. Tokens and composites

- [x] 2.1 Write `contrast.test.ts` cases for `--tag-{1..4}` and `--tag-neutral` (4.5:1, both themes),
  then add the tokens to `tokens.css` and `docs/design/tokens.md` (D5). Verify: the contrast and
  design-docs tests pass.
- [x] 2.2 Write tests and stories for `Tag` and `CategoryTag`, then implement them with the mappings
  in `tags.ts` (D5):
  - side, role, weapon kind and yes/no in three languages;
  - no icon;
  - axe in both themes;
  - an unknown value shows a neutral tag and a development warning.

  Verify: the tests and the catalogue test pass.
- [x] 2.3 Write `StatusBadge` tests for `twoFactor` `ENABLED` and `NOT_SET`, then add the mapping
  and the `ui:status.twoFactor.*` texts (D6). Verify: the tests pass.
- [x] 2.4 Write `PhotoUpload` tests for `variant="picture"`, then implement it (D7):
  - with an image, the trigger is named "{{label}}, options" and opens a menu with Replace and
    Remove;
  - without an image, the trigger opens the file chooser;
  - the overlay shows on hover and focus;
  - focus returns to the trigger after the menu, the crop dialog and the confirmation;
  - touch works without hover;
  - axe;
  - `variant="section"` is unchanged.

  Verify: the tests, the existing PhotoUpload tests and the stories pass.
- [x] 2.5 Update `DataTable`'s documentation and add a test for a cell link: clicking it opens its
  own target, and another cell opens the record. Update `docs/design/patterns.md`:
  - tags vs statuses;
  - picture actions;
  - links in rows;
  - every orderable column sortable;
  - muted missing values.

  Verify: the test passes.
- [x] 2.6 Review group 2 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 3. Shell: sections, icon rail and motion

- [x] 3.1 Write `AppLayout` and `AppShell` tests for the sections (D4):
  - the order and the labels;
  - named groups;
  - an empty section hidden for FiringChiefs;
  - the current entry on `/editions/:id/orders`, `/exports` and `/distribution`.

  Then add `section` to `NavigationEntry` and render the groups. Verify: the tests and axe pass.
- [x] 3.2 Write tests for the icon rail (D1):
  - the trigger collapses to icons;
  - each entry keeps its accessible name;
  - the tooltip shows on focus;
  - a counter becomes a dot with the count in the name;
  - comparsa cards show only the logo;
  - the version is hidden;
  - every entry is reachable by keyboard;
  - phones still get the drawer.

  Then switch to `collapsible="icon"` with the local edits in `sidebar.tsx`. Verify: the tests and
  axe pass in both themes.
- [x] 3.3 Write tests for the remembered state (D2): the state is restored after a remount, and a
  throwing `localStorage` falls back to expanded. Then implement it. Verify: the tests pass.
- [x] 3.4 Update `motion.test.ts` for the sidebar exception (D3): the tokenised width transition
  only in the allow-listed class list, labels on opacity, `none` under reduced motion. Then restore
  the transitions with tokens and add the reduced-motion rule in `globals.css`. Update
  `docs/design/README.md`: the "Sidebar" and "Motion" local edits, superseding D5's "never
  animates" note. Verify: the motion tests pass.
- [x] 3.5 Add the `ui:nav.sections.*`, `nav.expand` and `nav.collapse` texts in es-ES,
  ca-ES-valencia and en. Verify: `npm run check-i18n` passes.
- [x] 3.6 Review group 3 in parallel with `react-reviewer`, `a11y-architect` and
  `typescript-reviewer`. Fix CRITICAL/HIGH findings.

## 4. Screens

- [ ] 4.1 Write `UsersPage` tests: the role as a tag, two-step verification as a status, "never"
  muted, every column sortable. Then implement them. Update `UserDetailPage` to show the same tag
  and status. Verify: the tests and axe pass.
- [ ] 4.2 Write `ComparsasPage` and `ComparsaDetailPage` tests for the side tag. Then implement it.
  Verify: the tests pass.
- [ ] 4.3 Write `WeaponModelsPage` tests (D8):
  - the default request includes inactive models;
  - `?onlyActive=true` sends no flag and is kept in the address;
  - kind, side and rentable shown as tags;
  - the inactive status badge.

  Then implement them, and the detail page's tags. Verify: the tests pass.
- [ ] 4.4 Write `ComparsaLogoSection` and `FederationLogoSection` tests for the picture actions
  (Admin: menu with replace and remove, add from the placeholder, removal confirmed; FiringChief:
  no trigger). Then switch `LogoUpload` to `variant="picture"`. Verify: the tests and axe pass.
- [ ] 4.5 Move the tag labels to `ui:tag.*` and the weapon filter text to
  `catalog:weaponModels.filters.onlyActive` in the three locales, and remove the unused keys.
  Verify: `npm run check-i18n` and lint pass.
- [ ] 4.6 Review group 4 in parallel with `react-reviewer` and `a11y-architect`. Fix CRITICAL/HIGH
  findings.

## 5. End-to-end and verification

- [ ] 5.1 Extend the Playwright specs on the seeded stack:
  - collapse the sidebar, reload, expand with Ctrl+B, and check the tooltip and accessible names;
  - with reduced motion emulated, the width changes at once;
  - Distribution is current on the edition's distribution page;
  - replace a comparsa logo from the picture menu by keyboard;
  - the weapon catalogue shows an inactive model by default;
  - axe on the collapsed shell in both themes.

  Verify: the specs pass on the compose stack, and the full suite passes twice in a row.
- [ ] 5.2 Run `verification-loop`:
  - build, types and lint;
  - frontend tests with at least 80 % coverage on the changed composites and pages;
  - a diff review.

  Run `e2e-runner` and `pr-test-analyzer` on the change. Verify: the PASS report, with findings and
  follow-ups recorded in design.md.
