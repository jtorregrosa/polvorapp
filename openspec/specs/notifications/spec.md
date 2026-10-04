# notifications Specification

## Purpose
Emails the people who use PolvorApp about what they must act on: licenses about to expire, the
order window and its deadline, the review of each order and the edition's milestones (UC-23).
Each user chooses which kinds they receive, and every email stays within their comparsa scope
(BR-12) and holds no more personal data than it needs.

## Requirements

### Requirement: Notification kinds and recipients
PolvorApp SHALL send these kinds of notification (`NotificationKind`), each by email:

| Kind | Emails | Recipients |
|---|---|---|
| `LICENSE_DIGEST` | Monthly license digest | FiringChiefs |
| `ORDER_WINDOW` | Orders opened, orders closed, planned close reminders | FiringChiefs |
| `ORDER_STATUS` | Order returned or validated; order submitted | The order's FiringChiefs; Admins for a submission |
| `MILESTONE_REMINDER` | Milestone reminders | Admins and FiringChiefs |

A user SHALL receive a notification only while their status is `ACTIVE`. `INVITED` and
`DEACTIVATED` users SHALL NOT receive any. A user SHALL receive only the kinds that apply to their
current role and that they have not turned off. A FiringChief SHALL be told only about the
comparsas assigned to them and about editions they can see (BR-12). These rules SHALL be checked
again when each email is sent, so a user deactivated, moved to another role or unassigned from
a comparsa after a notification was recorded SHALL NOT receive it. Arquebusiers and any address
that is not a user's SHALL never receive a notification.

#### Scenario: Invited user receives nothing
- **WHEN** an Admin opens the orders while the FiringChief "Jefe Sintético" has status `INVITED`
- **THEN** every FiringChief with status `ACTIVE` is emailed, and "Jefe Sintético" is not

#### Scenario: Deactivated before sending
- **WHEN** an order is returned and an Admin deactivates one of its two FiringChiefs before the email is sent
- **THEN** only the other FiringChief receives the email

#### Scenario: Unassigned before sending
- **WHEN** an order of "Comparsa Sintética Norte" is validated and an Admin then removes the assignment of its FiringChief to that comparsa before the email is sent
- **THEN** that FiringChief does not receive the email

#### Scenario: Arquebusiers are never emailed
- **WHEN** the license digest is sent for a comparsa whose arquebusiers have email addresses
- **THEN** no email is sent to any arquebusier's address

### Requirement: Notification preferences
Each user SHALL be able to turn each kind that applies to their role on or off for themselves.
Every kind SHALL be on until the user turns it off. The kinds that apply SHALL be:
- for a FiringChief: `LICENSE_DIGEST`, `ORDER_WINDOW`, `ORDER_STATUS` and `MILESTONE_REMINDER`;
- for an Admin: `ORDER_STATUS` and `MILESTONE_REMINDER`.

A user SHALL read and change only their own preferences; no endpoint SHALL change another user's.
The following rules SHALL be blocking (`400 Bad Request` naming the field and the reason):
- every kind SHALL be a known `NotificationKind` (`invalid`);
- every kind SHALL apply to the user's role (`notApplicable`).

A preference SHALL be kept when the user's role changes. Only the kinds that apply to the new role
SHALL be shown and used. Saving the same preferences again SHALL succeed without recording
anything.

#### Scenario: FiringChief turns off the license digest
- **WHEN** a FiringChief turns `LICENSE_DIGEST` off and saves
- **THEN** they receive no further license digest, and still receive the other kinds

#### Scenario: Defaults
- **WHEN** a newly invited Admin accepts the invitation and reads their preferences
- **THEN** `ORDER_STATUS` and `MILESTONE_REMINDER` are both on, and no other kind is listed

#### Scenario: Kind that does not apply is blocking
- **WHEN** an Admin saves preferences turning `LICENSE_DIGEST` off
- **THEN** the request is rejected with `400 Bad Request` naming `LICENSE_DIGEST` with `notApplicable`, and nothing is changed

#### Scenario: Unknown kind is blocking
- **WHEN** a user saves preferences with the kind `SMS_ALERTS`
- **THEN** the request is rejected with `400 Bad Request` naming the kind with `invalid`, and nothing is changed

