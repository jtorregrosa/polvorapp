# 0010. Open-source license: MIT

- Status: Accepted
- Date: 2026-09-30

## Context

The repository is public (NFR-14). Other festival federations could reuse PolvorApp.

## Decision

Release the code under the **MIT License**. A `LICENSE` file is added in `bootstrap-platform`.

## Consequences

- Maximum reuse with no obligations for adopters.
- Forks offered as a service need not publish their changes (accepted trade-off vs. AGPL-3.0).
- Third-party dependencies must be MIT-compatible; libraries with revenue-based or
  non-commercial licenses (e.g. QuestPDF Community) are acceptable only if their terms fit the
  Federation's use — verified at bootstrap.
