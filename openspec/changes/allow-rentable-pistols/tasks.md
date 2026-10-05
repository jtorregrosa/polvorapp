# Tasks

## 1. Backend

- [x] 1.1 Update the `WeaponModelManagementTests` integration tests:
  - creating a rentable `PISTOL` stores it;
  - editing a pistol to rentable is audited with its previous and new values;
  - omitting `rentable` is still `400` naming `rentable`.

  Remove the "Rentable pistol is blocking" test. Then drop the `pistolNotRentable` validation from
  `WeaponModelEndpoints`. Verify: the catalogue tests pass.
- [x] 1.2 Remove the check constraint from `FederationCatalogDbContext` and add the
  `AllowRentablePistols` migration (D1). Add an integration test that saves a rentable pistol
  through the context against PostgreSQL. Verify: `dotnet ef migrations has-pending-model-changes`
  reports none and the test passes.
- [x] 1.3 Add integration tests:
  - an edition offers a rentable pistol, while a non-rentable pistol is still `notRentable`;
  - an order entry rents the offered pistol and counts in totals and billing;
  - the rental company export lists it.

  Verify: the tests pass with no production code change beyond comments.
- [x] 1.4 Update the XML docs and comments in `WeaponKind`, `ICatalogDirectory`,
  `WeaponModelContracts` and `CatalogSeeder` (the seed stays as it is). Verify: build and
  architecture tests pass.
- [x] 1.5 Review group 1 in parallel with `csharp-reviewer` and `database-reviewer`. Fix
  CRITICAL/HIGH findings.

## 2. Frontend

- [x] 2.1 Update `WeaponModelPages.test.tsx`:
  - a pistol is created as rentable;
  - switching the kind to `PISTOL` keeps the rentable choice;
  - editing a pistol shows its rentable flag.

  Then update `WeaponModelFields`, `WeaponModelDetailPage` and `weaponModelSchema` (D3). Verify:
  the tests and axe pass.
- [x] 2.2 Remove `pistolNotRentable` from `problems.ts` and its test, and rewrite the pistol hint in
  es-ES, ca-ES-valencia and en. Verify: `npm run check-i18n`, lint and typecheck pass.
- [x] 2.3 Review group 2 with `react-reviewer`. Fix CRITICAL/HIGH findings.

## 3. Docs and verification

- [x] 3.1 Update the docs:
  - `docs/data-model.md`: BR-07 and the `WeaponModel` line;
  - `docs/open-questions.md`: a Q-07 note recording the maintainer decision;
  - `docs/development.md`: the seed note.

  Verify: no remaining "never rentable" in docs or specs outside the archive (`grep`).
- [x] 3.2 Run `verification-loop` (build, types, lint, backend and frontend tests, a diff review)
  and the weapon-model and order Playwright specs. Run `pr-test-analyzer`. Verify: the PASS
  report.

  Result (2026-10-05): PASS. The build, types, lint and format are clean. The backend suite passes
  with 96 % line coverage; the only red is the Testcontainers PostgreSQL container failing to
  dispose at the end of the run, outside any test. The frontend has 2,613 tests passing with 95.6 %
  line coverage. The Playwright suite passes. `pr-test-analyzer` found every scenario covered, and
  its MEDIUM gap is now tested: a rollback with only non-rentable pistols succeeds.
