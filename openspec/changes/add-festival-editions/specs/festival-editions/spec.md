# Spec Delta

## Purpose

Lets the Federation set up each yearly festival edition: its festival dates, order window, flat
prices, rental models and calendar milestones (UC-10). Admins take the edition from preparation to
in progress to closed, and open and close its orders (UC-11, BR-10). The edition in progress is the
current edition that comparsa orders build on.

## ADDED Requirements

### Requirement: Festival editions (UC-10)
A `FestivalEdition` SHALL have:
- a `year`;
- the festival dates `festivalStartsOn` and `festivalEndsOn`;
- a `status`;
- an `ordersOpen` flag;
- two optional order window dates, `ordersOpenOn` and `ordersCloseOn`.

The window dates are the published plan of the order window. They SHALL NOT open or close the
orders by themselves.

The following rules SHALL be blocking:
- `year` SHALL be a whole number from 2000 to 2100 and unique across editions (`409 Conflict`
  when another edition has it). It SHALL NOT change after creation.
- Both festival dates SHALL be required and SHALL fall within the edition's `year`.
  `festivalEndsOn` SHALL NOT be before `festivalStartsOn`.
- Each window date that is given SHALL NOT be after `festivalStartsOn`. When both are given,
  `ordersCloseOn` SHALL NOT be before `ordersOpenOn`.

A violated date rule SHALL be answered with `400 Bad Request`, naming the field and the reason.

#### Scenario: Admin creates an edition
- **WHEN** an Admin creates the edition 2031 with the festival from 2031-04-22 to 2031-04-25
- **THEN** the edition is stored with status `DRAFT`, orders closed and no window dates, and returned with its identifier

#### Scenario: Duplicate year is blocking
- **WHEN** an Admin creates an edition for 2031 while an edition 2031 exists
- **THEN** the request is rejected with `409 Conflict` and nothing is stored

#### Scenario: Festival dates outside the year are blocking
- **WHEN** an Admin creates the edition 2031 with `festivalStartsOn` 2030-12-30
- **THEN** the request is rejected with `400 Bad Request` naming `festivalStartsOn`

#### Scenario: Window out of order is blocking
- **WHEN** an Admin saves `ordersOpenOn` 2031-02-10 and `ordersCloseOn` 2031-02-01
- **THEN** the request is rejected with `400 Bad Request` naming `ordersCloseOn`, and nothing is changed

#### Scenario: Window after the festival is blocking
- **WHEN** an Admin saves `ordersCloseOn` 2031-04-23 for a festival starting on 2031-04-22
- **THEN** the request is rejected with `400 Bad Request` naming `ordersCloseOn`

### Requirement: Edition prices
Each edition SHALL have four flat prices in euros (`EditionPrices`): `powderPerKg`, `capsBox`,
`weaponRental` and `flaskRental`. Each price SHALL be optional while the edition is `DRAFT`. A
given price SHALL be from 0.00 to 9999.99 with at most two decimals (blocking, `400 Bad Request`
naming the price). Prices SHALL be exact decimal amounts, never rounded through floating point. The
UI SHALL show them as currency in the user's language. Once the edition has left `DRAFT`, every
price and both order window dates SHALL stay set: clearing one SHALL be rejected with
`400 Bad Request` naming it as `required` (blocking), because starting the edition needed them.

#### Scenario: Admin sets the prices
- **WHEN** an Admin sets `powderPerKg` 55.00, `capsBox` 4.50, `weaponRental` 30.00 and `flaskRental` 6.00
- **THEN** the prices are stored exactly, and es-ES shows the powder price as "55,00 €"

#### Scenario: Invalid price is blocking
- **WHEN** an Admin sets `capsBox` to -1, to 4.555 or to 10000
- **THEN** the request is rejected with `400 Bad Request` naming `capsBox`

#### Scenario: Clearing a price outside a draft is blocking
- **WHEN** an Admin saves the edition in progress without `flaskRental`
- **THEN** the request is rejected with `400 Bad Request` naming `flaskRental` as required, and the price is kept

