# 0012. PolvorApp visual identity

- Status: Superseded by [0013](0013-polvora-visual-identity.md)
- Date: 2026-09-30
- Supersedes: [0009](0009-design-system-shadcn-tailwind.md) §5 (Brand) only

## Context

ADR-0009 §5 took the Federation green (from the current arquebusier badge) as `primary`. The
maintainer decided that PolvorApp is a product with its own identity, not a Federation site: the
Federation has not yet endorsed the project, other festival federations could reuse it (MIT,
ADR-0010), and the repository is public, so it cannot carry third-party brand assets whose
licence is not ours to grant.

## Decision

- PolvorApp has **its own identity**: name, an original mark and a palette chosen for the product
  (design-system change, `docs/design/`).
- **No logos or brand assets of the Federation or any other organisation are committed** to the
  repository. Colours may be inspired by the domain, never copies of a third-party mark.
- If a deployment wants a customer logo (for example on printed badges, UC-30), it is supplied at
  **deployment time** (configuration or object storage), outside git.
- The rest of ADR-0009 (tokens, component layers, guide, catalogue) is unchanged.

## Consequences

- The design system is free to optimise the palette for accessibility and the product's tone.
- Customer branding needs a small deployment-time mechanism when it is first required.

## Alternatives considered

- **Federation green as primary** (ADR-0009 §5) — ties the product to one organisation and
  implies an endorsement that does not exist yet.
- **Committing the Federation crest** — not licensable under MIT; rejected.
