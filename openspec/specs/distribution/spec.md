# distribution Specification

## Purpose
Lets the Federation plan each edition's powder and weapon distribution days with a slot per
comparsa (UC-18), keeps the exceptional pickup proxies and prints their authorisation forms
(UC-19), and prints the distribution lists with global numbering (UC-20).

## Requirements

### Requirement: Distribution days (UC-18)
A `FestivalEdition` SHALL have at most one `Distribution` of each `type`: `POWDER` (the powder and
the rented flasks) and `WEAPONS` (the rented weapons) (maintainer decision). A `Distribution`
SHALL have a `date` and a `location`. Only Admins SHALL plan, edit and delete distributions, and
only for the edition `IN_PROGRESS`. Deleting a distribution SHALL remove its slots and SHALL NOT
remove any pickup proxy.

The following rules SHALL be blocking:
- a second distribution of the same type in the edition SHALL be rejected (`409 Conflict`,
  `distribution.alreadyPlanned`), also under concurrent requests;
- `type` SHALL be `POWDER` or `WEAPONS` (`400 Bad Request` naming `type`);
- `date` SHALL fall within the edition's `year` and SHALL NOT be after `festivalEndsOn`
  (`400 Bad Request` naming `date`);
- `location` SHALL be 1 to 200 characters after trimming, without line breaks (`400 Bad Request`
  naming `location`);
- a write on an edition that is not `IN_PROGRESS` SHALL be rejected (`409 Conflict`,
  `distribution.editionNotInProgress`);
- an edit or deletion SHALL carry the distribution's version; an outdated one SHALL be rejected
  (`409 Conflict`, `distribution.modified`).

Saving without changing any value SHALL succeed without recording anything.

#### Scenario: Admin plans the powder day
- **WHEN** an Admin plans the `POWDER` distribution of the edition 2031 in progress on 2031-04-18 at "Paraje Sintético"
- **THEN** the distribution is stored with no slots, and the planning is audited

#### Scenario: Second powder day is blocking
- **WHEN** an Admin plans a second `POWDER` distribution for the edition 2031
- **THEN** the request is rejected with `409 Conflict` and `distribution.alreadyPlanned`, and nothing is stored

#### Scenario: Date after the festival is blocking
- **WHEN** an Admin saves the `WEAPONS` distribution on 2031-04-27 for a festival ending on 2031-04-25
- **THEN** the request is rejected with `400 Bad Request` naming `date`

#### Scenario: Closed edition is read-only
- **WHEN** an Admin edits the location of a distribution of the `CLOSED` edition 2030
- **THEN** the request is rejected with `409 Conflict` and `distribution.editionNotInProgress`

#### Scenario: Deleting a day keeps the proxies
- **WHEN** an Admin deletes the `POWDER` distribution, which has slots, of an edition with powder proxies, and confirms
- **THEN** the distribution and its slots are removed, and every pickup proxy is kept

#### Scenario: FiringChief cannot plan
- **WHEN** a FiringChief plans, edits or deletes a distribution
- **THEN** the API responds `403 Forbidden` and nothing is changed

