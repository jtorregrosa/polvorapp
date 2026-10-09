# Design

## Context

See `proposal.md` for the motivation and `specs/` for the behaviour.

Today the sidebar is a flex column with three parts. `SidebarHeader` holds the mark.
`SidebarContent` is the only part that scrolls and holds the comparsa cards and the navigation.
`SidebarFooter` holds the API version (`VersionFooter`). The whole footer is
`group-data-[collapsible=icon]:hidden`, so it disappears in the icon rail.

Labels fade with the shared `SIDEBAR_LABEL` classes (refine-navigation-and-lists D3): they fade out
before the rail is reached and fade in once the width is restored.

`AppLayout` is a role-agnostic composite. `AppShell` knows the session's role, and already uses
`identity:roles.<ROLE>` to label the role in the user menu.

The night sidebar is `#191524` (light theme) and `#0a0910` (dark theme). ADR-0013 makes ember the only
accent. The warning tone appears only in the content area, never in the sidebar. It is a dark amber
in the light theme (`#854d0e`, soft `#fdf3d6`), but a bright yellow in the dark theme (`#fcd34d`), close
to the armband's hue. So the dark theme is the case to check against warning confusion (see Risks).

## Goals / Non-Goals

**Goals:**
- Keep `AppLayout` free of role logic: it renders an armband only when it is given a label.
- Add no new strings, no backend work and no new dependency.
- Keep the collapse animation free of layout shift: the armband's height is the same in both states.

**Non-Goals:**
- A generic "badge slot" API for other insignia. YAGNI: one role, one armband.
- Animating the armband itself. It only takes part in the existing label fade.

## Decisions

### D1. `AppLayout` takes an `armband?: string`, and `AppShell` decides

`AppLayout` gets an optional, already translated `armband` label (same convention as the other
props: "already translated"). `AppShell` passes `tIdentity('roles.FIRING_CHIEF')` when
`session.account?.role === 'FIRING_CHIEF'`, and nothing otherwise.

- *Alternative: pass the role to `AppLayout`.* Rejected: composites must not know domain roles
  (ADR-0009 layers). The bottom bar and the cards already follow the "shell decides, layout renders"
  split.
- *Alternative: a separate component placed by `AppShell`.* Rejected: the armband must sit between
  `SidebarContent` and `SidebarFooter`, which only `AppLayout` controls.

Because it reuses `identity:roles.FIRING_CHIEF`, the armband always says the same as the user menu.
It also switches language with it (UC-27) through the existing `t` re-render.

### D2. Its own block between the content and the footer, outside the footer's padding

The armband is rendered as a sibling **between `SidebarContent` and `SidebarFooter`**, not inside
the footer:
- The footer is hidden in the rail, but the armband must stay there as a stripe.
- The footer has `p-2`, but the armband must reach both edges of the sidebar ("full width", like
  fabric around the arm).
- `SidebarContent` is the scrolling flex child, so a `shrink-0` block after it stays pinned at the
  bottom of the sidebar (spec: "stays visible while the navigation scrolls").

Markup: a `<p data-slot="sidebar-armband">` holding the label in a `<span data-sidebar-label>` with
the `SIDEBAR_LABEL` classes. It has no role, no landmark and no tabindex: plain text, read in
document order after the navigation, like the version line. It is not inside a `<nav>`, because it
is not navigation.

- *Alternative: a diagonal corner ribbon.* Rejected during exploration. It looks like a "beta"
  ribbon, rotated text copes badly with zoom and text spacing, and the rail would clip it.
- *Alternative: a pill/badge.* Rejected: a yellow pill reads as a warning status (design-system:
  Status semantics), and it loses the armband image.

### D3. Rail: same box, text faded, hidden from assistive technology

In the rail, the block keeps its height and its yellow background, so it becomes a stripe across the
3.5 rem rail with nothing moving. The label fades with `SIDEBAR_LABEL` and is clipped with
`overflow-hidden`, as the navigation labels are. While the rail is shown (`useIconRail()`), the block
gets `aria-hidden="true"`.

The spec asks for nothing to be announced in the rail. The role is still in the user menu, so no
information is lost. There is no tooltip: the stripe cannot take focus, and a tooltip on content that
cannot be focused would exclude keyboard users (WCAG 1.4.13 / 2.1.1).

Implementation notes (found in the Storybook review):
- In the rail, the faded label is kept on one line (`whitespace-nowrap`). Otherwise it wraps letter by
  letter in 3.5 rem and stretches the stripe over half the rail.
- The API version footer used to be `display: none` in the rail, which made the armband above it drop
  by the footer's height. Under an armband it is now `visibility: hidden` on one line: still hidden,
  also from assistive technology, as the Application shell requires, but keeping its height, so the
  armband keeps its place. Without an armband (Admins) it stays `display: none`, as before.

On phones the sidebar is the drawer, never the rail (`useIconRail` is false on mobile), so the
drawer shows the full armband with no extra code. The bottom bar is a separate component and is not
touched.

### D4. Look: flat yellow band with "stitch" edges, no gradient

- Background `sidebar-armband`, text `sidebar-armband-foreground`.
- A 1 px top and bottom border in `sidebar-armband-edge`, a darker yellow, reads as the band's
  hemmed edges. Borders, not a gradient: ADR-0013 allows gradients only in the mark.
- Label: the interface typeface (Geist), `text-label` size, semibold, `uppercase`, wide tracking
  (`tracking-wider`), centred. The display typeface (Bricolage) is kept for titles and figures only
  (ADR-0013).
- Vertical padding gives a band of about 32 px with one line of text. A long or spaced-out label
  wraps (`text-balance`, `break-words`) and the band grows; it is never clipped or truncated. In the
  rail the block keeps the expanded height, so nothing moves.
