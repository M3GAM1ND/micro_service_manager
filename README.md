# Microservice Manager

A Windows desktop tool for running FRE8 microservices on your own machine while the Angular app talks to them.
Tick the services you're working on and press ▶. The manager then:

- points those keys in Angular's `environment.ts` at `localhost`, and the rest at **QA**, **UAT1** or **UAT3**
- sets the database name in each local service's `appsettings.json`
- builds and runs the services **inside the app**, with live logs, CPU and memory per service
- puts every file back exactly as it was when you stop a service or close the app

## Features

| | |
|---|---|
| **Runs inside the app** | No console windows. Each service's output is shown in the log panel, colored by level (errors and warnings stand out), with *Errors only*, *Follow*, select-and-copy. |
| **CPU & RAM per service** | Live numbers and a CPU sparkline for every running service, plus totals and the manager's own memory in the status bar. |
| **Light** | *Fast & light* mode builds each service, then runs the compiled app directly, skipping the `dotnet run` host process that otherwise stays in memory next to every service. *Low-memory mode* runs services with Workstation GC. In testing, one service used **80 MB vs 322 MB** with `dotnet run`. |
| **Never stale files** | `environment.ts` is edited **in place**: only the values that need to change are touched. New keys your team adds stay, and nothing is replaced with an old copy. Same for the database name in `appsettings*.json`. |
| **Crash-safe** | Every change is written to a journal first. If the manager is killed, the next start puts your files back. Services run in a job object, so they never keep running hidden after the manager exits. |
| **QA / UAT1 / UAT3** | One click switches every remote URL. Environments are defined in an editable `environments.json` (see below). |
| **Smart key matching** | Finds the `environment.ts` key for each service by name (case-insensitive, with or without `API`), by known aliases (`InterComapanyAPI`, `FleetMaintenanceAndSafetyAPI`, …) and by the service name inside each URL (`…/frqa/LegalEntityService`). If nothing matches, the row says so; right-click → *Set environment key…* to choose one. |
| **Convenient** | Search (Ctrl+F), *Last session* (restart yesterday's set in one click), *Open in browser* (Swagger), *Open in Visual Studio* (opens the solution and switches files for debugging), port-conflict detection, crash/build-failure detection, one build at a time (no file-lock errors on shared projects). |

## How the file editing works

When you tick a service, the manager reads your **current** `environment.ts` and changes only the values it needs to:

```ts
LegalEntityAPI: 'https://fre8tmsqa.com/frqa/LegalEntityService',   // before
LegalEntityAPI: 'http://localhost:5002',                           // while LegalEntity runs locally
```

It records the original value in `%LOCALAPPDATA%\MicroserviceManager\session.json`. When you stop the service, or close the app, it puts back **only** the values it changed, and only if they still hold what it wrote. Lines you edited yourself in the meantime, and keys added by a `git pull`, are left alone.

The database name works the same way: `Database=` / `Initial Catalog=` in `appsettings.json`, `appsettings.Development.json` and `appsettings.{ASPNETCORE_ENVIRONMENT}.json`.

## Environments (QA, UAT1, UAT3)

Stored in `%LOCALAPPDATA%\MicroserviceManager\environments.json`, which is created on first run. Open it from **Settings → Edit environments**. Changes apply as soon as you save.

```jsonc
{
  "name": "UAT3",
  // swapped in every value of environment.ts, so keys added later are switched too
  "rewrite": [ { "from": "https://fre8tmsqa.com/frqa/", "to": "https://fre8tmsqa.com/uat3/" } ],
  // exact values for keys that don't follow the pattern
  "values": { "ApplicationName": "FRE8 UAT3", "newTabInitialUrl": "/uat3/home" }
}
```

Add another environment (e.g. `UAT2`) and it appears as a new button.

> ⚠ The UAT1 values were derived from UAT3 by swapping `uat3` for `uat1`. Check them once.

## Requirements

- Windows 10 (1809+) or Windows 11
- .NET 10 SDK to build the manager. Your services need whichever .NET SDK they already use.

## Build & run

```bash
dotnet run
```

Publish a single `.exe` (under 400 KB; needs the .NET 10 Desktop Runtime, which the .NET 10 SDK includes):

```bash
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o publish
```

Publish for machines without .NET 10 (self-contained, about 110 MB):

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Don't commit `bin/`, `obj/` or `publish/`; `.gitignore` already excludes them. To share a build, attach it to a GitHub Release.

## Upgrading from v1

v1 overwrote `environment.ts` with a built-in copy, and restored `appsettings.json` from old backups. Your files may still hold those old versions. Reset them once to your repo's latest:

```bash
git checkout -- src/environments/environment.ts
```

Run that in the Angular repo. For `appsettings*.json`, run `git status` in each service repo and `git checkout -- <file>` the ones v1 changed. The old backup folder `%LOCALAPPDATA%\MicroserviceManager\Backups` is no longer used and can be deleted.

## Keyboard

| Key | Action |
|---|---|
| Ctrl+F | Filter services |
| F5 | Rescan the services folder |
| ↑ / ↓ | Select service |
| Space | Tick / untick (run locally) |
| Enter | Start / stop the selected service |
| Ctrl+C / Ctrl+A | Copy / select log lines |

## Project layout

```
Core/      no UI code: scanning, key matching, file editing + journal, running services, metrics
UI/        WinForms: theme, custom-drawn controls, main window, settings
Program.cs single-instance start-up
```