### Requirement: Distribution slots (UC-18)
Each comparsa SHALL have at most one `DistributionSlot` per distribution, with its start time
`startsAt` (hours and minutes, on the distribution's date). Several comparsas MAY share a start
time. An Admin SHALL save the slots of a distribution as one set, which replaces the previous
set: a comparsa left out of the set has no slot. The comparsas without a slot SHALL be listed as
such on the planning page; this SHALL NOT block anything.

The following rules SHALL be blocking (`400 Bad Request` naming the slot's field):
- each slot SHALL name an existing comparsa (`comparsaId`, `unknown`);
- a comparsa SHALL NOT appear twice in the set (`comparsaId`, `duplicate`);
- `startsAt` SHALL be a valid time from 00:00 to 23:59 (`startsAt`, `invalid`).

The save SHALL carry the distribution's version (`409 Conflict`, `distribution.modified` when
outdated) and follow the same edition rule as the distribution (`409 Conflict`,
`distribution.editionNotInProgress`). Only Admins SHALL save slots (`403 Forbidden` for a
FiringChief). A comparsa that has a slot SHALL NOT be deleted from the catalogue (`409 Conflict`,
as any comparsa in use).

#### Scenario: Admin assigns slots
- **WHEN** an Admin saves the slots of the powder day with "Comparsa Sintética Norte" at 09:00 and "Comparsa Sintética Sur" at 09:30, while "Comparsa Sintética Este" is active
- **THEN** both slots are stored, and the planning page lists "Comparsa Sintética Este" without a slot

#### Scenario: Removing a slot
- **WHEN** an Admin saves the slots again without "Comparsa Sintética Sur"
- **THEN** Sur no longer has a slot on that day, and the change is audited with Sur's previous time

#### Scenario: Duplicate comparsa is blocking
- **WHEN** an Admin saves a set with Norte at 09:00 and Norte at 10:00
- **THEN** the request is rejected with `400 Bad Request` naming the second slot's `comparsaId`, and the slots are unchanged

#### Scenario: Invalid time is blocking
- **WHEN** an Admin saves a slot at 24:30
- **THEN** the request is rejected with `400 Bad Request` naming its `startsAt`

### Requirement: Distribution visibility (BR-12)
An Admin SHALL see the distributions, every slot and every pickup proxy of any edition. A
FiringChief SHALL see the distributions (type, date and location) of every edition that is not
`DRAFT`, and only the slots and pickup proxies of the comparsas in their scope. The distributions
of a `DRAFT` edition SHALL be answered to a FiringChief as if the edition did not exist
(`404 Not Found`). A proxy of a comparsa outside their scope SHALL be answered `404 Not Found`.

#### Scenario: FiringChief sees their slot
- **WHEN** the FiringChief of Norte opens the distribution of the edition in progress, where Norte has 09:00 and Sur 09:30 on the powder day
- **THEN** they see the powder day's date and location and Norte's slot at 09:00, and nothing of Sur

#### Scenario: FiringChief and another comparsa's proxy
- **WHEN** the FiringChief of Norte requests a pickup proxy of Sur
- **THEN** the API responds `404 Not Found`

### Requirement: Pickup proxies (UC-19, BR-06)
A `PickupProxy` SHALL record that a holder who cannot attend authorises another arquebusier to
collect for them. It SHALL have a `holderEntry`, a `proxyEntry` and a `type` (`POWDER` or
`WEAPONS`). It SHALL NOT store a reason (maintainer decision): the reason may reveal health data,
so it is written by hand on the signed form. A proxy MAY collect for several holders.

The following rules SHALL be blocking:
- the proxy's entry SHALL be an entry, `ACTIVE` or `RESERVE`, of the same comparsa's order in the
  same edition as the holder's entry (BR-06); any other entry SHALL be rejected (`400 Bad Request`
  naming `proxyEntryId`, `notInOrder`), without saying whether it exists elsewhere;
- the proxy SHALL NOT be the holder (`400 Bad Request` naming `proxyEntryId`, `sameAsHolder`);
- the holder SHALL have something to collect of that type: for `POWDER`, an `ACTIVE` entry with
  `powderKg` above 0; for `WEAPONS`, an `ACTIVE` entry with weapon source `RENTAL`
  (`400 Bad Request` naming `holderEntryId`, `nothingToCollect`);
- the proxy SHALL hold an active weapons license (maintainer decision; the paper form states it):
  their arquebusier SHALL be in the registry with an issued license, of any type, that does not
  expire before the **reference date** — the date of the distribution of that type, or the
  festival's first day while that day is not planned. A pending, missing or expiring license SHALL
  be rejected (`400 Bad Request` naming `proxyEntryId`, `licenseInvalid`). This is a deliberate
  exception to compliance checks being warnings: it is a condition to collect for someone else,
  not a warning about one's own entry;
- a holder SHALL have at most one proxy of each type (`409 Conflict`,
  `proxies.alreadyAuthorised`), also under concurrent requests;
