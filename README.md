# Todo Dock

A simple daily to-do list for the PowerToys Command Palette **Dock**: a colored light (🔴 overdue, 🟠 pending today, 🟢 all done, ⚪ nothing today) plus today's progress (`3/7 · 43%`).

> Unofficial community project. Not affiliated with or endorsed by Microsoft.

<!-- TODO: docs/dock.png -->

## Features

- **Status light + progress** in the Dock, with a tooltip line for the most urgent task ("Overdue: pay bill (+2 more)", "Next: call Ann at 17:00").
- **Quick add** from one text box: type `pay bill tomorrow 17:00`, press Enter. English and Turkish words.
- **Recurring tasks:** every day, every weekday, every week / every Monday.
- **Grouped list:** Overdue, Today, Upcoming, Done today.
- **One-key actions:** Enter completes, context menu postpones, edits or deletes. One-level **Undo**.
- **Local only:** a JSON file on your machine, no network.

## Quick add

Type into the search box of the Todo Dock page. The first row, **Add: `<title>`**, previews the parsed date, time and recurrence in its subtitle ("Tomorrow · 17:00 · every day") before you press Enter.

| What | English | Turkish |
|---|---|---|
| Today | `today` | `bugun` (`bugün`) |
| Tomorrow | `tomorrow`, `tmr` | `yarin` (`yarın`) |
| Next week (+7 days) | `next week` | `haftaya` |
| In N days | `in 3 days` | `3 gun sonra` (`3 gün sonra`) |
| Weekday | `monday` ... `sunday` | `pazartesi`, `sali`, `carsamba`, `persembe`, `cuma`, `cumartesi`, `pazar` |
| Date with month name | `oct 12`, `12 oct`, `october 12 2027` | `12 ekim`, `12 ekim 2027` |
| ISO date | `2026-10-12` | same |
| Time | `17:00`, `17.00`, `9:30`, `5pm`, `5:30 pm`, `at 17:00` | `17:00`, `17.00`, `saat 17:00`, `saat 17` |
| Every day | `every day` | `her gun` (`her gün`) |
| Every weekday (Mon-Fri) | `every weekday`, `weekdays` | `hafta ici` (`hafta içi`), `hafta ici her gun` |
| Every week | `every week` | `her hafta` |
| Every `<weekday>` | `every friday` | `her cuma` |

Month names accept full and 3-letter English forms (`jan`, `sep`, `oct`) and the Turkish names (`ocak`, `subat`, `mart`, `nisan`, `mayis`, `haziran`, `temmuz`, `agustos`, `eylul`, `ekim`, `kasim`, `aralik`). Turkish letters are folded to ASCII, so `Salı`, `Çarşamba` and `Şubat` work too. Matching is case-insensitive.

Rules:

- **No date means today.** A task with only a date (no time) becomes overdue at midnight, once that day has passed. A task with a time is overdue as soon as that time passes.
- **A bare weekday** is the next such day; if it is today's weekday, it means **next week** (7 days ahead).
- **`10.10` and `17.00` are times** (hour `HH.MM` with a two-digit hour, 24 h), not dates. Write dates with a month name (`oct 10`) or ISO (`2026-10-10`). A bare number like `17` is not a time (except after `saat`).
- **Month-day without a year** means its next occurrence on or after today. Add a year (`2000`-`2999`) to pin it; an explicit past date is accepted and shows as overdue.
- **`every <weekday>`** starts on the next such day (today, if today is that weekday) and keeps that weekday when you complete or postpone.
- **Where the words go:** if the text starts with date/time words, only that leading run is parsed and the rest is the title as typed (`tomorrow 17:00 call mum`). Otherwise the words can be anywhere (`call mum tomorrow`) and are removed from the title.
- A text with no title left (only date words) is not added.

## Using it

Click the Todo Dock item in the Dock (or search **Todo Dock** in Command Palette) to open the list:

| Group | Contents |
|---|---|
| 🔴 **Overdue** | Open tasks past their date or time, with an "Overdue" tag |
| 🟠 **Today** | Open tasks due today (timed ones first) |
| ⚪ **Upcoming** | Open tasks due later |
| ✅ **Done today** | Tasks you completed today |

- **Enter** completes a task (on a done task, Enter reopens it). Completing a recurring task creates the next occurrence.
- **Context menu** (right-click or <kbd>Ctrl</kbd>+<kbd>K</kbd>): **Postpone to tomorrow** (open tasks; moves the due date to tomorrow, or one day past the current due date if that is later), **Edit** (a form prefilled with the task in quick-add text), **Delete**.
- **Undo:** after any change an `Undo: <what you did>` row appears above Settings and reverts the last action. One level only.
- **Settings** row at the bottom opens the settings page.

