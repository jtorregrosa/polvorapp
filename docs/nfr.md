# Non-Functional Requirements

> Status: **v1.0**.

## Scale and usage profile

| Dimension | Estimate |
|---|---|
| Comparsas | ~20 |
| Arquebusiers | ~600–800 active per edition, growing slowly |
| Users | ~40–60 FiringChiefs + a handful of Admins |
| Concurrency | Low; peaks during order windows and just before deadlines |
| Files | ~3 photos per arquebusier (~2–3k images, a few GB at most) |

This is a **small system**: simplicity, low cost and ease of maintenance matter more than scalability.

## Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-01 | Platform | Responsive web application. Desktop for Admins; mobile-friendly for FiringChiefs (e.g. taking license photos with the phone camera). |
| NFR-02 | i18n | UI in Spanish, Valencian and English from day one; all texts in translation files; dates and numbers localised. Exports keep the format required by each recipient. |
| NFR-03 | Offline | Offline mode only for on-site distribution (UC-21, later). The architecture must allow it (e.g. PWA) without a rewrite. |
| NFR-04 | Availability | Best effort (~99%). Maintenance outside order windows. No 24/7 support. |
| NFR-05 | Performance | Pages < 2 s on 4G; exports of the whole Federation < 30 s. |
| NFR-06 | Security | See `compliance.md` (SEC-01..12). |
| NFR-07 | Accessibility | WCAG 2.1 AA as a target. |
| NFR-08 | Cost / hosting | Minimal cost. Local development with Docker; deployment as containers. The Federation's existing hosting is to be assessed later (it must be able to run containers). |
| NFR-09 | Maintainability | Maintained by one developer (the author), skilled in .NET, Angular, React, Node. Few moving parts, automated tests, CI. |
| NFR-10 | Portability | Data exportable (Excel/CSV) so the Federation is never locked in. |
| NFR-11 | Email | Transactional email (notifications, invitations, password reset) from a Federation domain. |
| NFR-12 | Observability | Error tracking and basic logs; no personal data in logs. |
| NFR-13 | Environments | Production + staging with **synthetic data** only. |
| NFR-14 | Open source | **Public repository.** No secrets or real data in git; secrets via environment variables. |
| NFR-15 | Photos | ID photos must print sharp on the credit-card-size badge: **3:4 portrait**, minimum **600 × 800 px**, in-app cropping. |
