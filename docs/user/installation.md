# Install DAXDiff

Every release is published on the [Releases page](https://github.com/tanvir073/DAXDiff/releases). Each release lists
its changes and the files to download. `SHA256SUMS.txt` holds the checksum of each file.

## Windows: installer (recommended)

1. Download `DAXDiff-<version>-win-x64.msi` from the latest release.
2. Run it. Installing requires administrator rights.
3. Start **DAXDiff** from the Start menu.

The installer:

- installs DAXDiff in `C:\Program Files\DAXDiff`, with everything it needs, so no .NET runtime is required;
- adds DAXDiff to Power BI Desktop's **External Tools** ribbon, where it opens with the current file already
  connected as model A;
- replaces an older version when you install a newer one.

To uninstall, use *Settings → Apps → Installed apps → DAXDiff*.

## Windows: portable zip (no administrator rights)

1. Download `DAXDiff-<version>-win-x64-portable.zip`.
2. Unzip it into a folder you own, such as `Documents\DAXDiff`.
3. Start `DAXDiff.exe`.

The portable copy does not add a Start menu entry or the External Tools button. To update it, replace the folder
with the new version. Your settings are not stored in the folder, so they are kept.

## macOS and Linux: web app

Open **https://tanvir073.github.io/DAXDiff/** in a current browser (Edge, Chrome, Firefox or Safari). Nothing is
installed. The page runs entirely in your browser, and queries go straight from your browser to Power BI. See
[The web app](web-app.md) for what it can do.

The web app also works on Windows. Your organisation can host its own copy: each release includes
`DAXDiff-<version>-web.zip` (see the [administrator guide](administrators.md#hosting-the-web-app)).

## "Windows protected your PC" and blocked programs

DAXDiff is not code-signed yet, so Windows SmartScreen may warn about the download. Choose **More info → Run anyway**
if you trust the source. Check the file's checksum against `SHA256SUMS.txt` first:

```powershell
Get-FileHash .\DAXDiff-0.1.0-win-x64.msi -Algorithm SHA256
```

Some organisations block unsigned programs, which shows as "Access is denied" when DAXDiff starts. Ask your IT
department to allow DAXDiff. If the .NET 10 runtime is installed, `dotnet DAXDiff.dll` in the install folder may
also work.

## Updating

Settings → **Release notes and downloads** opens the Releases page. The version you have is shown under Settings →
*Diagnostics and version*. Projects (`.pbicomp`) from older versions open in newer ones and are upgraded when saved.
