# Spec Delta

## MODIFIED Requirements

### Requirement: Data tables
Tabular lists SHALL support sorting by column, pagination with a translated summary, a loading
state and an empty state with a translated message. A row MAY show a second line with secondary
data. When a row leads to a record, the row's name SHALL be a link, and pointing or tapping anywhere
on the row SHALL open the record too. On screens narrower than the phone breakpoint, each row SHALL
be shown as a stacked item with the name, the identifier and the main status, and the page SHALL
NOT scroll horizontally. On wider screens, a table wider than its container SHALL scroll inside the
container. Row hover SHALL change the background at once, without a transition.

A table MAY offer row selection. When it does:
- each row, and each stacked item on phones, SHALL have a checkbox labelled with the row's name;
- the header SHALL have a checkbox that selects or clears every row of the current page, shown as
  mixed when only some of them are selected;
- the selection SHALL be kept across pages, sorting and filtering, and SHALL be owned by the
  screen, which can set, read and clear it;
- the number of selected rows SHALL be announced to screen readers when it changes;
- choosing a checkbox SHALL NOT open the row's record, and a selected row SHALL be shown as
  selected by more than colour alone.

#### Scenario: Empty list
- **WHEN** a table has no rows
- **THEN** a translated empty-state message is shown instead of an empty grid

#### Scenario: Narrow screen
- **WHEN** a table wider than its container is shown on a screen between the phone breakpoint and the table's width
- **THEN** the table scrolls horizontally inside its container and the page does not

#### Scenario: Stacked rows on a phone
- **WHEN** the arquebusiers list is shown on a 360 px screen
- **THEN** each arquebusier is a stacked item with their name, nationalId, comparsa and license status, and the page does not scroll horizontally

#### Scenario: Whole row opens the record
- **WHEN** a user clicks the comparsa cell of an arquebusier row
- **THEN** that arquebusier's detail page opens

#### Scenario: Keyboard opens the record
- **WHEN** a keyboard user tabs to a row and activates its name link
- **THEN** that arquebusier's detail page opens, and the row adds no extra tab stop

#### Scenario: Selecting a row does not open it
- **WHEN** a user clicks the checkbox of a row in a table with row selection
- **THEN** the row is selected, the record does not open, and the selected count is announced

#### Scenario: Select the page
- **WHEN** a user ticks the header checkbox on a page of 20 rows where 3 were already selected
- **THEN** all 20 rows of the page are selected, rows selected on other pages stay selected, and ticking it again clears the 20 rows of the page only

#### Scenario: Selection on a phone
- **WHEN** a table with row selection is shown on a 360 px screen
- **THEN** each stacked item has its labelled checkbox, and the page does not scroll horizontally
