# Tasks

## 1. Insignia tokens

- [x] 1.1 Check with Context7 how Tailwind v4 handles `@theme inline` colour mappings, `uppercase`,
  `tracking-wider` and `text-balance`. Check in the installed shadcn/ui `sidebar.tsx` that a
  `shrink-0` sibling between `SidebarContent` and `SidebarFooter` stays pinned while the content
  scrolls (D2). Verify: design.md has no new open question, and any workaround is recorded there.
- [x] 1.2 Add to `contrast.test.ts` the pair `sidebar-armband-foreground` on `sidebar-armband`
  (`TEXT`) and `sidebar-armband` on `sidebar` (`UI`), and see them fail. Then add
  `--sidebar-armband`, `--sidebar-armband-foreground` and `--sidebar-armband-edge` to both theme
  blocks of `tokens.css`, with their `@theme inline` mappings (D5). Verify: the contrast test passes
  in both themes.
- [x] 1.3 Describe the three tokens in `design-docs.ts` `TOKEN_ROLES` as "insignia only, never an
  accent or a status". Regenerate `docs/design/tokens.md` with `npm run docs:design`. Verify: the
  design-docs test passes and the generated page lists the tokens.
- [x] 1.4 Review group 1 with `a11y-architect` (contrast and token roles). Fix CRITICAL/HIGH
  findings.

## 2. Armband in the layout composite

- [x] 2.1 Write `AppLayout.test.tsx` cases for the `armband` prop (platform: FiringChief armband)
  and see them fail:
  - with a label: the text shows below the navigation and above the footer, outside every `nav`,
    and is not focusable (tabbing through the sidebar never lands on it);
  - without a label: no armband element;
  - in the icon rail: the element is still rendered, with `aria-hidden="true"` and no tooltip;
  - in the drawer on a phone: the text is shown and not hidden from assistive technology;
  - axe passes expanded and in the rail.
- [x] 2.2 Implement the armband in `AppLayout` (D1–D4): the optional `armband?: string` prop
  ("already translated"); a `shrink-0` block between `SidebarContent` and `SidebarFooter` reaching
  both edges; the label in a `data-sidebar-label` span with `SIDEBAR_LABEL`; `aria-hidden` while
  `useIconRail()`; the token classes, 1 px edges, `uppercase`, `tracking-wider` and wrapping. No
  arbitrary values. Verify: the 2.1 tests pass, and lint passes, including the primitives guard.
- [x] 2.3 Add the armband to the `AppLayout` stories: a FiringChief story with comparsa cards and the
  armband, `LongValencian` with "Cap de disparada", and `IconRail`, in both themes. Verify: the
  Storybook axe checks pass, and the stripe in the rail sits where the band was, with nothing moving
  when toggling.
- [x] 2.4 Describe the armband in the `AppLayout` row of `docs/design/README.md`, and add the
  armband's token rule (insignia only, never an accent or a status) to the colour rules. Verify: the
  guide names the prop, the rail behaviour and the tokens.
- [x] 2.5 Review group 2 in parallel with `react-reviewer`, `typescript-reviewer` and
  `a11y-architect`. Fix CRITICAL/HIGH findings.

## 3. Armband for FiringChiefs in the shell

- [x] 3.1 Write `src/app/AppLayout.test.tsx` cases and see them fail:
  - a FiringChief session shows the armband with "Jefe de disparo" in es-ES;
  - it changes to "Cap de disparada" at once after switching to ca-ES-valencia;
  - an Admin session shows no armband;
  - a FiringChief with no comparsa cards still shows it;
  - the bottom navigation bar on a phone holds no armband.
- [x] 3.2 Pass `armband={tIdentity('roles.FIRING_CHIEF')}` from `AppShell` only when the role is
  `FIRING_CHIEF` (D1), reusing the existing key with no new translations. Verify: the 3.1 tests pass,
  and `npm run check-i18n` reports no missing or unused key.
- [x] 3.3 Add the armband (Brazalete de jefe de disparo → `armband`) to `docs/glossary.md`, as the
  sidebar insignia that mirrors the physical one. Verify: the glossary row exists and the code uses
  the term.
- [x] 3.4 Review group 3 with `react-reviewer` and `security-reviewer` (to confirm the armband grants
  nothing and shows only the user's own role). Fix CRITICAL/HIGH findings.

## 4. End-to-end and verification

- [x] 4.1 Extend the Playwright specs on the seeded stack (`layout.spec.ts` / `navigation.spec.ts`,
  signed in as the seeded FiringChief and Admin):
  - a FiringChief sees "JEFE DE DISPARO" at the bottom of the sidebar, and it stays visible after
    scrolling the navigation in a short viewport;
  - after collapsing, it is a stripe with no accessible text;
  - at 360 px the drawer shows it;
  - an Admin sees none;
  - axe on the FiringChief shell, expanded and collapsed, in both themes.

  Verify: the specs pass on the compose stack (built from PowerShell), twice in a row.
- [x] 4.2 Run `verification-loop`:
  - build, types and lint;
  - frontend tests with at least 80 % coverage on `AppLayout` and `AppShell`;
  - a security grep and a diff review.

  Then run `e2e-runner` and `pr-test-analyzer` on the change. Verify: a PASS report, with any
  findings and follow-ups recorded in design.md.
