# Spec Delta

## MODIFIED Requirements

### Requirement: Planned close reminders (BR-10)
While the orders of the current edition are open, the FiringChiefs with `ORDER_WINDOW` on of each
active comparsa assigned to them whose order is not prepared, `DRAFT` or `RETURNED` SHALL receive a
reminder:
- once when the planned `ordersCloseOn` is 2 days ahead up to the first close reminder lead time
  of the Federation settings (7 days by default);
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

#### Scenario: Longer lead time
- **WHEN** the first close reminder is set to 10 days, the planned close is 9 days ahead, and the order of "Comparsa Sintética Sur" is `DRAFT`
- **THEN** its FiringChiefs receive the first reminder that day

### Requirement: Milestone reminders
A `CalendarMilestone` with `notify` on SHALL be reminded once, when its date is 0 days ahead up to
the milestone reminder lead time of the Federation settings (7 days by default), to:
- every Admin with `MILESTONE_REMINDER` on, for an edition that is `DRAFT` or `IN_PROGRESS`;
- every FiringChief with `MILESTONE_REMINDER` on, for an edition that is `IN_PROGRESS` only,
  because FiringChiefs cannot see a `DRAFT` edition (BR-12).

The reminder SHALL give the milestone's title and date and the edition's year, and link to the
edition. A milestone of a `CLOSED` edition, a milestone dated before today and a milestone with
`notify` off SHALL NOT be reminded. Each milestone SHALL be reminded at most once per date; when
an Admin moves its date, the reminder SHALL be due again for the new date. A milestone added or
marked within the lead time SHALL be reminded on the next scheduled run.

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

#### Scenario: Shorter lead time
- **WHEN** the milestone reminder is set to 3 days and a milestone with `notify` on is 5 days ahead
- **THEN** no reminder is sent that day, and it is sent when the milestone is 3 days ahead

### Requirement: Email content
Each notification email SHALL:
- be written in the recipient's `locale` (`es-ES`, `ca-ES-valencia` or `en`), with dates in that
  language;
- have a subject and a plain-text body, and MAY have a minimal HTML alternative without remote
  content, images or tracking;
- link only to PolvorApp pages under the configured public address, which require signing in;
- be sent with the sender name of the Federation settings and the configured sender address, and
  with the reply-to address of the settings when there is one;
- end with a line saying why the user received it and a link to their notification settings, then
  the Federation's short name and, when set, its public contact email and website.

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

#### Scenario: Federation contact in the footer
- **WHEN** the Federation settings have the contact email "info@federacion.example" and a user receives a notification
- **THEN** the email ends with the settings link, the Federation's short name and "info@federacion.example"
