# Spec Delta

## Purpose

Tells each comparsa, and the Federation, how much the comparsa owes the Federation for a festival
edition: its order's powder, caps and rentals at the edition's prices (UC-28). Payments themselves
stay outside PolvorApp.

## ADDED Requirements

### Requirement: Billing summary of an order (UC-28)
Every prepared `ComparsaOrder` SHALL have a `BillingSummary`, computed from the order's entries
and the four `EditionPrices` of its edition. It SHALL have four lines, in this order, each with a
quantity, a unit price and an amount (quantity × unit price):
- **powder**: the `powderKg` of all entries, at `powderPerKg`;
- **caps**: the `capsBoxes` of all entries, `NORMAL` and `SMALL` together, at `capsBox`;
- **weapon rentals**: the entries with weapon source `RENTAL`, whatever the model, at
  `weaponRental`;
- **flask rentals**: the entries with flask `RENTAL_1KG` or `RENTAL_2KG`, at `flaskRental`.

The summary SHALL also have the total, the sum of the four amounts. Owned weapons, loans, owned
flasks and `RESERVE` entries SHALL add nothing (BR-05). Every entry of the order SHALL count,
including the entries of arquebusiers no longer in the registry, exactly as in the order totals;
an entry removed by an arquebusier's deletion SHALL no longer count. Amounts SHALL be exact euros
with two decimals, never rounded through floating point. An order with no charged item SHALL have
every quantity, amount and the total at zero. A comparsa that has not prepared its order SHALL
have no summary.

#### Scenario: Summary of an order
- **WHEN** an edition has `powderPerKg` 55.00, `capsBox` 4.50, `weaponRental` 30.00 and `flaskRental` 6.00, and an order has one `RESERVE` entry and four `ACTIVE` entries: 2 kg, two `NORMAL` caps boxes, a rented weapon and a rented 2 kg flask; 1 kg, one `SMALL` caps box, an owned weapon and a rented 1 kg flask; 2 kg, a loaned weapon and an owned flask; and no powder with a rented weapon
- **THEN** its summary has powder 5 kg × 55.00 = 275.00, caps 3 × 4.50 = 13.50, weapon rentals 2 × 30.00 = 60.00, flask rentals 2 × 6.00 = 12.00, and a total of 360.50

#### Scenario: Amounts are exact
- **WHEN** the edition's `capsBox` is 0.10 and an order has three caps boxes and nothing else
- **THEN** the caps amount and the total are exactly 0.30

#### Scenario: Nothing charged
- **WHEN** every entry of an order is `RESERVE`, or has no powder, no caps, an owned or loaned weapon and an owned flask
- **THEN** its summary has every quantity and amount at zero and a total of 0.00

#### Scenario: Entry of an arquebusier no longer in the registry
- **WHEN** an arquebusier with a 2 kg entry in a closed past edition is deleted from the registry
- **THEN** that order's summary still counts the 2 kg

#### Scenario: Entry removed with the arquebusier
- **WHEN** the orders are open and an arquebusier with a 2 kg entry in the edition in progress is deleted
- **THEN** the order's powder line no longer counts those 2 kg

#### Scenario: Comparsa without an order
- **WHEN** a comparsa has not prepared its order for the edition
- **THEN** it has no billing summary for that edition

### Requirement: Billing is always derived
The billing summary SHALL never be stored. It SHALL always reflect the order's current entries
and the edition's current prices (maintainer decision), whatever the order's and the edition's
status, `CLOSED` included. An entry edit, an entry added, or an Admin's price change SHALL change
the summary the next time it is read.

#### Scenario: Price change after validation
- **WHEN** an Admin changes `powderPerKg` from 55.00 to 60.00 in an edition whose orders are all `VALIDATED`
- **THEN** every order's powder line and total use 60.00 from then on

#### Scenario: Admin edits a validated order
- **WHEN** an Admin changes an entry of a `VALIDATED` order from 1 kg to 2 kg
- **THEN** the order's summary counts one more kilogram

#### Scenario: Entry edited in a draft
- **WHEN** a FiringChief adds a rented 1 kg flask to an entry of their `DRAFT` order
- **THEN** the order's flask rentals line counts one more flask

### Requirement: Provisional or final
A billing summary SHALL be **provisional** while its order is not `VALIDATED` (`DRAFT`,
`SUBMITTED` or `RETURNED`) and **final** while it is `VALIDATED` (maintainer decision). A final
summary SHALL still follow Admin edits and price changes (see "Billing is always derived").

#### Scenario: Submitted order
- **WHEN** a FiringChief opens their `SUBMITTED` order
- **THEN** its billing summary is marked provisional

#### Scenario: Validated order
- **WHEN** an Admin validates an order
- **THEN** its billing summary is marked final

#### Scenario: Returned after validation
- **WHEN** an Admin returns a `VALIDATED` order
- **THEN** its billing summary is marked provisional again

### Requirement: Missing prices
Prices are optional while an edition is `DRAFT`, and an edition with orders may be moved back to
`DRAFT`. A line whose price is not set SHALL have its quantity and no unit price or amount, the
summary SHALL have no total, and it SHALL name the missing prices. This SHALL NOT be an error and
SHALL NOT block any order operation.

