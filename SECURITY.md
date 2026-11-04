# Security policy

## Reporting a vulnerability

Please **do not** open a public issue for a security problem. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/tanvir073/DAXDiff/security/advisories/new).
Include the version (Settings → Diagnostics and version), the steps to reproduce and the impact you expect.

You will get an acknowledgement within five working days. Fixes are released as a new version and described in the
advisory once users have had a chance to update.

## Supported versions

Only the latest release receives security fixes.

## What DAXDiff protects

- Models are only queried, never modified or refreshed.
- Tokens are never written to projects, logs or workbooks. The desktop app keeps them in an encrypted per-user cache;
  the web app keeps them in the browser tab's session storage.
- Logs contain no measure values or DAX literals.
- User text is never spliced into DAX; identifiers are escaped and filter values are parsed by type.
- Exported text that starts with `=`, `+`, `-` or `@` is written as text, not as an Excel formula.

Reports that break any of these are especially welcome.
