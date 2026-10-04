# Spec Delta

## ADDED Requirements

### Requirement: Erased entries in distribution
An entry erased by a GDPR request (comparsa orders capability, "Erased entries") SHALL NOT be a
pickup proxy's holder or proxy. Choosing one SHALL be blocking (`400 Bad Request` naming
`holderEntryId` or `proxyEntryId`, `entryErased`). The erasure itself removes the existing proxies
in which the entry takes part. Erased entries SHALL be left out of the distribution lists and SHALL
not take a number in the global numbering. The page SHALL not offer them as holders or proxies.

#### Scenario: Erased entry cannot be a proxy
- **WHEN** a FiringChief authorises an erased entry as the powder proxy of a holder
- **THEN** the request is rejected with `400 Bad Request` naming `proxyEntryId` with `entryErased`

#### Scenario: Erased holder left out of the list
- **WHEN** a validated order has an erased `ACTIVE` 2 kg entry and another `ACTIVE` 1 kg entry
- **THEN** the powder list has one row for that comparsa, numbered without a gap
