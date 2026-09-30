# PolvorApp Development Workflow

This rule **overrides** the generic ECC rules in `rules/ecc/` (notably `common/agents.md`,
`common/development-workflow.md`, `common/testing.md`) wherever they conflict.

## Agent names

ECC agents are installed at project level **without** the `ecc:` prefix. Use `planner`,
`code-reviewer`, `tdd-guide`, etc. — not `ecc:planner`.

## OpenSpec is the only plan

Planning happens exclusively in OpenSpec changes (`/opsx:propose`): proposal, delta specs,
design and tasks. Do not produce separate PRDs, task lists or plans with the `planner` agent.
Use `architect` only to evaluate an architectural question inside a change's `design.md`;
if it leads to a new decision, write an ADR in `docs/adr/`.

Small changes that do not alter specified behaviour (bug fixes, copy, minor styling, dependency
and tooling updates) skip OpenSpec — see `CLAUDE.md` — but still follow TDD, reviews and verification.

## Research before implementing

Before adding a dependency or writing non-trivial code, verify current behaviour and versions:

1. **Context7** — official, version-specific library docs (.NET, EF Core, ASP.NET Core Identity,
   React, Vite, TanStack, React Hook Form, Zod, react-i18next, Tailwind, shadcn/ui, Playwright, QuestPDF…).
2. **gh** (`gh search code`, `gh search repos`) — real-world usage and proven implementations.
3. **Exa** — broader web research when the first two are insufficient.

Prefer battle-tested libraries already fixed in the ADRs; a new overlapping library needs an ADR.

## Phase → practice → subagents

| Phase | Practice | Subagents |
|---|---|---|
| `/opsx:propose` | Proposal, specs, design, tasks | `architect` only for architectural questions |
| `/opsx:apply`, per task | **TDD**: failing test → minimal code → refactor (`tdd-workflow`, `csharp-testing`, `react-testing`) | Implementation is done **sequentially in the main session** — no parallel writers in the same working tree |
| After each task group | Review **in parallel** (read-only agents), fix CRITICAL/HIGH findings before continuing | `csharp-reviewer` (C#), `react-reviewer` / `typescript-reviewer` (frontend), `security-reviewer` (auth, personal data, uploads, exports), `database-reviewer` (migrations, queries), `a11y-architect` (screens/components), `silent-failure-hunter` (error handling) |
| Before `/opsx:archive` | `verification-loop` (build, types, lint, tests + coverage, security grep, diff review) and Playwright E2E for the change's critical flows | `e2e-runner`, `pr-test-analyzer` |
| Build broken | Minimal fix only | `build-error-resolver` (.NET/TS), `react-build-resolver` (Vite/React) |
| Docs drift | Update docs touched by the change | `doc-updater` |

## Pull request and archive

One pull request per OpenSpec change; the change is archived **inside that pull request**, so
merging leaves `main` with the code, the synced main specs and the archived change at once.

1. Implement on a `feat/<change-id>` branch and open the pull request (commit and push only with
   the maintainer's go-ahead).
2. CI green and the maintainer's review/approval recorded in the pull request.
3. Only then, as the last commit on the same branch: `/opsx:archive` (sync delta specs into
   `openspec/specs/`, move the change to `openspec/changes/archive/`, mark it done in `docs/mvp.md`).
   Never archive before approval: requested changes would then touch an archived change.
4. CI green again, then merge (rebase).

## Testing

- Coverage target **80 %** for domain logic and API endpoints; UI components tested by behaviour.
- Backend: xUnit, integration tests against real PostgreSQL via Testcontainers (no in-memory DB).
- Frontend: Vitest + Testing Library, axe assertions for components.
- E2E: **Playwright only** (ADR-0011). Ignore `e2e-runner`'s "Vercel Agent Browser" preference.
- Test data is always synthetic.

## Stack-specific corrections to generic ECC guidance

- `database-reviewer`: PostgreSQL via **EF Core migrations**; Supabase/RLS advice does not apply
  (authorisation is enforced in the application, BR-12).
- `backend-patterns` / `error-handling`: examples are Node/TS; apply the principles to ASP.NET Core
  with `dotnet-patterns` as the primary .NET reference.
- Commits follow Conventional Commits, in English, one logical change per commit.
