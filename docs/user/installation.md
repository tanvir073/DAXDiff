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

## macOS

1. Download `DAXDiff-<version>-osx-arm64.dmg` for Apple Silicon (M1 and later), or `…-osx-x64.dmg` for Intel Macs.
   *Apple menu → About This Mac* shows which you have.
2. Open the `.dmg` and drag **DAXDiff** to **Applications**.
3. Start DAXDiff from Applications. macOS 12 or later is required.

DAXDiff is not notarized by Apple yet, so the first start is blocked with "Apple could not verify DAXDiff". Open
*System Settings → Privacy & Security*, scroll to the message about DAXDiff and choose **Open Anyway**. You only do
this once per version. Alternatively, in Terminal:

```sh
xattr -dr com.apple.quarantine /Applications/DAXDiff.app
```

Sign-ins are kept in your login keychain. To uninstall, move DAXDiff from Applications to the Bin.

## Linux

1. Download `DAXDiff-<version>-linux-x64.tar.gz` (or `…-linux-arm64.tar.gz` for ARM computers).
2. Unpack it where you keep programs, for example `tar -xzf DAXDiff-0.2.0-linux-x64.tar.gz -C ~/Apps`.
3. Run `./DAXDiff` in the unpacked folder. Run `./install.sh` once to add DAXDiff to your application menu.

DAXDiff needs a graphical desktop (X11 or Wayland) and the usual desktop libraries; on a minimal system install
`libicu`, `fontconfig` and `libsecret` from your distribution. Sign-ins are kept in the desktop keyring (GNOME
Keyring or KWallet). Without a keyring, you sign in again each time DAXDiff starts; tokens are never written to disk
unencrypted. To update, unpack the new version and delete the old folder.

## What works where

| | Windows | macOS | Linux | Web app |
|---|---|---|---|---|
| Power BI Desktop files (PBIX open in Power BI Desktop) | Yes | No (Power BI Desktop is Windows-only) | No | No |
| Power BI Service over XMLA | Yes | Yes | Yes | Over REST instead |
| Power BI Service over REST | Yes | Yes | Yes | Yes |
| Build and edit comparisons | Yes | Yes | Yes | Not yet |

## Web app (any system, nothing to install)

Open **https://tanvir073.github.io/DAXDiff/** in a current browser (Edge, Chrome, Firefox or Safari). The page runs
entirely in your browser, and queries go straight from your browser to Power BI. See [The web app](web-app.md) for
what it can do.

Your organisation can host its own copy: each release includes `DAXDiff-<version>-web.zip` (see the
[administrator guide](administrators.md#hosting-the-web-app)).

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
