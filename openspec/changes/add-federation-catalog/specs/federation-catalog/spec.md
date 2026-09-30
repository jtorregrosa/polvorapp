# Spec Delta

## Purpose

Holds the Federation's reference data that every other capability builds on: the comparsas with
their side, the FiringChiefs assigned to each comparsa (which determines their comparsa scope,
BR-12) and the shared weapon model catalogue (UC-24, BR-07).

## ADDED Requirements

### Requirement: Comparsas
A `Comparsa` SHALL have a `name` and a `side`. The name SHALL be 1 to 100 characters after trimming
and SHALL be unique across the Federation, compared case-insensitively. The side SHALL be `MOORISH`
or `CHRISTIAN`. Each comparsa SHALL also have an `active` flag. These rules SHALL be blocking.

#### Scenario: Admin creates a comparsa
- **WHEN** an Admin creates a comparsa named "Comparsa Sintética Norte" with side `CHRISTIAN`
- **THEN** the comparsa is stored as active and returned with its identifier

#### Scenario: Duplicate name is blocking
- **WHEN** an Admin creates or renames a comparsa to "comparsa sintética norte" while "Comparsa Sintética Norte" exists
- **THEN** the request is rejected with `409 Conflict` and nothing is changed

#### Scenario: Invalid side or empty name is blocking
- **WHEN** an Admin submits a comparsa with side `NEUTRAL`, or a name that is empty or only spaces
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

### Requirement: Comparsa management by Admins
Admins SHALL be able to create comparsas, edit a comparsa's name and side, deactivate and
reactivate it, and delete it (see "Deleting comparsas and weapon models"). Only Admins SHALL have
these operations. A FiringChief SHALL receive `403 Forbidden` from the API and SHALL NOT see these
actions in the UI. The UI SHALL ask for confirmation before a deactivation. Deactivating a comparsa
SHALL keep its FiringChief assignments. Deactivation is for a comparsa that no longer takes part
but whose history must stay visible. Deletion is for a comparsa entered by mistake or never used.

#### Scenario: Admin edits a comparsa
- **WHEN** an Admin changes the side of a comparsa from `CHRISTIAN` to `MOORISH`
- **THEN** the comparsa is updated and the change is audited with its previous and new values

#### Scenario: Admin deactivates a comparsa
- **WHEN** an Admin deactivates a comparsa and confirms
- **THEN** the comparsa becomes inactive, it keeps its assignments, and it is still listed when inactive comparsas are included

#### Scenario: Admin reactivates a comparsa
- **WHEN** an Admin reactivates an inactive comparsa
- **THEN** the comparsa becomes active again

#### Scenario: FiringChief cannot manage comparsas
- **WHEN** a signed-in FiringChief tries to create, edit, deactivate, reactivate or delete a comparsa through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Comparsa visibility (BR-12)
The list of comparsas SHALL be sorted by name. It SHALL be filterable by side and by active state,
and by default it SHALL show only active comparsas. An Admin SHALL see every comparsa. A FiringChief
SHALL see only the comparsas assigned to them, active or not, and read-only. A comparsa outside the
user's scope SHALL be answered as if it did not exist (`404 Not Found`). This rule is blocking and
SHALL be enforced on the server.

#### Scenario: Admin lists comparsas
- **WHEN** an Admin opens the comparsas page
- **THEN** every active comparsa is listed with its name, side and state
- **AND** the Admin can include the inactive ones and filter by side

#### Scenario: FiringChief lists comparsas
- **WHEN** a FiringChief assigned to one comparsa opens the comparsas page
- **THEN** only that comparsa is listed and no create, edit or assignment action is offered

#### Scenario: FiringChief opens another comparsa
- **WHEN** a FiringChief requests a comparsa they are not assigned to
- **THEN** the API responds `404 Not Found` and the UI shows the not-found page

#### Scenario: FiringChief without assignments
- **WHEN** a FiringChief with no assignments opens the comparsas page
- **THEN** an empty state explains that no comparsa is assigned to them yet and that an Admin must assign one

