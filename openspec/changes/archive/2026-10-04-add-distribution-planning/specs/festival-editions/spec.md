# Spec Delta

## MODIFIED Requirements

### Requirement: Edition management by Admins
Only Admins SHALL create editions, edit their festival dates, window dates and prices, change the
offered models, manage milestones, change the status, open and close the orders, and delete
editions. Admins SHALL be able to edit an edition in any status, including `CLOSED` (BR-10,
exceptional cases). An edit of the festival dates, window dates or prices SHALL carry the edition's
version. An edit based on an outdated version SHALL be rejected with `409 Conflict`
(`editions.modified`). The set of offered models and each milestone SHALL be saved as given; the
last save wins. Only a `DRAFT` edition SHALL be deleted, with its offered models and milestones.
Deleting an edition in any other status SHALL be rejected with `409 Conflict`
(`editions.notDraft`). A `DRAFT` edition that has comparsa orders or distribution days, because it
was moved back to preparation after they were created, SHALL NOT be deleted (`409 Conflict`,
`editions.inUse`). This rule SHALL be blocking and SHALL also hold when the deletion races with the
preparation of an order. A FiringChief SHALL receive `403 Forbidden` from every one of these
operations. Saving without changing any value SHALL succeed without recording anything.

#### Scenario: Admin edits an edition with closed orders
- **WHEN** an Admin changes `weaponRental` of the edition in progress after its orders are closed
- **THEN** the price is updated and the change is audited

#### Scenario: Outdated edit
- **WHEN** two Admins open the same edition, the first saves new prices and the second then saves new window dates
- **THEN** the second save is rejected with `409 Conflict`, and the UI explains that the edition changed and reloads it

#### Scenario: Admin deletes a draft
- **WHEN** an Admin deletes a `DRAFT` edition and confirms
- **THEN** the edition, its offered models and its milestones are removed, and the deletion is audited

#### Scenario: Deleting a started edition is blocking
- **WHEN** an Admin deletes a `CLOSED` edition
- **THEN** the request is rejected with `409 Conflict` and nothing is removed

#### Scenario: Draft with orders cannot be deleted
- **WHEN** an Admin moves the edition in progress back to `DRAFT` after a comparsa prepared its order, and then deletes it
- **THEN** the request is rejected with `409 Conflict` and `editions.inUse`, the UI explains that the edition has orders, and nothing is removed

#### Scenario: Draft with distribution days cannot be deleted
- **WHEN** an Admin moves the edition 2031, which has a planned powder day and no order, back to `DRAFT` and then deletes it
- **THEN** the request is rejected with `409 Conflict` and `editions.inUse`, the UI explains that the edition has orders or distribution days, and nothing is removed

#### Scenario: FiringChief cannot manage editions
- **WHEN** a FiringChief tries to create, edit, delete an edition, change its models or manage its milestones through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed
