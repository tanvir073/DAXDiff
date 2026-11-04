# DAXDiff

[![CI](https://github.com/tanvir073/DAXDiff/actions/workflows/ci.yml/badge.svg)](https://github.com/tanvir073/DAXDiff/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/tanvir073/DAXDiff?include_prereleases)](https://github.com/tanvir073/DAXDiff/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Compare the data two Power BI semantic models return.** DAXDiff lists every row that matches, differs or exists on
one side only, and exports the result to an Excel audit workbook.

Use it to check a migrated or refactored model against the original, a test workspace against production, or a PBIX
on your laptop against the published model.

- Compare any pair: a PBIX open in Power BI Desktop, a Service model over XMLA, or a Service model over REST.
- Choose columns and measures on each side, mark the key columns, aggregate the rest, and pair fields by synonym.
- Filter both models to the same slice. Compare exactly or with tolerances, with optional key normalization.
- Each model signs in separately, so the two can be in different tenants.

## Download

| System | Get it |
|---|---|
| **Windows 10 / 11** | [Latest release](https://github.com/tanvir073/DAXDiff/releases/latest): `DAXDiff-<version>-win-x64.msi` (installer) or `…-portable.zip` (no admin rights) |
| **macOS 12+** | [Latest release](https://github.com/tanvir073/DAXDiff/releases/latest): `…-osx-arm64.dmg` (Apple Silicon) or `…-osx-x64.dmg` (Intel) |
| **Linux** | [Latest release](https://github.com/tanvir073/DAXDiff/releases/latest): `…-linux-x64.tar.gz` or `…-linux-arm64.tar.gz` |
| **Any browser** | Web app: **https://tanvir073.github.io/DAXDiff/** (Service models over REST) |

Comparing a PBIX open in Power BI Desktop needs Windows, because Power BI Desktop only runs there. See
[Install DAXDiff](docs/user/installation.md) for details.

## Documentation

- **[User guide](docs/user/README.md)**: installation, getting started, connecting to models, comparisons, results,
  troubleshooting.
- **[Administrator guide](docs/user/administrators.md)**: Entra ID app registration, Power BI settings, deployment.
- **[Technical documentation](docs/dev/README.md)**: architecture, building, testing, releasing.
- **[Changelog](CHANGELOG.md)**

## Guarantees

- **Read-only.** Models are only queried, never modified or refreshed.
- **No silent truncation.** A row limit, a REST limit, a cancellation or a duplicate key makes the run *Partial*,
  *Cancelled* or *Failed*, never *Complete*.
- **Security as observed.** Each side is queried as the signed-in user, and results are labelled that way.
- **No secrets at rest.** Projects, logs and workbooks never contain tokens or passwords. Logs contain no values or DAX.
- **No telemetry.** Nothing is sent anywhere except to Microsoft for sign-in and to the models you query.

## Feedback

- Found a bug? Use **Settings → Report a bug** in the app, or [open a bug report](https://github.com/tanvir073/DAXDiff/issues/new/choose).
- Have an idea? [Request a feature](https://github.com/tanvir073/DAXDiff/issues/new/choose).
- Security problem? Report it privately; see [SECURITY.md](SECURITY.md).

## Build from source

```powershell
dotnet build
dotnet test tests/DaxDiff.Tests.Unit
dotnet run --project src/DaxDiff.Desktop
```

Requires the .NET 10 SDK. See [Building and testing](docs/dev/building.md) and [CONTRIBUTING.md](CONTRIBUTING.md).

## Status

DAXDiff is a preview (0.x). It is well covered by unit tests, but has not yet been verified against live Power BI
models. Please report anything that does not work.

## License

[MIT](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
