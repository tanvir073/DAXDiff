# Building and testing

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows, macOS or Linux. Everything builds and the tests run on all three; the MSI can only be built on Windows.
- Optional: Power BI Desktop, for trying local PBIX comparisons.

## Commands

```powershell
dotnet build                                   # whole solution (DaxDiff.sln)
dotnet test tests/DaxDiff.Tests.Unit           # all unit tests
dotnet test tests/DaxDiff.Tests.Unit --filter "FullyQualifiedName~ComparisonEngineTests"   # one class
dotnet run --project src/DaxDiff.App           # desktop app (Avalonia)
dotnet run --project src/DaxDiff.Web --launch-profile http   # web app on http://localhost:5215
./scripts/build.ps1                            # Release restore, build, test, publish to ./publish
./scripts/package.ps1                          # Windows: MSI and portable zip in ./dist
bash scripts/package-app.sh osx-arm64          # macOS .dmg / Linux .tar.gz in ./dist (run on that system)
```

The desktop output is in `src/DaxDiff.App/bin/Debug/net10.0/`. If an application-control policy blocks the unsigned
`DAXDiff.exe` ("Access is denied"), run `dotnet DAXDiff.dll` from that folder.

## Packaging

`scripts/package.ps1` runs the tests, publishes a self-contained win-x64 build to `publish/app` and produces in `dist/`:

- `DAXDiff-<version>-win-x64.msi`, built with WiX Toolset 5 (`installer/`). WiX is a NuGet build SDK, so nothing needs
  installing. The MSI installs per machine to `C:\Program Files\DAXDiff`, adds a Start menu shortcut and registers
  the Power BI Desktop External Tool (`tools/DAXDiff.pbitool.json`).
- `DAXDiff-<version>-win-x64-portable.zip`, the same files for users without administrator rights.

`scripts/package-app.sh <rid>` builds the cross-platform app for `osx-arm64`, `osx-x64`, `linux-x64` or `linux-arm64`:

- macOS: `DaxDiff.app` (Info.plist with the `.pbicomp` document type, an `.icns` made from
  `src/DaxDiff.App/Assets/DAXDiff.png`), ad-hoc signed so Apple Silicon runs it, in `DAXDiff-<version>-<rid>.dmg`.
  It is not notarized; users confirm the first start under *Privacy & Security*.
- Linux: `DAXDiff-<version>-<rid>.tar.gz` with the app, an icon, a `.desktop` entry and `install.sh`.

Pull requests build `osx-arm64` and `linux-x64` previews (CI artifacts, kept a week).

Releases are built by the release workflow, not by hand; see [Versioning and releases](releasing.md).

## Tests

xUnit tests are in `tests/DaxDiff.Tests.Unit`. Test names start with the requirement or acceptance-test ID they cover,
such as `AT07_…` or `FR018_…`.

- `Fixtures/Fixture.cs` builds synthetic metadata, configs and query results (`Key(...)`, `Value(...)`, `Field(...)`,
  `config.With(o => o with { ... })`).
- `Fixtures/FakeConnector.cs` is an in-memory `IModelConnector` for orchestration tests.
- Tests run with the English UI culture (`Fixtures/TestCulture.cs`).

The desktop project has no tests. Exercise its view models through an offscreen harness.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and every push to `main`, `ppe` and `dev`. It builds the whole
solution and runs the tests on Windows, and builds the cross-platform projects and runs the tests on Linux and macOS.

## Localization

Every user-facing string is in resources: `Resources/Strings.resx` (English, neutral) and `Strings.sv.resx` (Swedish)
in each project, plus `Resources/Ui.resx` for desktop XAML text. Add both languages together. Logs, diagnostic codes,
DAX and project JSON stay English. The web app is English only.
