# PolvorApp

Management portal for the arquebusiers of the Moros y Cristianos festival of San Vicente del
Raspeig (Federation + ~20 comparsas). Start with `docs/README.md`.

## Language

- Talk to the maintainer in **Spanish**.
- Everything in the repository is in **English**: code, identifiers, comments, commits, specs, docs.
- Domain terms follow `docs/glossary.md` ("Code term" column). Keep Spanish-only terms as defined there (e.g. `Comparsa`).
- The UI ships in `es-ES`, `ca-ES-valencia` and `en`; never hard-code user-facing text.

## Workflow

- Spec-driven with **OpenSpec**: `/opsx:propose` → review → `/opsx:apply` → `/opsx:archive`.
  Project context lives in `openspec/config.yaml`; implement changes in the order of `docs/mvp.md`.
- Do not write application code outside an approved OpenSpec change.
- Accepted decisions are in `docs/adr/`. Do not contradict them; propose a new ADR that supersedes one instead.
- Business rules (`BR-xx`), use cases (`UC-xx`), security measures (`SEC-xx`) and NFRs (`NFR-xx`) are
  referenced by ID in specs, code comments where useful, and tests.
- ECC agents, skills and rules in `.claude/` are a curated subset (`.claude/ECC.md`). Project docs win over generic ECC guidance.

## Hard rules

- **Never commit real personal data.** `docs/sources/` holds real spreadsheets, PDFs and photos and is
  git-ignored (except `answers.md`). Never copy names, DNI/NIE, phones, emails or photos from it into
  code, fixtures, seeds, specs, docs or commit messages. Use synthetic data only. The repository is public.
- No secrets in git: configuration via environment variables; commit `.env.example` only.
- Compliance checks (license, course, age) are **warnings**; data-integrity rules are **blocking** (see `docs/data-model.md`).
- FiringChiefs only access their own comparsa; enforce it server-side. Every write and export is audit-logged.
- Frontend feature screens use only `src/components/app/` composites and design tokens (ADR-0009).

## Tooling

- Keep ECC in sync with `node scripts/ecc-sync.mjs --check` / `node scripts/ecc-sync.mjs`, then review `git diff .claude`.
