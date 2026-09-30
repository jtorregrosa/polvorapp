# Tasks

## 1. Decision record and research

- [x] 1.1 Write `docs/adr/0012-polvorapp-visual-identity.md` (own identity, no Federation assets in git, customer logo only at deploy time, supersedes ADR-0009 §5), set ADR-0009 status to "Accepted — §5 superseded by 0012" and update `docs/adr/README.md`; verify the index links resolve
- [x] 1.2 Verify current versions and setup with Context7/npm for Tailwind CSS v4 + `@tailwindcss/vite`, shadcn/ui CLI (React 19, Vite, Tailwind v4), Storybook (React + Vite), `lucide-react`, `@fontsource-variable/inter`, `@tanstack/react-table`, React Hook Form + resolvers + Zod, `prettier-plugin-tailwindcss`, `eslint-plugin-better-tailwindcss`; record pinned versions and the D7 plugin decision in design.md; verify by the updated design.md
- [x] 1.3 Add the new dependencies to `docs/third-party-licenses.md` (OFL-1.1 font assessed as acceptable); verify every new direct dependency has a row
- [x] 1.4 Review group 1 with `code-reviewer`; fix CRITICAL/HIGH findings

## 2. Tokens and theme foundation

- [x] 2.1 Install Tailwind CSS v4 with the Vite plugin, create `src/styles/globals.css` and `src/styles/tokens.css` with the D1 light/dark tokens, Inter Variable and `color-scheme`; verify `npm run build` succeeds and the E2E suite still passes against the rebuilt stack
- [x] 2.2 Write `src/styles/contrast.test.ts` with the declared pairs (D3) and a probe proving a low-contrast pair fails with theme, pair and ratio; tune token values until all pairs pass; verify tests pass
- [x] 2.3 Write tests for `ThemeProvider` (system default, explicit choice remembered, storage failure tolerated, reacts to system changes), then implement it and `public/theme-init.js` loaded synchronously from `index.html`; verify tests pass and a reload in the E2E stack shows no theme flash (dark class present before first paint)
- [x] 2.4 Add `prettier-plugin-tailwindcss` and format the codebase; verify `npm run format:check` passes
- [x] 2.5 Review group 2 in parallel with `react-reviewer` and `a11y-architect`; fix CRITICAL/HIGH findings

## 3. Primitives and guardrails

- [x] 3.1 Initialise shadcn/ui (`components.json`, `cn` package) and add the D2 primitives mapped to our tokens; verify `npm run typecheck` and `npm run build` succeed
- [x] 3.2 Extend `scripts/eslint-guardrails.test.mjs` with failing probes for: feature importing `@/components/ui/*`, arbitrary value `p-[7px]`, raw palette `bg-emerald-600`, `style` prop in a feature, and passing probes for a composite importing a primitive and a token class; verify the new probes fail
- [x] 3.3 Implement the D7 rules in `eslint.config.js` (or `eslint-plugin-better-tailwindcss` if chosen in 1.2); verify the probes pass and `npm run lint` passes on the codebase
- [x] 3.4 Review group 3 in parallel with `typescript-reviewer` and `code-reviewer`; fix CRITICAL/HIGH findings

## 4. Status semantics and simple composites

- [x] 4.1 Add the `ui` namespace in es-ES, ca-ES-valencia and en with the D9 keys and extend the typed-keys declaration; verify `npm run check-i18n` and `npm run typecheck` pass
- [x] 4.2 Write tests for `status.ts` + `StatusBadge` (every D6 value renders label + icon + tone; `EXPIRED` in Valencian; compliance warnings never destructive; unknown value neutral with raw code; axe clean in both themes), then implement them; verify tests pass
- [x] 4.3 Write tests for `PageHeader`, `EmptyState`, `AlertBanner` (roles by severity) and `StatCard`, then implement them; verify tests pass
- [x] 4.4 Write tests for `ConfirmDialog` (action-named destructive button, cancel focused first, Escape closes without action, focus returns to the trigger, axe clean), then implement it; verify tests pass
- [x] 4.5 Restyle `LanguageSwitcher` on the `select` primitive keeping its existing tests green, and write tests for `ThemeSwitcher` (three translated options, choice applied), then implement it; verify tests pass
- [x] 4.6 Review group 4 in parallel with `react-reviewer` and `a11y-architect`; fix CRITICAL/HIGH findings; flag new Valencian texts for native review

