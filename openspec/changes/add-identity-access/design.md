# Design

## Context

After `bootstrap-platform` and `add-design-system` the API host has platform endpoints only, no
module, no EF Core and no authentication; `Modules/README.md` already fixes the persistence
convention for this change (one DbContext and one PostgreSQL schema per module, migrations applied
by a host `migrate` command). The UI has the design-system shell, a generated API client whose
mutator (`src/api/http.ts`) sends same-origin cookies, and nginx proxying `/api` on the same origin.
Compose already runs Mailpit. See `proposal.md` for motivation and the specs for behaviour:
`specs/identity-access`, `specs/audit-privacy`, `specs/platform`.

Constraints: ADR-0004 (Identity, cookies, TOTP, invitation-only), ADR-0001 module boundaries
(enforced by architecture tests), ADR-0007 (culture-independent codes, localised texts), SEC-11
(synthetic data only), NFR-12 (no personal data in logs), one maintainer (few moving parts).

Maintainer decisions taken while proposing: audit table owned by a minimal `AuditPrivacy` module;
TOTP with an optional **30-day remembered device**; **self-service** password reset by email.

## Goals / Non-Goals

**Goals:**
- A reusable authentication/authorization foundation: later modules only declare endpoints and
  read `ICurrentUser` / `IComparsaScope`; secure defaults need no per-endpoint work.
- The persistence and audit conventions every later module copies (DbContext per schema,
  migrations, audit entry in the same transaction).
- Every flow testable end to end against the compose stack, including TOTP and emails.

**Non-Goals (design level):**
- No generic permission system beyond two roles and the comparsa scope.
- No outbox or background queue for email; sending is synchronous (low volume).
- No encryption of individual columns (authenticator keys rely on database encryption at rest and
  backups, SEC-07); revisit with the DPIA (SEC-10).

## Decisions

### D1. Modules and projects

```
Modules/IdentityAccess/PolvorApp.IdentityAccess/            Identity, endpoints, emails, seeder
Modules/IdentityAccess/PolvorApp.IdentityAccess.Contracts/  ICurrentUser, IComparsaScope,
                                                            IFiringChiefAssignmentSource, UserRole
Modules/AuditPrivacy/PolvorApp.AuditPrivacy/                AuditDbContext (owns the table + migration)
Modules/AuditPrivacy/PolvorApp.AuditPrivacy.Contracts/      (empty for now; viewer contracts in #15)
SharedKernel/Auditing/                                      AuditEntry, IAuditTrail, model extension
SharedKernel/Email/                                         IEmailSender, EmailMessage
SharedKernel/Persistence/                                   module DbContext conventions, IDatabaseMigrator
```

