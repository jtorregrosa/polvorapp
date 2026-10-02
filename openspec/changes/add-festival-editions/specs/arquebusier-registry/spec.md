# Spec Delta

## ADDED Requirements

### Requirement: Registry lock (BR-10, UC-11)
The registry SHALL have a lock that Admins turn on and off. It is independent of the festival
editions. Every signed-in user SHALL be able to read whether the registry is locked and since
when. Only Admins SHALL lock or unlock it. A FiringChief SHALL receive
`403 Forbidden`.

While the registry is locked, a FiringChief's writes SHALL be refused with `409 Conflict` and the
code `registry.locked`, and nothing SHALL be changed. This SHALL be blocking and SHALL be enforced on
the server. These writes are:
- registering an arquebusier;
- editing one, including the license, the course and the status;
- deleting one;
- adding, editing and removing owned weapons;
- uploading, replacing and removing photos.

Reading SHALL stay available to FiringChiefs. Admins SHALL keep every registry write while the
registry is locked, including transfers and the spreadsheet import.

The lock SHALL take effect atomically. A FiringChief write SHALL either commit before the lock is
committed, or be refused. No FiringChief write SHALL commit after the lock is on. Locking or
unlocking SHALL be recorded in the audit trail in the same transaction, with the acting Admin and
the new state. Locking a locked registry, or unlocking an unlocked one, SHALL succeed without
recording anything.

#### Scenario: Admin locks the registry
- **WHEN** an Admin locks the registry
- **THEN** the registry is locked and one audit entry records the Admin and the locked state

#### Scenario: FiringChief edit refused while locked
- **WHEN** a FiringChief saves a new phone for an arquebusier of their comparsa while the registry is locked
- **THEN** the request is rejected with `409 Conflict` and the code `registry.locked`, and the arquebusier is unchanged

#### Scenario: FiringChief photo upload refused while locked
- **WHEN** a FiringChief uploads an ID photo while the registry is locked
- **THEN** the request is rejected with `409 Conflict` and no image is stored

#### Scenario: FiringChief still reads while locked
- **WHEN** a FiringChief opens the arquebusier list and an arquebusier's photos while the registry is locked
- **THEN** both are shown as usual

#### Scenario: Admin edits while locked
- **WHEN** an Admin edits, transfers or imports arquebusiers while the registry is locked
- **THEN** the changes are applied and audited as usual

#### Scenario: Unlocking restores FiringChief writes
- **WHEN** an Admin unlocks the registry and a FiringChief then registers an arquebusier in their comparsa
- **THEN** the arquebusier is registered

#### Scenario: FiringChief cannot lock
- **WHEN** a FiringChief tries to lock or unlock the registry through the API
- **THEN** the API responds `403 Forbidden` and the lock is unchanged

### Requirement: Registry lock screens
The Arquebusiers page SHALL show Admins a "Lock registry" or "Unlock registry" action. Each SHALL be
confirmed in a dialog that says what changes for FiringChiefs. While the registry is locked, the
Arquebusiers page and every arquebusier detail page SHALL show a notice to every user:
- for FiringChiefs, the notice SHALL say that the Federation has locked the registry and that they
  can view but not change arquebusiers. The register, edit, status, delete, owned-weapon and photo
  actions SHALL NOT be offered;
- for Admins, the notice SHALL say that the registry is locked for FiringChiefs and that Admins can
  still edit.

When a FiringChief's change is refused because the registry was locked in the meantime, the UI SHALL
show the translated reason and SHALL update the page to the locked state. Every text SHALL be
available in es-ES, ca-ES-valencia and en. The notice and the dialogs SHALL pass automated
accessibility checks (NFR-07).

#### Scenario: Admin locks from the Arquebusiers page
- **WHEN** an Admin chooses "Lock registry" and confirms
- **THEN** the notice for Admins appears and the action becomes "Unlock registry"

#### Scenario: FiringChief sees a locked registry
- **WHEN** a FiringChief opens an arquebusier's detail page while the registry is locked
- **THEN** the locked notice is shown and no edit, status, delete, owned-weapon or photo action is offered

#### Scenario: Locked while editing
- **WHEN** a FiringChief saves an edit panel after an Admin has locked the registry
- **THEN** the panel shows that the registry is locked, and the page then shows the locked notice without edit actions