## Light and progress rules

The light always looks at **today**, whatever the progress setting:

| Light | When |
|---|---|
| 🔴 | At least one open task is overdue |
| 🟠 | Nothing overdue, but at least one open task is due today |
| 🟢 | Nothing overdue or pending today, and at least one task was completed today or is due today/earlier |
| ⚪ | Otherwise (no tasks, or only upcoming ones) |

Progress label, default mode "Today and overdue tasks":

- done = tasks completed today
- total = done + open tasks due today or earlier (overdue included)
- percent = done / total, rounded half up; the label is empty when total is 0

Example: 3 done today, 4 still open for today or overdue gives `3/7 · 43%`.

## Settings

Command Palette, Todo Dock, Settings:

| Setting | Choices |
|---|---|
| **Label format** | **Progress** (`3/7 · 43%`, default), **Percent only** (`43%`), **Icon only** (light only) |
| **Progress counts** | **Today and overdue tasks** (default), **All tasks** (done = every completed task still stored, total = every stored task, including upcoming) |

## Requirements

- Windows 10 / Windows 11
- PowerToys with **Command Palette** enabled and its **Dock** turned on (the Dock was introduced in PowerToys 0.98)

## Installation

Download from the latest [GitHub Release](../../releases): `TodoDock_<version>_x64.msix` (Intel/AMD) or `TodoDock_<version>_arm64.msix` (Arm), plus `TodoDockDev.cer`. Not sure which? Run `$env:PROCESSOR_ARCHITECTURE`: `AMD64` is x64, `ARM64` is arm64.

**1. Trust the certificate (once per machine).** In PowerShell **as Administrator**, in the download folder:

```powershell
Import-Certificate -FilePath .\TodoDockDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Why a self-signed certificate? A publicly trusted code-signing certificate costs money every year, and this project is free. Windows only installs signed MSIX packages, so the package is signed with its own certificate, `CN=TodoDockDev`, which you opt into. It is imported only into **TrustedPeople**, not into Trusted Root, so it is not trusted as a certificate authority and only vouches for packages signed with it.

**2. Install:**

```powershell
Add-AppxPackage .\TodoDock_0.1.0.0_x64.msix
```

**3. Show it in the Dock.** Open Command Palette and run **Reload**. Then enter the Dock's edit mode and add **Todo Dock** to the Dock.

### Update / Uninstall

Update: install the newer `.msix` the same way, then run **Reload**. Uninstall:

```powershell
Get-AppxPackage TodoDock | Remove-AppxPackage
```

## Data and privacy

- Everything stays on your machine in `%LOCALAPPDATA%\TodoDock\tasks.json`.
- No network calls, no telemetry.
- `diag.log` in the same folder records errors only, rolled at 1 MB.
- **Completed tasks are removed after 7 days.** Tasks completed today are never purged.
- An unreadable `tasks.json` is moved aside as `tasks.json.bad-<timestamp>`, not overwritten.

## Build from source

Needs the .NET 10 SDK.

```powershell
dotnet build TodoDock.sln -c Debug -p:Platform=x64
dotnet test tests/TodoDock.Tests -p:Platform=x64      # unit tests
.\scripts\dev-deploy.ps1                              # build + register (Developer Mode on)
.\scripts\dev-deploy.ps1 -Remove                      # unregister
.\scripts\pack.ps1 -Platform x64 -Sign                # dist\...\TodoDock_<ver>_x64.msix + dist\TodoDockDev.cer
```

After deploying, run **Reload** in Command Palette. `-Platform ARM64` builds the Arm package. The first `-Sign` run creates a self-signed `CN=TodoDockDev` certificate in `Cert:\CurrentUser\My` and reuses it afterwards. Back it up (with its private key) with `scripts\backup-signing-cert.ps1`, or every user has to trust a new `.cer` after a rebuild.

## Known limitations

- The ARM64 package is built but **not tested on ARM hardware**.
- No reminders or notifications yet; the Dock only shows state.
- Single machine, no sync.
- The Command Palette Dock is a **preview** feature in PowerToys and its UI may change. If a Dock item stops repainting, run **Reload**.

## Roadmap

- Due-time notifications
- Monthly recurrence
- WinGet package

## License

[MIT](LICENSE). Parts derived from the PowerToys extension template are (c) Microsoft, MIT; see [NOTICE](NOTICE). Not affiliated with Microsoft.
