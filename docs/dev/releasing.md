# Versioning and releases

## Semantic Versioning

DAXDiff uses [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`, with an optional pre-release suffix.

| Change | Bump | Example |
|---|---|---|
| A project file written by the new version cannot be opened by the old one in a way users notice, or a feature is removed | MAJOR | 1.4.2 → 2.0.0 |
| New features, compatible with existing projects | MINOR | 1.4.2 → 1.5.0 |
| Bug fixes only | PATCH | 1.4.2 → 1.4.3 |
| Preview of the next version | suffix | 1.5.0-beta.1 |

While the version is `0.x`, the API and project format may still change in minor versions.

The **project schema version** (`ProjectFile.CurrentSchemaVersion`) is separate from the app version. It changes when
the `.pbicomp` JSON changes, together with a migration step in `ProjectStore.Migrate`.

## Where the version lives

`Directory.Build.props` is the only place:

```xml
<VersionPrefix>0.2.0</VersionPrefix>
<VersionSuffix>beta.1</VersionSuffix>   <!-- empty for a final release -->
```

Every assembly, the MSI and the file names use it. The informational version also carries the commit
(`0.2.0-beta.1+3f2a9c1…`). It is shown in Settings, written to logs and to each workbook's execution log, and filled
into bug reports. The MSI's ProductVersion uses `VersionPrefix` only, because Windows Installer accepts numbers only.

## Changelog

`CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com/). Every pull request with a user-visible change
adds a line under **Unreleased**, in *Added*, *Changed*, *Fixed*, *Removed* or *Security*.

## Branches

| Branch | Purpose |
|---|---|
| `dev` | Integration of finished work; deploys the web app |
| `ppe` | Pre-production: what is being tested before release |
| `main` | Released code; deploys the web app; release tags are made here. Protected: pull requests with passing CI only |
| `feature/*`, `fix/*` | Short-lived work branches, merged into `dev` through pull requests |

## Making a release

1. On `dev`, move the *Unreleased* entries in `CHANGELOG.md` into a new section `## [0.2.0] - 2026-11-20` and update
   the compare links at the bottom.
2. Set `VersionPrefix` (and `VersionSuffix`) in `Directory.Build.props`.
3. Merge `dev` → `ppe`, test, then open a pull request from `ppe` into `main` and merge it once CI passes. `main` is
   protected: it only takes pull requests whose CI checks pass, and it cannot be force-pushed or deleted.
4. Tag `main` and push the tag:

   ```powershell
   git switch main
   git pull
   git tag v0.2.0
   git push origin v0.2.0
   ```

The **Release** workflow (`.github/workflows/release.yml`) then:

1. checks that the tag equals `v<Version>` and that `CHANGELOG.md` has a section for it, and stops otherwise;
2. runs the tests and builds the MSI and the portable zip on Windows, and the `.dmg` (Apple Silicon, Intel) and
   `.tar.gz` (x64, ARM) downloads on macOS and Linux;
3. builds a self-hostable web zip;
4. creates the GitHub Release with that changelog section as its notes, the files and `SHA256SUMS.txt`. A version
   with a suffix is marked as a pre-release.

If the workflow fails, fix the problem, delete the tag (`git push origin :v0.2.0` and `git tag -d v0.2.0`) and tag
again. Never move a tag that has already produced a published release; release a new patch version instead.

## Dependency updates

Dependabot (`.github/dependabot.yml`) opens grouped pull requests into `dev` once a month for NuGet packages and GitHub
Actions. Merge them like any other pull request once CI passes.

## Code signing

Releases are not code-signed yet (§9). When a certificate is available, sign `DAXDiff.exe` and the MSI in the
`windows` job of the release workflow, before the files are uploaded. For macOS, a Developer ID certificate and
notarization (`xcrun notarytool`) in `scripts/package-app.sh` remove the *Open Anyway* step; until then the app is only
ad-hoc signed.