Host-level concerns (fallback authorization policy, authentication/authorization middleware order,
anti-forgery filter, rate limiter, forwarded headers, SMTP sender, `migrate` command) live in
`PolvorApp.Api/Platform/*`, because every module relies on them. The identity module configures
Identity, its cookie schemes, the `Admin` policy and the Data Protection key store through
`AddServices` (they need the module's internal `User` and DbContext types), and contributes the
`create-admin` command through a SharedKernel `IHostCommand` contract. *Alternative:* put all
authentication wiring in the host — impossible without exposing the module's internal types.

**Findings from research (task 1.1, .NET 10 `SignInManager` source):**
- `TwoFactorRecoveryCodeSignInAsync` neither runs `PreSignInCheck` (so neither the `Active` flag nor
  lockout is checked) nor counts failures. The recovery-code endpoint therefore checks lockout and
  `Active` itself and calls `AccessFailedAsync` on failure, as the spec requires.
- `IsTwoFactorClientRememberedAsync` relies on the remember-me cookie's own security-stamp
  validation (configured by Identity), so stamp updates forget remembered devices.
- With `ValidationInterval = 0` the security-stamp validator rebuilds the principal on every request;
  the `auth_time` and `amr` claims are copied in `SecurityStampValidatorOptions.OnRefreshingPrincipal`,
  otherwise the absolute session limit would be lost.
- Cookie redirects are replaced by 401/403 through `CookieAuthenticationEvents` for every path (the
  API has no server-rendered pages), rather than relying on endpoint API metadata detection.
- Packages pinned: EF Core / Identity EF / Data Protection EF 10.0.12, Npgsql EF 10.0.3,
  `EFCore.NamingConventions` 10.0.1, MailKit 4.18.1, `dotnet-ef` 10.0.12; UI `qrcode` 1.5.4 (SVG
  as `data:` URL, allowed by `img-src`), `otpauth` 9.5.2 (E2E only).

### D2. Persistence and migrations

- EF Core with the Npgsql provider, one `DbContext` per module, schema `identity` and `audit`, each
  with its own `__EFMigrationsHistory` table in its schema. Snake-case naming via
  `EFCore.NamingConventions` (verify at apply; if it is not current for EF Core 10, use explicit
  `ToTable`/`HasColumnName`).
- Every module registers its context with `AddModuleDbContext<TContext>(schema, migrationOrder)`,
  which also registers an `IDatabaseMigrator`; the host command `migrate` applies them in
  `migrationOrder` (audit first). Compose runs `migrate` as a one-shot
  `api-migrate` service that `api` depends on (`service_completed_successfully`). Tests apply
  migrations in the Testcontainers fixture.
- `IdentityDbContext`: `IdentityUserContext<User, Guid>` (no role tables — a user has one role, a
  column). There is no invitation table: invitations use Identity's token providers (D4).
  `User : IdentityUser<Guid>` adds `Name` (max 200), `Role` (`ADMIN` | `FIRING_CHIEF`, stored as
  text), `Locale` (text), `Active`, `LastSignInAt`, `CreatedAt`. `NormalizedEmail` gets a unique
  index (Identity `RequireUniqueEmail`). The Data Protection key ring is also stored in this context
  (`IDataProtectionKeyContext`, table `identity.data_protection_keys`) so cookies survive restarts
  and multiple containers. *Alternative:* key ring on a volume — one more piece of host state.
- `data-model.md` lists `User.active`; status (`INVITED` / `ACTIVE` / `DEACTIVATED`) is derived:
  `!Active` → `DEACTIVATED`; `PasswordHash == null` → `INVITED`; else `ACTIVE`.

### D3. Audit trail in the caller's transaction

`AuditEntry` (SharedKernel): `Id` (Guid v7), `OccurredAt` (UTC), `ActorUserId?`, `Action` (code,
e.g. `UserInvited`, `SignInFailed`), `EntityType`, `EntityId?`, `ComparsaId?`, `TraceId`, `Data`
(jsonb, a small DTO serialised by the caller: previous/new values, never secrets).

Each module DbContext calls `modelBuilder.AddAuditTrail()`, which maps `AuditEntry` to
`audit.audit_entries` **excluded from that context's migrations**; `AuditDbContext` owns the table
and its migration. A module records an entry with `IAuditTrail.Record(dbContext, entry)` (adds it to
the same context) before `SaveChangesAsync`, so change and entry commit atomically (spec: "same
transaction"). `IAuditTrail` fills `OccurredAt`, `ActorUserId` (from `ICurrentUser`) and `TraceId`.
Anonymous security events (failed sign-in) are saved on their own.

Append-only is enforced by having no update/delete code path, plus a guard in `SaveChanges` that
throws if an `AuditEntry` is `Modified` or `Deleted` (architecture-style unit test). *Alternatives:*
a separate `IAuditLog` service with its own context — not atomic without distributed transactions;
database triggers — invisible to a single-maintainer codebase.

Identity actions that `UserManager` saves internally (lockout counters, token updates) are audited
by recording the entry on the same `IdentityDbContext` and letting `UserManager`'s save commit it;
where a flow saves several times, the whole flow runs in one explicit database transaction
(`Database.BeginTransactionAsync`) so no step commits without its entry.

From the group 2 review: `IAuditTrail` truncates attacker-supplied identifiers, strips NUL
characters, caps `Data` at 16 KB and refuses data properties named like secrets (password, token,
code, key); the trace id comes from the same helper as `X-Trace-Id`; composite indexes serve the
future viewer; an architecture test forbids `ExecuteUpdate`/`ExecuteDelete`/raw SQL on the audit
table (the save interceptor cannot see them). A database-level guard (trigger or low-privilege
runtime role) is decided with GDPR erasure in #15.

### D4. Invitations, reset links and one-time tokens

- Invitation link: `{PublicBaseUrl}/invitations/accept?user={id}&token={token}` where `token` is a
  Data Protection token from a dedicated `InvitationTokenProvider` (`DataProtectorTokenProvider`
  subclass, 7-day lifespan, purpose `Invitation`). Tokens embed the security stamp, so **resending
  or deactivating updates the stamp and invalidates older links**; setting the password updates
  it again, making the link single-use.
- Password reset uses Identity's default provider configured to 1 hour (`PasswordReset` purpose).
  Only `ACTIVE` users with 2FA enrolled get the email, at most once per 5 minutes per account; the
  email is queued (`IEmailOutbox`, a bounded in-memory channel drained by a background service) so
  the endpoint always returns `202 Accepted` after the same constant minimum delay, and any
  failure is logged and answered as success. The pending second-step cookie carries the security
  stamp, so a reset also cancels a half-finished sign-in.
- Links go to UI routes; the UI calls the API with the token in the body (never logged: the request
  logger already omits bodies and query strings).

### D5. Sign-in state machine and 2FA

Identity's `SignInManager` with the application cookie, `Identity.TwoFactorUserId` (between the
password and the second step, 5 minutes) and `Identity.TwoFactorRememberMe` (30 days) schemes.

```
POST /login {email,password}
   ├─ invalid / deactivated / locked ─▶ 401 generic problem (code auth.invalidCredentials)
   ├─ password ok, 2FA not enabled ───▶ 200 {next:"ENROL"}      (TwoFactorUserId cookie)
   ├─ password ok, device remembered ─▶ 200 {next:"DONE"}       (application cookie)
   └─ password ok ────────────────────▶ 200 {next:"SECOND_FACTOR"} (TwoFactorUserId cookie)
POST /login/second-factor {code, rememberDevice} ─▶ 200 DONE | 401
POST /login/recovery-code {code}                  ─▶ 200 DONE {recoveryCodesLeft} | 401
GET  /enrolment  ─▶ {sharedKey, otpauthUri}   POST /enrolment {code} ─▶ 200 {recoveryCodes[10]}
```

- Deactivated users are rejected by overriding `SignInManager.CanSignInAsync` (`Active == false`),
  and the response is the same generic 401.
- Enrolment endpoints accept the `TwoFactorUserId` principal **only** when the user has no 2FA
  enabled; a completed enrolment signs in with the application cookie.
- **TOTP replay**: Identity's authenticator provider accepts a code again within its window. A
  custom `AuthenticatorTokenProvider` stores the last accepted time step per user (user token
  `LastTotpStep`) and rejects equal or older steps.
- Lockout: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`, `lockoutOnFailure:
  true` on password and second-factor checks (Identity counts both).
- Password policy (spec): `RequiredLength = 12`, no character-class requirements, plus a custom
  `IPasswordValidator` that rejects the email and passwords in an embedded list of common
  passwords (the top 10k list from SecLists, MIT, as a resource file — record in the license register).
- Recovery codes: 10, Identity's `GenerateNewTwoFactorRecoveryCodesAsync`. Identity stores them
  unhashed in the user tokens table; accepted (they only matter together with the password) and
  noted for the DPIA.
- `PasswordSignInAsync` signs a user without 2FA straight in, so it is **not** used for the first
  step: the endpoint calls `CheckPasswordSignInAsync` (lockout on failure), then either checks the
  remembered-device cookie and signs in, or signs into the `TwoFactorUserId` scheme for the second
  step or enrolment. The application cookie is only ever issued to users with 2FA enabled.

### D6. Cookies, sessions and anti-forgery

- Application cookie `polvorapp.session`: HttpOnly, SameSite=Strict, `SecurePolicy = Always` outside
  Development (Development = `SameAsRequest` because compose serves plain HTTP on localhost);
  `ExpireTimeSpan = 60 min`, `SlidingExpiration = true`; absolute limit of 12 h enforced in
  `OnValidatePrincipal` from an `auth_time` claim. Cookie redirects disabled for `/api`
  (`DisableCookieRedirect` or events returning 401/403) so the SPA gets status codes.
- `SecurityStampValidatorOptions.ValidationInterval = TimeSpan.Zero`: the stamp and `Active` are
  checked on every request (one indexed query; ~60 users). Deactivation, role change, password
  change/reset, 2FA reset and "sign out everywhere" update the stamp. Role is read from the
  refreshed principal, so a role change applies on the next request. The remembered-device cookie
  is validated against the stamp by Identity, so those events also forget remembered devices.
- Anti-forgery: `AddAntiforgery(HeaderName = "X-XSRF-TOKEN")`; `GET /api/auth/antiforgery`
  (anonymous) stores the cookie token and returns the request token in a JS-readable `XSRF-TOKEN`
  cookie (SameSite=Strict, not HttpOnly). A middleware after authentication validates every
  non-safe `/api` request (including the anonymous sign-in steps and unknown routes) and answers
  400 problem `antiforgery.invalid`. Tokens are bound to the user, so the UI refreshes the token
  after sign-in/sign-out.
- Authorization: the `/api` route group requires an authenticated user (not a global fallback
  policy, which would turn unknown routes into 401 instead of 404), authenticated by the
  application-cookie scheme (the
  `TwoFactorUserId` scheme is never the default authenticate scheme, so a half-signed-in principal
  is anonymous to the API). As defence in depth, `OnValidatePrincipal` also rejects a principal
  whose user has 2FA disabled (after an Admin reset). Policy `Admin` = role claim `ADMIN`. Anonymous endpoints use `.AllowAnonymous()`;
  an OpenAPI test lists the anonymous endpoints and fails if the set changes unexpectedly.
- Rate limiting (`Microsoft.AspNetCore.RateLimiting`): partitioned by client IP, policy `auth`
  (10 requests/min) on login, second-factor, recovery-code, enrolment and invitation acceptance;
  policy `auth-email` (5 per 15 min) on forgot-password. Rejection → 429 problem details. Client IP
  comes from `UseForwardedHeaders` trusting only the configured proxy network (`ForwardedHeaders__KnownNetworks`,
  compose network by default).

### D6b. Hardening after the group 4 reviews

- **`SignInFlow`** (scoped service) holds the whole state machine; endpoints only map outcomes to
  responses. Every attempt on a known user runs in one transaction that takes `FOR UPDATE` on the
  user's row and reloads it (`UserLock`): failure counters, the TOTP replay marker, recovery codes
  and audit entries are consistent under parallel requests and commit together; every Identity
  result is checked (`ThrowIfFailed`), so nothing is half-saved.
- `ValidateSecurityStampAsync` also requires `Active` and `TwoFactorEnabled`, independent of stamps.
- Remembered devices: `SlidingExpiration = false` and the remembering time kept in the ticket, so
  renewals never extend the 30 days; the pending second-step cookie does not slide either.
- `auth_time` is kept only by `RefreshSignInAsync`; a fresh sign-in restarts the 12 hours.
- Locked or unusable accounts hash a dummy password (configured hasher) so timing reveals nothing;
  inputs are capped (email 320, password 256, codes 64); PBKDF2 iterations 210,000.
- Every `/api` response carries `Cache-Control: no-store`; failed attempts are audited as
  anonymous even if the browser carries a cookie; `Role` defaults to `FIRING_CHIEF` and the table
  has CHECK constraints for role and locale.
- Rate limits key IPv6 clients by /64; compose pins its subnet (172.28.0.0/24) and trusts
  `X-Forwarded-For` only from it. Sign-out needs no session, so a pending second step can be cleared.
- Identity's lockout and token lifetimes read the system clock (not `TimeProvider`); tests assert
  the configured lifetimes and move lockout ends explicitly.

### D7. Comparsa scoping contract (BR-12)

`IComparsaScope` (scoped service, Contracts): `bool IsAll`, `IReadOnlySet<Guid> ComparsaIds`,
`bool CanAccess(Guid comparsaId)`, and `IQueryable<T> Apply<T>(IQueryable<T>, Expression<Func<T,Guid>>)`
helper for later modules. It is computed from `ICurrentUser` and an `IFiringChiefAssignmentSource`
(Contracts interface). The identity module registers a default source returning an empty set;
`add-federation-catalog` (#4) replaces it with the real assignments (its module references only
`IdentityAccess.Contracts`, as ADR-0001 allows). Out-of-scope access returns 404 (spec). Unit tests
cover Admin, FiringChief without and with assignments; an architecture test later ensures
comparsa-owned endpoints use the scope (added in #4 when the first one exists).

### D8. API endpoints

All under `/api`, JSON, problem details on errors with a stable `code` extension
(e.g. `auth.invalidCredentials`, `users.lastAdmin`) that the UI maps to translation keys.

| Method | Path | Access | Notes |
|---|---|---|---|
| GET | `/auth/antiforgery` | anonymous | 204, sets `XSRF-TOKEN` |
| POST | `/auth/login` | anonymous, rate-limited | D5 |
| POST | `/auth/login/second-factor` | 2FA-pending | `{code, rememberDevice}` |
| POST | `/auth/login/recovery-code` | 2FA-pending | returns remaining count |
| GET/POST | `/auth/enrolment` | 2FA-pending, no 2FA yet | key + otpauth URI / confirm |
| POST | `/auth/logout` | anonymous | 204; also clears a pending second step |
| GET | `/auth/invitations/validate` | anonymous | `?user&token` → `{name,email}` or 410 |
| POST | `/auth/invitations/accept` | anonymous, rate-limited | `{userId, token, password}` → next ENROL |
| POST | `/auth/password/forgot` | anonymous, rate-limited | always 202 |
| POST | `/auth/password/reset` | anonymous, rate-limited | `{userId, token, password}` |
| GET | `/account` | authenticated | `{id,name,email,role,locale,recoveryCodesLeft}` (also the "me" call) |
| PUT | `/account/locale` | authenticated | `{locale}` |
| POST | `/account/password` | authenticated | `{currentPassword,newPassword}`; re-signs current session |
| POST | `/account/recovery-codes` | authenticated | `{code}` → new codes |
| POST | `/account/sign-out-everywhere` | authenticated | 204 |
| GET | `/users` | Admin | list with status, 2FA, last sign-in; filter `role`, `status` |
| POST | `/users` | Admin | invite `{email,name,role,locale}` → 201 |
| GET | `/users/{id}` | Admin | detail |
| PUT | `/users/{id}` | Admin | `{name,role,locale}`; last-Admin guard → 409 |
| POST | `/users/{id}/deactivate` · `/reactivate` | Admin | last-Admin guard on deactivate |
| POST | `/users/{id}/invitation` | Admin | resend; 409 unless `INVITED` |
| POST | `/users/{id}/two-factor/reset` | Admin | 409 when not enrolled |

The last-Admin guard counts active Admins inside the same transaction with a `SELECT … FOR UPDATE`
on Admin rows, so two Admins demoting each other concurrently cannot leave zero.

Host commands (one dispatcher, `HostCommands`; unknown verbs exit 2, failures exit 1 with the
exception type only): `migrate`; `seed`; `create-admin --email <e> --name <n> [--locale es-ES]`
(refused and audited once an Admin has a password; re-run for the same invited Admin resends the
link; rolled back if the email cannot be sent; logs never contain the email). The last-Admin guard
and create-admin share one transaction-scoped advisory lock (`UserLock.LockAdminsAsync`, with a
5 s `lock_timeout`); invited Admins do not count as active. Role changes do not rotate the stamp:
the principal is rebuilt on every request, so the new role applies on the next request. Account
step-ups (`/account/password`, `/account/recovery-codes`) are rate limited and count failures
towards the lockout. nginx logs paths without query strings (invitation and reset tokens).

### D9. Email

`IEmailSender` (SharedKernel) implemented in `Platform/Email` with **MailKit** (MIT; `System.Net.Mail`
is not recommended by Microsoft for new code). Options `Email__SmtpHost`, `Email__SmtpPort`,
`Email__From`, `Email__Username`, `Email__Password`, `Email__Security` (`None` | `StartTls` |
`SslOnConnect`; `None` only in Development/Testing and never with credentials),
`Email__TimeoutSeconds` (deadline for the whole send) and `App__PublicBaseUrl` (https outside local
environments; a path prefix is kept in links), validated at startup like the database settings.
`EmailMessage.ToString()` prints the template only. Templates: localized `.resx` strings in the identity
module rendered into a plain-text body and a minimal HTML body (no remote images, no tracking).
Sending is awaited in the request; a failure returns 502 problem `email.sendFailed` after the user
change has been committed (the invitation can be resent). Logs record only the template name,
the failing phase (connect, authenticate, send) and PII-free codes (SMTP status, socket error); a
failing QUIT after the server accepted the message still counts as sent.

### D10. Synthetic users

`IdentitySeeder` (Order 10) creates, only through the guarded `seed` command: one `ADMIN`
("Admin Sintética", `admin@polvorapp.example`), two `FIRING_CHIEF`s, one `INVITED` and one
`DEACTIVATED` user — all with `.example` addresses and fixed ids. Password and authenticator key
(Base32, at least 80 bits) come from `Seed__UserPassword` and `Seed__AuthenticatorKey`,
placeholders in `.env.example`, given only to the on-demand compose service `api-seed` (profile
`seed`); the seeder fails if they are missing. Outside Development and Testing it refuses the
published placeholder values and any database that holds non-synthetic users. E2E tests read the same variables and compute TOTP codes with
`otpauth` (MIT, dev dependency).

### D11. Frontend

Feature folder `src/features/identity-access/`:

| Route | Page | Layout |
|---|---|---|
| `/login` | `SignInPage` (email, password; `returnTo` preserved) | public |
| `/login/second-factor` | `SecondFactorPage` (code with `autocomplete="one-time-code"`, remember-device checkbox, link to recovery code) | public |
| `/login/recovery-code` | `RecoveryCodePage` | public |
| `/enrolment` | `EnrolmentPage` (QR, manual key, confirm code) → `RecoveryCodesPage` (list, copy, print, "I saved them") | public |
| `/invitations/accept` | `AcceptInvitationPage` (validate → set password) | public |
| `/password/forgot`, `/password/reset` | `ForgotPasswordPage`, `ResetPasswordPage` | public |
| `/account` | `AccountPage` (profile, language, password, recovery codes, sign out everywhere) | shell |
| `/users` | `UsersPage` (DataTable, filters, "Invite user") | shell, Admin |
| `/users/new` | `InviteUserPage` (form) | shell, Admin |
| `/users/:id` | `UserDetailPage` (edit form + actions with `ConfirmDialog`) | shell, Admin |

- **Session state**: `useSession()` = TanStack Query on `GET /api/account` (`staleTime` 60 s). A
  `RequireSession` route wrapper redirects to `/login?returnTo=…` on 401; `RequireAdmin`
  renders `ForbiddenPage`. `returnTo` is accepted only as a same-origin path (URL-normalised, no
  control characters, backslashes or protocol-relative results). The API mutator: fetches `/api/auth/antiforgery` lazily, sends
  `X-XSRF-TOKEN` from the cookie on non-GET requests, retries once after refreshing the token on an
  anti-forgery 400, and on 401 from a protected call clears the query cache and navigates to
  `/login?returnTo=…&reason=expired`.
- **Language**: after sign-in the UI calls `i18n.changeLanguage(user.locale)`; `LanguageSwitcher`
  gets an optional `onLanguageChange` used by the shell to `PUT /api/account/locale` when signed in;
  a failed save keeps the new language and says so above the page.
- **Navigation**: `NavigationEntry` gains `roles?: UserRole[]`; `AppShell` filters by the session role.
  New entries: Users (Admin). User menu composite in the top bar.
- **New composites** in `components/app/` (with stories and tests, catalogue rule): `PublicLayout`
  (mark, switchers, version, centred card), `UserMenu` (name, translated role, account, sign out),
  `QrCode` (renders an SVG from a string with an accessible label; library chosen at apply —
  candidate `qrcode` (MIT) generating SVG, rendered via an `<img>` data URL to stay CSP-compliant),
  `RecoveryCodeList` (monospace list, copy button with success/failure announced, print). Thin form
  composites wrap the shadcn primitives so feature screens keep to ADR-0009: `Button` (variants,
  `pending` that stays focusable), `TextInput`, `PasswordInput` (show/hide toggle, `aria-pressed`),
  `SelectInput`, `CheckboxField`. Existing composites gain small, backwards-compatible options:
  `PageHeader`/`AlertBanner` `focusOnMount` (for a view that replaces the page after an action),
  `ConfirmDialog` shows the reason of a `ConfirmFailure` rejection and keeps focus while pending,
  `AppLayout` `userMenu` and `onLanguageChange` slots.
- **Focus**: after client-side navigation, including arriving in the other shell on sign-in or
  sign-out, focus moves to `<main>` unless the page already put it inside (a notice, a heading).
- **One-time recovery codes** travel from enrolment to `RecoveryCodesPage` in memory (query cache),
  never in `history.state`, so a reload or the back button does not show them again.
- Forms: React Hook Form + Zod; password rules mirrored client-side (length only; the server is
  authoritative for common passwords). Server `code`s map to `identity:errors.<code>`.

**i18n**: new namespace `identity` in `es-ES`, `ca-ES-valencia`, `en`: `signIn.*`, `secondFactor.*`,
`recovery.*`, `enrolment.*`, `invitation.*`, `password.*`, `account.*`, `users.*` (list, filters,
form, actions, confirmations), `roles.ADMIN|FIRING_CHIEF`, `status.INVITED|ACTIVE|DEACTIVATED`,
`errors.<code>`, `session.expired`; `common`: `nav.users`, `nav.account`, `forbidden.*`. User
statuses are added to the design-system status mapping (`StatusBadge`) and `docs/design/status.md`.
Backend `.resx` for email subjects/bodies and problem titles in the three cultures. Valencian texts
flagged for native review as in #2.

### D12. Security and GDPR summary

- SEC-03/BR-12: D7, server-side, deny by default. SEC-04: D4–D6. SEC-05: D3, every write and the
  security events listed in the spec. SEC-11: D10 synthetic users, credentials from env.
- Personal data handled: user name and email (Normal). Not logged (NFR-12): request logger already
  omits bodies/query; audit `Data` never includes secrets; failed sign-in stores the attempted
  normalised email in the audit trail only (needed to detect attacks) — retention to be decided in #15.
- Cookies are strictly necessary (session, 2FA, anti-forgery, remembered device): no consent banner,
  but the privacy notice (Q-50) should mention the 30-day remembered-device cookie.
- ADR-0004 gets a dated clarification note: remembered devices (30 days) are allowed; 2FA remains
  mandatory at enrolment and on new devices.

### Follow-ups from the group 9 reviews (not blocking)

- Accessibility: persistent live regions instead of conditionally mounted status banners; announce
  the result count after changing a users filter; headings (not only legends) for `FormSection`;
  loading texts for pages that wait on a query; the setup key shown in groups of four with a copy
  button; row headers in `DataTable`.
- Contract: declare the problem-details extension members (`code`, `errors`, `userId`) in the
  OpenAPI document so the generated client types them.
- Language: two fast switches may reach the API out of order (the last response wins in the UI).
- Tests (from the group 10 coverage review; the HIGH gaps are closed): real expiry of reset and
  invitation links (today the lifetimes are asserted as options), audit assertions for the remaining
  actions (`RecoveryCodesRegenerated`, `SignedOutEverywhere`, `InvitationAccepted`,
  `InvitationResent`, `UserReactivated`, `LocaleChanged`) and a scan of every audit row for
  secrets, lockout during enrolment and pending-step expiry, forgot-password responses compared
  across all account states, last-Admin edge cases, the comparsa filter translated by EF Core, and
  HTTP-level password-length boundaries.
- Copy: Valencian texts for native review — "cap de disparada" (fixed term for *jefe de disparo*),
  "Et donem la benvinguda", the imperative button set ("Imprimix", "Restablix…", "Continua"),
  "Ja els he guardat", «El meu compte» quoting, and "Torna-ho a provar" as the standard retry.
  The platform home heading ("Bienvenida"/"Benvinguda") is left for a copy change of its own.

## Risks / Trade-offs

- [Data Protection keys are stored unencrypted in `identity.data_protection_keys`; database or
  backup read access allows forging cookies and tokens] → accepted for the MVP with encrypted
  backups (SEC-07); protecting the key ring with a certificate supplied at deploy time is a
  go-live item for the DPIA (SEC-10).
- [After an Admin 2FA reset, anyone holding the user's password can enrol a new authenticator]
  → accepted: the password remains the first factor, the reset is audited and done on the user's
  request; revisit (e.g. emailed enrolment link) if the Federation asks for it.
- [Signing out removes the cookie but does not revoke a copied cookie server-side] → idle (60 min)
  and absolute (12 h) limits; "sign out everywhere" and any stamp change revoke all sessions.

- [Remembered devices weaken 2FA on shared or lost computers] → opt-in per sign-in, 30-day limit,
  forgotten on password change/2FA reset/deactivation/sign-out-everywhere; hint text advises not to
  use it on shared devices.
- [Security-stamp check on every request adds a query] → ~60 users, indexed PK lookup; measurable
  in NFR-05 terms as negligible; can raise the interval later without spec change except the
  "next request" wording.
- [Email failures leave an invited user without a link] → user is created, error shown, resend action.
- [TOTP secrets and recovery codes stored in clear in the database] → database encryption at rest
  and encrypted backups (SEC-07); DPIA note (SEC-10).
- [Excluding the audit table from module migrations can drift] → a test builds each module model and
  asserts `AuditEntry` is mapped identically and excluded from migrations.
- [Anti-forgery on anonymous endpoints requires a token fetch before sign-in] → mutator fetches it
  lazily; one extra request per session.
- [Clock skew breaks TOTP] → Identity accepts ±1 step (90 s window); container time from host NTP.

## Migration Plan

1. Deploy: run `migrate` (creates `audit` and `identity` schemas), start the API, run
   `create-admin` once to invite the first Admin. Staging: `migrate` + `seed`.
2. API consumers: none besides the SPA; the committed OpenAPI contract and generated client are
   updated in the same PR.
3. Rollback: redeploy the previous images; the new schemas are unused by the old version and can be
   dropped (no business data yet).

## Open Questions

- Retention period of audit entries and failed-sign-in emails — decided with the viewer in #15.
- Production SMTP provider and sender domain (NFR-11, Q-50) — configuration only.
