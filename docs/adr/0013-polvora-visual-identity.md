# 0013. "Pólvora" visual identity

- Status: Accepted
- Date: 2026-10-01
- Supersedes: [0012](0012-polvorapp-visual-identity.md) (its rules are carried over below)

## Context

ADR-0012 gave PolvorApp its own identity and left the palette to the design system. The first
version of that design system used stock shadcn/ui with Inter, warm stone greys and an ember accent.
The maintainer found the result plain and unattractive. The audit in
`docs/design/layout-audit.md` confirmed that the look is generic ("stock shadcn"), and it found
contrast defects in focus and hover states.

Three directions were compared on the same screens:
- **A "Consola"**: Linear / Vercel;
- **B "Registro"**: GOV.UK Design System;
- **C "Pólvora"**: Stripe Dashboard / Atlassian with its own character.

The maintainer chose C with B's form rules, after reviewing a working demo. A new identity and new
fonts need an ADR that supersedes ADR-0012.

## Decision

- PolvorApp keeps **its own identity**: name, its original "P" mark and a palette chosen for the
  product. **No logos or brand assets of the Federation or any other organisation are committed**.
  A customer logo (for example on printed badges, UC-30) is supplied at deployment time, outside
  git (carried over from ADR-0012).
- **Typefaces**, all OFL-1.1 and self-hosted with the application:
  - Bricolage Grotesque for titles, record names and key figures;
  - Geist for the interface;
  - Geist Mono for identifiers (nationalId, federationId, guide numbers).

  They replace Inter.
- **Palette**: cool lavender greys for surfaces and text, and a dark "night" sidebar (`#191524`
  light, `#0a0910` dark) in both themes. **Ember** (`#b8430b` light, `#ff8a4a` dark) is the only
  accent: the primary action, links, focus, the current item and selection. Status colours remain
  separate semantic tones. Every pair is verified at WCAG 2.2 AA in both themes.
- **Character with restraint**: one display face used for titles and figures only, at most one
  accent, no gradients except the mark, and motion limited to the tokens of the design system.
- The exact values live in `frontend/src/styles/tokens.css` and `docs/design/tokens.md` (generated),
  as ADR-0009 requires. ADR-0009's component layers, guardrails and catalogue are unchanged.

## Consequences

- The UI gains a recognisable look without adopting another organisation's brand.
- Three font packages replace one: about 120 KB of WOFF2 instead of about 40 KB, loaded with
  `font-display: swap`. The CSP is unchanged, because the fonts are served from `'self'`.
- The night sidebar adds a second surface family whose contrast has to be tested as well. The
  contrast test covers it.
- Changing the identity again needs a new ADR.

## Alternatives considered

- **A "Consola" (Linear / Vercel)**: dense, near-monochrome and keyboard-first. Rejected: too dense
  for occasional volunteer users, and still close to the generic look.
- **B "Registro" (GOV.UK)**: Atkinson Hyperlegible Next, large type and no ornament. Rejected as the
  identity, because it still read as plain. Its form rules were adopted.
- **Keeping Inter with a new palette**: the smallest change, but Inter was part of the generic
  fingerprint.
- **Loading the fonts from Google Fonts**: a third-party request and a CSP change, for no gain.
