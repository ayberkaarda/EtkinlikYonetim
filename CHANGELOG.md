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
- Waiting list: a full event puts the participant on its waiting list with a queue number; a cancelled seat goes at
  once to the first one waiting, with a ticket (migration `AddWaitingList`).
- Event management for admins: change the date, capacity (not below the seats held) and description of an event
  that has not passed, or cancel it; a cancelled event stays listed and its registrations are shown to their
  accounts as cancelled (migration `AddEventManagement`).
- Tickets with QR codes (QRCoder 1.6.0, MIT) that the participant's account can show again, and an entrance check-in
  window for admins with "already used" on a second scan and a counter (migration `AddTicketCheckIn`).
- CSV and printable HTML export of an event's participant and check-in list, without ticket codes.
- Account management for admins: enable, disable, make admin, make user; the own account and the last enabled
  admin are protected (migration `AddAccountDisabledFlag`).
- SQL Server race tests for the waiting list, capacity changes, event cancellation, simultaneous check-ins and
  admins demoting each other.

### Changed

- Errors are written to the log file instead of the trace output; the error dialogs are unchanged.
- Ticket codes are now `ETK-` and 26 characters (130 random bits); the migration gives every existing ticket a new
  code of 128 random bits, so codes shown before the upgrade stop working.
- The admin participant list shows the state of each registration (checked in, waiting, cancelled) instead of its
  ticket code, and an admin sees ticket codes only of participants it added.
- Refused operations no longer write an "Operation refused" line to the log file.
- A change that waits more than 10 seconds for another change of the same event or of the accounts is refused with
  "nothing was changed, try again in a few seconds" instead of a message about the server. Statements get 60
  seconds before the client cancels them, and the start-up (legacy upgrade and migrations) five minutes, so the
  client never gives up before the lock's own wait.

### Security

- The setup key's expiry is decided by the database's clock at the moment the key is used: the final `UPDATE`
  requires `SonKullanma > SYSUTCDATETIME()` and stamps `TuketimTarihi` with the database's time, and the selection
  of usable keys no longer depends on the computer's clock. A key that expires while a setup waits, or that only a
  clock running behind would accept, is refused and stays unused.
- Names, e-mail addresses, national ids, passwords, setup keys and exception messages are never written to the log.
- Open sessions are checked against the database on every operation: a demoted, disabled or deleted account can no
  longer act from a window that is still open, and disabled accounts cannot sign in. Every change of an account's
  access raises its session version (migration `AddSessionVersion`), so a window opened before the account was
  disabled or demoted stays closed after it is enabled or promoted again; the account signs in anew.
- Ticket codes are found by their SHA-256 and compared in constant time; they are never logged or exported.
- The log file writes one neutral code for every outcome of a sign-up, a registration, a cancellation and a scan at
  the entrance, so it no longer tells which accounts, registrations or tickets exist.
- Dates and times come from the database's clock: which events are past, whether a registration can still be made
  or cancelled, sign-in locks, and the times written for registrations, tickets, cancellations and check-ins. A
  computer whose clock is wrong, or was set back on purpose, can no longer register for a past event, cancel its
  registrations, end a sign-in lock early or write false check-in times.
- A registration whose ticket was scanned at the entrance can no longer be cancelled ("Giriş yapılmış bir kayıt
  iptal edilemez."), and the check-in takes the event's lock: a participant who is inside keeps the seat instead of
  it going to the next one waiting, so the room never holds more people than seats.

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