### Requirement: FiringChief assignments
An Admin SHALL be able to assign a user to a comparsa as its FiringChief and remove that
assignment. A comparsa MAY have several FiringChiefs, and a user MAY be assigned to several
comparsas. The following rules SHALL be blocking:
- only a user whose role is `FIRING_CHIEF` SHALL be assignable (`409 Conflict` otherwise);
- a `DEACTIVATED` user or an inactive comparsa SHALL NOT receive new assignments (`409 Conflict`);
- a user or comparsa that does not exist SHALL be answered with `404 Not Found`.

Assigning an existing assignment SHALL succeed without creating a duplicate. Removing an
assignment that does not exist SHALL succeed without changing anything. Existing assignments SHALL
be kept when the user is deactivated, when the comparsa is deactivated or when the user's role
changes to `ADMIN`. They SHALL be removed when the comparsa is deleted. While the user is an Admin they have no effect, because an Admin's scope is
every comparsa. Only Admins SHALL list, create or remove assignments. FiringChiefs SHALL receive
`403 Forbidden`.

#### Scenario: Admin assigns a FiringChief
- **WHEN** an Admin assigns the FiringChief "Jefe Sintético Uno" to the active comparsa "Comparsa Sintética Norte"
- **THEN** the assignment is stored and audited with the comparsa and the user

#### Scenario: Invited FiringChief can be assigned
- **WHEN** an Admin assigns a `FIRING_CHIEF` user whose status is `INVITED`
- **THEN** the assignment is stored, so the user has their scope from their first sign-in

#### Scenario: Assigning an Admin is blocking
- **WHEN** an Admin tries to assign a user whose role is `ADMIN` to a comparsa
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Assigning a deactivated user or to an inactive comparsa is blocking
- **WHEN** an Admin tries to assign a `DEACTIVATED` FiringChief, or any FiringChief to an inactive comparsa
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Repeated assignment
- **WHEN** an Admin assigns a FiringChief to a comparsa they are already assigned to
- **THEN** the request succeeds, only one assignment exists and no additional audit entry is recorded

#### Scenario: Admin removes an assignment
- **WHEN** an Admin removes a FiringChief from a comparsa and confirms
- **THEN** the assignment is deleted and the removal is audited with the comparsa and the user

#### Scenario: Several FiringChiefs and several comparsas
- **WHEN** an Admin assigns two FiringChiefs to one comparsa and one of them also to a second comparsa
- **THEN** the first comparsa lists both FiringChiefs and that FiringChief's detail lists both comparsas

#### Scenario: FiringChief cannot manage assignments
- **WHEN** a signed-in FiringChief calls an assignment endpoint, including for their own comparsa
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Assignments determine the comparsa scope
The comparsas assigned to a FiringChief SHALL be exactly the comparsas their comparsa scope grants
(BR-12). An assignment or removal SHALL take effect on the FiringChief's next request, without
signing in again.

#### Scenario: Scope granted by an assignment
- **WHEN** an Admin assigns a signed-in FiringChief to a comparsa
- **THEN** that FiringChief's next request for that comparsa is answered and the comparsa appears in their list

#### Scenario: Scope removed with the assignment
- **WHEN** an Admin removes the only assignment of a signed-in FiringChief
- **THEN** that FiringChief's next request for that comparsa responds `404 Not Found` and their list is empty

#### Scenario: Scope of a user promoted to Admin
- **WHEN** a FiringChief assigned to one comparsa is changed to the `ADMIN` role
- **THEN** their next request is scoped to every comparsa and the kept assignment has no effect

### Requirement: Managing assignments from the comparsa and from the user
The comparsa detail page SHALL show Admins the comparsa's FiringChiefs, each with their name, email
and user status. From that page an Admin SHALL be able to add a FiringChief by choosing among
`FIRING_CHIEF` users who are not deactivated and not yet assigned, and to remove a FiringChief.
The user detail page of a `FIRING_CHIEF` user SHALL show Admins that user's comparsas and SHALL
let them add and remove comparsas in the same way. For an `ADMIN` user who still has assignments,
the page SHALL say that they have no effect. Removals SHALL be confirmed. A rejected change SHALL
show its translated reason.

#### Scenario: Assign from the comparsa page
- **WHEN** an Admin adds "Jefa Sintética Dos" from the FiringChiefs section of a comparsa
- **THEN** that user is listed in that section and the comparsa appears on their user detail page

#### Scenario: Assign from the user page
- **WHEN** an Admin adds a comparsa from the Comparsas section of a FiringChief's user detail page
- **THEN** the comparsa is listed in that section and the FiringChief appears on the comparsa detail page

