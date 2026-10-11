# Restore Organization

A portable Windows application for saving and switching between named arrangements of a collection. A snapshot records **filenames, file locations, every subfolder, and empty folders**. It does not back up file contents or save Explorer view settings.

## Start here

If you cloned this repository or downloaded its GitHub source ZIP, run **Launch Restore Organization.cmd** at the project root, or open **dist/Restore Organization.exe**. The complete runnable application, including the optional Explorer-menu components for x86, x64, and ARM64, is included; no build tools are needed to run it. Keep the files and subfolders inside **dist** together.

For the separately packaged application ZIP, follow these steps:

1. Open **Restore Organization.exe** in the extracted application folder.
2. Choose the root of your ebook collection.
3. Name the arrangement, for example **By Author**, and click **Create snapshot**.
4. The application saves **Restore - By Author.cmd** inside the collection.
5. Arrange the same collection differently and create another snapshot, for example **By Subject**.
6. Double-click either saved script. Inspect the preview and click **Restore**.

Keep a restore script at the **root of the collection it describes**. The script restores the folder containing it, including the whole folder tree beneath it. It does not search outside that folder.

A script is a single independent file containing both its saved arrangement and its restoration code. It needs no generator, companion JSON file, Python installation, download, or network connection. It uses Windows PowerShell and Windows' .NET Framework/Windows Forms. You can move the collection and its scripts to another drive.

An existing snapshot is never overwritten when another snapshot has the same name; the new script gets a numbered filename.

## Preview and restoration

The preview shows current locations, saved locations, and the reason for each decision.

| Preview status | Meaning |
|---|---|
| Move | A recognized file will move or regain its saved filename. |
| Rename folder | A folder's saved capitalization will be restored. |
| Already correct | The recognized file is already at the saved location. |
| Missing / unidentified | A file is absent, or changed beyond reliable identification. |
| Ambiguous | Identical copies cannot be distinguished reliably. |
| Conflict | An occupied destination or parent folder prevents a safe move. |
| Folder conflict | An existing file prevents a saved folder from being created. |
| New / unmatched | Leave this file where it is. |
| Unreadable | Resolve the listed access, lock, cloud, or filesystem error first. |

Participating files are staged inside the collection before they are placed at their destinations. This allows filename swaps and rename cycles without overwriting anything. The engine verifies moved files against their **current** SHA-256 fingerprints, preserving edited content.

The preview is checked again immediately before restoration. If files or folders changed, the operation stops and asks you to refresh instead of applying a stale plan. Two operations cannot restore the same collection simultaneously.

No file is intentionally overwritten or deleted. Missing, ambiguous, and conflicting entries are skipped while unrelated safe moves proceed. Empty saved folders are recreated. Cleanup removes only empty ancestors of files successfully moved; unrelated empty folders stay.

If no snapshot files can be recognized, restoration is disabled so you can check the collection root. Completely empty folder snapshots remain usable.

## Edited ebooks and duplicates

Matching uses:

1. A Windows file identity together with its creation timestamp, where available.
2. Unchanged content at the saved path.
3. A unique SHA-256 content fingerprint among unmatched files.

A file that was renamed, moved, and edited can be recognized if its Windows identity survives. Editors, synchronization tools, or cross-drive copies sometimes replace the file and change that identity. If both identity and content change, the engine cannot prove which file is the original and leaves it untouched.

Identical copies can be distinguished by their surviving Windows identities. Without reliable identities, duplicate matches are reported instead of guessed.

A matching content fingerprint identifies content, not the historical origin of a copied file. An unchanged copy can therefore stand in for a missing original when the match is unique.

## Undo and interrupted operations

Before applying an arrangement, a complete **Undo - timestamp-id.cmd** snapshot of the current arrangement is saved in the collection. Run it like any other snapshot to preview restoring the previous organization.

Undo also preserves current content. It cannot recover a deleted file or reverse edits to an ebook.

Recovery journals, staging files, and reports are stored in **.restore-organization** inside the collection. Keep that folder until any interrupted operation has been recovered. Journals are written before moves and flushed to disk; filesystem and power-loss behavior still depends on Windows and the storage device.

After an ordinary failure the engine attempts to return participating files to their pre-restore locations. If an application or machine stops abruptly, reopen a saved restore script and use **Recover interrupted restore**. Recovery restores the arrangement from before that attempt. New operations are blocked until recovery finishes.

