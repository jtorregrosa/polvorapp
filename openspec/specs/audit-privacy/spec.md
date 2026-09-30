# audit-privacy Specification

## Purpose
Keeps an append-only record of who changed what and when, and of every export and security event,
so the Federation can reconstruct and justify its data (SEC-05, UC-25), and later serves GDPR
requests (UC-26).

## Requirements

### Requirement: Audit trail of writes and security events
Every write to business data, every export and every security event SHALL be recorded as an audit
entry with: the time (UTC), the acting user (or none for anonymous events), the action code, the
target entity type and identifier, the comparsa concerned when there is one, the request's
correlation identifier and a structured description of the change. An audit entry SHALL be stored
in the same transaction as the change it describes, so a change is never saved without its entry
and an entry is never saved for a change that failed. Audit entries SHALL NOT contain passwords,
authenticator keys, one-time codes, recovery codes, invitation or reset tokens, or photo content.

#### Scenario: Entry stored with the change
- **WHEN** a user-management change is saved
- **THEN** exactly one audit entry describing it exists with the actor, action, target, time and correlation identifier

#### Scenario: Failed change leaves no entry
- **WHEN** a change fails and its transaction is rolled back
- **THEN** no audit entry for it is stored

#### Scenario: Secrets are never recorded
- **WHEN** a password change, invitation acceptance or recovery-code sign-in is audited
- **THEN** the audit entry contains no password, token or code value

### Requirement: Audit trail is append-only
The application SHALL provide no operation that updates or deletes audit entries. Erasure of a
person's data under GDPR (UC-26, later change) is the only permitted exception and is out of scope
here.

#### Scenario: No modifying endpoint
- **WHEN** the API description is inspected
- **THEN** it contains no endpoint that updates or deletes audit entries
