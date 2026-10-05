# identity-access Specification

## Purpose
Controls who can use PolvorApp and what they can reach: invitation-only users with the `ADMIN` or
`FIRING_CHIEF` role, sign-in with mandatory TOTP two-factor authentication, sessions, account
self-service, user management by Admins and the comparsa scoping rule (BR-12, SEC-03, SEC-04).

## Requirements

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

### Requirement: Invitation-only accounts
Accounts SHALL only be created by an Admin invitation (SEC-04); there SHALL be no self-registration.
An invitation SHALL be sent by email in the invitee's `locale`, SHALL contain a single-use link
valid for 7 days, and SHALL be invalidated when it is used, when a new invitation is sent to the
same user or when the user is deactivated. Accepting an invitation SHALL require setting a password
that meets the password policy and SHALL continue with two-factor enrolment; the account is not
usable until enrolment completes.

#### Scenario: Admin invites a FiringChief
- **WHEN** an Admin invites `jefe.sintetico@example.test` with name "Jefe Sintético", role `FIRING_CHIEF` and locale `ca-ES-valencia`
- **THEN** a user with status `INVITED` is created
- **AND** an invitation email in Valencian with a single-use link is sent to that address

#### Scenario: FiringChief cannot invite
- **WHEN** a signed-in FiringChief calls the invitation endpoint
- **THEN** the API responds `403 Forbidden` and no user is created

#### Scenario: Accepting an invitation
- **WHEN** the invitee opens a valid invitation link and sets a password meeting the policy
- **THEN** the password is stored and the invitee is taken to two-factor enrolment
- **AND** the invitation link can no longer be used

#### Scenario: Expired or reused invitation
- **WHEN** someone opens an invitation link older than 7 days, already used or replaced by a newer one
- **THEN** the UI shows a translated "invitation no longer valid" message with the advice to ask an Admin
- **AND** no password can be set with it

#### Scenario: Resending an invitation
- **WHEN** an Admin resends the invitation of a user with status `INVITED`
- **THEN** a new link is emailed and the previous link stops working

#### Scenario: Resending to an active user is blocking
- **WHEN** an Admin tries to resend an invitation to a user with status `ACTIVE`
- **THEN** the request is rejected with `409 Conflict`

### Requirement: Password policy
Passwords SHALL be between 12 and 128 characters long, SHALL NOT be equal to the user's email and
SHALL NOT be one of a list of commonly used passwords; no character-class composition rules SHALL
be imposed. Passwords SHALL be stored only as salted, adaptive hashes.

#### Scenario: Too-short password is blocking
- **WHEN** a user sets the password `corta12345`
- **THEN** the password is rejected with a translated message stating the 12-character minimum

#### Scenario: Common password is blocking
- **WHEN** a user sets the password `password1234`
- **THEN** the password is rejected with a translated message

### Requirement: Mandatory two-factor enrolment
Every user SHALL enrol a TOTP authenticator (RFC 6238, 6 digits, 30-second step) before accessing
any protected feature. Enrolment SHALL show a QR code and the manual key, SHALL be confirmed with a
valid code from the authenticator, and SHALL then show 10 single-use recovery codes exactly once.
A user whose two-factor authentication was reset SHALL enrol again at their next sign-in.

#### Scenario: Successful enrolment
- **WHEN** a user with a password but no authenticator enters a valid code from the scanned QR code
- **THEN** two-factor authentication is enabled, 10 recovery codes are shown once and the user is signed in

#### Scenario: Wrong enrolment code
- **WHEN** the user enters a code that does not match the authenticator key
- **THEN** enrolment is not completed and a translated "code not valid" message is shown

#### Scenario: Protected feature before enrolment
- **WHEN** a user who has set a password but not completed enrolment calls a protected endpoint
- **THEN** the API responds `401 Unauthorized`

### Requirement: Sign-in with two-factor authentication
Signing in SHALL require the email and password followed by either a current TOTP code or an unused
recovery code; each recovery code SHALL work only once. Failure responses SHALL NOT reveal whether
the email exists, whether the account is deactivated or which factor was wrong beyond the current
step. After 5 consecutive failed attempts (password or second factor) the account SHALL be locked
for 15 minutes (blocking), and sign-in endpoints SHALL be rate limited per client address.

#### Scenario: Successful sign-in
- **WHEN** an active user enters the correct email and password and then a valid authenticator code
- **THEN** a session is established and the user lands on the page they originally requested, or the start page

