# Spec Delta

## MODIFIED Requirements

### Requirement: Calendar milestones
An edition SHALL have any number of `CalendarMilestone`s, up to 50. Each SHALL have a `date`, a
`title` and a `notify` flag. Milestones mark administrative dates, such as the license renewal
call, the course or the deadline for new arquebusiers. `notify` SHALL say whether the milestone is
reminded by email (notifications capability, "Milestone reminders"). It SHALL be off unless the
Admin turns it on, and milestones that existed before it SHALL have it off. Milestones SHALL be
listed by date, then by title. The following rules SHALL be blocking:
- `date` SHALL be a valid date (`400 Bad Request`);
- `title` SHALL be 1 to 100 characters after trimming, without line breaks (`400 Bad Request`);
- `notify`, when given, SHALL be `true` or `false` (`400 Bad Request`, like any malformed body);
- an edition SHALL NOT have more than 50 milestones (`409 Conflict`).

Admins SHALL add, edit and remove milestones in any status. Every signed-in user who can see the
edition SHALL see each milestone's `notify` flag.

#### Scenario: Admin adds a milestone
- **WHEN** an Admin adds "Deadline for new arquebusiers" on 2030-11-30 to the edition 2031
- **THEN** the milestone is stored with `notify` off and listed among the edition's milestones in date order

#### Scenario: Admin adds a milestone with a reminder
- **WHEN** an Admin adds "Course" on 2030-11-15 with `notify` on
- **THEN** the milestone is stored with `notify` on, and the change is audited with that field

#### Scenario: Admin turns a reminder off
- **WHEN** an Admin edits a milestone with `notify` on and turns it off
- **THEN** the milestone is saved with `notify` off, it is no longer reminded, and the audit entry records the previous and new values

#### Scenario: Empty title is blocking
- **WHEN** an Admin saves a milestone whose title is only spaces
- **THEN** the request is rejected with `400 Bad Request` naming `title`

#### Scenario: Invalid notify is blocking
- **WHEN** an Admin saves a milestone with `notify` set to `"yes"`
- **THEN** the request is rejected with `400 Bad Request`, and nothing is changed

#### Scenario: FiringChief sees the reminder flag
- **WHEN** a FiringChief reads the edition in progress, which has a milestone with `notify` on
- **THEN** the milestone is returned with `notify` on, read-only

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
- milestones, as a list with add, edit and remove actions. A milestone with `notify` on SHALL be
  marked as reminded by email, with a text equivalent and not by an icon alone. The add and edit
  panel SHALL have a "Send an email reminder" option that says who is reminded and when (7 days
  before, to Admins and, once the edition is in progress, to FiringChiefs).

For Admins, each section SHALL have its own edit action in a side panel (a bottom sheet on phones).
Admins SHALL also have these actions:
- for a `DRAFT`: "Start edition" as the primary action, and the deletion in a "More actions" menu;
- for the edition in progress: "Open orders" or "Close orders" as the primary action, and in
  "More actions" "Close edition" and "Back to preparation";
- for a `CLOSED` edition: "Reopen edition" in "More actions".

Every action SHALL be confirmed in a dialog that says what changes for FiringChiefs. The dialog for
opening or closing the orders SHALL also say that FiringChiefs are told by email. A move rejected
as incomplete SHALL list the missing fields in words. FiringChiefs SHALL see the same page
read-only, without these actions.

The forms and edit panels SHALL validate the same blocking rules as the server before submitting
and SHALL show a rejected change's translated reason. Dates and amounts SHALL be shown in the user's
language. The pages SHALL work on a phone (NFR-01). Every text SHALL be available in es-ES,
ca-ES-valencia and en. The pages SHALL pass automated accessibility checks (NFR-07).

#### Scenario: Admin opens the orders from the detail page
- **WHEN** an Admin chooses "Open orders" on the edition in progress and confirms
- **THEN** the page shows the orders as open, the primary action becomes "Close orders", and the change is announced

#### Scenario: Orders dialog mentions the email
- **WHEN** an Admin chooses "Close orders" on the edition in progress
- **THEN** the confirmation dialog says that the orders become read-only for FiringChiefs and that they are told by email

#### Scenario: Incomplete edition explained
- **WHEN** an Admin chooses "Start edition" on a `DRAFT` edition without prices
- **THEN** the dialog lists the missing prices in words and the edition stays `DRAFT`

#### Scenario: Admin edits the prices
- **WHEN** an Admin edits the prices section, types "4,50" for the caps box in es-ES and saves
- **THEN** the panel closes, the prices section shows "4,50 €", and "Changes saved" is announced

#### Scenario: Admin marks a milestone to remind
- **WHEN** an Admin edits a milestone, turns on "Send an email reminder" and saves
- **THEN** the panel closes, the milestone is marked as reminded by email in the list, and "Changes saved" is announced

#### Scenario: FiringChief reads an edition
- **WHEN** a FiringChief opens the current edition on a phone
- **THEN** the dates, order window, prices, offered models and milestones, with their reminder marks, are shown without edit, status or orders actions
