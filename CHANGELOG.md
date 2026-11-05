# Changelog

All notable changes to DAXDiff are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and DAXDiff uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). How versions are chosen and released is
described in [docs/dev/releasing.md](docs/dev/releasing.md).

## [Unreleased]

### Added

- MIT licence.
- Separate user documentation (`docs/user/`) and technical documentation (`docs/dev/`).
- **Report a bug**, **User guide** and **Release notes** in Settings (desktop and web). A bug report opens a GitHub
  issue form with the version and operating system filled in.
- GitHub issue forms for bug reports and feature requests, a pull-request checklist and a security policy.
- Continuous integration on every pull request, and a release workflow that publishes the MSI installer, a portable
  zip and a self-hostable web build to GitHub Releases when a version is tagged.
- **macOS and Linux desktop app**: the desktop app now also runs on macOS (`.dmg`, Apple Silicon and Intel) and Linux
  (`.tar.gz`, x64 and ARM), built on Avalonia. Power BI Service models connect over XMLA or REST; Power BI Desktop
  files need Windows. Sign-ins are kept in the macOS Keychain or the Linux keyring.
- **Graphite** colour theme: dark grey with orange accents (Settings → Appearance).

### Changed

- The **Dark** colour theme now looks like dark mode in Windows: charcoal grey instead of near-black, with the light-blue
  Windows accent.
- **The Windows app is the same app as on macOS and Linux** (Avalonia), replacing the Windows-only WPF app. It looks
  and works the same; projects, settings, sign-ins and the External Tools button carry over unchanged.
- The MSI is named `DAXDiff-<version>-win-x64.msi`.
- The Results page's details panel has a maximum height and scrolls, so it no longer pushes the grid off the page.

### Fixed

- The MSI failed to build from a clean output folder (ICE50: the Start menu shortcut's icon must come from the exe).

## [0.1.0]

First preview, not yet verified against live Power BI models.

### Added

- Desktop app (Windows): projects with two sources and any number of comparisons; Power BI Desktop, Service XMLA and
  Service REST sources with per-source sign-in; keys, aggregations, synonyms, basic and advanced filters, tolerances
  and key normalization; parallel runs with retries and timeouts; results grid and Excel audit workbook; English and
  Swedish; colour themes.
- Web app (browser): opens projects, connects Service models over REST, runs comparisons and exports the workbook.

[Unreleased]: https://github.com/tanvir073/DAXDiff/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/tanvir073/DAXDiff/releases/tag/v0.1.0
