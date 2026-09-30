# Tasks

## 1. Research and dependencies

- [x] 1.1 Verify with Context7 (+ gh for real-world usage) the .NET 10 APIs used in design D2–D6: `IdentityUserContext<TUser,TKey>`, `SignInManager` two-factor/remember-client flow, `CheckPasswordSignInAsync`, `DisableCookieRedirect`, `SecurityStampValidatorOptions`, `IAntiforgery` with minimal APIs, `AddRateLimiter`, `UseForwardedHeaders`, EF Core Data Protection key store, EF Core per-schema migrations history; record any deviation in design.md; verify by the updated design.md
- [x] 1.2 Choose and pin backend packages (EF Core + `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, `EFCore.NamingConventions` if current, `MailKit`, `dotnet-ef` local tool) in `Directory.Packages.props`; verify `dotnet build` succeeds
- [x] 1.3 Choose and pin frontend packages (QR library producing SVG, `otpauth` dev dependency for E2E) with Context7/npm; verify `npm run build` succeeds
- [x] 1.4 Add every new dependency and the common-password list source to `docs/third-party-licenses.md`; verify each new direct dependency has a row
- [x] 1.5 Review group 1 with `code-reviewer`; fix CRITICAL/HIGH findings

## 2. Persistence, migrations and audit trail foundation

- [x] 2.1 Write SharedKernel tests for `AuditEntry` mapping helper and the append-only guard (modified/deleted `AuditEntry` throws on save), then implement `SharedKernel/Auditing` (`AuditEntry`, `IAuditTrail`, `AddAuditTrail()` model extension) and `SharedKernel/Persistence` (`AddModuleDbContext`, `IDatabaseMigrator`); verify tests pass
- [x] 2.2 Create `Modules/AuditPrivacy` projects, `AuditDbContext` (schema `audit`, own history table) and its initial migration; register the module; verify architecture tests and `ModuleRegistrationTests` pass
- [x] 2.3 Write integration tests for the host `migrate` command (applies every module's migrations in order on an empty Testcontainers database; idempotent on rerun; non-zero exit on failure), then implement it; verify tests pass
- [x] 2.4 Add the one-shot `api-migrate` service to `compose.yaml` and make `api` depend on it; update the Testcontainers fixture to run migrations; verify `docker compose up --build` reaches healthy and existing E2E tests pass
- [x] 2.5 Update `backend/src/Modules/README.md` (DbContext per schema, `AddModuleDbContext`, audit entry in the same transaction) and `docs/development.md` (`migrate`, adding a migration); verify the documented commands run as written
- [x] 2.6 Review group 2 in parallel with `csharp-reviewer` and `database-reviewer`; fix CRITICAL/HIGH findings

## 3. Transactional email (platform)

- [x] 3.1 Write tests for email options validation (missing SMTP host / public base URL fails startup naming the setting, not the value) and for the SMTP sender against a Mailpit Testcontainer (plain-text part present, failure raises a typed exception, nothing of the message logged), then implement `SharedKernel/Email` and `Platform/Email` with MailKit; verify tests pass
- [x] 3.2 Add `Email__*` and `App__PublicBaseUrl` to `.env.example`, compose and `ConfigurationValidationTests`; verify `docker compose up` sends to Mailpit with a manual smoke send in a test
- [x] 3.3 Review group 3 in parallel with `csharp-reviewer` and `silent-failure-hunter`; fix CRITICAL/HIGH findings

## 4. Identity module: users, authentication and session plumbing

- [x] 4.1 Create `Modules/IdentityAccess` projects with `User`, `UserRole`, derived `UserStatus`, `IdentityDbContext` (schema `identity`, Data Protection keys, audit trail mapped and excluded) and the initial migration; write a test asserting the audit entity is excluded from this context's migrations; verify tests and architecture tests pass
- [x] 4.2 Write tests for the password policy (12–128 chars, not the email, common-password list, no composition rules), then implement the validator and embedded list (name the exact SecLists file and commit in `docs/third-party-licenses.md` and ship its MIT notice next to the resource); verify tests pass
- [x] 4.3 Write tests for the replay-safe authenticator token provider (same time step rejected, next step accepted, ±1 step tolerated), then implement it; verify tests pass
- [x] 4.4 Configure in the host: Identity (lockout 5/15 min, unique email, token lifespans), application / two-factor / remember-me cookies (D6), `ValidationInterval = 0`, absolute 12 h limit, `CanSignInAsync` rejecting inactive users, `DisableCookieRedirect` on `/api`, fallback authorization policy and `Admin` policy, and mark health/system/openapi endpoints anonymous; write tests that a protected probe endpoint returns 401 problem details and health/system stay anonymous; verify tests pass
- [x] 4.5 Write tests for anti-forgery (non-GET without token → 400 problem details, with token → passes, GET unaffected), then implement `GET /api/auth/antiforgery` and the `/api` group filter; verify tests pass
- [x] 4.6 Write tests for rate limiting (`auth` and `auth-email` policies → 429 problem details) and forwarded headers (only trusted proxy honoured), then implement them; verify tests pass
- [x] 4.7 Write tests for `ICurrentUser` and `IComparsaScope` (Admin → all; FiringChief without assignments → none; with a fake assignment source → only assigned), then implement them with the default empty `IFiringChiefAssignmentSource`; verify tests pass
- [x] 4.8 Review group 4 in parallel with `csharp-reviewer`, `security-reviewer` and `database-reviewer`; fix CRITICAL/HIGH findings

## 5. Sign-in, enrolment and recovery flows (API)

- [x] 5.1 Write integration tests for `POST /api/auth/login` (wrong password, unknown email and deactivated user give the same 401; not enrolled → `ENROL`; enrolled → `SECOND_FACTOR`; remembered device → `DONE`; lockout after 5 failures; audit entries `SignInFailed`/`LockedOut` without password), then implement; verify tests pass
- [x] 5.2 Write tests for `/login/second-factor` and `/login/recovery-code` (valid TOTP signs in and sets `LastSignInAt`; replayed code rejected; remember-device cookie issued only when asked; recovery code single-use with remaining count; failures count towards lockout; audited), then implement; verify tests pass
- [x] 5.3 Write tests for `GET/POST /api/auth/enrolment` (only for 2FA-pending users without 2FA; wrong code rejected; success enables 2FA, returns 10 codes once and signs in; audited), then implement; verify tests pass
- [x] 5.4 Write tests for `POST /api/auth/logout` and session expiry (idle 60 min and absolute 12 h with a fake `TimeProvider`; half-signed-in principal gets 401 on protected endpoints; 2FA-disabled user rejected), then implement; verify tests pass
- [x] 5.5 Write tests for password reset (`forgot` always 202 and emails only `ACTIVE` users in their locale; `reset` with expired/used token rejected; success updates security stamp so sessions and remembered devices end and next sign-in needs the second factor; audited), then implement with localized email templates (`.resx` in es-ES, ca-ES-valencia, en); verify tests pass and the email appears in Mailpit in the test
- [x] 5.6 Review group 5 in parallel with `csharp-reviewer`, `security-reviewer` and `silent-failure-hunter`; fix CRITICAL/HIGH findings

## 6. Invitations, user management and account (API)

- [x] 6.1 Write tests for `POST /api/users` (Admin invites → `INVITED` user + localized email; duplicate email case-insensitive → 409; invalid role/locale → 400; FiringChief → 403; SMTP failure → 502 `email.sendFailed` with user kept; audited), then implement with the invitation token provider; verify tests pass
- [x] 6.2 Write tests for `GET /api/auth/invitations/validate` and `POST /api/auth/invitations/accept` (valid → name/email and `ENROL`; expired, used, replaced or deactivated → 410; policy violations → 400), then implement; verify tests pass
- [x] 6.3 Write tests for `GET /api/users` (filters by role/status, derived status, 2FA state, last sign-in; FiringChief → 403) and `GET /api/users/{id}`, then implement; verify tests pass
- [x] 6.4 Write tests for `PUT /api/users/{id}`, deactivate, reactivate, resend invitation and 2FA reset (last-Admin guard 409 including two concurrent demotions; deactivation and role change effective on the target's next request; 2FA reset forces enrolment and forgets remembered devices; resend only for `INVITED`; every action audited with previous/new values), then implement; verify tests pass
- [x] 6.5 Write tests for `/api/account` endpoints (profile, locale update, password change with wrong current password rejected and other sessions ended, recovery-code regeneration requiring a valid TOTP, sign out everywhere; audited), then implement; verify tests pass
- [x] 6.6 Write tests for the `create-admin` host command (creates invited Admin and sends email on an empty database; refused with non-zero exit when an Admin exists), then implement; verify tests pass
- [x] 6.7 Write an OpenAPI test listing the exact set of anonymous endpoints and asserting no endpoint updates or deletes audit entries; regenerate `contracts/openapi.json`; verify the contract check passes
- [x] 6.8 Add backend problem titles and email texts to the three cultures and a test that every `.resx` key exists in all three; verify tests pass
- [x] 6.9 Review group 6 in parallel with `csharp-reviewer`, `security-reviewer` and `database-reviewer`; fix CRITICAL/HIGH findings

## 7. Synthetic users

- [x] 7.1 Write tests for `IdentitySeeder` (deterministic users: one Admin, two FiringChiefs, one invited, one deactivated, `.example` addresses; fails naming the missing `Seed__*` setting), then implement; add `Seed__UserPassword` and `Seed__AuthenticatorKey` placeholders to `.env.example` and compose; verify tests pass and gitleaks does not flag the placeholders
- [x] 7.2 Review group 7 with `csharp-reviewer` and `security-reviewer` (SEC-11); fix CRITICAL/HIGH findings

## 8. Frontend foundation: session, API mutator and composites

- [x] 8.1 Add the `identity` namespace (es-ES, ca-ES-valencia, en) with the D11 keys, `common` keys `nav.users`, `nav.account`, `forbidden.*`, and extend the typed-keys declaration; verify `npm run check-i18n` and `npm run typecheck` pass
- [x] 8.2 Write tests for the API mutator (fetches anti-forgery token lazily, sends `X-XSRF-TOKEN` on non-GET only, retries once after an anti-forgery 400, 401 on a protected call triggers the session-expired handler), then implement in `src/api/http.ts`; verify tests pass
- [x] 8.3 Write tests for `useSession`, `RequireSession` (redirect with `returnTo`) and `RequireRole` (renders `ForbiddenPage`), then implement them; verify tests pass
- [x] 8.4 Write tests and stories for `PublicLayout`, `UserMenu`, `QrCode` (accessible label, CSP-safe rendering) and `RecoveryCodeList` (copy, print, axe clean in both themes), then implement them; verify tests and `catalogue.test.tsx` pass
- [x] 8.5 Add user statuses (`INVITED`, `ACTIVE`, `DEACTIVATED`) to the status mapping with tests, and to `docs/design/status.md`; verify tests and `design-docs.test.ts` pass
- [x] 8.6 Review group 8 in parallel with `react-reviewer`, `typescript-reviewer`, `a11y-architect` and `security-reviewer` (mutator, token handling); fix CRITICAL/HIGH findings

## 9. Frontend screens

- [x] 9.1 Write tests (behaviour + axe) for `SignInPage`, `SecondFactorPage` and `RecoveryCodePage` (generic error, lockout message, remember-device checkbox, `returnTo` honoured, remaining recovery codes shown), then implement them with MSW handlers; verify tests pass
- [x] 9.2 Write tests for `EnrolmentPage` and `RecoveryCodesPage` (QR and manual key, wrong code message, codes shown once), then implement; verify tests pass
- [x] 9.3 Write tests for `AcceptInvitationPage`, `ForgotPasswordPage` and `ResetPasswordPage` (invalid-link state, password rules, identical confirmation), then implement; verify tests pass
- [x] 9.4 Update shell tests (user menu, role-filtered navigation, public layout for signed-out routes, forbidden page), then wire routes, `AppShell`, navigation `roles` and sign-out; verify tests pass
- [x] 9.5 Write tests for language at sign-in and saving on switch (`PUT /api/account/locale` only when signed in), then implement; verify tests pass
- [x] 9.6 Write tests for `AccountPage` (password change, recovery-code regeneration, sign out everywhere with `ConfirmDialog`), then implement; verify tests pass
- [x] 9.7 Write tests for `UsersPage`, `InviteUserPage` and `UserDetailPage` (DataTable with filters, invite form validation and 409 message, destructive actions confirmed, last-Admin error shown, `email.sendFailed` with resend), then implement; verify tests pass
- [x] 9.8 Review translations in the three locales (`i18n-sync`), flag new Valencian texts for native review; verify `npm run check-i18n` and lint pass
- [x] 9.9 Review group 9 in parallel with `react-reviewer`, `a11y-architect` and `typescript-reviewer`; fix CRITICAL/HIGH findings

## 10. End-to-end tests

- [x] 10.1 Add Playwright helpers: seeded credentials from env, TOTP with `otpauth`, Mailpit API client for invitation/reset links; update existing platform/shell/theme/language E2E tests to sign in first; verify the existing suite passes against the compose stack
- [x] 10.2 Write E2E flows: Admin invites a FiringChief → invitee accepts, enrols and signs in; sign-in with TOTP and with remembered device; password reset by email; Admin deactivates a signed-in user who is then signed out; FiringChief cannot see Users; axe on sign-in, enrolment and users pages in both themes; verify the suite passes against the compose stack
- [x] 10.3 Review group 10 with `e2e-runner` (Playwright only, ADR-0011) and `pr-test-analyzer`; fix CRITICAL/HIGH findings

## 11. Documentation

- [x] 11.1 Update `docs/glossary.md` (user status terms `INVITED`/`ACTIVE`/`DEACTIVATED`, `AuditEntry`), `docs/data-model.md` (User fields, derived status, audit entry) and add a dated clarification note to ADR-0004 on 30-day remembered devices; verify links resolve
- [x] 11.2 Update `docs/development.md` (signing in locally with seeded users and an authenticator, Mailpit, `create-admin`, `migrate`), `README.md` quick start and `docs/mvp.md` status of change #3; verify the quick start works on a fresh `docker compose up`
- [x] 11.3 Review group 11 with `doc-updater` for drift between docs and code; fix findings

## 12. Verification

- [ ] 12.1 Run `verification-loop` (backend build/format/tests with coverage ≥ 80 % for the new modules, frontend lint/typecheck/check-i18n/tests/build, contract check, security grep for secrets and logged tokens, diff review) and the full Playwright suite against compose; verify a PASS report and green CI on the pull request