#### Scenario: Role change keeps the preference
- **WHEN** a FiringChief who turned `MILESTONE_REMINDER` off becomes an Admin
- **THEN** they receive no milestone reminders as an Admin either, and their preferences list `ORDER_STATUS` and `MILESTONE_REMINDER` only

#### Scenario: Not signed in
- **WHEN** an anonymous request reads or saves notification preferences
- **THEN** the API responds `401 Unauthorized`

### Requirement: License digest (UC-23, BR-04)
On the first day of each month, each FiringChief with `LICENSE_DIGEST` on SHALL receive one
license digest. For each comparsa assigned to them, it SHALL count the `ACTIVE` arquebusiers
whose license is:
- missing;
- pending;
- expired before the digest date;
- valid but expiring within 90 days of the digest date.

Dates SHALL be evaluated in Europe/Madrid. A comparsa with nothing to count SHALL be left out.
A FiringChief whose comparsas have nothing to count SHALL receive no digest that month. The digest
SHALL link to the alerts dashboard. It SHALL NOT name any arquebusier. A digest that was not sent on
the first day, because PolvorApp was not running, SHALL be sent on the next day it runs in that
month. A FiringChief SHALL receive at most one digest per month. The digest is a reminder only: it
SHALL NOT block anything (BR-04 warnings).

#### Scenario: Monthly digest
- **WHEN** on 2031-01-01 the FiringChief of "Comparsa Sintética Norte" has two `ACTIVE` arquebusiers whose licenses expire on 2031-02-15 and 2031-08-01, and one with a pending license
- **THEN** they receive one digest counting one license expiring within 90 days and one pending license for that comparsa, with a link to the alerts dashboard and no names

#### Scenario: Several comparsas in one email
- **WHEN** a FiringChief assigned to two comparsas with licenses to report receives the digest
- **THEN** one email lists the counts of each comparsa separately

#### Scenario: Nothing to report
- **WHEN** every `ACTIVE` arquebusier of a FiringChief's comparsas holds a license valid for more than 90 days
- **THEN** that FiringChief receives no digest that month

#### Scenario: Reserve arquebusiers are left out
- **WHEN** the only arquebusier of a comparsa with an expired license is `RESERVE`
- **THEN** the digest counts nothing for that comparsa

#### Scenario: Missed first day
- **WHEN** PolvorApp does not run on 2031-03-01 and runs again on 2031-03-02
- **THEN** the March digest is sent on 2031-03-02, and no second March digest is sent later

### Requirement: Orders opened and closed (BR-10)
When an Admin opens the orders of the current edition, each FiringChief with `ORDER_WINDOW` on and
at least one active comparsa assigned SHALL be told that the orders are open, with the edition's
year and the planned `ordersCloseOn`. When an Admin closes them, the same FiringChiefs SHALL be
told that the orders are closed and are read-only for them. Reopening the orders for corrections
SHALL send the "orders open" email again. Opening open orders or closing closed ones changes
nothing and SHALL send nothing. When the orders were opened or closed again before the email is
sent, the email that no longer holds SHALL NOT be sent. A failure to record the notification
SHALL fail the opening or closing like any other part of its transaction. A failure to send the
email SHALL NOT affect the orders.

#### Scenario: Orders opened
- **WHEN** an Admin opens the orders of the edition 2031, whose planned close date is 2031-02-10
- **THEN** each FiringChief with an active comparsa receives an email saying that the orders of 2031 are open until 10 February 2031, with a link to their orders

#### Scenario: Orders closed
- **WHEN** an Admin closes the orders of the edition 2031
- **THEN** each of those FiringChiefs receives an email saying that the orders are closed and can no longer be edited

#### Scenario: Toggled before sending
- **WHEN** an Admin opens the orders and closes them again before the "orders open" email is sent
- **THEN** no "orders open" email is sent, and the "orders closed" email is sent

#### Scenario: SMTP down while opening
- **WHEN** an Admin opens the orders while the SMTP server cannot be reached
- **THEN** the orders open as usual, and the emails are sent once the SMTP server is reachable again

