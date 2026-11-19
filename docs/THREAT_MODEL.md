# Threat Model

A short threat model for the Etkinlik Yönetim desktop application. The application stores accounts with
passwords, and participants with a Turkish national identity number (T.C. Kimlik No), a full name and an e-mail
address, together with the events they are registered for. Under the Turkish Personal Data Protection Law (KVKK,
Law No. 6698) the national id, the name and the e-mail address are personal data. This document lists technical
measures only; it is not a legal assessment.

## Scope and assets

- **Application:** a Windows Forms client with user and admin sign-in that talks directly to SQL Server.
- **Assets:**
  - National ids, full names and e-mail addresses of participants, and their registrations, waiting places and
    ticket codes (a ticket code alone admits its holder at the entrance).
  - Exported participant lists (CSV and HTML files chosen by an admin).
  - Passwords of accounts and their stored hashes; sign-in counters and lockout times.
  - Setup keys issued by the database owner (`KurulumAnahtarlari`, stored as SHA-256 hashes).
  - The connection string (server, database, and possibly SQL credentials).
  - Log files under `%LOCALAPPDATA%\EtkinlikYonetim\logs`.
  - Database backups made by the operator (`.bak` files).
- **Trust boundaries:** the Windows user session that runs the application; the network link to SQL Server; the
  local file system (settings, logs, backups); the boundary between a signed-in account and the data of other
  accounts; the boundary between the application's SQL login and the database owner.

## Assumptions

- The application runs on trusted workstations; people sign in on them with their own accounts.
- Every instance connects with the same SQL Server login, so the database itself does not tell accounts apart:
  roles and ownership are enforced by the application.
- The database owner (db_owner or sysadmin) is a different, more trusted principal than the application's login,
  and is the only one who creates schema and setup keys.
- The operator is the data controller and is responsible for consent, retention, deletion requests and the
  security of the server, its logins and its backups.
- A new database contains no account. Demo accounts (fictional, passwords published in the README) are created
  only when demo mode is switched on explicitly with `ETKINLIK_DEMO=1` or `Seed:Demo=true`, and only in a
  completely empty database; demo mode is never used with real data.

## Identity and authentication

| Threat | Mitigation | Status |
|---|---|---|
| Passwords read from the database or a backup | PBKDF2-HMAC-SHA256 with 600,000 iterations and a 16 byte random salt per password; compared in constant time | Mitigated |
| Plain text passwords of the original application | The legacy upgrade marks them as legacy plain text in their own format column; the first successful sign-in replaces each with a PBKDF2 hash in the same conditional update | Partly mitigated (see residual risks) |
| A stored value read in the wrong format | The format comes from the format column, never from the shape of the value | Mitigated |
| Online password guessing | 5 failed attempts lock the account for 15 minutes; the counter is increased in one atomic `UPDATE`, attempts during a lock are neither counted nor extend it, and a success is written only while the account is still unlocked and unchanged | Mitigated |
| Account enumeration through sign-in answers or timing | Unknown accounts, wrong passwords, locked and disabled accounts get the same message, and each costs one key derivation | Mitigated |
| A demoted, disabled or deleted account keeps working in a window that is still open | Every service call of a session reads the account's role and enabled state from the database first (ISessionGuard) and refuses a session whose account is gone, disabled or has another role; the window then returns to the sign-in. Tested as DemotedAdminSession_CannotManageEvents with mocks and on SQLite | Mitigated |
| A window left open on a compromised computer starts working again once its account is enabled or promoted back | Every change of an account's access (disable, enable, role change) raises the account's session version in the same `UPDATE`; a session carries the version it signed in with and is refused once they differ, and a sign-in is written only while the version it read is unchanged. Tested on SQLite with an account disabled and enabled, and demoted and promoted, again (migration `AddSessionVersion`) | Mitigated |
| An account disabled, demoted or changed while one of its writes waits for a lock, so the write commits after the session check passed | Every write carries the acting account (id, role, session version) and checks it again inside its own transaction, after its resource lock and before it writes: event create, change and cancel, participant add, registration and its cancellation, check-in and account changes. On SQL Server the check is a `HOLDLOCK` read of the account row, held until the commit, so a concurrent change of that account waits for the write to finish. A revoked actor writes nothing and the session ends. Lock order is always the resource lock first (event or accounts application lock), then the account row; an account change never waits for an event lock, so the two cannot wait for each other, and a deadlock victim (error 1205) is reported with the busy message, nothing written. Tested on SQL Server with an admin disabled while an event change waits for the event lock and a user disabled while a registration waits, and on SQLite for every write | Mitigated |
| Lockout used to block a known account (denial of service) | The lock ends by itself after 15 minutes; no permanent lock exists | Accepted |
| Accounts with published passwords in a default installation | The migrations insert no rows; demo data is added only in demo mode and only to an empty database | Mitigated (see residual risks for interim databases) |

