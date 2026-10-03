# Spec Delta

## MODIFIED Requirements

### Requirement: Deleting an arquebusier (UC-05, BR-14)
A FiringChief SHALL be able to delete an arquebusier in their scope, and an Admin any arquebusier,
when the person leaves the Federation. The UI SHALL ask for a confirmation that names the
arquebusier and says that the deletion cannot be undone. The confirmation SHALL also remind the
user that an arquebusier who only stops firing is set to `RESERVE`, which keeps their data, and
that registering them again later starts from empty data.

When the arquebusier has an entry in the edition in progress and its orders are open, the
confirmation SHALL first warn that the entry will be deleted from that edition's order, naming
the edition, the comparsa and the order status (maintainer decision). When the orders are closed,
it SHALL instead say that the entry stays in the order as history. When owned weapons of theirs
are lent in that edition, it SHALL say that those loans will show the weapon as removed in the
borrowers' orders. When they have entries in past editions, it SHALL say that those are kept as
the editions' history.

Deletion SHALL erase the arquebusier's registry data, owned weapons and photos. After a deletion:
- the arquebusier SHALL no longer be found in the registry (`404 Not Found`), nor SHALL their
  photos;
- their stored photo images SHALL be erased (see "Stored photo cleanup (BR-14, SEC-08)");
- their `nationalId`, `federationId` and ownership guide numbers MAY be registered again;
- while the orders of the edition in progress are open, their entry in that edition, with its
  loan, SHALL be removed in the same transaction;
- every other entry of theirs, and the loans of their owned weapons, SHALL be kept with the copy
  of identity and weapon data that the comparsa orders capability keeps as history. That includes
  the entry of the edition in progress once its orders are closed. They SHALL be anonymised only
  on a GDPR erasure request (UC-26), not because of the deletion;
- no audit entry SHALL contain their personal data.

Edition entries SHALL NOT block or delay the deletion.

#### Scenario: FiringChief deletes an arquebusier
- **WHEN** a FiringChief deletes an arquebusier of their comparsa who has two owned weapons, and confirms
- **THEN** the arquebusier and both owned weapons no longer exist, and a request for the arquebusier responds `404 Not Found`

#### Scenario: Photos are erased with the arquebusier
- **WHEN** an arquebusier with an `idPhoto`, a `frontPhoto` and a `backPhoto` is deleted
- **THEN** requests for those photos respond `404 Not Found` and the three stored images are erased

#### Scenario: Data can be registered again
- **WHEN** an arquebusier is deleted and a new arquebusier is then registered with the same nationalId and federationId
- **THEN** the new arquebusier is stored

#### Scenario: Confirmation warns about the current entry
- **WHEN** the orders of 2031 are open and a user opens the delete confirmation of an arquebusier who has an entry in the `SUBMITTED` 2031 order of "Comparsa Sintética Norte"
- **THEN** the dialog warns that their entry will be deleted from the 2031 order of "Comparsa Sintética Norte", which is submitted, before the user confirms

#### Scenario: Current entry deleted, history kept
- **WHEN** the orders are open and an arquebusier with a 2 kg `ACTIVE` entry in the 2030 edition and one in the edition in progress is deleted, after confirming
- **THEN** the deletion succeeds, the entry of the edition in progress no longer exists, and the 2030 entry remains with 2 kg and with the name and national ID from its copy, marked as no longer in the registry

#### Scenario: Orders closed keep the current entry
- **WHEN** the orders of the edition in progress are closed and a user deletes an arquebusier with an entry in it
- **THEN** the confirmation says that the entry stays in the order as history, and after the deletion the entry remains with its copy

#### Scenario: Confirmation mentions lent weapons
- **WHEN** a user opens the delete confirmation of an arquebusier whose owned weapon is lent to another arquebusier in the edition in progress
- **THEN** the dialog says that the loan will show the weapon as removed in the borrower's order

#### Scenario: Confirmation suggests Reserve
- **WHEN** a user opens the delete confirmation of an `ACTIVE` arquebusier
- **THEN** the dialog says that the deletion cannot be undone and that an arquebusier who only stops firing should be set to `RESERVE` instead

#### Scenario: Cancelled deletion
- **WHEN** a user opens the delete confirmation and cancels
- **THEN** nothing is deleted, and their entry in the edition in progress is unchanged