- No icon, no motion of its own.

Using CSS `text-transform: uppercase` instead of upper-case strings keeps the translation keys
shared with the user menu, and screen readers read the text as words, not as letters.

### D5. Three insignia tokens, the same in both themes, outside the accent

`tokens.css` gains, in both theme blocks (the sidebar is night in both), with `@theme inline`
mappings:

| Token | Value | Role |
|---|---|---|
| `--sidebar-armband` | `#f5c518` | Armband yellow |
| `--sidebar-armband-foreground` | `#1f1a05` | Armband text |
| `--sidebar-armband-edge` | `#b8890a` | The band's 1 px hemmed edges |

Measured contrast:
- text on yellow: 10.66:1 (AA text needs 4.5:1);
- yellow on night: 10.97:1 light, 12.16:1 dark (non-text needs 3:1);
- the edge is decorative and is not a UI component, so it has no minimum. It is 5.64:1 against the
  night surface anyway.

`contrast.test.ts` gains the text pair (`TEXT`) and yellow on `sidebar` (`UI`). `design-docs.ts`
describes the three tokens as "insignia only; never an accent or a status", so the generated
`docs/design/tokens.md` documents the restriction.

**Why no new ADR**: ADR-0013 limits *accents*, the colours that carry interaction and selection.
The armband carries none of that. It depicts a physical object, just as comparsa logos bring their
own colours into the same sidebar. The `design-system` delta records the exception and its limits.
If the maintainer later wants yellow anywhere else, that would be a second accent and would need a
new ADR.

- *Alternative: reuse `warning`.* Rejected: its value changes between themes (dark amber in the
  light theme, bright yellow in the dark one), and it would tie the insignia to "something is
  wrong".
- *Alternative: the Tailwind palette (`yellow-400`).* Rejected: feature code uses design tokens only
  (design-system: No values outside the tokens).

### D6. Backend, API, i18n, security

- **Backend / data model / migrations / API**: none. The role comes from the existing session
  endpoint.
- **i18n**: no new keys. `identity:roles.FIRING_CHIEF` exists in es-ES ("Jefe de disparo"),
  ca-ES-valencia ("Cap de disparada") and en ("Firing chief").
- **Security / GDPR**: the armband only displays the signed-in user's own role, which the user menu
  already shows. It grants nothing: authorisation stays server-side (BR-12, SEC measures unchanged).
  It holds no personal data and involves no write or export, so nothing is audit-logged.

## Risks / Trade-offs

- [Yellow next to ember on the night surface could look busy] → It sits only at the bottom edge,
  away from the ember current-item bar and counters. It is reviewed in the Storybook stories
  (expanded, rail, long Valencian label, both themes) before merging.
- [A FiringChief could read the yellow as a warning, above all in the dark theme, where `warning` is
  a similar yellow] → It is not a pill, has no icon, uses no status token, and sits in the sidebar,
  where no status is ever shown. The stories are to be reviewed in the dark theme. The component catalogue test already renders every story in both themes and runs axe on them. A test allows the
  armband tokens only in `AppLayout`, so they cannot leak into statuses (design-system: "Armband
  yellow is not reused").
- [The two theme blocks hold the same literals and could drift] → The contrast test asserts that the
  armband tokens have equal values in both themes.
- [Vertical space on short screens: the band takes about 32 px from the scrolling navigation] → The
  navigation scrolls and its entries stay reachable. The cost is the same as the version line.
- [An `aria-hidden` element in the rail that was focusable would be an axe failure] → The armband is
  never focusable, so `aria-hidden` is safe. Covered by the axe runs in the rail.
- [Uppercase plus tracking makes "CAP DE DISPARADA" (16 characters) the widest label] → It fits in
  16 rem at the default size. The WCAG 1.4.12 text-spacing scenario and the `LongValencian` story
  check that it wraps.

## Verification (2026-10-09)

- Build, lint, Prettier, `check-i18n` and typecheck pass.
- Vitest: the full suite passes (2757 tests). Two tests time out only under load and pass on their
  own; neither involves the shell:
  - `ComparsaFacts` fails once in a full run without coverage and passes three times in a row alone;
  - with coverage, a few timeouts appear in unrelated features.
- Coverage of the changed files: `AppLayout.tsx` 94.7 % and `AppShell.tsx` 97.1 % of lines.
- Playwright on the compose stack:
  - `armband.spec.ts` (9 tests, desktop and 360 px) passes twice in a row;
  - `layout`, `navigation`, `platform` and `comparsa-logos` pass;
  - `identity.spec.ts` fails locally with 429s from the default auth rate limits, which CI raises
    (`RATE_LIMIT_AUTH_*`). The change does not touch the public layout or sign-in.
- Reviews (`a11y-architect`, `react-reviewer`, `typescript-reviewer`, `security-reviewer`,
  `pr-test-analyzer`, `e2e-runner`): no CRITICAL findings. Two HIGH findings were fixed:
  - the WCAG 1.4.12 scenario had no test; the e2e spec now checks the Valencian label with text
    spacing;
  - the e2e spec assumed Spanish names, and its tooltip check proved nothing. It now matches all
    three languages, restores the language it changes, and has a positive tooltip control.

  MEDIUM findings fixed:
  - the version footer keeps its height only under an armband;
  - a test guards against reusing the armband yellow;
  - a test checks that the armband tokens match in both themes;
  - the warning-tone note now covers the dark theme.
- Follow-up (outside this change): with Playwright's instant pointer moves, a rail tooltip can stay
  open after the pointer leaves its entry (Radix grace area). It was not seen with a real pointer.
