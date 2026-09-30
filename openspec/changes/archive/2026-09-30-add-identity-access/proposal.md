# Proposal

## Why

PolvorApp will hold ID documents, license photos and weapons data (SEC-02, SEC-04), yet today every
endpoint is anonymous and there are no users. Nothing else in the MVP can be built safely until the
application knows **who** is calling, **what role** they have and **which comparsa** they may see
(BR-12). ADR-0004 fixed the approach: ASP.NET Core Identity, invitation-only accounts, mandatory
TOTP two-factor authentication and same-origin HttpOnly cookies. Change #2 delivered the design
system, so this change can build the first real screens: sign-in, 2FA and user management.

`docs/mvp.md` also requires audit logging from this change onwards (SEC-05, UC-25), so the audit
trail that every later change writes to is introduced here.

Capability (from `docs/mvp.md`): **`identity-access`** — change #3 of the sequence. It also creates
the write side of **`audit-privacy`** (the viewer and GDPR tooling stay in #15).

## What Changes

- **Users and roles** (UC-24, users part): `User` with `email`, `name`, `role` (`ADMIN` |
  `FIRING_CHIEF`), `locale` and `active`, stored with ASP.NET Core Identity in PostgreSQL (ADR-0002,
  ADR-0004). Admins list users, invite, edit name/role/locale, deactivate/reactivate, resend
  invitations and reset a user's two-factor authentication. The last active Admin cannot be
  deactivated or demoted (blocking).
- **Invitation-only accounts** (SEC-04): single-use invitation links sent by email, valid 7 days;
  the invitee sets a password and enrols an authenticator app before first use. No self-registration.
- **Sign-in with mandatory TOTP 2FA**: email + password, then a 6-digit authenticator code or a
  one-time recovery code. The user may choose to **remember the device for 30 days** (maintainer
  decision); remembered devices are forgotten on password change, 2FA reset, deactivation or
  "sign out everywhere". Account lockout after repeated failures; rate limiting on sign-in endpoints.
- **Self-service password reset** by single-use email link (NFR-11); 2FA is still required afterwards.
  A lost authenticator is recovered by an Admin 2FA reset, audited (ADR-0004).
- **Own account page**: language preference, change password, regenerate recovery codes, sign out
  everywhere.
- **Sessions**: HttpOnly, Secure, SameSite=Strict cookie on the SPA origin; idle and absolute
  expiry; deactivation and security-sensitive changes take effect on the next request;
  anti-forgery token required for every state-changing request (ADR-0004).
- **Authenticated by default** (**BREAKING** for the API surface): every API endpoint requires a
  signed-in user with completed 2FA unless explicitly anonymous (health, system info, sign-in and
  recovery steps). Admin-only endpoints reject FiringChiefs.
- **Comparsa scoping foundation** (BR-12, SEC-03): a server-side scope that gives Admins every
  comparsa and FiringChiefs only their assigned ones, **deny by default**. Assignments themselves
  arrive with `add-federation-catalog` (#4); until then a FiringChief is scoped to no comparsa.
- **First Admin bootstrap**: a host command that invites the first Admin, refused once an Admin exists.
- **Audit trail** (SEC-05, UC-25): an append-only audit log written in the same transaction as each
  change, recording actor, action, target and time — never passwords, codes or tokens. Sign-ins,
  failed sign-ins, lockouts, invitations and every user-management action are audited here.
- **Transactional email** (NFR-11): SMTP sending with localized invitation and password-reset
  messages (Mailpit locally).
- **UI**: sign-in, two-factor, recovery-code, invitation, enrolment (QR code + manual key),
  recovery-codes, forgot/reset password pages in a public layout; users list and user detail
  (Admin); account page; user menu in the top bar; navigation filtered by role; the signed-in
  user's language is applied at sign-in and saved when switched.
- **Synthetic seed**: one Admin and FiringChiefs with credentials from environment variables, so
  local runs and E2E tests can sign in (SEC-11).

## Capabilities

### New Capabilities
- `identity-access`: users and roles, invitations, sign-in with TOTP 2FA and recovery codes,
  remembered devices, password reset, sessions and anti-forgery, account self-service, user
  management by Admins, first-Admin bootstrap and the comparsa scoping rule (BR-12).
- `audit-privacy`: the append-only audit trail of writes and security events (SEC-05, UC-25). This
  change adds recording only; viewing and GDPR requests (UC-25 viewer, UC-26) come in #15.

### Modified Capabilities
- `platform`: requirement "Anonymous surface is limited until identity exists" is removed and
  replaced by "Authenticated API by default".
- `platform`: requirement "Application shell" gains the user menu, role-filtered navigation and a
  public layout for signed-out pages.
- `platform`: requirement "Switch UI language (UC-27)" applies and saves the signed-in user's language.
- `platform`: new requirement "Transactional email".

## Non-goals

- Comparsas and FiringChief assignments (`add-federation-catalog`, #4): only the scoping rule and
  its deny-by-default behaviour are delivered here.
- The audit log viewer, GDPR export/erasure (UC-25 viewer, UC-26 — change #15).
- External identity providers, magic links, passkeys/WebAuthn, SMS or email codes (ADR-0004).
- Self-registration, changing a user's email address (deactivate and invite again), deleting users
  (they are deactivated so the audit trail keeps its references).
- Notification emails other than invitation and password reset (#14).
- Error-tracking service and production mail provider selection (go-live, NFR-12, Q-50).

## Impact

- **Backend**: new modules `IdentityAccess` and `AuditPrivacy` (+ `.Contracts`), first EF Core
  DbContexts and migrations (one schema per module) and a host `migrate` command; authentication,
  authorization fallback policy, anti-forgery, rate limiting and forwarded-headers configuration in
  the host; SMTP sender in the platform; `create-admin` host command; synthetic user seeder.
- **API**: new `/api/auth/*`, `/api/account/*` and `/api/users/*` endpoints; all other endpoints now
  require authentication; OpenAPI contract and generated client regenerated.
- **Frontend**: `features/identity-access/` pages, route guards, 401 handling and anti-forgery
  header in the API mutator, role-aware navigation, user menu, new composites (public layout, QR
  code), `identity` translation namespace in the three locales.
- **New dependencies** (verified at apply): EF Core + Npgsql provider, ASP.NET Core Identity EF
  stores, EF Core Data Protection key store, MailKit, a QR-code library for the UI, a TOTP helper for
  E2E tests; recorded in `docs/third-party-licenses.md`.
- **Configuration**: SMTP settings, public base URL, seed credentials in `.env.example` (placeholders).
- **Docs**: glossary (user status terms), `data-model.md` (User), ADR-0004 clarification note on
  remembered devices, `docs/development.md` (sign-in locally, `migrate`, `create-admin`), `docs/mvp.md`.
- **Implements**: UC-24 (users), UC-25 (recording), UC-27 (per-user language), BR-12, SEC-03, SEC-04,
  SEC-05, SEC-11, NFR-11, NFR-12, ADR-0001, ADR-0002, ADR-0004, ADR-0007, ADR-0009.