### Requirement: Planned close reminders (BR-10)
While the orders of the current edition are open, the FiringChiefs with `ORDER_WINDOW` on of each
active comparsa assigned to them whose order is not prepared, `DRAFT` or `RETURNED` SHALL receive a
reminder:
- once when the planned `ordersCloseOn` is 2 to 7 days ahead;
- once when it is tomorrow or today.

Each reminder SHALL name the comparsa, the edition's year, the planned close date and the order's
status, and link to the order. Each reminder SHALL be sent at most once per comparsa for a given
close date; when an Admin moves `ordersCloseOn`, the reminders SHALL be due again for the new date.
No reminder SHALL be sent while the orders are closed, or for a comparsa whose order is
`SUBMITTED` or `VALIDATED`. The planned date SHALL NOT close the orders (BR-10).

#### Scenario: Reminder a week before
- **WHEN** on 2031-02-03 the orders of 2031 are open, `ordersCloseOn` is 2031-02-10, and the order of "Comparsa Sintética Sur" is `DRAFT`
- **THEN** its FiringChiefs receive one reminder that the orders close on 10 February 2031 and their order is a draft

#### Scenario: Submitted order gets no reminder
- **WHEN** the reminders of 2031-02-03 are sent and the order of "Comparsa Sintética Norte" is `SUBMITTED`
- **THEN** its FiringChiefs receive no reminder

#### Scenario: Last-day reminder
- **WHEN** on 2031-02-09 the order of "Comparsa Sintética Sur" is still `DRAFT`
- **THEN** its FiringChiefs receive a second reminder that the orders close tomorrow

#### Scenario: Close date moved
- **WHEN** after the reminder of 2031-02-03 an Admin moves `ordersCloseOn` to 2031-02-17, and the order is still `DRAFT` on 2031-02-10
- **THEN** its FiringChiefs receive a reminder for 17 February 2031

#### Scenario: Orders closed
- **WHEN** `ordersCloseOn` is in 5 days but the orders are closed
- **THEN** no reminder is sent

### Requirement: Order status emails (UC-14, UC-15)
- When an Admin returns an order, the FiringChiefs of its comparsa with `ORDER_STATUS` on SHALL be
  told that the order was returned and SHALL be asked to read the reason in PolvorApp. The email
  SHALL NOT quote the reason.
- When an Admin validates an order, the same FiringChiefs SHALL be told that it was validated.
- When a FiringChief submits an order, every Admin with `ORDER_STATUS` on SHALL be told that the
  comparsa submitted it. An Admin's submission on the comparsa's behalf SHALL send nothing.

Each email SHALL name the comparsa and the edition's year and link to the order. When the order's
status has changed again before the email is sent, the email that no longer holds SHALL NOT be
sent. A rejected review or submission SHALL send nothing. A failure to send SHALL NOT affect the
order.

#### Scenario: Order returned
- **WHEN** an Admin returns the order of "Comparsa Sintética Sur" for 2031 with the reason "Check the flask of two arquebusiers"
- **THEN** its FiringChiefs receive an email saying that the order was returned, asking them to read the reason in PolvorApp, with a link to the order and without the reason's text

#### Scenario: Order validated
- **WHEN** an Admin validates the order of "Comparsa Sintética Sur"
- **THEN** its FiringChiefs receive an email saying that the order was validated

#### Scenario: Order submitted
- **WHEN** a FiringChief submits the order of "Comparsa Sintética Norte"
- **THEN** every Admin with `ORDER_STATUS` on receives an email saying that "Comparsa Sintética Norte" submitted its order, with a link to it

#### Scenario: Admin submits on behalf
- **WHEN** an Admin submits a `DRAFT` order on the comparsa's behalf
- **THEN** no email is sent

#### Scenario: Other comparsas are not told
- **WHEN** an order of "Comparsa Sintética Norte" is validated
- **THEN** the FiringChiefs of other comparsas receive no email about it

#### Scenario: Rejected review sends nothing
- **WHEN** an Admin's return is rejected with `409 Conflict`
- **THEN** no email is sent

