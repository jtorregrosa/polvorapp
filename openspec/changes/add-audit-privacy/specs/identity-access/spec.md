# Spec Delta

## MODIFIED Requirements

### Requirement: Users and roles
A `User` SHALL have a unique `email` (compared case-insensitively), a `name`, exactly one `role`
(`ADMIN` or `FIRING_CHIEF`), a preferred `locale` (`es-ES`, `ca-ES-valencia` or `en`) and an
`active` flag. Each user SHALL have a derived status: `INVITED` (active, no password set yet),
`ACTIVE` (active, password set), `DEACTIVATED` (not active) or `ERASED` (their data was erased on
a GDPR request, audit-privacy capability; never active again). Users SHALL NOT be deleted; they are
deactivated, or erased by anonymisation, so that audit entries keep their actor.

#### Scenario: Email uniqueness is blocking
- **WHEN** an Admin invites `nuria.ejemplo@example.test` while a user with `Nuria.Ejemplo@example.test` exists
- **THEN** the request is rejected with `409 Conflict` and no user or invitation is created

#### Scenario: Invalid role or locale is blocking
- **WHEN** an Admin submits a user with role `SUPERVISOR` or locale `fr-FR`
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

#### Scenario: Erased user keeps their id
- **WHEN** a user who validated an order is erased
- **THEN** the user still exists with status `ERASED`, and the order and the audit entries still refer to them by id

### Requirement: User management by Admins
Admins SHALL be able to list users (name, email, role, status, two-factor enabled, last sign-in),
filter them by role and status (including `ERASED`), edit a user's name, role and locale, deactivate and reactivate
users, resend invitations and reset a user's two-factor authentication. Every one of these
operations SHALL be available only to Admins; FiringChiefs SHALL receive `403 Forbidden` from the
API and a translated "not allowed" page in the UI. The last active Admin SHALL NOT be deactivated or
changed to `FIRING_CHIEF` (blocking). An `ERASED` user SHALL NOT be edited, reactivated, re-invited
or have their two-factor authentication reset (blocking, `409 Conflict`). Destructive actions SHALL be confirmed in the UI.

#### Scenario: Admin lists users
- **WHEN** an Admin opens the users page
- **THEN** a table of all users with name, email, role, status, two-factor state and last sign-in is shown

#### Scenario: FiringChief opens the users page
- **WHEN** a FiringChief navigates to the users page
- **THEN** a translated "not allowed" page is shown inside the shell and no user data is requested

#### Scenario: Deactivate a user
- **WHEN** an Admin deactivates a FiringChief and confirms
- **THEN** the user's status becomes `DEACTIVATED`, their sessions end and they can no longer sign in

#### Scenario: Reactivate a user
- **WHEN** an Admin reactivates a `DEACTIVATED` user who had set a password
- **THEN** the user's status becomes `ACTIVE` and they can sign in with their password and second factor

#### Scenario: Erased user cannot be reactivated
- **WHEN** an Admin tries to reactivate or edit an `ERASED` user
- **THEN** the request is rejected with `409 Conflict` and a translated explanation, and nothing changes

#### Scenario: Filter erased users
- **WHEN** an Admin filters the users page by status `ERASED`
- **THEN** only erased users are listed, shown with the erased marker as their name

#### Scenario: Last Admin is protected
- **WHEN** the only active Admin tries to deactivate themselves or change their own role to `FIRING_CHIEF`
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Reset two-factor authentication
- **WHEN** an Admin resets the two-factor authentication of a user who lost their phone and confirms
- **THEN** the user's authenticator and recovery codes are removed, their sessions and remembered devices end, and they must enrol again at their next sign-in

#### Scenario: Role change takes effect
- **WHEN** an Admin changes a user's role from `ADMIN` to `FIRING_CHIEF` while another Admin remains
- **THEN** that user's next request is authorised as a FiringChief

### Requirement: Security events are audited
Successful sign-ins, failed sign-ins (with the attempted email normalised but never the password or
code), lockouts, sign-outs everywhere, password changes and resets, two-factor enrolment and resets,
recovery-code use and regeneration, invitations and every user-management change SHALL be recorded
in the audit trail with the acting user (or none for anonymous attempts) and the target user.

A change of a user's name SHALL be recorded as a changed field, without the previous or new name;
role and locale changes SHALL keep their previous and new values. The attempted email of a failed
sign-in SHALL be kept only as long as access and security events are kept (1 year, audit-privacy
capability, "Audit retention"), and SHALL be redacted when the user with that email is erased.

#### Scenario: Failed sign-in audited
- **WHEN** someone fails to sign in
- **THEN** an audit entry with action `SignInFailed`, the time and the attempted email is recorded, without the password

#### Scenario: Admin change audited
- **WHEN** an Admin changes a user's role
- **THEN** an audit entry records the Admin as actor, the user as target and the previous and new role

#### Scenario: Name change audited without values
- **WHEN** an Admin changes a user's name from "Nuria Ejemplo" to "Nuria Ejemplo Prueba"
- **THEN** the audit entry records that the name changed, and contains neither name