- someone absent SHALL NOT collect: the proxy SHALL NOT have a proxy of the same type themselves
  (`409 Conflict`, `proxies.proxyAbsent`), and a holder who already collects for others of that
  type SHALL NOT get a proxy of that type (`409 Conflict`, `proxies.holderIsProxy`), also under
  concurrent requests.

A proxy SHALL NOT be edited: it is removed and registered again.

#### Scenario: FiringChief registers a powder proxy
- **WHEN** the FiringChief of Norte authorises a `RESERVE` entry of Norte's order to collect the powder of an `ACTIVE` 2 kg entry of the same order
- **THEN** the proxy is stored without a reason, and the registration is audited

#### Scenario: Proxy of another comparsa is blocking
- **WHEN** a user names an entry of Sur's order as the proxy of a holder of Norte
- **THEN** the request is rejected with `400 Bad Request` naming `proxyEntryId` with `notInOrder`

#### Scenario: Nothing to collect is blocking
- **WHEN** a user registers a `WEAPONS` proxy for a holder whose weapon is `OWNED`
- **THEN** the request is rejected with `400 Bad Request` naming `holderEntryId` with `nothingToCollect`

#### Scenario: Proxy without an active license is blocking
- **WHEN** a user names as proxy a `RESERVE` entry whose license expires on 2031-03-31, for the powder day planned on 2031-04-18
- **THEN** the request is rejected with `400 Bad Request` naming `proxyEntryId` with `licenseInvalid`, and nothing is stored

#### Scenario: Pending license is blocking
- **WHEN** a user names as proxy an entry whose arquebusier's license is pending
- **THEN** the request is rejected with `400 Bad Request` naming `proxyEntryId` with `licenseInvalid`

#### Scenario: One proxy collects for two holders
- **WHEN** the same `RESERVE` entry is authorised for the powder of two holders of Norte
- **THEN** both proxies are stored

#### Scenario: Second proxy for the same holder is blocking
- **WHEN** a holder already has a `POWDER` proxy and another `POWDER` proxy is registered for them
- **THEN** the request is rejected with `409 Conflict` and `proxies.alreadyAuthorised`

#### Scenario: An absent arquebusier cannot collect for others
- **WHEN** holder A has authorised B for the powder, and A is then named as the powder proxy of holder C
- **THEN** the request is rejected with `409 Conflict` and `proxies.proxyAbsent`

#### Scenario: A proxy cannot be absent
- **WHEN** B collects the powder for A, and a powder proxy is then registered for B
- **THEN** the request is rejected with `409 Conflict` and `proxies.holderIsProxy`

### Requirement: Who may manage pickup proxies (BR-10, BR-12)
A FiringChief SHALL register and remove the pickup proxies of the comparsas in their scope while
the edition is `IN_PROGRESS`, whether its orders are open or closed and whatever the order's
status (maintainer decision: absences are known at the last moment). An Admin SHALL register and
remove the proxies of any comparsa in any edition that is not `DRAFT`. Otherwise:
- a FiringChief's write on an edition that is not `IN_PROGRESS` SHALL be rejected (`409 Conflict`,
  `distribution.editionNotInProgress`);
- a holder entry of a comparsa outside the FiringChief's scope, or of a `DRAFT` edition, SHALL be
  answered `404 Not Found`.

#### Scenario: Proxy after the orders closed
- **WHEN** the orders of the edition in progress are closed and Norte's order is `VALIDATED`, and the FiringChief of Norte registers a proxy
- **THEN** the proxy is stored

#### Scenario: Closed edition for a FiringChief
- **WHEN** the FiringChief of Norte removes a proxy of the `CLOSED` edition 2030
- **THEN** the request is rejected with `409 Conflict` and `distribution.editionNotInProgress`, and the proxy is kept

#### Scenario: Admin removes a proxy
- **WHEN** an Admin removes a proxy of Sur and confirms
- **THEN** the proxy no longer exists, and the removal is audited

