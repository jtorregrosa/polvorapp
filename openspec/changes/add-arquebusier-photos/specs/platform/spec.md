# Spec Delta

## ADDED Requirements

### Requirement: Private object storage
The API SHALL keep files in an S3-compatible object storage (ADR-0005): MinIO in the local
environment, and any S3-compatible provider in an EU region elsewhere. Its endpoint, bucket and
credentials SHALL come from the environment and SHALL be validated at startup. A missing or invalid
setting SHALL stop the API, naming the setting but not its value. The bucket SHALL be private: no
object SHALL be readable without the API's credentials, and the API SHALL never hand storage
addresses or credentials to the browser. The migrate command SHALL create the bucket when it does
not exist, and SHALL leave an existing bucket unchanged. Each module SHALL keep its files under its
own name prefix, and SHALL log stored names and sizes only, never file contents or personal data.
The local environment SHALL start the storage with the other services and SHALL configure the API
for it from `.env.example`.

#### Scenario: Missing storage setting
- **WHEN** the API starts without the storage bucket setting
- **THEN** the process exits with a non-zero code and logs which setting is missing, without any credential value

#### Scenario: Bucket created by migrate
- **WHEN** the migrate command runs against a storage without the configured bucket
- **THEN** the bucket exists afterwards, and running the command again succeeds without changing it

#### Scenario: Anonymous access to the bucket
- **WHEN** a client without credentials requests an object directly from the storage
- **THEN** the storage refuses the request

### Requirement: Stored file cleanup
The API SHALL periodically erase stored files that no record references, so files left behind by a
failed erasure or a failed write never stay longer than 24 hours (BR-14, SEC-08). Each module SHALL
declare which of its stored files are referenced. The cleanup SHALL keep files written less than
one hour earlier, so a write in progress is never erased. When it cannot read the references, it
SHALL erase nothing. Each run SHALL log how many files it erased, without their contents or the
personal data they belong to.

#### Scenario: Orphan file erased
- **WHEN** the cleanup runs and finds a file that is more than one hour old and that no record references
- **THEN** the file is erased and the run logs the count

#### Scenario: References unavailable
- **WHEN** the cleanup runs while the database is unreachable
- **THEN** no file is erased

## MODIFIED Requirements

### Requirement: Health endpoints
The API SHALL expose an anonymous liveness endpoint at `GET /api/health/live` and an anonymous
readiness endpoint at `GET /api/health/ready`. Liveness SHALL report healthy whenever the process
can serve requests, without checking dependencies. Readiness SHALL report healthy only when the
database is reachable and the object storage bucket is reachable. Neither response SHALL contain
connection strings, host names, bucket names, exception messages or personal data.

#### Scenario: API and database are up
- **WHEN** a client calls `GET /api/health/ready` and the database accepts connections and the storage bucket is reachable
- **THEN** the API responds `200 OK` with status `Healthy`

#### Scenario: Database is unreachable
- **WHEN** a client calls `GET /api/health/ready` and the database does not accept connections
- **THEN** the API responds `503 Service Unavailable` with status `Unhealthy`
- **AND** the body names the failing check (`database`) without exception details or connection information

#### Scenario: Storage is unreachable
- **WHEN** a client calls `GET /api/health/ready` and the storage bucket cannot be reached
- **THEN** the API responds `503 Service Unavailable` with status `Unhealthy`
- **AND** the body names the failing check (`storage`) without exception details, host or bucket names

#### Scenario: Liveness ignores dependencies
- **WHEN** a client calls `GET /api/health/live` while the database or the storage is unreachable
- **THEN** the API responds `200 OK`
