# Spec Delta

## MODIFIED Requirements

### Requirement: Synthetic order data
The synthetic seed SHALL create, in the scenarios dataset:
- `VALIDATED` orders of the closed past edition, so the first-year flag is known in the current
  edition, and some seeded arquebusiers have no `ACTIVE` entry in it;
- orders of the current edition in `DRAFT` and `SUBMITTED`, and a seeded comparsa with no order;
- entries covering every weapon source and flask, both caps types, `RESERVE` entries and entries
  with compliance warnings;
- a loan between two seeded comparsas, and a loan from a fictional external owner with a valid DNI
  from the seeded range;
- an entry of the past edition whose arquebusier is no longer in the registry, kept with its
  fictional copy of the identity;
- `ACTIVE` entries without powder and without a weapon.

The full dataset SHALL also create, for the sixteen comparsas it adds:
- one `VALIDATED` order of the past edition per comparsa, holding most of its arquebusiers but not
  all, so that some of them are first-year in the current edition;
- orders of the current edition in a mix of statuses: four `DRAFT`, five `SUBMITTED`, two
  `RETURNED` with a return reason and three `VALIDATED`, and two comparsas without an order;
- in each order, an entry for most of the comparsa's arquebusiers, `ACTIVE` and `RESERVE`, with one
  or two left out and listed as not in the order.

Every entry SHALL be consistent with its arquebusier and SHALL meet every blocking rule of the
entries, weapon loans and submission:
- a `RESERVE` entry SHALL carry no powder, caps, weapon or flask (BR-05);
- an `ACTIVE` arquebusier who owns a trabuco or an arcabuz SHALL use it, and MAY also lend it to a
  team-mate (one weapon MAY be lent to several borrowers, BR-09);
- the others SHALL rent a model offered in the edition (BR-07) that matches their side, borrow a
  team-mate's weapon (BR-09), or carry no weapon;
- most `ACTIVE` entries SHALL carry 2 kg of powder, fewer 1 kg and a few none;
- some entries SHALL show compliance warnings, which stay warnings (BR-04).

It SHALL use fixed identifiers, SHALL be safe to run again, and SHALL NOT contain real names,
national IDs, weapon numbers or ownership guides.

#### Scenario: Seeding twice
- **WHEN** the seed runs twice
- **THEN** the orders, entries and loans exist once

#### Scenario: Seeded orders on the dashboard
- **WHEN** an Admin opens the orders of the seeded current edition
- **THEN** the dashboard shows orders in `DRAFT` and `SUBMITTED` and a comparsa not prepared

#### Scenario: Full dataset orders
- **WHEN** the full dataset has run and an Admin opens the orders of the current edition
- **THEN** the sixteen added comparsas show four `DRAFT`, five `SUBMITTED`, two `RETURNED` and three `VALIDATED` orders and two comparsas not prepared, and each added comparsa has a `VALIDATED` order in the past edition

#### Scenario: Consistent entries
- **WHEN** the full dataset has run
- **THEN** no entry of the added comparsas breaks a blocking rule, every `RESERVE` entry carries nothing, every rental model is offered in the edition and matches the arquebusier's side, and every owner of a trabuco or arcabuz uses it

#### Scenario: First-year arquebusiers
- **WHEN** the full dataset has run
- **THEN** some arquebusiers of the added comparsas have an `ACTIVE` entry in the current edition and none in the past edition, and they are flagged as first-year