#### Scenario: Wrong password
- **WHEN** a user enters a wrong password
- **THEN** a translated generic "email or password not valid" message is shown and no second step is offered

#### Scenario: Unknown or deactivated account
- **WHEN** someone signs in with an email that does not exist or belongs to a `DEACTIVATED` user
- **THEN** the same generic message as for a wrong password is shown

#### Scenario: Recovery code
- **WHEN** a user completes the second step with an unused recovery code
- **THEN** the user is signed in and that recovery code can no longer be used
- **AND** the UI tells the user how many recovery codes remain

#### Scenario: Lockout
- **WHEN** a user fails the password or the second factor 5 times in a row
- **THEN** further attempts are rejected for 15 minutes even with correct credentials
- **AND** a lockout audit entry is recorded

#### Scenario: Rate limit
- **WHEN** one client address exceeds the sign-in rate limit
- **THEN** the API responds `429 Too Many Requests` with a problem-details body

### Requirement: Remembered devices
At the second step the user MAY choose to remember the device; a remembered device SHALL skip the
second factor on that browser for 30 days. Remembered devices SHALL stop being honoured when the
user changes or resets their password, when their two-factor authentication is reset, when they are
deactivated or when they sign out everywhere. The password SHALL always be required.

#### Scenario: Remembered device skips the code
- **WHEN** a user who chose "remember this device" 10 days ago signs in again with email and password on the same browser
- **THEN** the user is signed in without a second-factor code

#### Scenario: Remembered device after password change
- **WHEN** the user changed their password after the device was remembered
- **THEN** the next sign-in on that device asks for the second factor again

#### Scenario: Not remembered by default
- **WHEN** a user completes the second step without choosing "remember this device"
- **THEN** the next sign-in on that browser asks for the second factor

### Requirement: Password reset by email
A user SHALL be able to request a password reset by entering their email. The response SHALL be
identical, in content and timing, whether or not the email belongs to an account. For an `ACTIVE`
user who has enrolled two-factor authentication a single-use link valid for 1 hour SHALL be emailed
in their `locale`, at most once every 5 minutes per account; users without two-factor
authentication get no link (an Admin re-invites or resets them), so a mailbox alone never gives
access to an account. Setting a new password with it SHALL end all
existing sessions and remembered devices and SHALL NOT bypass the second factor at the next sign-in.
A lost authenticator SHALL be handled only by an Admin two-factor reset.

#### Scenario: Reset requested for an existing account
- **WHEN** an active user requests a reset
- **THEN** a translated confirmation is shown and a reset email with a single-use link is sent

#### Scenario: Reset requested for an account without two-factor authentication
- **WHEN** someone requests a reset for an active user who has not enrolled an authenticator
- **THEN** the same confirmation is shown and no email is sent

#### Scenario: Reset requested for an unknown email
- **WHEN** someone requests a reset for an email with no account
- **THEN** the same confirmation is shown and no email is sent

#### Scenario: Expired reset link
- **WHEN** a reset link older than 1 hour or already used is opened
- **THEN** a translated "link no longer valid" message is shown and the password is not changed

#### Scenario: Second factor still required
- **WHEN** a user sets a new password through a reset link and signs in
- **THEN** the second factor is required, even on a previously remembered device

### Requirement: Sessions
A session SHALL be carried only in an HttpOnly, SameSite=Strict cookie (Secure outside local
development) on the UI origin; no credential or token SHALL be stored in browser storage. A session
SHALL expire after 60 minutes without activity and at the latest 12 hours after sign-in. Every
state-changing request SHALL carry a valid anti-forgery token, otherwise it SHALL be rejected. Signing
out SHALL end the session. Deactivation, role change, password change and two-factor reset SHALL
take effect on the affected user's next request.

#### Scenario: Idle timeout
- **WHEN** a signed-in user makes a request after 61 minutes of inactivity
- **THEN** the API responds `401 Unauthorized` and the UI shows the sign-in page with a translated "session expired" message, returning to the same page after sign-in

#### Scenario: Missing anti-forgery token
- **WHEN** a signed-in user's browser sends a `POST` to the API without a valid anti-forgery token
- **THEN** the API responds `400 Bad Request` and nothing is changed

#### Scenario: Deactivation ends sessions
- **WHEN** an Admin deactivates a user who is currently signed in
- **THEN** that user's next request responds `401 Unauthorized`