#### Scenario: Edition moved back to draft without a price
- **WHEN** an Admin opens an order of a `DRAFT` edition whose `flaskRental` is not set
- **THEN** the summary shows the flask rentals quantity without an amount, shows no total, and says that the flask rental price is missing

### Requirement: Edition billing (Admins)
For an Admin, the orders overview of an edition SHALL include the edition billing: the four lines
and the total summed over the billing summaries of every prepared order of the edition, whatever
its status. It SHALL be final when the edition has at least one prepared order and every prepared
order is `VALIDATED`, and provisional otherwise; with no prepared order, every amount SHALL be zero.
Comparsas without an order SHALL add nothing. When a price is missing,
the edition billing SHALL follow "Missing prices". FiringChiefs SHALL NOT receive the edition
billing.

#### Scenario: Edition billing
- **WHEN** an Admin opens the orders overview of an edition with a `VALIDATED` order owing 360.50, a `SUBMITTED` order owing 120.00 and a comparsa not prepared
- **THEN** the edition billing has a total of 480.50 and is marked provisional

#### Scenario: Every order validated
- **WHEN** every prepared order of the edition is `VALIDATED`
- **THEN** the edition billing is marked final

#### Scenario: No order prepared yet
- **WHEN** an Admin opens the orders overview of an edition in which no comparsa has prepared its order
- **THEN** the edition billing has every amount and the total at 0.00 and is marked provisional

#### Scenario: FiringChief gets no edition billing
- **WHEN** a FiringChief opens the orders overview
- **THEN** the response and the page contain no edition billing

### Requirement: Billing visibility (BR-12)
A billing summary SHALL be visible exactly where its order is: Admins SHALL see every order's
summary; a FiringChief SHALL see only the summaries of the orders of the comparsas in their scope,
and none of a `DRAFT` edition. A request for an order outside the scope SHALL be answered as for
the order itself (`404 Not Found`), revealing no amount. A FiringChief whose arquebusier lends a
weapon to another comparsa SHALL NOT see that comparsa's billing. Reading a billing summary SHALL
NOT change any data.

#### Scenario: Another comparsa's order
- **WHEN** a FiringChief requests the order of a comparsa not assigned to them
- **THEN** the API responds `404 Not Found` and no billing summary is returned

#### Scenario: FiringChief overview amounts
- **WHEN** a FiringChief assigned to two comparsas opens the orders overview
- **THEN** only the amounts of those two comparsas' orders are shown

#### Scenario: Lender's comparsa
- **WHEN** a FiringChief of "Comparsa Sintética Sur" opens their order while one of their arquebusiers lends a weapon to an arquebusier of "Comparsa Sintética Norte"
- **THEN** the page shows the billing summary of "Comparsa Sintética Sur" only

### Requirement: Billing is not audited
Billing SHALL record no audit entry: it writes nothing and is not an export. The changes that move
an amount (entry edits, status changes, price changes) SHALL stay audited by their own
capabilities.

#### Scenario: Opening an order
- **WHEN** a FiringChief opens their order and its billing summary
- **THEN** no audit entry is recorded

### Requirement: Billing screens
The **order page** SHALL show a "Billing summary" section with the four lines (concept, quantity,
unit price and amount), the total, and whether the summary is provisional or final. A provisional
summary SHALL say that the amount can change until the Federation validates the order. A summary
with missing prices SHALL say which prices are missing instead of the total.

The **orders overview** SHALL show, in each row of a prepared order, the order's total and whether
it is provisional or final; a comparsa not prepared SHALL show no amount. For Admins, the edition
totals SHALL be followed by the edition billing with its four lines, total and state.

Amounts SHALL be shown as currency in the user's language. The tables of amounts SHALL be real
tables with a caption and a total row, readable by screen readers. The billing SHALL offer no
download. The pages SHALL work on a 360 px wide phone without horizontal scrolling of the page
(NFR-01), every text SHALL be available in es-ES, ca-ES-valencia and en, and the pages SHALL pass
automated accessibility checks (NFR-07).

#### Scenario: Order page in Spanish
- **WHEN** a FiringChief opens, in es-ES, the order of the "Summary of an order" scenario while it is `DRAFT`
- **THEN** the billing summary shows powder "275,00 €", caps "13,50 €", weapon rentals "60,00 €", flask rentals "12,00 €", a total of "360,50 €", and says that it is provisional

#### Scenario: Overview in English
- **WHEN** an Admin opens, in en, the orders overview with a `VALIDATED` order owing 360.50
- **THEN** that row shows "€360.50" marked final

#### Scenario: Missing price on screen
- **WHEN** an Admin opens an order of a `DRAFT` edition whose `capsBox` is not set
- **THEN** the billing summary shows the caps quantity without an amount and says, instead of the total, that the caps box price is missing

#### Scenario: Billing on a phone
- **WHEN** the seeded FiringChief opens their order on a 360 px wide screen
- **THEN** the billing summary is readable without horizontal scrolling of the page

#### Scenario: Not prepared row
- **WHEN** an Admin opens the orders overview with a comparsa not prepared
- **THEN** that row shows no amount
