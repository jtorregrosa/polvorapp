# 0004. Authentication: Identity, cookies, mandatory TOTP 2FA

- Status: Accepted
- Date: 2026-09-30

## Context

Users are Admins and FiringChiefs (~60). They access ID documents, photos and weapons data.
Round 4 decision: email + password with two-step verification.

## Decision

- **ASP.NET Core Identity** with PostgreSQL storage.
- **Invitation-only** accounts: Admins invite users by email; no self-registration.
- **TOTP 2FA mandatory for every user** (authenticator app), with one-time recovery codes.
- Session via **HttpOnly, Secure, SameSite=Strict cookies** on the same origin as the SPA
  (no tokens in browser storage); anti-forgery protection for state-changing requests.
- Roles: `Admin`, `FiringChief`; comparsa scoping enforced server-side on every query (BR-12).
- Account lockout, password policy, audit of logins (UC-25).

## Clarification (2026-09-30, change `add-identity-access`)

Users may mark a browser as **remembered for 30 days**, so the TOTP code is not asked again there.
2FA stays mandatory at enrolment and on every other browser; remembering is opt-in per sign-in and is
forgotten on password change, 2FA reset, deactivation and "sign out everywhere". Sessions end after
60 minutes idle and 12 hours at most.

Anti-forgery uses the double-submit pattern for SPAs: the cookie token (`polvorapp.af`) is HttpOnly,
and the request token is handed to the UI in a script-readable `XSRF-TOKEN` cookie that the UI echoes
in `X-XSRF-TOKEN`. That cookie is deliberately not HttpOnly (code scanning flags it): alone it is
useless, it is bound to the signed-in user, and script able to read it could already send requests.

## Consequences

- No external identity provider cost or dependency.
- Recovery process needed when a user loses their authenticator (Admin reset, audited).

## Alternatives considered

- **Magic links** — simpler for users but rejected in round 4.
- **External IdP (Entra ID, Auth0, Keycloak)** — extra cost or infrastructure for ~60 users.
