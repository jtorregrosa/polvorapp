# Spec Delta

## ADDED Requirements

### Requirement: Erased entries in exports
Entries erased by a GDPR request (comparsa orders capability, "Erased entries") SHALL be left out
of the rows of the per-person exports (`rental-company`, `arms-authority` and `comparsa-list`).
Totals SHALL still count them: the `powder-supplier` totals and every total row SHALL equal those
of the order. A loan whose lender was erased SHALL show the weapon model and no lender in the Arms
Authority export. The export's audit entry SHALL count the rows actually written.

#### Scenario: Erased entry left out of the Arms Authority export
- **WHEN** a validated order of "Comparsa Sintética Norte" has two `ACTIVE` entries, one of them erased
- **THEN** the Arms Authority export has one row for Norte, and the powder supplier total for Norte still counts both entries' powder

#### Scenario: Erased lender
- **WHEN** a validated entry borrows a weapon from an external owner who was erased
- **THEN** the Arms Authority export shows the borrower with the weapon model and an empty lender