### Requirement: Rental models offered in an edition (BR-07)
An Admin SHALL choose which catalogue `WeaponModel`s are offered for rental in an edition
(`EditionWeaponModel`). The choice SHALL be saved as a whole set. The following rules SHALL be
blocking (`400 Bad Request` naming `weaponModelIds` with the reason):
- a model added to the set SHALL exist (`notFound`);
- a model added to the set SHALL be active and rentable (`notRentable`). A `PISTOL` is never
  rentable.

A model that was in the set before it was deactivated or made non-rentable in the catalogue SHALL
stay in the set until an Admin removes it. The edition SHALL mark it as no longer rentable. A model
SHALL count as **offered for rental** in an edition only while it is in the set, active and
rentable. Comparsa orders (#10) SHALL offer only those models.

#### Scenario: Admin offers models
- **WHEN** an Admin saves a set with two active, rentable arcabuz models
- **THEN** both are offered for rental in the edition

#### Scenario: Pistol cannot be offered
- **WHEN** an Admin adds the "PISTOLA" model to an edition's set
- **THEN** the request is rejected with `400 Bad Request` naming `weaponModelIds` with `notRentable`, and the set is unchanged

#### Scenario: Model deactivated after being offered
- **WHEN** a model offered in the current edition is deactivated in the catalogue
- **THEN** the edition still lists it, marked as no longer rentable, and it is not offered for rental until it is active and rentable again

#### Scenario: Offered model cannot be deleted from the catalogue
- **WHEN** an Admin deletes a weapon model that is in the set of any edition
- **THEN** the deletion is rejected with `409 Conflict`, as for any weapon model in use

### Requirement: New editions start from the previous one
When an Admin creates an edition, it SHALL start with the prices of the latest edition whose `year`
is earlier. It SHALL also start with the models of that edition's set that are still active and
rentable. The window dates and the milestones SHALL start empty. With no earlier edition, prices
and models SHALL start empty. The copied values SHALL be editable like any other.

#### Scenario: Copy from the previous edition
- **WHEN** an Admin creates the edition 2032 after the edition 2031, which has prices and three offered models, one of them since deactivated
- **THEN** the edition 2032 has the 2031 prices and the two models that are still rentable, and no window dates or milestones

#### Scenario: First edition starts empty
- **WHEN** an Admin creates the first edition
- **THEN** it has no prices, no offered models and no milestones

### Requirement: Calendar milestones
An edition SHALL have any number of `CalendarMilestone`s, up to 50. Each SHALL have a `date` and a
`title`. Milestones mark administrative dates, such as the license renewal call, the course or the
deadline for new arquebusiers. They SHALL be listed by date, then by title. The following rules
SHALL be blocking:
- `date` SHALL be a valid date (`400 Bad Request`);
- `title` SHALL be 1 to 100 characters after trimming, without line breaks (`400 Bad Request`);
- an edition SHALL NOT have more than 50 milestones (`409 Conflict`).

Admins SHALL add, edit and remove milestones in any status.

#### Scenario: Admin adds a milestone
- **WHEN** an Admin adds "Deadline for new arquebusiers" on 2030-11-30 to the edition 2031
- **THEN** the milestone is stored and listed among the edition's milestones in date order

#### Scenario: Empty title is blocking
- **WHEN** an Admin saves a milestone whose title is only spaces
- **THEN** the request is rejected with `400 Bad Request` naming `title`

### Requirement: Edition lifecycle (UC-11)
An edition's `status` SHALL be `DRAFT` (in preparation), `IN_PROGRESS` or `CLOSED`, in that order.
Only an Admin SHALL change it, and only by hand. A change SHALL move it exactly one step forward,
or one step back to reopen. The request SHALL carry the edition's version.

The following rules SHALL be blocking:
- a move that is not one step SHALL be rejected (`409 Conflict`, `editions.invalidTransition`);
- at most one edition SHALL be `IN_PROGRESS`. A move that would put a second edition in progress
  SHALL be rejected (`409 Conflict`, `editions.anotherInProgress`). This SHALL hold under
  concurrent requests;
- moving from `DRAFT` to `IN_PROGRESS` SHALL require the festival dates, both window dates and the
  four prices. A move without them SHALL be rejected (`409 Conflict`, `editions.incomplete`), and
  the response SHALL list the missing fields;
- an edition SHALL leave `IN_PROGRESS`, in either direction, only with its orders closed
  (`409 Conflict`, `editions.ordersOpen`);
- a move based on an outdated version SHALL be rejected (`409 Conflict`, `editions.modified`).

#### Scenario: Admin starts an edition
- **WHEN** an Admin moves a complete `DRAFT` edition to `IN_PROGRESS` while no other edition is in progress
- **THEN** the edition becomes `IN_PROGRESS` with its orders still closed, and the change is audited with both statuses

#### Scenario: Skipping a step is blocking
- **WHEN** an Admin moves a `DRAFT` edition directly to `CLOSED`
- **THEN** the request is rejected with `409 Conflict` and the status is unchanged

#### Scenario: Incomplete edition cannot start
- **WHEN** an Admin moves a `DRAFT` edition without `flaskRental` and without `ordersCloseOn` to `IN_PROGRESS`
- **THEN** the request is rejected with `409 Conflict` listing `flaskRental` and `ordersCloseOn`

#### Scenario: Second edition in progress is blocking
- **WHEN** an Admin moves the edition 2032 to `IN_PROGRESS` while the edition 2031 is `IN_PROGRESS`
- **THEN** the request is rejected with `409 Conflict` and a translated explanation naming the edition in progress

#### Scenario: Closing with open orders is blocking
- **WHEN** an Admin closes an `IN_PROGRESS` edition whose orders are open
- **THEN** the request is rejected with `409 Conflict` and the edition stays in progress

#### Scenario: Reopening a closed edition while another is in progress
- **WHEN** an Admin moves the `CLOSED` edition 2030 back to `IN_PROGRESS` while the edition 2031 is `IN_PROGRESS`
- **THEN** the request is rejected with `409 Conflict`

#### Scenario: FiringChief cannot change the status
- **WHEN** a FiringChief requests a status change
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Opening and closing orders (UC-11, BR-10)
An Admin SHALL open and close the orders of the edition in progress, by hand, as many times as
needed. Reopening the orders after a review is how the Federation opens a corrections window.
FiringChiefs SHALL edit orders only while the orders of the edition in progress are open. Otherwise,
orders SHALL be read-only for them. Admins SHALL always be able to edit orders (BR-10). Comparsa
orders (#10) SHALL follow this rule, enforced on the server.

The following rules SHALL be blocking:
- only an edition `IN_PROGRESS` SHALL have its orders opened (`409 Conflict`,
  `editions.notInProgress`);
- the request SHALL carry the edition's version (`409 Conflict`, `editions.modified` when
  outdated).

Opening open orders, or closing closed ones, SHALL succeed without recording anything. A
FiringChief SHALL receive `403 Forbidden`.

#### Scenario: Admin opens the orders
- **WHEN** an Admin opens the orders of the edition in progress
- **THEN** the orders are open, FiringChiefs may edit orders, and the change is audited

#### Scenario: Admin reopens the orders for corrections
- **WHEN** an Admin closes the orders, reviews them, and then opens them again
- **THEN** the orders are open again and each change is audited

#### Scenario: Orders of a draft cannot open
- **WHEN** an Admin opens the orders of a `DRAFT` edition
- **THEN** the request is rejected with `409 Conflict` and the orders stay closed

#### Scenario: FiringChief cannot open the orders
- **WHEN** a FiringChief tries to open or close the orders through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Current edition
The **current edition** SHALL be the edition `IN_PROGRESS`. When no edition is in progress, there
SHALL be no current edition. Every signed-in user SHALL be able to read the current edition. The
answer SHALL say whether its orders are open, which is whether FiringChiefs may edit orders.

#### Scenario: Orders open
- **WHEN** a FiringChief reads the current edition while its orders are open
- **THEN** the answer is that edition, saying that its orders are open

#### Scenario: Orders closed
- **WHEN** the orders of the current edition are closed
- **THEN** the answer says that the orders are closed and FiringChiefs may not edit them

#### Scenario: No current edition
- **WHEN** the only editions are one `CLOSED` and one `DRAFT`
- **THEN** reading the current edition answers that there is none

### Requirement: Edition management by Admins
Only Admins SHALL create editions, edit their festival dates, window dates and prices, change the
offered models, manage milestones, change the status, open and close the orders, and delete
editions. Admins SHALL be able to edit an edition in any status, including `CLOSED` (BR-10,
exceptional cases). An edit of the festival dates, window dates or prices SHALL carry the edition's
version. An edit based on an outdated version SHALL be rejected with `409 Conflict`
(`editions.modified`). The set of offered models and each milestone SHALL be saved as given; the
last save wins. Only a `DRAFT` edition SHALL be deleted, with its offered models and milestones.
Deleting an edition in any other status SHALL be rejected with `409 Conflict`
(`editions.notDraft`). A FiringChief SHALL receive `403 Forbidden` from every one of these
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

#### Scenario: FiringChief cannot manage editions
- **WHEN** a FiringChief tries to create, edit, delete an edition, change its models or manage its milestones through the API
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Edition visibility (BR-12)
Editions are Federation-wide and not scoped to a comparsa. An Admin SHALL see every edition. A
FiringChief SHALL see every edition that is not `DRAFT`, read-only, with its dates, prices, offered
models and milestones. A `DRAFT` edition SHALL be answered to a FiringChief as if it did not exist
(`404 Not Found`). The list SHALL be sorted by `year`, newest first.

#### Scenario: FiringChief lists editions
- **WHEN** a FiringChief opens the editions page while the editions 2030 (`CLOSED`), 2031 (`IN_PROGRESS`) and 2032 (`DRAFT`) exist
- **THEN** only 2031 and 2030 are listed, and no create, edit, status or orders action is offered

#### Scenario: FiringChief opens a draft
- **WHEN** a FiringChief requests the `DRAFT` edition 2032
- **THEN** the API responds `404 Not Found` and the UI shows the not-found page

### Requirement: Edition changes are audited
Every creation, edit, change of offered models, milestone addition, edit and removal, status change,
opening and closing of the orders and deletion of an edition SHALL be recorded in the audit trail
in the same transaction as the change. Each entry SHALL record the acting Admin, the action and the
edition. Edits SHALL record the changed fields with their previous and new values. Status changes
SHALL record the previous and the new status. Changes of offered models SHALL record the models
added and removed. A deletion SHALL record a snapshot of the deleted edition. An operation that
changes nothing SHALL NOT record an entry. A rejected request SHALL NOT record an entry.

#### Scenario: Orders closing audited
- **WHEN** an Admin closes the orders of the edition 2031
- **THEN** one audit entry records the Admin, the edition and that the orders were closed

#### Scenario: Rejected move is not audited
- **WHEN** a status move is rejected with `409 Conflict`
- **THEN** no audit entry is recorded

### Requirement: Editions screens
The UI SHALL offer every signed-in user an "Editions" page in the main navigation, following the
list template. It SHALL list the editions the user can see, each with its year, festival dates and
status, and, for the edition in progress, whether its orders are open. The current edition SHALL
be marked. Admins SHALL also have:
- a "New edition" action, opening a form with the year and the festival dates. The form SHALL say
  which prices and models will be copied, and from which edition;
- an empty state inviting them to create the first edition.

FiringChiefs SHALL get an empty state saying that the Federation has not opened any edition yet.

Each edition SHALL have a detail page in read mode. The header SHALL show the year, the status and,
for the edition in progress, whether its orders are open. The key facts SHALL show the festival
dates and the next order window date from today, with what it is (for example "Orders close on
10 Feb 2031"). The page SHALL have these sections:
- dates and order window;
- prices, shown as currency;
- offered rental models, with the models that are no longer rentable marked;
- milestones, as a list with add, edit and remove actions.

For Admins, each section SHALL have its own edit action in a side panel (a bottom sheet on phones).
Admins SHALL also have these actions:
- for a `DRAFT`: "Start edition" as the primary action, and the deletion in a "More actions" menu;
- for the edition in progress: "Open orders" or "Close orders" as the primary action, and in
  "More actions" "Close edition" and "Back to preparation";
- for a `CLOSED` edition: "Reopen edition" in "More actions".

Every action SHALL be confirmed in a dialog that says what changes for FiringChiefs. A move rejected
as incomplete SHALL list the missing fields in words. FiringChiefs SHALL see the same page
read-only, without these actions.

The forms and edit panels SHALL validate the same blocking rules as the server before submitting
and SHALL show a rejected change's translated reason. Dates and amounts SHALL be shown in the user's
language. The pages SHALL work on a phone (NFR-01). Every text SHALL be available in es-ES,
ca-ES-valencia and en. The pages SHALL pass automated accessibility checks (NFR-07).

#### Scenario: Admin opens the orders from the detail page
- **WHEN** an Admin chooses "Open orders" on the edition in progress and confirms
- **THEN** the page shows the orders as open, the primary action becomes "Close orders", and the change is announced

#### Scenario: Incomplete edition explained
- **WHEN** an Admin chooses "Start edition" on a `DRAFT` edition without prices
- **THEN** the dialog lists the missing prices in words and the edition stays `DRAFT`

#### Scenario: Admin edits the prices
- **WHEN** an Admin edits the prices section, types "4,50" for the caps box in es-ES and saves
- **THEN** the panel closes, the prices section shows "4,50 €", and "Changes saved" is announced

#### Scenario: FiringChief reads an edition
- **WHEN** a FiringChief opens the current edition on a phone
- **THEN** the dates, order window, prices, offered models and milestones are shown without edit, status or orders actions

### Requirement: Current edition on the start page
The start page SHALL show every signed-in user a current-edition card above the alerts dashboard.
The card SHALL show:
- the current edition's year;
- whether its orders are open;
- the next order window date from today, with what it is;
- up to three milestones dated today or later;
- a link to the edition.

When there is no current edition, the card SHALL say so. For Admins, it SHALL also link to the
editions page. A failure to load the card SHALL show a translated message and SHALL NOT hide the
alerts dashboard.

#### Scenario: Current edition on the start page
- **WHEN** a FiringChief signs in while the orders of the edition 2031 are open, closing on 2031-02-10, with two upcoming milestones
- **THEN** the start page shows the 2031 card with "Orders open", the closing date and both milestones

#### Scenario: No edition in progress
- **WHEN** an Admin opens the start page while no edition is in progress
- **THEN** the card says that no edition is in progress and links to the editions page

### Requirement: Synthetic edition data
The synthetic seed SHALL create:
- a `CLOSED` edition for a past year;
- an `IN_PROGRESS` current edition with open orders, every date and price, rentable models of
  several kinds and several milestones;
- a `DRAFT` edition for the following year.

It SHALL use fixed identifiers and SHALL be safe to run again. Its years SHALL be computed from the
seed date, so that the current edition's order window includes it. It SHALL NOT contain real
Federation dates, prices or texts. Production data SHALL never come from it.

#### Scenario: Seeding twice
- **WHEN** the seed runs twice
- **THEN** the editions, their models and their milestones exist once

#### Scenario: Seeded current edition
- **WHEN** the seeded FiringChief signs in after seeding
- **THEN** the start page shows the seeded current edition with its orders open
