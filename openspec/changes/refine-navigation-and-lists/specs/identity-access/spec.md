# Spec Delta

## MODIFIED Requirements

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