#### Scenario: Candidates exclude ineligible users and comparsas
- **WHEN** an Admin opens the add-FiringChief choice on a comparsa page, or the add-comparsa choice on a user page
- **THEN** deactivated users, users with the `ADMIN` role, inactive comparsas and existing assignments are not offered

#### Scenario: Rejected assignment shows its reason
- **WHEN** an assignment is rejected because the user was deactivated after the page loaded
- **THEN** the page shows the translated reason and the list reflects the current assignments

### Requirement: Weapon models
A `WeaponModel` SHALL have:
- a `kind`: `TRABUCO`, `ARCABUZ` or `PISTOL`;
- a `side`: `MOORISH` or `CHRISTIAN`;
- a `handedness`: `RIGHT` or `LEFT`;
- a `size`: `NORMAL` or `SMALL`;
- a `rentable` flag;
- a `label` in the Federation's naming (for example "TRABUCO CRISTIANO DIESTRO (PEQUEÑO)");
- an `active` flag.

The following rules SHALL be blocking:
- the label SHALL be 1 to 100 characters after trimming and unique, compared case-insensitively;
- a `PISTOL` SHALL NOT be rentable (BR-07);
- for `TRABUCO` and `ARCABUZ`, side, handedness and size SHALL be required, and the combination of
  kind, side, handedness and size SHALL be unique;
- for `PISTOL`, side, handedness and size SHALL be optional.

The kind and the side MAY be combined freely.

#### Scenario: Admin creates a rentable model
- **WHEN** an Admin creates a model with kind `TRABUCO`, side `CHRISTIAN`, handedness `LEFT`, size `SMALL`, rentable, label "TRABUCO CRISTIANO ZURDO (PEQUEÑO)"
- **THEN** the model is stored as active and returned with its identifier

#### Scenario: Rentable pistol is blocking
- **WHEN** an Admin creates or edits a model of kind `PISTOL` with `rentable` set
- **THEN** the request is rejected with `400 Bad Request` naming `rentable`

#### Scenario: Pistol without attributes
- **WHEN** an Admin creates a model of kind `PISTOL`, not rentable, labelled "PISTOLA", without side, handedness or size
- **THEN** the model is stored

#### Scenario: Missing attributes are blocking
- **WHEN** an Admin creates a model of kind `ARCABUZ` without a handedness
- **THEN** the request is rejected with `400 Bad Request` naming `handedness`

#### Scenario: Duplicate combination or label is blocking
- **WHEN** an Admin creates an `ARCABUZ` `MOORISH` `RIGHT` `NORMAL` model while one exists, or reuses an existing label in a different letter case
- **THEN** the request is rejected with `409 Conflict` and a translated explanation

#### Scenario: Free kind and side combination
- **WHEN** an Admin creates a model of kind `ARCABUZ` with side `CHRISTIAN`, labelled "ARCABUZ CRISTIANO DIESTRO"
- **THEN** the model is stored

#### Scenario: Invalid enum value is blocking
- **WHEN** an Admin submits a model with kind `CANNON` or size `LARGE`
- **THEN** the request is rejected with `400 Bad Request` naming the invalid field

### Requirement: Weapon catalogue access
Every signed-in user SHALL be able to list weapon models. The list SHALL be sorted by label,
filterable by kind and active state, and by default it SHALL show only active models. Only Admins
SHALL create, edit, deactivate, reactivate and delete models. FiringChiefs SHALL receive `403 Forbidden`
from these operations and SHALL NOT see the weapon catalogue pages in the UI navigation. The UI
SHALL show kind, side, handedness and size as translated labels, and the Federation `label` as
entered.

#### Scenario: FiringChief reads the catalogue through the API
- **WHEN** a signed-in FiringChief requests the weapon models
- **THEN** the active models are returned

#### Scenario: FiringChief cannot change the catalogue
- **WHEN** a signed-in FiringChief tries to create, edit, deactivate, reactivate or delete a model
- **THEN** the API responds `403 Forbidden` and nothing is changed

#### Scenario: Admin deactivates a model
- **WHEN** an Admin deactivates a model and confirms
- **THEN** the model is inactive, hidden from the default list and shown when inactive models are included

