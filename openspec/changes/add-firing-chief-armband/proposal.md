# Proposal

## Why

At the festival, FiringChiefs are recognised by a yellow armband worn on the upper arm with the
words "Jefe de disparo". The maintainer wants the same armband in the app: a yellow band across the
bottom of the night sidebar for FiringChief accounts. It gives the shell some character from the
festival itself, which the design review asked for.

The sidebar's contents are specified in the `platform` spec (*Application shell*). The
`design-system` spec says the brand accent is the sidebar's only accent colour. Both have to change,
so this needs a change rather than a fix.

Capabilities (from `docs/mvp.md`): **`platform`** (application shell) and **`design-system`**
(visual identity, tokens). It follows ADR-0009 (composites and tokens only), ADR-0013 (the "Pólvora"
identity, night sidebar) and ADR-0007 (i18n, three locales). It also follows NFR-01 (usable from
360 px) and NFR-07 (WCAG 2.2 AA). It reuses the translated role label already shown in the user
menu (UC-27, switching language changes it). It implements no business rule and changes no
permission.

## What Changes

- **FiringChief armband** at the bottom of the sidebar, below the navigation and above the API
  version. It stays visible while the navigation scrolls. It is a horizontal yellow band spanning
  the sidebar's full width, with the role label in capitals:
  - es-ES: "Jefe de disparo";
  - ca-ES-valencia: "Cap de disparada";
  - en: "Firing chief".
  The text follows the UI language. It is plain text with no interaction. Admins see no armband.
- **Icon rail**: when the sidebar is collapsed, the armband becomes a yellow stripe without text,
  hidden from assistive technology and with no tooltip. The role stays in the user menu.
- **Phones**: the armband shows in the navigation drawer as in the expanded sidebar. The bottom
  navigation bar does not change.
- **Insignia tokens**: the yellow and its text colour become their own design tokens (light and
  dark themes, both on the night surface) and are used only for the armband. The armband is a domain
  insignia, not an accent: the ember accent remains the only accent for actions, links, focus and
  the current item. The new pairs are covered by the contrast test.
- **Docs**: the design guide describes the armband and its tokens; the generated token page lists
  them; the glossary gains the armband term.

## Non-goals

- No insignia for Admins or any other role.
- No armband on printed documents or badges. Those keep their own layout.
- No configurable colour or text per comparsa or per Federation setting.
- No new ADR. The yellow is scoped to this insignia and is not a second accent (see `design.md`).
- No backend, API or data-model change. The role is already in the session.
- No change to the bottom navigation bar, the user menu or the public (signed-out) layout.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `platform`: a new requirement for the FiringChief armband in the application shell: content,
  position, icon-rail and drawer behaviour, and roles.
- `design-system`: the *PolvorApp visual identity* requirement allows the armband's insignia colour
  on the night sidebar, alongside the single brand accent, and requires its contrast to be verified.

## Impact

- Frontend:
  - `components/app/AppLayout` gets an optional, already translated armband label;
  - `app/shell/AppShell` passes the role label for FiringChiefs;
  - `styles/tokens.css` gains the armband tokens;
  - `styles/contrast.test.ts` and `styles/design-docs.ts` cover them;
  - AppLayout unit tests and stories, and the shell's Playwright flow, are extended.
- i18n: no new keys. The armband reuses `identity:roles.FIRING_CHIEF` in the three locales.
- Docs: `docs/design/README.md`, the generated `docs/design/tokens.md` and `docs/glossary.md`.
- Backend, API, database, audit log and GDPR: unaffected.