### Requirement: Milestone reminders
A `CalendarMilestone` with `notify` on SHALL be reminded once, when its date is 0 to 7 days ahead,
to:
- every Admin with `MILESTONE_REMINDER` on, for an edition that is `DRAFT` or `IN_PROGRESS`;
- every FiringChief with `MILESTONE_REMINDER` on, for an edition that is `IN_PROGRESS` only,
  because FiringChiefs cannot see a `DRAFT` edition (BR-12).

The reminder SHALL give the milestone's title and date and the edition's year, and link to the
edition. A milestone of a `CLOSED` edition, a milestone dated before today and a milestone with
`notify` off SHALL NOT be reminded. Each milestone SHALL be reminded at most once per date; when
an Admin moves its date, the reminder SHALL be due again for the new date. A milestone added or
marked less than 7 days ahead SHALL be reminded on the next scheduled run.

#### Scenario: Reminder a week before
- **WHEN** on 2030-11-23 the edition 2031 is `IN_PROGRESS` and has the milestone "Deadline for new arquebusiers" on 2030-11-30 with `notify` on
- **THEN** every Admin and FiringChief with `MILESTONE_REMINDER` on receives a reminder with that title and date

#### Scenario: Draft edition
- **WHEN** a milestone with `notify` on belongs to a `DRAFT` edition and is 7 days ahead
- **THEN** only the Admins receive the reminder

#### Scenario: Milestone without notify
- **WHEN** a milestone with `notify` off is 3 days ahead
- **THEN** no reminder is sent

#### Scenario: Date moved after the reminder
- **WHEN** after the reminder of a milestone on 2030-11-30 an Admin moves it to 2030-12-14
- **THEN** a new reminder is sent 7 days before 14 December 2030

#### Scenario: Added late
- **WHEN** an Admin adds a milestone with `notify` on dated in 2 days
- **THEN** the reminder is sent on the next scheduled run, before the milestone's date

### Requirement: Scheduled notifications
The license digest, the planned close reminders and the milestone reminders SHALL be worked out
once a day for today's date in Europe/Madrid, and SHALL NOT be sent before 08:00 Europe/Madrid.
A run SHALL send only what is due and not yet sent, so running it again the same day, or after a
restart, SHALL send nothing twice. An operator SHALL be able to run the scheduled notifications
for today on demand from the command line, with the same rules. Event emails (orders opened or
closed, order status) SHALL be sent as soon as possible, at any hour.

#### Scenario: Running twice
- **WHEN** the scheduled notifications run twice on 2031-02-03
- **THEN** each reminder and digest due that day is sent once

#### Scenario: Not before eight
- **WHEN** PolvorApp starts at 02:00 Europe/Madrid on the first day of a month
- **THEN** no digest is sent before 08:00, and they are sent from 08:00

#### Scenario: Run on demand
- **WHEN** an operator runs the scheduled notifications from the command line at 07:00
- **THEN** what is due today and not yet sent is sent at once

### Requirement: Email content
Each notification email SHALL:
- be written in the recipient's `locale` (`es-ES`, `ca-ES-valencia` or `en`), with dates in that
  language;
- have a subject and a plain-text body, and MAY have a minimal HTML alternative without remote
  content, images or tracking;
- link only to PolvorApp pages under the configured public address, which require signing in;
- end with a line saying why the user received it and a link to their notification settings.

Notification emails SHALL contain only the edition's year, comparsa names, counts, dates, order
statuses, milestone titles and links. They SHALL NOT contain arquebusiers' names, DNI/NIE, contact
data, birth dates, weapon data, or return reasons. The address, subject and body of an email SHALL
NOT be written to logs (NFR-12).

#### Scenario: Email in Valencian
- **WHEN** an order is validated and one of its FiringChiefs has the locale `ca-ES-valencia`
- **THEN** that FiringChief's email is in Valencian, with the date written in Valencian

#### Scenario: Settings link
- **WHEN** a user receives any notification
- **THEN** the email ends by saying which kind it is and links to the user's notification settings

#### Scenario: No personal data in the digest
- **WHEN** a license digest is sent for a comparsa with an expired license
- **THEN** the email shows counts only, with no arquebusier's name or DNI/NIE