If a new entry blocks recovery, it is preserved and recovery remains pending. Resolve the reported obstruction and try recovery again. Never delete the recovery folder to clear this error; staged files may be inside it.

**Open report** shows the operation's moved, unchanged, skipped, and unmatched entries. Completed journals are retained for auditing. Snapshot scripts, undo scripts, and recognized recovery data are excluded from snapshots. An existing unrecognized folder using the reserved name is reported, rather than silently excluded.

The **Choose folder** button opens the Windows Explorer-style folder picker with an address bar, search, and navigation sidebar. It starts at the collection folder already entered when that folder is available. Select a folder and click **Select folder**; cancelling leaves the collection path unchanged.

## Optional Explorer context menu

The feature is **off by default**.

Click **Enable context menu** in the creator to install the optional component for your Windows account. It adds **Create snapshot** for a selected folder and the background inside a folder. On Windows 11 it uses the modern context-menu extension mechanism; Windows decides the exact placement and grouping for your version.

Enabling copies the application component into a stable location under **%LOCALAPPDATA%\RestoreOrganization** and registers its complete signed shell-component package for your Windows account. Windows requires the public signing certificate in **Local Computer / Trusted People**, so the first enable shows a Windows administrator prompt. If you use another administrator's credentials, only certificate trust is installed as that administrator; the menu still registers for your original account. No private signing key is distributed.

The certificate helper accepts only the public certificate embedded in this build. It does not add a certificate authority to the Trusted Root store. Enable also repairs a missing trust entry and updates an older installation. Cancelled administrator prompts, invalid or expired certificates, incomplete application folders, invalid signatures, and package-policy failures produce clear errors.

Click **Disable context menu** to unregister the menu. If this feature added machine certificate trust, removal can show another administrator prompt. Trust is retained if another Windows account still uses the menu, or if the certificate was already trusted before enabling. Cancelling trust cleanup leaves the menu disabled and explains that certificate trust remains. Copied application files are retained so an open application is not disrupted. Existing restore scripts keep working. Sign out and back in if Explorer has not refreshed; the application does not forcibly restart Explorer.

The optional menu requires **Windows 10 version 2004 or later, or Windows 11**. The ZIP includes native **x86, x64, and ARM64** components selected automatically using the operating system architecture, including when the installer runs in a 32-bit process. The creator and generated scripts use **.NET Framework 4.8** and **Windows PowerShell 5.1**. Administrator approval and permission to install signed packages are required; organization-managed policies can prevent installation. Unsupported operating systems cannot be made compatible by a certificate fix.

The packages and native COM component are checked locally. Visible Windows 11 main-menu placement and execution on ARM64 hardware cannot be tested on this Windows 10 x64 development machine. See **docs/VALIDATION.md** for the current installation-test results.

## Dropbox and other storage

Make Dropbox collections **available offline** before scanning. Fingerprinting reads every included file and can download online-only content. Fully downloaded cloud files are supported; cloud download errors are reported. Local tests used a OneDrive-backed workspace; live Dropbox synchronization and an unplugged external drive were not exercised.

Allow cloud synchronization to settle, and close ebook editors before restoring. A participating file that is locked or changed is reported. Windows sharing failures on moves get a brief bounded retry.

Symbolic links, junctions, and unsupported reparse points are not followed; they produce explicit scan errors. Snapshots are refused on incomplete scans. Restores show unreadable entries and remain disabled until the collection can be fully scanned for an undo snapshot.

Windows may still reject paths, permissions, or scripts under organization-managed policies. Such failures are surfaced and recovery data is retained. This application restores organization, so keep normal backups for file content.

## Source and validation

The project includes all C#, native shell-extension source, build scripts, and transaction tests. See **docs/BUILD.md** and **docs/VALIDATION.md** in the source project.

Microsoft references for the optional menu component:

- [File Explorer context-menu integration](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer)


- [Microsoft: resolve untrusted MSIX signing certificates](https://learn.microsoft.com/en-us/windows/msix/msix-troubleshooting-guide)

- [Microsoft: modern Windows folder-selection dialog](https://learn.microsoft.com/en-us/windows/win32/shell/common-file-dialog)