### Requirement: Proxies that no longer hold
A registered proxy SHALL be checked again whenever it is shown or used, because the data it was
checked against can change afterwards. It SHALL be shown with one of these problems:
- **does not apply**: the holder no longer has anything to collect of that type, after their
  entry changed;
- **license not active**: the proxy's license no longer meets the license rule on the reference
  date, because the license changed, the arquebusier left the registry, or the day was planned or
  moved.

A proxy with a problem SHALL stay until it is removed, SHALL be left out of the distribution
lists, and its form SHALL be refused (`409 Conflict`, `proxies.notApplicable` or
`proxies.licenseInvalid`).

#### Scenario: Holder's powder removed
- **WHEN** an Admin changes a holder's entry to 0 kg after their powder proxy was registered
- **THEN** the proxy is listed as not applying, is left out of the powder list, and its form is refused with `409 Conflict` and `proxies.notApplicable`

#### Scenario: Day planned after the proxy's license expires
- **WHEN** a proxy was registered with a license expiring on 2031-04-10, and the powder day is then planned on 2031-04-18
- **THEN** the proxy is listed with the license problem, is left out of the powder list, and its form is refused with `409 Conflict` and `proxies.licenseInvalid`

### Requirement: Pickup proxies follow their entries (BR-14)
When an entry is removed, because its arquebusier is deleted from the registry while the orders
of the edition in progress are open (BR-14), every pickup proxy where that entry is the holder or
the proxy SHALL be removed in the same transaction. The removal of the entry is audited by the
deletion. No other change SHALL remove a proxy.

#### Scenario: Proxy deleted from the registry
- **WHEN** the orders are open and the arquebusier of a proxy's entry is deleted from the registry
- **THEN** the entry and the proxy no longer exist, and the holder has no proxy

### Requirement: Pickup authorisation form (UC-19)
For each pickup proxy, Admins and the FiringChiefs of its comparsa SHALL download a pre-filled
authorisation form as a PDF, to be printed and signed on paper by the holder and the proxy. The
form SHALL be in the language of the user who downloads it (es-ES, ca-ES-valencia or en)
(maintainer decision). It SHALL contain:
- the Federation's logo, when an Admin has uploaded it (see the federation catalogue); without
  it, the form SHALL be printed without a logo;
- the edition's year and what is collected (the powder, or the rented weapon);
- the holder's last name and first name, DNI/NIE, license type and comparsa;
- the proxy's last name and first name, DNI/NIE and license type, and that they belong to the same
  comparsa;
- the distribution's date and location when that day is planned;
- blank lines for the reason, the place and date, and a box for each signature.

It SHALL contain no other image. Identities and licenses SHALL come from the registry at the time
of the download; a license type the holder lacks SHALL be left blank.

#### Scenario: FiringChief prints a form
- **WHEN** the FiringChief of Norte downloads the form of a powder proxy of Norte in ca-ES-valencia, after an Admin uploaded the Federation's logo
- **THEN** the PDF is in Valencian, shows the Federation's logo, names the holder and the proxy with their DNI/NIE, license type and comparsa, the edition 2031 and the powder, and leaves the reason and the signatures blank

#### Scenario: Form without a logo
- **WHEN** a user downloads a form before any Federation logo was uploaded
- **THEN** the PDF is generated without a logo, with the same contents

#### Scenario: Weapon form
- **WHEN** an Admin downloads the form of a `WEAPONS` proxy
- **THEN** the PDF authorises the collection of the rented weapon, with the weapon day's date and location when it is planned

### Requirement: Distribution lists (UC-20)
Admins SHALL download, for each planned distribution, its list as Excel and as PDF, in the
language of the user who downloads it (es-ES, ca-ES-valencia or en) (maintainer decision). Model
labels and comparsa names SHALL stay as the catalogue stores them. A list SHALL include only the
`VALIDATED` orders of the edition. Each row SHALL be one holder:
- **powder list**: each `ACTIVE` entry with `powderKg` above 0: number, slot time, comparsa, last
  name and first name, DNI/NIE, kilograms, flask (owned, rented 1 kg, rented 2 kg, or none), empty
  columns for the flask number, traceability 1 and traceability 2 (filled on the day, Q-43), and
  the proxy's last name, first name and DNI/NIE when the holder has a `POWDER` proxy without a
  problem (see "Proxies that no longer hold");
