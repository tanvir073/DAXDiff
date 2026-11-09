# Changelog

All notable changes to DAXDiff are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and DAXDiff uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html). How versions are chosen and released is
described in [docs/dev/releasing.md](docs/dev/releasing.md).

## [Unreleased]

## [0.1.0] - 2026-11-09

First public preview. It is well covered by unit tests but has not yet been verified widely against live Power BI
models; please report anything that does not work.

### Added

- **Desktop app for Windows, macOS and Linux** (Avalonia): projects with two sources and any number of comparisons;
  Power BI Desktop (Windows), Service XMLA and Service REST sources with per-source sign-in, so the two models can be
  in different tenants; keys, aggregations, synonyms, basic and advanced filters, tolerances and key normalization;
  parallel runs with retries and timeouts; results grid with a column picker and the Excel audit workbook; English and
  Swedish; eleven colour themes, with **Graphite** (dark grey with orange) as the default and a **Dark** theme like dark
  mode in Windows.
- Downloads: Windows MSI installer (which also adds DAXDiff to Power BI Desktop's External Tools ribbon) and portable
  zip, macOS `.dmg` (Apple Silicon and Intel) and Linux `.tar.gz` (x64 and ARM). Sign-ins are kept in an encrypted
  per-user cache (Windows), the macOS Keychain or the Linux keyring.
- **Web app** (https://tanvir073.github.io/DAXDiff/): opens projects, connects Service models over REST, runs
  comparisons and exports the workbook, all in the browser.
- XMLA works without an Entra client ID, through Microsoft's own sign-in; a client ID adds workspace browsing and REST.
- **Report a bug**, **User guide** and **Release notes** in Settings. A bug report opens a GitHub issue form with the
  version and operating system filled in.
- User guide and administrator guide (`docs/user/`), technical documentation (`docs/dev/`), MIT licence, code of
  conduct, security policy, issue forms and Discussions.

[Unreleased]: https://github.com/tanvir073/DAXDiff/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/tanvir073/DAXDiff/releases/tag/v0.1.0
