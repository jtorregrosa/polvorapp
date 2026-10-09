# Spec Delta

## MODIFIED Requirements

### Requirement: Status semantics
Every domain status SHALL be rendered by one status component that always shows a translated text
label together with a semantic colour and an icon, never colour alone. The mapping SHALL cover:
- `LicenseStatus`: `VALID` → success, `EXPIRED` → destructive, `PENDING` → info. The derived
  "expiring soon" state of a `VALID` license with the compliance warning `LICENSE_EXPIRING` →
  warning;
- `ArquebusierStatus`: `ACTIVE` → success, `RESERVE` → muted;
- `ComparsaOrder` status: `DRAFT` → muted, `SUBMITTED` → info, `RETURNED` → warning, `VALIDATED` →
  success;
- `FestivalEdition` status: `DRAFT` and `CLOSED` → muted, `ORDERS_OPEN` → success,
  `CORRECTIONS_OPEN` → warning, `LOCKED` → info;
- two-step verification of a user: `ENABLED` → success, `NOT_SET` → muted;
- compliance warnings (BR-04): `LICENSE_MISSING`, `LICENSE_PENDING`, `LICENSE_EXPIRED`,
  `LICENSE_EXPIRING`, `COURSE_MISSING`, `UNDER_AGE`, `ID_PHOTO_MISSING` and
  `LICENSE_PHOTOS_MISSING` → warning;
- a holder's powder handover on a capture device (UC-21): `TO_DELIVER` → muted, `PENDING` → info,
  `SYNCED` → success, `CONFLICT` → warning.

Compliance warnings SHALL never use the destructive colour, because they do not block (BR-04 is a
warning).

#### Scenario: Expired license
- **WHEN** a license with status `EXPIRED` is rendered in Valencian
- **THEN** the badge shows the Valencian label for "expired", the destructive colour and an icon

#### Scenario: Compliance warning is not an error
- **WHEN** a missing-course warning (BR-04) is rendered
- **THEN** it uses the warning colour and icon, not the destructive ones

#### Scenario: Expired-license warning is not an error
- **WHEN** the `LICENSE_EXPIRED` warning is rendered
- **THEN** it uses the warning colour and icon, while the `EXPIRED` license status keeps the destructive colour

#### Scenario: Unknown status value
- **WHEN** a status value outside the mapping is rendered
- **THEN** a neutral badge with the raw code is shown and the case is reported in development builds

#### Scenario: Two-step verification not set
- **WHEN** the users table shows an invited user who has not enrolled an authenticator
- **THEN** the second line of the user's status shows a muted two-step verification badge with the translated "not set" label and its icon

#### Scenario: Handover in conflict
- **WHEN** the capture screen shows a holder whose handover another device recorded first
- **THEN** the holder's badge shows the translated "conflict" label, the warning colour and an icon
