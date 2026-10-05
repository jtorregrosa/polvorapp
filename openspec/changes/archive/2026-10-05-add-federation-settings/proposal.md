# Proposal

## Why

Several values that belong to the Federation are fixed in code or deployment variables today:
- its names printed in documents (`Exports.Contracts.FederationNames`);
- the sender shown in every email (`Email:From`);
- the reminder lead times (`Schedule`, 7 days).

Its logo sits at the bottom of the comparsas page. The maintainer wants one "Settings" section
where an Admin manages these global values, and that later customisations can grow into: texts,
document templates, more order and calendar options. This change creates that section with a
first, useful set of settings.

Capability (from `docs/mvp.md`): **`federation-catalog`**, which already owns the Federation's
settings row and its logo, with the reminder and email effects in **`notifications`**. It relies
on:
- UC-24 (Federation catalogue) and UC-23 (notifications);
- **SEC-05** (audit of every write);
- **SEC-01/SEC-02** (no secrets outside environment variables, so SMTP credentials and the sender
  address stay deployment settings);
- ADR-0001 (other modules read settings through `FederationCatalog.Contracts`), ADR-0008, ADR-0009.

It **depends on `refine-navigation-and-lists`**: it uses the "Administration" navigation section
and the picture actions of the Federation logo. Apply it after that change.

## What Changes

- **Federation settings** (one row, the existing `FederationSettings`), in sections:
  - *Identity*: official name in Spanish and Valencian (used by documents instead of the
    constants), a short name (UI and email footer), and an optional public contact email and
    website;
  - *Federation logo*: moved from the comparsas page, with the same rules;
  - *Emails*: the sender display name and an optional reply-to. The sender address, SMTP host and
    credentials stay in environment variables;
  - *Orders*: the first close reminder lead time, 2–14 days, default 7;
  - *Calendar*: the milestone reminder lead time, 1–14 days, default 7.
- **Settings page** for Admins, under "Administration". It is a read-mode detail page with one
  section per group, each edited in a side panel and saved separately with optimistic concurrency.
- **Defaults** come from today's values through the migration, so nothing changes until an Admin
  edits a setting.
- **Readers**:
  - documents (badges, pickup authorisation form) read the official names;
  - email sending reads the sender name, the reply-to and the footer contact;
  - the notification schedule reads the lead times.
- **Audit**: every change is audited with previous and new values, per section.

## Non-goals

- Editable free texts and templates: the order attestation, the PDF footers, email bodies, export
  layouts (Q-44 is still open). The page is laid out for them, and they come in later changes.
- SMTP host, port, credentials or the sender address in the database.
- Per-comparsa settings, user-level preferences (already on the account page), feature flags.
- Default prices or rental models for new editions (editions already start from the previous one).
- Changing which notifications exist or who receives them.

## Capabilities

### New Capabilities
- none.

### Modified Capabilities
- `federation-catalog`:
  - "Federation logo": its section moves to the Settings page (building on the picture actions of
    `refine-navigation-and-lists`);
  - new requirements "Federation settings" and "Settings screen".
- `notifications`:
  - "Planned close reminders (BR-10)" and "Milestone reminders": lead times from the settings;
  - "Email content": the sender name, reply-to and Federation footer.

## Impact

- **Backend**:
  - `FederationCatalog`:
    - `FederationSettings` gains its columns, with a migration seeding today's values;
    - `GET`/`PUT /federation-settings/{section}` endpoints;
    - validation and audit;
    - `FederationCatalog.Contracts` gains `IFederationSettings` (read-only, cached per request);
  - `Exports`: `FederationNames` constants are replaced by the contract;
  - `Badges` and `Distribution` read the names through the contract;
  - `Notifications`: `Schedule` reads the lead times, and the email footer reads the settings;
  - the API host's email sender applies the display name and reply-to.
- **Frontend**:
  - `features/federation-catalog/pages/SettingsPage` and its sections, with `FederationLogoSection`
    moved there;
  - a navigation entry;
  - i18n in three locales;
  - the generated client.
- **Docs**:
  - `docs/development.md` (what is a setting vs an environment variable);
  - `docs/compliance.md` (no personal data in settings);
  - `docs/design/patterns.md` (settings page);
  - `docs/data-model.md` (`FederationSettings`);
  - `.env.example` notes.
- **Security and GDPR**:
  - no secrets stored;
  - Admin-only writes with audit;
  - header-injection-safe sender name;
  - the contact data must be the Federation's, which the UI says.
