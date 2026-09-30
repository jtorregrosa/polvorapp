# 0009. Design system: shadcn/ui + Tailwind CSS v4 with project guardrails

- Status: Accepted — §5 (Brand) superseded by [0012](0012-polvorapp-visual-identity.md)
- Date: 2026-09-30

## Context

PolvorApp needs a coherent, accessible (WCAG 2.1 AA, NFR-07), responsive UI with room for the
Federation's own identity. Most UI code will be AI-generated, which tends to drift unless rules
are explicit. shadcn/ui gives flexibility and code ownership but, unlike a full design system
(e.g. IBM Carbon), it does **not** provide usage guidelines — the project must supply them.

## Decision

Use **shadcn/ui** (Radix primitives) styled with **Tailwind CSS v4**, plus these guardrails:

1. **Closed design tokens.** Colours, typography, spacing, radii, shadows and breakpoints are
   defined once as CSS variables in the Tailwind `@theme` (light and dark). Semantic colour tokens
   (`primary`, `destructive`, `warning`, `success`, `muted`…) — never raw palette colours in features.
   Lint forbids arbitrary values (`text-[#c00]`, `mt-[13px]`).
2. **Two component layers.**
   - `src/components/ui/` — shadcn/ui primitives, kept close to upstream.
   - `src/components/app/` — PolvorApp composites: `PageHeader`, `DataTable`, `FormField`,
     `FormSection`, `StatusBadge` (license/course/order states), `EmptyState`, `ConfirmDialog`,
     `PhotoUpload` (crop), `AlertBanner`, `LanguageSwitcher`…
   - **Feature screens use `components/app/` only**; importing `components/ui/` directly from a
     feature is a lint error, except inside `components/app/`.
3. **Design guide** in `docs/design/` (written in the `add-design-system` change): principles,
   page templates (list, detail, form, dashboard), validation and error patterns, confirmation
   and destructive actions, status colour semantics, empty/loading states, responsive rules,
   microcopy tone in the three languages, and the arquebusier badge layout. It is referenced from
   the OpenSpec project context so every change follows it.
4. **Component catalogue** (Storybook) for `components/app/`, with automated accessibility checks (axe).
5. **Brand**: Federation green (from the current arquebusier badge) as `primary`; exact values
   defined in the design guide.

## Consequences

- Full visual freedom and code ownership; excellent AI-generation quality.
- The project owns the design system: the guide and the `components/app/` layer must be built
  **before** the first feature screen (change `add-design-system`).
- shadcn/ui updates are pulled manually when useful.

## Alternatives considered

- **IBM Carbon** — complete design system with guidelines and free data table, but corporate look
  and less flexible branding.
- **MUI** — advanced data grid is paid; strong Material look.
- **Mantine** — great forms, no design guidelines.
