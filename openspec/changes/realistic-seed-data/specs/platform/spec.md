# Spec Delta

## MODIFIED Requirements

### Requirement: Guarded synthetic seed
The platform SHALL provide a single command that populates a database with synthetic,
deterministic data produced by the registered seeders of each module (SEC-11). The command SHALL
run only in the Development, Staging (synthetic data only, NFR-13) and Testing environments and
SHALL refuse any other environment, including Production and unknown names. Seeders SHALL NOT read
from files outside the repository's synthetic-data sources.

The command SHALL offer two datasets, chosen by a seed setting:
- *scenarios*, the default: the fixed cases each module's seed requirement lists, at their current
  size;
- *full*: the scenarios plus a realistic population at the festival's scale.

An unknown dataset name SHALL be refused before anything is written. Running the full dataset on a
database seeded with the scenarios SHALL add the population and leave the scenario rows as they
are.

#### Scenario: Seeding in Development
- **WHEN** the seed command runs in the Development environment
- **THEN** every registered seeder runs and the command exits successfully

#### Scenario: Seeding in Production is blocked
- **WHEN** the seed command runs with the environment set to Production
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal

#### Scenario: Unknown environment is blocked
- **WHEN** the seed command runs with the environment set to a name outside the allowed list, such as `Prod`
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal

#### Scenario: Deterministic output
- **WHEN** the seed command runs twice against two empty databases with the same dataset
- **THEN** both databases contain identical data

#### Scenario: Scenarios dataset by default
- **WHEN** the seed command runs without a dataset setting
- **THEN** only the scenario data is created

#### Scenario: Full dataset
- **WHEN** the seed command runs with the full dataset
- **THEN** the scenario data and the realistic population are created

#### Scenario: Full dataset after the scenarios
- **WHEN** the seed command runs with the full dataset on a database already seeded with the scenarios
- **THEN** the population is added and the scenario rows are unchanged

#### Scenario: Unknown dataset is refused
- **WHEN** the seed command runs with the dataset set to `huge`
- **THEN** it exits with a non-zero code without writing to the database and logs the refusal
