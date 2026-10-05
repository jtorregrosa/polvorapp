# Spec Delta

## ADDED Requirements

### Requirement: Charts
The composite layer SHALL offer line and bar charts (grouped, stacked and 100 % stacked) for
feature screens, built on the design tokens. Every chart SHALL:
- have a visible heading and a text summary, and SHALL offer the same data as a table with labelled
  rows and columns, so no figure is available only in the drawing (WCAG 1.1.1, 1.4.1);
- tell series apart by more than colour: a legend with the series names, and direct labels or
  distinct markers or patterns, and SHALL keep series colours from categorical chart tokens that
  reach 3:1 against the surface in both themes (WCAG 1.4.11);
- show a tooltip with the exact values on pointer hover and on keyboard focus of a data point, and
  SHALL let keyboard users move between data points;
- format numbers, percentages and years in the user's language;
- mark provisional values (for example with a hatched or outlined mark and the word "provisional"
  in the tooltip and the table);
- follow "Motion": at most a 200 ms fade on first render, none when the user asks for reduced
  motion, and no animation when the data changes;
- fit its container down to a 360 px screen without horizontal scrolling of the page.

#### Scenario: Chart with its table
- **WHEN** a screen reader user reaches a chart of active arquebusiers per edition
- **THEN** they hear its heading and summary, and can open the table with every value of the chart

#### Scenario: Keyboard tooltip
- **WHEN** a keyboard user focuses a chart and moves with the arrow keys to the 2026 point
- **THEN** a tooltip shows 2026 with its values, formatted in the user's language

#### Scenario: Series not by colour alone
- **WHEN** a stacked bar chart shows owned, rental, loan and none
- **THEN** a legend names each series, and each series is told apart by its pattern or label as well as its colour

#### Scenario: Chart contrast
- **WHEN** the chart series tokens are checked against the card surface in the light and dark themes
- **THEN** each reaches at least 3:1
