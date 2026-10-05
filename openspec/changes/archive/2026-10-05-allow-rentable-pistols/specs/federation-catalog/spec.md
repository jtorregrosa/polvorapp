# Spec Delta

## REMOVED Requirements

### Requirement: Weapon models
**Reason**: BR-07 no longer forbids rentable pistols (maintainer decision); the scenario "Rentable pistol is blocking" no longer holds.
**Migration**: Replaced by "Weapon models (BR-07)", the same rules without the pistol restriction.

## ADDED Requirements

### Requirement: Weapon models (BR-07)
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
- for `TRABUCO` and `ARCABUZ`, side, handedness and size SHALL be required, and the combination of
  kind, side, handedness and size SHALL be unique;
- for `PISTOL`, side, handedness and size SHALL be optional.

The kind and the side MAY be combined freely. Any kind, `PISTOL` included, MAY be rentable: the
`rentable` flag SHALL be set and changed by an Admin for every model (BR-07, maintainer decision
superseding "pistols are never rentable").

#### Scenario: Admin creates a rentable model
- **WHEN** an Admin creates a model with kind `TRABUCO`, side `CHRISTIAN`, handedness `LEFT`, size `SMALL`, rentable, label "TRABUCO CRISTIANO ZURDO (PEQUEÑO)"
- **THEN** the model is stored as active and returned with its identifier

#### Scenario: Rentable pistol
- **WHEN** an Admin creates a model of kind `PISTOL`, labelled "PISTOLA", without side, handedness or size, with `rentable` set
- **THEN** the model is stored as rentable

#### Scenario: Pistol made rentable later
- **WHEN** an Admin edits an existing non-rentable `PISTOL` model and sets `rentable`
- **THEN** the model is rentable from then on, and the change is audited with its previous and new values

#### Scenario: Missing rentable flag is blocking
- **WHEN** an Admin creates a model of any kind without `rentable`
- **THEN** the request is rejected with `400 Bad Request` naming `rentable` as required

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
