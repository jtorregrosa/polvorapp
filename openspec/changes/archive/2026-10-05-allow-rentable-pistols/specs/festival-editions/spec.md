# Spec Delta

## REMOVED Requirements

### Requirement: Rental models offered in an edition (BR-07)
**Reason**: A rentable pistol may now be offered; the scenario "Pistol cannot be offered" no longer holds.
**Migration**: Replaced by "Rental models offered in an edition", the same rules without the pistol restriction.

## ADDED Requirements

### Requirement: Rental models offered in an edition
An Admin SHALL choose which catalogue `WeaponModel`s are offered for rental in an edition
(`EditionWeaponModel`). The choice SHALL be saved as a whole set. The following rules SHALL be
blocking (`400 Bad Request` naming `weaponModelIds` with the reason):
- a model added to the set SHALL exist (`notFound`);
- a model added to the set SHALL be active and rentable (`notRentable`), whatever its kind.

A model that was in the set before it was deactivated or made non-rentable in the catalogue SHALL
stay in the set until an Admin removes it. The edition SHALL mark it as no longer rentable. A model
SHALL count as **offered for rental** in an edition only while it is in the set, active and
rentable. Comparsa orders (#10) SHALL offer only those models.

#### Scenario: Admin offers models
- **WHEN** an Admin saves a set with two active, rentable arcabuz models
- **THEN** both are offered for rental in the edition

#### Scenario: Rentable pistol can be offered
- **WHEN** an Admin adds an active, rentable "PISTOLA" model to an edition's set
- **THEN** the pistol is offered for rental in the edition

#### Scenario: Non-rentable model cannot be offered
- **WHEN** an Admin adds an active model that is not rentable, such as a non-rentable "PISTOLA", to an edition's set
- **THEN** the request is rejected with `400 Bad Request` naming `weaponModelIds` with `notRentable`, and the set is unchanged

#### Scenario: Model deactivated after being offered
- **WHEN** a model offered in the current edition is deactivated in the catalogue
- **THEN** the edition still lists it, marked as no longer rentable, and it is not offered for rental until it is active and rentable again

#### Scenario: Offered model cannot be deleted from the catalogue
- **WHEN** an Admin deletes a weapon model that is in the set of any edition
- **THEN** the deletion is rejected with `409 Conflict`, as for any weapon model in use