#### Scenario: Sign out
- **WHEN** a user signs out from the user menu
- **THEN** the session cookie is removed and protected pages redirect to the sign-in page

### Requirement: Account self-service
A signed-in user SHALL be able to see their name, email and role; change their preferred language;
change their password (requiring the current password); regenerate recovery codes (requiring a
current authenticator code; previous codes stop working); and sign out everywhere (ending every
session and remembered device, including the current one). A wrong current password or
authenticator code on these steps SHALL count towards the account lockout, like a failed sign-in,
and these steps SHALL be rate limited.

#### Scenario: Change password
- **WHEN** a user enters their current password and a new password meeting the policy
- **THEN** the password is changed, other sessions and remembered devices are ended and the current session continues

#### Scenario: Wrong current password
- **WHEN** a user enters a wrong current password
- **THEN** the password is not changed and a translated message is shown

#### Scenario: Regenerate recovery codes
- **WHEN** a user confirms with a valid authenticator code
- **THEN** 10 new recovery codes are shown once and the previous codes stop working

#### Scenario: Sign out everywhere
- **WHEN** a user chooses "sign out everywhere" and confirms
- **THEN** all their sessions end, remembered devices are forgotten and the user is taken to the sign-in page

### Requirement: User management by Admins
Admins SHALL be able to list users (name, email, role, status, two-factor enabled, last sign-in) in
a table where every column is sortable, the role is a tag (see "Tags for fixed values"), the status
and two-step verification are status badges, and a user who never signed in shows "never" in the
muted text colour,
filter them by role and status (including `ERASED`), edit a user's name, role and locale, deactivate and reactivate
users, resend invitations and reset a user's two-factor authentication. Every one of these
operations SHALL be available only to Admins; FiringChiefs SHALL receive `403 Forbidden` from the
API and a translated "not allowed" page in the UI. The last active Admin SHALL NOT be deactivated or
changed to `FIRING_CHIEF` (blocking). An `ERASED` user SHALL NOT be edited, reactivated, re-invited
or have their two-factor authentication reset (blocking, `409 Conflict`). Destructive actions SHALL be confirmed in the UI.

#### Scenario: Admin lists users
- **WHEN** an Admin opens the users page
- **THEN** a table of all users with name, email, role as a tag, status and two-factor state as badges, and last sign-in is shown, sortable by every column

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

### Requirement: First Admin bootstrap
The application SHALL provide a host command that invites the first Admin by email and name. The
command SHALL refuse to run once an Admin has set a password (blocking), so it cannot be used to
gain access to an initialised installation, and the refusal SHALL be audited. Run again with the
email of an Admin who is still only invited, it SHALL send a new invitation link and invalidate the
previous one, so a lost or expired first invitation needs no manual repair. Nothing SHALL be kept
if the invitation email cannot be sent.

#### Scenario: Empty installation
- **WHEN** the command runs with an email and name on a database with no users
- **THEN** an `ADMIN` user with status `INVITED` is created and the invitation email is sent

#### Scenario: Admin already exists
- **WHEN** the command runs on a database with an Admin who has set a password
- **THEN** it exits with a non-zero code, creates nothing, and logs and audits the refusal

#### Scenario: First invitation lost
- **WHEN** the command runs again with the email of the Admin it invited, who has not accepted yet
- **THEN** a new invitation email is sent and the previous link stops working

### Requirement: Comparsa scoping (BR-12)
The server SHALL determine, for every request, the set of comparsas the signed-in user may access:
all comparsas for an Admin, and only the comparsas assigned to them for a FiringChief. A FiringChief
with no assignments SHALL have access to no comparsa (deny by default). Data of a comparsa outside
the user's scope SHALL NOT be returned, and a request for it SHALL be answered as if it did not exist
(`404 Not Found`). This rule is blocking and SHALL be enforced on the server regardless of the UI.

#### Scenario: Admin scope
- **WHEN** the scope of a signed-in Admin is evaluated
- **THEN** it grants access to every comparsa

#### Scenario: FiringChief without assignments
- **WHEN** the scope of a FiringChief with no comparsa assignments is evaluated
- **THEN** it grants access to no comparsa

#### Scenario: FiringChief with an assignment
- **WHEN** the scope of a FiringChief assigned to one comparsa is evaluated for that comparsa and for another one
- **THEN** access is granted for the assigned comparsa and denied for the other

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