- **weapons list**: each `ACTIVE` entry with weapon source `RENTAL`: number, slot time, comparsa,
  last name and first name, DNI/NIE, rented model, an empty column for the weapon number, and the
  proxy as above for a `WEAPONS` proxy.

Identities SHALL come from the registry while the arquebusier is in it, and from the entry's copy
otherwise. The list SHALL state the edition, the type, the date, the location and the generation
date. It SHALL contain no license, phone, email or birth date (SEC-06). An edition without
validated orders SHALL produce a list with its headings and no rows. A FiringChief SHALL receive
`403 Forbidden`. A list of an unknown distribution SHALL be answered `404 Not Found`.

#### Scenario: Powder list contents
- **WHEN** Norte's validated order has an `ACTIVE` 2 kg entry with a rented 2 kg flask and a powder proxy, an `ACTIVE` 0 kg entry and a `RESERVE` entry
- **THEN** the powder list has one row for Norte: the 2 kg holder, "rented 2 kg", the empty flask number and traceability columns, and the proxy's name and DNI/NIE

#### Scenario: Only validated orders
- **WHEN** Norte's order is `VALIDATED` and Sur's is `SUBMITTED`
- **THEN** the lists include Norte's holders and none of Sur's, and the page warns that Sur is not validated

#### Scenario: Weapons list contents
- **WHEN** a validated order has entries renting "ARCABUZ MORO DIESTRO", with an owned weapon and with a loan
- **THEN** the weapons list has one row, for the rental, with the model and an empty weapon number

### Requirement: Global numbering (UC-20)
Each list SHALL number its rows from 1, continuously across the comparsas of the day. Comparsas
SHALL follow their slot times, then their names in Spanish alphabetical order for the same time;
comparsas without a slot SHALL come last, by name, with an empty slot time. Within a comparsa,
holders SHALL follow the Spanish alphabetical order of last name and first name. The numbers
SHALL be derived each time the list is generated and SHALL NOT be stored (maintainer decision): a
list generated after the data changed MAY number differently, and the page SHALL say so.

#### Scenario: Numbering across comparsas
- **WHEN** Norte has the 09:00 slot with holders "Abad" and "Zamora", Sur the 09:30 slot with holder "Bernabeu", and Este no slot with holder "Climent"
- **THEN** the powder list numbers Abad 1, Zamora 2, Bernabeu 3 and Climent 4

#### Scenario: Renumbered after a change
- **WHEN** a new holder "Alcaraz" is validated in Norte after the list was printed, and the list is generated again
- **THEN** Alcaraz is number 1, and every holder after them moves one number down

### Requirement: Distribution documents are protected and audited
Every list and form SHALL be generated on request and SHALL NOT be stored. Each one returned
SHALL be recorded in the audit trail before it is sent (SEC-05): the user, the document and its
version, the format, the edition, the distribution type, the comparsa for a form, and the number
of rows for a list, without names or DNI/NIE. A refused request SHALL NOT be recorded, and a
failure to record SHALL return no file. Responses SHALL carry `Cache-Control: no-store`, SHALL
require a signed-in user, and SHALL share the per-user limit of the exports (`429 Too Many
Requests`). File names SHALL contain no personal data. An unknown format SHALL be answered
`404 Not Found`.

#### Scenario: List download audited
- **WHEN** an Admin downloads the powder list as PDF
- **THEN** one audit entry records the Admin, the powder list and its version, `PDF`, the edition and the number of rows, and no name or DNI/NIE

#### Scenario: Form file name
- **WHEN** a user downloads a proxy's form for the edition 2031
- **THEN** the file name contains the year, the type and the comparsa's name, and no name or DNI/NIE of the holder or the proxy