## Roles and ownership

| Threat | Mitigation | Status |
|---|---|---|
| A user creating events or listing the participants of an event | These calls require the admin role in the service; a denied call touches no repository | Mitigated |
| IDOR: a user registering, listing or cancelling another account's participants by national id or registration id | Every call takes the session issued by the authentication service; a participant belongs to the account that added it; lookups and lists are filtered by the session's own id; cancellation repeats owner, status and date in the `UPDATE`. A missing record and somebody else's record get the same answer, so ids and national ids cannot be probed | Mitigated |
| Self-promotion to admin | Sign-up always creates the user role; the first admin is created only on an empty database, or with a setup key from the database owner while no admin exists, atomically under a table lock. Only an admin can change roles, never their own; a user's call is refused before the database is asked | Mitigated |
| Two admins removing each other, or the last admin disabled | Account changes run under one application lock (EtkinlikYonetim.Accounts); inside the writing transaction the acting admin must still be an enabled admin with the session version of its session (so an admin disabled and enabled again meanwhile is refused) and another enabled admin must remain. Tested with two instances demoting or disabling each other at once on SQL Server | Mitigated |
| Overbooking or double registration through concurrent requests | Seat count checked and increased in the same transaction under an application lock per event; unique index on the seats and waiting places of a participant per event; tested with races on SQL Server | Mitigated |
| Passing the waiting list, or a freed seat left empty while someone waits | A new registration takes a seat only while nobody waits; a freed or added seat goes to the lowest queue number in the transaction that frees it; queue numbers are unique per event. Tested with a cancellation racing a registration, two cancellations and two registrations on a full event | Mitigated |
| Lost update: an admin's form loaded before another admin's change writes the old date, capacity or description back | Every change and cancellation of an event raises its edit version (`DuzenlemeSurumu`, migration `AddEventEditVersion`); the form sends the version it loaded and the `UPDATE` requires it. When it differs nothing is written, the admin is told the event was changed meanwhile and the form shows the current values. A version, not the values, is compared: on the database's case-insensitive collation a change of only the case of the description would otherwise compare equal and be overwritten. Tested on SQLite with two admins and on SQL Server (`Turkish_CI_AS`) with a case-only change from "Salon" to "SALON" | Mitigated |
| Lowering the capacity below the seats held, or changing a past or cancelled event | The UPDATE itself requires the event to be open and the seats held not above the new capacity; the check constraint repeats the limit. Tested with a capacity change racing a registration | Mitigated |

## Dates and times

| Threat | Mitigation | Status |
|---|---|---|
| A computer clock set back (or running wrong) to register for, or cancel registrations of, a past event, or to change or cancel a past event | "Today" is the database's clock (`SYSUTCDATETIME()`), turned into a calendar day in the computer's time zone; the conditional `UPDATE`s repeat the date check with that day. Tested on SQL Server with client clocks days behind and ahead | Mitigated |
| A write that waited for a lock across midnight acting for a day that has passed | Registration, registration cancellation, event change and cancellation, and check-in read the database's time on their own connection inside the writing transaction, after their locks, and decide the day and stamp the rows with it; the services' earlier reading only gives a quick answer. Tested on SQLite with the day ending right before each write, and on SQL Server with a registration stamped after waiting for the event lock | Mitigated |
| The event management window locking current events, or offering past ones, because the computer's clock is wrong | The window asks the service for the database's day and decides which events can be changed with it; the computer's clock plays no part. Tested with the database's day far from the computer's clock in both directions | Mitigated |
| Forged registration, ticket, cancellation or check-in times | Every stored time is the database's UTC time, read in the transaction that writes it; the computer's clock plays no part | Mitigated |
| A sign-in lock ended early by a clock running ahead | The lock end is written and compared with the database's clock | Mitigated |

## National id data

| Threat | Mitigation | Status |
|---|---|---|
| National ids shown in admin lists | Only the masked form (`123******01`) is shown | Mitigated |
| Disclosing whether another account has a participant with a given national id | National ids are unique per owning account, so adding one checks only the account's own list | Mitigated |
| Fake or malformed national ids | Adding a participant checks the 11 digit format and the checksum; lookups of rows from the original application check the format only | Mitigated |
| National ids written to logs | Never logged; see Logging | Mitigated |

## Tickets and the entrance