#### Scenario: Admin edits a model
- **WHEN** an Admin changes the label and size of a model
- **THEN** the model is updated and the change is audited with its previous and new values

### Requirement: Deleting comparsas and weapon models
An Admin SHALL be able to delete a comparsa or a weapon model after confirming in the UI. Deleting
a comparsa SHALL also remove its FiringChief assignments in the same operation. The confirmation
SHALL state how many FiringChiefs lose access to the comparsa. A comparsa or weapon model that
other records reference SHALL NOT be deleted (blocking, `409 Conflict`). Such references are, for
example, arquebusiers, owned weapons, edition availability or orders, which later changes add. In
that case the UI SHALL show the translated reason and suggest deactivating instead. After a
deletion:
- the record SHALL no longer be found (`404 Not Found`);
- its name or label MAY be reused;
- FiringChiefs who were assigned to a deleted comparsa SHALL lose its scope on their next request.

Only Admins SHALL delete. FiringChiefs SHALL receive `403 Forbidden`.

#### Scenario: Admin deletes an unused comparsa
- **WHEN** an Admin deletes a comparsa that no other record references and confirms
- **THEN** the comparsa no longer exists, a request for it responds `404 Not Found` and its name can be used for a new comparsa

#### Scenario: Deleting a comparsa removes its assignments
- **WHEN** an Admin deletes a comparsa with two assigned FiringChiefs, after a confirmation stating that two FiringChiefs lose access
- **THEN** both assignments are removed and each of those FiringChiefs' next request no longer includes that comparsa

#### Scenario: Referenced comparsa cannot be deleted
- **WHEN** an Admin deletes a comparsa that another record references
- **THEN** the request is rejected with `409 Conflict`, nothing is deleted and the UI suggests deactivating the comparsa

#### Scenario: Admin deletes an unused weapon model
- **WHEN** an Admin deletes a weapon model that no other record references and confirms
- **THEN** the model no longer exists and its label can be used for a new model

#### Scenario: Referenced weapon model cannot be deleted
- **WHEN** an Admin deletes a weapon model that another record references
- **THEN** the request is rejected with `409 Conflict`, nothing is deleted and the UI suggests deactivating the model

#### Scenario: Deleting something that does not exist
- **WHEN** an Admin deletes a comparsa or weapon model id that does not exist
- **THEN** the API responds `404 Not Found`

### Requirement: Catalogue changes are audited
Every creation, edit, deactivation, reactivation and deletion of a comparsa or weapon model, and
every assignment and removal of a FiringChief, SHALL be recorded in the audit trail in the same
transaction as the change. Each entry SHALL record the acting Admin, the action, the target and,
for comparsa changes and assignments, the comparsa concerned. An operation that changes nothing
(a repeated assignment, an edit with identical values, deactivating an inactive record) SHALL NOT
record an entry.

#### Scenario: Assignment audited with its comparsa
- **WHEN** an Admin assigns a FiringChief to a comparsa
- **THEN** an audit entry with action `FiringChiefAssigned`, the Admin as actor, the comparsa id and the assigned user id is recorded

#### Scenario: Deletion audited with a snapshot
- **WHEN** an Admin deletes a comparsa with assigned FiringChiefs
- **THEN** one audit entry with action `ComparsaDeleted` records the comparsa id, its name, side and active state, and the ids of the users whose assignments were removed

#### Scenario: Unchanged edit is not audited
- **WHEN** an Admin saves a weapon model without changing any value
- **THEN** no audit entry is recorded

### Requirement: Synthetic catalogue data
The synthetic seed SHALL create fictional comparsas of both sides, including one inactive. It
SHALL assign the seeded FiringChiefs so that one comparsa has two FiringChiefs and one FiringChief
has two comparsas. It SHALL also create a weapon catalogue covering every kind, including a
non-rentable pistol and an inactive model. It SHALL use fixed identifiers and SHALL be safe to run
again. The seed SHALL NOT contain real comparsa names or real Federation data. Production data
SHALL be entered by an Admin.

#### Scenario: Seeding twice
- **WHEN** the seed command runs twice on the same database
- **THEN** the same comparsas, assignments and models exist once each

#### Scenario: Seeded FiringChief scope
- **WHEN** the seeded FiringChief "Jefe Sintético Uno" signs in
- **THEN** only the seeded comparsas assigned to them are listed
