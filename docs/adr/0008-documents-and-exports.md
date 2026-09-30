# 0008. Excel and PDF generation on the server

- Status: Accepted
- Date: 2026-09-30

## Context

The app must produce: Excel exports for the powder supplier, rental company, Arms Authority and
per-comparsa lists (UC-17); printable distribution lists (UC-20); pre-filled proxy authorisation
forms (UC-19); arquebusier badges (UC-30). Recipient templates are still unknown (Q-44).

## Decision

- Generate documents **on the server**, so the output is identical for every user and audited (SEC-05).
- Excel: **ClosedXML** (MIT).
- PDF: **QuestPDF** (free Community license for open-source / non-profit use — verify at bootstrap).
- Each export is a named, versioned **export definition** (columns, order, formatting) so that a new
  template from a recipient only changes that definition.
- Exports contain only the columns required by each recipient (SEC-06); every export is logged.

## Consequences

- Adding or changing a recipient format is isolated and testable (golden-file tests).

## Alternatives considered

- **Client-side generation** (SheetJS, pdfmake) — harder to audit and keep consistent.
