# Contributing to DAXDiff

Thanks for helping. Bug reports, ideas and pull requests are all welcome.

## Reporting bugs and requesting features

Use the [issue forms](https://github.com/tanvir073/DAXDiff/issues/new/choose). In the app, **Settings → Report a bug**
opens the form with your version and operating system filled in. Never include tokens or confidential model data.
Report security problems privately (see [SECURITY.md](SECURITY.md)).

## Making a change

1. For anything larger than a small fix, open an issue first so the approach can be agreed.
2. Branch from `dev`: `feature/<short-name>` or `fix/<short-name>`.
3. Build and test (see [Building and testing](docs/dev/building.md)):

   ```powershell
   dotnet build
   dotnet test tests/DaxDiff.Tests.Unit
   ```

4. Open a pull request into `dev`. CI must pass.

## Guidelines

- **Requirements.** The specification is [docs/dev/DAXDiff_Requirements.docx](docs/dev/DAXDiff_Requirements.docx).
  Cite its IDs (FR-xxx, NFR-xx, AT-xx, §n) in code comments and test names where they apply, such as
  `AT07_DuplicateKeysFailTheRun`.
- **Invariants.** Read the invariants in [Architecture](docs/dev/architecture.md#invariants) before changing the
  engine, the connectors or the project format. In particular: never truncate silently, never splice user text into DAX,
  and bump the project schema version with a migration for any breaking JSON change.
- **Tests.** New behaviour needs unit tests. Use `Fixtures/Fixture.cs` and `FakeConnector` rather than real models.
- **Text.** Every user-facing string goes in resources, in English and Swedish. `LocalizationTests` checks this.
- **Desktop UI.** Use the theme's keyed styles and `DynamicResource` brushes, never hard-coded colours.
- **Platform.** Keep Windows-only code in `DaxDiff.Connectors` and `DaxDiff.Desktop`, so the web app keeps building.
- **Changelog.** Add a line under *Unreleased* in [CHANGELOG.md](CHANGELOG.md) for user-visible changes.
- **Docs.** Update the [user guide](docs/user/README.md) or the [technical docs](docs/dev/README.md) when behaviour
  changes.

By contributing, you agree that your contribution is licensed under the [MIT License](LICENSE).