| Threat | Mitigation | Status |
|---|---|---|
| Guessing or deriving a ticket code | 130 bits from the operating system's cryptographic generator per ticket; the migration reissued the older 50 and 40 bit codes with 128 bits from CRYPT_GEN_RANDOM | Mitigated |
| Timing of the code comparison | The database finds the ticket by the SHA-256 of the code (unique index), so its timing depends on the hash only; the stored code is then compared with the scanned one with CryptographicOperations.FixedTimeEquals | Mitigated |
| Stored codes read from the database | **Decision:** the code is stored as it is, next to its hash, because the participant's account must show the QR code again; a hash alone cannot, and encryption with a key on every client would protect nothing the database permissions do not. Whoever can read the ticket table can use its codes, as with the accepted limit below | Accepted |
| A ticket used twice, also at two entrances at once | A conditional UPDATE sets the check-in time only while it is empty, for a seat of the chosen, not cancelled event; the second scan is told "already used". Tested with simultaneous scans from two instances on SQL Server | Mitigated |
| A participant cancelled after being let in, so the seat goes to the next one waiting and the room holds more people than seats | The cancellation `UPDATE` requires an unused ticket, and the service refuses a checked-in registration with its own message; the check-in runs under the same per-event application lock as cancellations, so the two are decided one after the other (also under `READ_COMMITTED_SNAPSHOT`). Tested with a check-in racing a cancellation on SQL Server | Mitigated |
| Tickets of cancelled registrations or events, or of another event, admitted | The lookup and the UPDATE both require an active seat of the chosen event; all such codes get the same refusal as unknown ones | Mitigated |
| A ticket used up by a scan on the wrong day (a test scan, the wrong event chosen, or the event moved to another day while the scan waited for the event's lock) | A valid ticket is checked in only on its event's day by the database's clock. The day is decided again under the event's lock: the check-in reads the database's time and the event's date there, writes nothing on another day, and its `UPDATE` requires the event to be on that day. The entrance then says it is not the event's day and the ticket stays valid. Tested on SQLite and on SQL Server with the event moved to tomorrow while the check-in waits for the lock | Mitigated |
| A ticket code passed on to somebody else (the code is a bearer token) | The entrance shows the participant's name with every check-in, so the staff can compare it with an identity document; whether they do is an operating decision | Partly mitigated |
| A cancelled ticket shown as valid in an old window | The ticket window reads the registration again before it shows a QR code; the entrance rejects such codes anyway | Mitigated |
| Ticket codes seen by others | Only the participant's own account sees its codes (an admin only those of participants it added); admin lists, exports and logs never contain them | Mitigated |

## Exports

| Threat | Mitigation | Status |
|---|---|---|
| Formula injection when a CSV is opened in a spreadsheet | Every field is quoted and a field starting with =, +, -, @, a tab or a carriage return gets a leading apostrophe | Mitigated |
| Script or markup injection in the HTML list | No script; every value is HTML encoded; a Content-Security-Policy allows nothing but the page's own style block, pinned by its SHA-256 | Mitigated |
| Exported files exposing more than needed | National ids masked, no ticket codes, file names without event text; only admins can export, and each export is audited without content. Once saved, the file is the operator's responsibility | Mitigated (residual) |
| A half written file replacing a good one | Files are written to a temporary file next to the target, flushed and then moved over it | Mitigated |

## Setup key and the accepted limit

| Threat | Mitigation | Status |
|---|---|---|
| A client creating its own authority to become the first admin | The application has no way to create a key. Only the database owner runs `docs/issue-setup-key.sql`, which takes 16 random bytes from `CRYPT_GEN_RANDOM`, stores only the SHA-256 hash and shows the key once; the script refuses when an admin exists | Mitigated |
| The application's login forging, extending or deleting keys | `docs/grant-app-login.sql` grants `SELECT` on the key table and `UPDATE` only of `Tuketildi` and `TuketimTarihi`, and denies `INSERT`, `DELETE` and `UPDATE` of the id, hash, issue time and expiry; tested with that login on SQL Server | Mitigated |
| Guessing a key | 128-bit keys; every refused key is followed by a fixed 1 second pause; all stored keys are compared in constant time. Wrong attempts are not counted, so nobody can revoke the founder's key by guessing | Mitigated |
| Reusing a key | A key is used up by a conditional `UPDATE` in the same serializable transaction that writes the admin; a failed write rolls the use back; unused keys are refused once an admin exists | Mitigated |
| Using a key after its 24 hours | The expiry is decided by the database's clock (`SonKullanma > SYSUTCDATETIME()`) in the final `UPDATE`, and the use time is written with the same clock; a key that expires while a setup waits for locks, or that only a client clock running behind would accept, is refused. Tested on SQL Server with a pause before the final `UPDATE` and with a client clock far behind | Mitigated |
| Keys in logs or settings | The key is never logged or stored by the application; `ETKINLIK_SETUP_KEY` only prefills the field and a malformed value is not shown | Mitigated |
| **Accepted limit:** whoever holds the application login's credentials | That login may read and write the account, event, participant, registration and ticket tables, read the setup key hashes and mark keys used. Anyone with the connection string can therefore read all personal data, change rows directly, or spend an unused key without creating an admin; they cannot create or extend a key. Protect the connection string and grant the login nothing more than `docs/grant-app-login.sql` | Accepted |

## Logging

| Threat | Mitigation | Status |
|---|---|---|
| Personal data written to log files | Error entries contain only exception types, HResult codes and method names, without messages or file paths; refused operations write nothing of their own; audit entries are a fixed code and a role, with no account id, user name, participant name, e-mail, national id, password, setup key, ticket code or event detail. Tests run real service calls and check the files | Mitigated |
| Lock state or account existence disclosed through the log | Every failed sign-in writes the same `auth.sign_in.failed` entry, whatever the reason | Mitigated |
| Existence of an account, a participant, a registration or a ticket disclosed through the log | The outcomes of a sign-up, of a registration, of a cancellation and of a scan at the entrance each share one neutral code in the file (`auth.account.registration_submitted`, `registration.submitted`, `registration.cancel_submitted`, `ticket.check_in_submitted`) and a promotion from the waiting list is not written; tested by comparing the files of each outcome | Mitigated |
| Log files read by other Windows users | Logs are under the user's own local application data folder | Mitigated by OS permissions |
| Log files read by another application account on the same Windows account | The files hold only anonymous codes, roles and exception types; counts and times of events remain visible | Mitigated (residual) |
| Log files growing without bound | Daily rolling files; files dated more than 30 days ago and any beyond the newest 30 are deleted. Tests create dated files and check which remain | Mitigated |
| Personal data in error dialogs | Users see fixed Turkish messages; raw exception text is never shown | Mitigated |

## Backups

| Threat | Mitigation | Status |
|---|---|---|
| Backups exposing personal data and legacy plain text passwords | `*.bak`, `*.mdf` and `*.ldf` are git-ignored. Backups taken before the upgrade, or while legacy accounts remain, contain plain text passwords: encrypt them (SQL Server backup encryption or an encrypted volume), restrict access and delete old ones | Documented |
| Legacy upgrade altering or exposing existing rows | The upgrade runs in one transaction under a checked `sp_getapplock` lock and refuses before any change when rows break the new rules | Mitigated |

## Connection security

| Threat | Mitigation | Status |
|---|---|---|
| Connection string leaked through the repository | `appsettings.json` is git-ignored; user secrets and the `ETKINLIK_CONNECTION` environment variable are supported; only `appsettings.example.json` is committed | Mitigated |
| Interception of the database connection | `TrustServerCertificate=True` in the example is for a local development server only; for any other server use `Encrypt=True` with a certificate the client trusts and leave `TrustServerCertificate` off | Documented |
| SQL injection | All access goes through EF Core with parameters; the raw statements are constant lock and schema statements, and the setup key `UPDATE` is parameterised | Mitigated |
| Excessive rights of the application's login | Use `docs/grant-app-login.sql`: no DDL, no `DELETE`, restricted key table rights. Do the first start and every migration with the owner's connection; prefer Windows authentication | Documented |

## KVKK

- Personal data (national id, name, e-mail) is kept only in the configured SQL Server database, shown masked where
  the full value is not needed, and never written to log files.
- The application has no retention or deletion feature; cancelled registrations stay as history. The operator
  deletes data in the database when required.

## Residual risks

- Accounts migrated from the original application keep their plain text password until their next successful
  sign-in; an operator can force the change by resetting those passwords.
- Databases set up with interim versions may contain the sample accounts `admin` and `ornek` with published
  passwords; the operator deletes them or changes their passwords.
- There is no password change or reset screen.
- Reads (lists, the participant export, the ticket window) check the session when they start; a read already
  running when its account is disabled still returns its result once. Writes check the account again inside their
  transaction (see Identity and authentication).
- The edit version tells changes made through the application apart. A change written directly in the database
  that does not raise `DuzenlemeSurumu` is not noticed by a form loaded before it.
- The calendar day is the database's time in the computer's time zone; a computer with a wrong time zone (not a
  wrong clock) still sees the day change some hours early or late.
- On a Windows account shared by several application users, each of them can read when sign-ins and registrations
  happened from the log file, though not by whom.

## Out of scope

- Network services, web access, multi-factor authentication, encryption of data at rest inside the application,
  automated backups and retention policies, and a per-user audit trail of who read which record.