### Requirement: Notification delivery
A notification caused by a change SHALL be recorded in the same transaction as that change, so it
is recorded exactly when the change is saved. Notifications SHALL be sent in the background: the
change SHALL NOT wait for the SMTP server, and an SMTP failure SHALL NOT fail or undo it. A
delivery to a recipient that fails SHALL be retried with growing intervals for at least 24 hours,
and then given up and logged without the email's content. A recipient SHALL NOT receive the same
notification twice; the only exception is when PolvorApp stops between handing an email to the SMTP
server and recording that it was sent. Each delivery SHALL record the recipient user, the kind,
what it was about, its status, its attempts and when it was sent, but SHALL NOT store the address,
subject or body. Delivery records SHALL be deleted one year after they were created.

#### Scenario: Change rolled back
- **WHEN** an Admin's validation of an order fails and is rolled back
- **THEN** no notification is recorded and no email is sent

#### Scenario: SMTP failure is retried
- **WHEN** the SMTP server rejects an order status email and accepts it on a later attempt
- **THEN** the recipient receives the email once

#### Scenario: Giving up
- **WHEN** a delivery keeps failing for more than 24 hours
- **THEN** it is marked as failed, it is logged without the address, subject or body, and it is not tried again

#### Scenario: Restart while sending
- **WHEN** PolvorApp restarts while event emails are waiting
- **THEN** the waiting emails are sent after the restart, and those already sent are not sent again

### Requirement: Notification changes are audited
Every change of a user's notification preferences SHALL be recorded in the audit trail in the same
transaction, with the user and the kinds turned on and off. Saving unchanged preferences or a
rejected request SHALL NOT record an entry. Sending an email writes no business data and SHALL NOT
record an audit entry; the delivery record is its trace.

#### Scenario: Preference change audited
- **WHEN** a FiringChief turns `ORDER_WINDOW` off
- **THEN** one audit entry records the FiringChief and that `ORDER_WINDOW` was turned off

#### Scenario: Sending is not audited
- **WHEN** the license digest is sent to ten FiringChiefs
- **THEN** no audit entry is recorded for it

### Requirement: Notification screens
The account page SHALL have a "Notifications" section in read mode for every signed-in user. It
SHALL list each kind that applies to the user's role, with a short description of the emails it
covers and whether it is on. An edit action SHALL open a side panel (a bottom sheet on phones) with
one checkbox per kind. Saving SHALL update the section and announce "Changes saved". A failed save
SHALL show a translated reason and keep the panel's values. When the preferences cannot be loaded,
the section SHALL say so with a retry action, and the rest of the account page SHALL keep working.
The section SHALL work on a phone (NFR-01), SHALL have no automatically detectable WCAG 2.2 AA
violations in either theme (NFR-07), and every text SHALL be available in es-ES, ca-ES-valencia and
en.

#### Scenario: FiringChief turns a kind off
- **WHEN** a FiringChief opens the account page, edits the Notifications section, turns off the license digest and saves
- **THEN** the section shows the license digest as off, and "Changes saved" is announced

#### Scenario: Admin sees their kinds only
- **WHEN** an Admin opens the Notifications section
- **THEN** it lists the order status emails and the milestone reminders only

#### Scenario: Link from an email
- **WHEN** a signed-in user opens the settings link of a notification email
- **THEN** the account page opens at the Notifications section

#### Scenario: Preferences unavailable
- **WHEN** the preferences request fails
- **THEN** the section shows a translated message with a retry action, and the rest of the account page works

### Requirement: Synthetic notification data
The synthetic seed SHALL give the seeded current edition one milestone with `notify` on, dated
within the coming 7 days from the seed date, and one with `notify` off. It SHALL turn off one kind
for one seeded FiringChief. It SHALL use fixed identifiers, SHALL be safe to run again, and SHALL
contain no real names, addresses or dates of the Federation. Seeding SHALL send no email.

#### Scenario: Seeding twice
- **WHEN** the seed runs twice
- **THEN** the milestones and the preference exist once, and no email was sent

#### Scenario: Seeded reminder on demand
- **WHEN** an operator runs the scheduled notifications after seeding in the local environment
- **THEN** the mail catcher receives the reminder of the seeded milestone with `notify` on for each seeded Admin and FiringChief who has milestone reminders on
