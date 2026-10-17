# Persistence

**Status:** Draft  
**Location:** `Infrastructure/Persistence/`: `StubbyDbContext`, `Configurations/ReceiptConfiguration.cs`, `Configurations/FieldRecordConfiguration.cs`, `Migrations/`. Application sees repository interfaces defined in Application.  
**Depends on:** 01-domain-model.

## Scope

Map the domain to PostgreSQL as described in the database design, and create the first migration.

## Behavior

- Npgsql provider. `DbSet<Receipt> Receipts`, `DbSet<FieldRecord> FieldRecords`.
- Enums stored as text (`HasConversion<string>()`). Money `HasPrecision(15, 2)`.
- `TransactionNo` is an identity column (`UseIdentityAlwaysColumn()`), so the database assigns it on insert.
- Constraints, indexes and names exactly as in the database design. No foreign key between the tables.
- Migrations apply on startup in development only. Integration tests use a separate test database.

## Acceptance criteria

- The migration creates both tables with the specified columns, types, nullability and defaults.
- Two receipts with the same `Checksum` fail on the unique index.
- Inserted receipts get increasing `TransactionNo` values starting at 1. Writing a value to it explicitly fails.
- After a receipt is deleted, the next insert gets a number higher than any used before.
- A bad status, a file size of 0 or 10 485 761, an unknown category or field name, an Approved row missing a final value, and a manual row with an extracted value each fail their check constraint.
- A total of `1234567890123.45` round-trips unchanged.
- Deleting a receipt leaves its field records.
- Enum columns hold the enum name, for example `ForReview`.
