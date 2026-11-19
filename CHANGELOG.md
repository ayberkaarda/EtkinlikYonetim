# Changelog

🇬🇧 **English** · 🇹🇷 [Türkçe](CHANGELOG.tr.md)

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Daily rolling file logs with Serilog under `%LOCALAPPDATA%\EtkinlikYonetim\logs`: files older than 30 days are
  deleted and at most 30 files are kept (tests check this with dated files). Error entries keep only exception
  types, HResult codes and method names.
- Anonymous audit entries: a fixed code and a role only, never an account. Every failed sign-in (unknown account,
  wrong password, locked account) writes the same code, `auth.sign_in.failed`.
- Central FluentValidation validators for registration, sign-in, the first admin, participants, events,
  registrations and cancellations; the rules and the Turkish messages are unchanged.
- More Moq and SQLite tests: capacity limits, past dates, cancelling and registering again, IDOR answers, the
  lockout duration and the national id checksum, and tests that the log files carry no personal data or keys.
- English and Turkish README with an architecture diagram, threat model (`docs/THREAT_MODEL.md`) and this
  changelog.
- Release job in CI: on `v*` tags the tag name is checked, a win-x64 build is published and uploaded as a zip
  artifact.

### Changed

- Errors are written to the log file instead of the trace output; the error dialogs are unchanged.

### Security

- The setup key's expiry is decided by the database's clock at the moment the key is used: the final `UPDATE`
  requires `SonKullanma > SYSUTCDATETIME()` and stamps `TuketimTarihi` with the database's time, and the selection
  of usable keys no longer depends on the computer's clock. A key that expires while a setup waits, or that only a
  clock running behind would accept, is refused and stays unused.
- Names, e-mail addresses, national ids, passwords, setup keys and exception messages are never written to the log.

## Foundation

The rewrite of the original application, merged before this changelog was started.

### Added

- Layered .NET 10 solution (Domain, Business, Data, UI) with EF Core, dependency injection and automatic
  migrations; the original database is upgraded in place, keeping its data, and a participant's single event
  becomes a registration with a ticket.
- Registrations with ticket codes, cancellation, capacity and duplicate registration checks inside the database
  transaction.
- First-run setup for an empty database; for a database with accounts but no admin, a single use setup key that
  only the database owner issues with `docs/issue-setup-key.sql`, and example rights for the application's login
  in `docs/grant-app-login.sql`.
- Optional demo mode (`ETKINLIK_DEMO`, `Seed:Demo`), off by default.
- Tests for the services on SQLite and for races, setup keys and the legacy upgrade on SQL Server; CI with a SQL
  Server job.

### Security

- PBKDF2 password hashes with an upgrade of plain text passwords on the next sign-in; lockout after 5 failed
  attempts for 15 minutes with the same answer for every failure.
- Role and owner checks in every service; national ids unique per account and masked in admin lists.
- No account with a known password in a default installation.