#### Scenario: List in the user's language
- **WHEN** an Admin whose language is English downloads the weapons list
- **THEN** its title, headings and words are in English, and the model labels and comparsa names are as the catalogue stores them

### Requirement: Distribution changes are audited
Every planning, edit and deletion of a distribution, every change of its slots, and every
registration and removal of a pickup proxy SHALL be recorded in the audit trail in the same
transaction as the change, with the acting user, the action, the edition and the distribution or
proxy. Edits SHALL record the changed fields with their previous and new values; slot changes
SHALL record each comparsa added, moved or removed with its previous and new time; proxy entries
SHALL record the comparsa, the type and the two entries by id, without names or DNI/NIE. A rejected
request or a save that changes nothing SHALL NOT record an entry.

#### Scenario: Proxy registration audited
- **WHEN** a FiringChief registers a powder proxy
- **THEN** one audit entry records the FiringChief, the comparsa, `POWDER` and the holder's and the proxy's entries by id, and no name or DNI/NIE

#### Scenario: Rejected proxy not audited
- **WHEN** a proxy registration is rejected with `400 Bad Request`
- **THEN** no audit entry is recorded

### Requirement: Distribution screens
The UI SHALL offer every signed-in user a "Distribution" item in the main navigation, opening the
distribution page of the current edition; with no current edition, the page SHALL say so and link
to the editions. Each edition that is not `DRAFT` SHALL link from its page to its own distribution
page. The distribution page SHALL show, for the powder day and for the weapons day:
- its date and location, or that it is not planned yet;
- for Admins, actions to plan, edit and delete it (the deletion confirmed in a dialog that says its
  slots are removed), and to edit its slots in a side panel (a bottom sheet on phones) with a time
  for each comparsa;
- the slots in time order: every comparsa's for Admins, with the comparsas without a slot; only
  their own comparsas' for FiringChiefs;
- for Admins, the list as Excel and as PDF, a warning naming the comparsas whose orders are not
  validated, and a note that the numbering is derived on each download.

It SHALL also show the pickup proxies (every comparsa's for Admins, with a comparsa filter; their
own for FiringChiefs): holder, type, proxy, comparsa and any problem (see "Proxies that no longer
hold"), with "Print form" (not offered while there is a problem) and "Remove" (confirmed in a
dialog). "Add proxy" SHALL open a side panel choosing the comparsa (when the user has several),
the type, the holder among the entries with something to collect of that type without a proxy of
it, and the proxy among the other entries of the same order; entries without an active license on
the reference date SHALL be shown as not eligible, saying why. The panel SHALL say that the reason
is written by hand on the form. When the user may not change proxies, the page SHALL say
why and offer no write action. Every rejection SHALL show its translated reason. The page SHALL
work on a 360 px wide phone (NFR-01), every text SHALL be available in es-ES, ca-ES-valencia and
en, and it SHALL pass automated accessibility checks (NFR-07).

#### Scenario: Admin plans from the page
- **WHEN** an Admin opens the distribution of the edition in progress with no day planned
- **THEN** both days read "not planned" with a "Plan" action, and no list download is offered

#### Scenario: FiringChief's page
- **WHEN** the FiringChief of Norte opens the distribution page
- **THEN** they see both days, Norte's slots, Norte's proxies with "Add proxy", "Print form" and "Remove", and no planning action or list download

#### Scenario: Read-only in a closed edition
- **WHEN** a FiringChief opens the distribution page of the `CLOSED` edition 2030
- **THEN** the page shows its days, slots and proxies, says that the edition is closed, and offers "Print form" but no add or remove action

### Requirement: Synthetic distribution data
The synthetic seed SHALL create, for the current edition, a planned powder day and a planned
weapons day with slots for some seeded comparsas and one seeded comparsa without a slot, a powder
proxy and a weapons proxy, each with a proxy whose synthetic license is valid on its day. It SHALL
use fixed identifiers, SHALL be safe to run again, and SHALL NOT contain real names, places or
national IDs.

#### Scenario: Seeding twice
- **WHEN** the seed runs twice
- **THEN** the distributions, slots and proxies exist once

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