## 5. Forms and tables

- [x] 5.1 Add React Hook Form, `@hookform/resolvers` and Zod; write tests for `FormField`/`FormSection` (label, required marker, help, translated error linked by `aria-describedby`, focus on first invalid field after submit, axe clean), then implement them; verify tests pass
- [x] 5.2 Add TanStack Table; write tests for `DataTable` (sorting with announced direction, pagination summary translated, loading skeleton, empty state, horizontal scroll container, axe clean), then implement it; verify tests pass
- [x] 5.3 Review group 5 in parallel with `react-reviewer`, `typescript-reviewer` and `a11y-architect`; fix CRITICAL/HIGH findings

## 6. Application layout (platform shell, MODIFIED)

- [x] 6.1 Update shell tests for the new layout: sidebar with PolvorApp mark, navigation with current item, API version and "version unavailable" in the sidebar, top bar with breadcrumbs and both switchers, skip link and focus-on-navigation preserved, not-found and error pages inside the layout, axe clean in both themes; verify they fail against the old shell
- [x] 6.2 Implement `AppLayout`, `Breadcrumbs` (route `handle.breadcrumb`), `src/app/navigation.ts`, mobile navigation drawer; replace `AppShell`, delete `shell.css` and remove the TEMPORARY legacy-class ignore from `eslint.config.js`; restyle `HomePage`, `NotFoundPage` and `ErrorPage` with `PageHeader`/`EmptyState`; verify tests pass
- [x] 6.3 Create the PolvorApp mark (`public/icon.svg` + sidebar component), regenerate PWA icons and set manifest colours from the tokens; verify `npm run build` emits the icons and the manifest has the new colours
- [x] 6.4 Update E2E: shell and navigation on desktop and 360 px (drawer opens, traps focus, closes with Escape), theme switch persists across reload without flash, axe in both themes, CSP fixture green; apply D10 if style injection is reported (narrow the `unsafe-inline` assertion to `script-src` and adjust nginx `style-src` only if needed); verify the suite passes against the compose stack
- [x] 6.5 Review group 6 in parallel with `react-reviewer`, `a11y-architect` and `security-reviewer` (CSP); fix CRITICAL/HIGH findings

## 7. Component catalogue

- [x] 7.1 Set up Storybook (React + Vite) with locale, theme and provider decorators and the a11y addon; add `storybook` and `build-storybook` scripts; verify `npm run build-storybook` succeeds
- [x] 7.2 Write stories for every composite (default, states, long Valencian labels, both themes); verify they render in Storybook
- [x] 7.3 Write `catalogue.test.tsx` (every story rendered in both themes with zero axe violations; every composite file has a stories file, failing with its name) and a probe proving a composite without stories fails; verify tests pass
- [x] 7.4 Add `build-storybook` to the CI `frontend` job; verify with `actionlint` and a green CI run
- [x] 7.5 Review group 7 with `code-reviewer` and `pr-test-analyzer`; fix CRITICAL/HIGH findings

## 8. Design guide and documentation

- [x] 8.1 Write `docs/design/` (`README.md`, `tokens.md`, `status.md`, `patterns.md`, `copy.md`) per design D12, including the D10 CSP outcome and any local primitive edits; verify every token and status in code appears in the guide
- [x] 8.2 Update `docs/README.md` (design guide row), `docs/development.md` (Storybook, theme, guardrails), `openspec/config.yaml` context wording for the guide and `docs/mvp.md` status of change #2; verify links resolve
- [x] 8.3 Review group 8 with `doc-updater` for drift between guide and code; fix findings

## 9. Verification

- [x] 9.1 Run `verification-loop` (build, types, lint incl. guardrails, i18n check, unit + catalogue tests with coverage ≥ 80 %, Storybook build, security grep, diff review) and the Playwright suite on the compose stack; verify a PASS report and a green CI run on the pull request
- [x] 9.2 Check every scenario in `specs/design-system/spec.md` and the modified `specs/platform/spec.md` maps to at least one passing test or CI check (`pr-test-analyzer`); verify no scenario is uncovered
- [ ] 9.3 Maintainer review of the palette and components in the catalogue (light/dark, three languages); verify approval is recorded in the pull request before archiving
