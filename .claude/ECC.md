# ECC (Everything Claude Code) — curated subset

Source: https://github.com/affaan-m/ECC — MIT License (see `ECC-LICENSE`).

PolvorApp uses a **curated, project-scoped subset** of ECC (agents, skills and path-scoped
rules for .NET, React/TypeScript, PostgreSQL, Docker, accessibility, testing and security).
No hooks, MCP servers or commands are installed: the OpenSpec `/opsx:*` commands drive the workflow.

- **What is installed:** `ecc-manifest.json` (single source of truth, including installed version and commit).
- **Check for updates:** `node scripts/ecc-sync.mjs --check`
- **Update:** `node scripts/ecc-sync.mjs` (or `--ref <tag>`), then review `git diff .claude` and commit.
- **Add or remove an item:** edit `ecc-manifest.json` and run the sync. Removed items must be deleted by hand.

Project decisions in `docs/` (ADRs, design guide) take precedence over generic ECC guidance.
