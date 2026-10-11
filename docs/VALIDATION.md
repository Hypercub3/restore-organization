# Validation — 2026-10-10

The final engine and standalone integration suite passed **90 checks** on Windows 10 build 19045, using .NET Framework and stock Windows PowerShell 5.1. Fixtures were created under tests/work; no real ebook collection was reorganized.

Verified:

- Complete nested inventories, hidden recovery-data exclusion, and saved empty folders.
- Switching between named arrangements, repeat-run behavior, and relocating a collection.
- Edited content preserved through Windows file-identity matching.
- Unique hash fallback, identity-based duplicate matching, and ambiguous duplicates skipped.
- Missing files and new files handled without file deletion.
- Occupied destinations, dependent conflicts, required-folder obstructions, and safe unrelated moves.
- Filename swaps, case-only filenames, case-only folder names, and file-to-folder transitions.
- SHA-256 content preservation across a 250-file arrangement change.
- Automatic rollback after injected staging and placement errors.
- Recovery after actual child-process exits during file staging, file placement, and directory renaming.
- Blocked recovery preserves new occupants and can be retried.
- Concurrent operations blocked; unreadable/locked files refuse incomplete snapshots.
- Stale previews rejected before mutation.
- Unsafe paths, unsupported schema versions, conflicting destinations, and unowned reserved recovery folders rejected.
- Unicode, spaces, brackets, ampersands, percent signs, and exclamation marks in filenames.
- Existing snapshot scripts not overwritten; failed generation leaves no partial script.
- A generated CMD decoded and previewed using stock Windows PowerShell in an isolated folder with no generator or companion files.
- The C# engine embedded in a generated script restored an arrangement and created an independent undo script; that embedded undo restored the prior arrangement under Windows PowerShell 5.1.
- Creator and restore-preview Windows Forms layouts rendered offscreen and visually inspected.
- The actual portable executable launched successfully without context-menu registration.

The native shell DLL passed direct COM tests for class activation, title, canonical identity, selected-folder state, site interface, and unload lifetime. Its imports are standard Windows libraries; the compiler runtime is statically linked.

The corrected context-menu installer passed **23 additional checks** under stock Windows PowerShell 5.1. These cover the helper's embedded certificate pin, rejection of arbitrary operations and unelevated imports, complete-bundle validation, native architecture detection from a 32-bit process, simulated UAC cancellation, simulated certificate retention for other accounts, certificate validity errors, missing components, unsigned/corrupt packages, package signers, complete manifests and native PE architecture for x86, x64 and ARM64.

The original failure was caused by trusting the certificate only in CurrentUser TrustedPeople. Windows accepted the signature after the exact public certificate was installed in LocalMachine TrustedPeople through the narrowly scoped administrator helper. Live testing also exposed an external-location deployment failure; the final release uses complete signed MSIX packages validated by MakeAppx without /nv and registers them without ExternalLocation.

**Live Windows 10 x64 installation was verified for Phill's Windows account:** enable, repeat-enable, status, disable, and re-enable all succeeded. Disable removed the package, per-user launcher registry entry, and machine certificate trust owned by this feature. Re-enable restored the correct trust and package registration. The final menu is enabled, package version **1.0.2.0**, status **Ok**. A separate test activated the registered packaged COM server through Windows (CLSCTX_LOCAL_SERVER), verified the menu title, selected-folder state and launcher icon path pointing to the current user's stable portable copy. The full restoration suite still passes 90 checks; direct native COM tests also pass. The updated creator layout was rendered and visually inspected.

Not exercised:

- Running on physical ARM64 or 32-bit Windows hardware; their native binaries, package architecture and signatures were checked on x64.
- Supplying a different administrator's credentials at UAC and simultaneous installations across multiple Windows accounts. Those isolation and retention paths were reviewed; retention and cancellation errors were also tested with stubs.
- Visible placement in the Windows 11 main menu, because the test machine runs Windows 10.
- Live Dropbox synchronization, online-only Dropbox downloads, physical drive disconnection, and actual power loss. Cloud-tagged OneDrive-backed files and simulated process interruption were tested.
- Organization-managed script, certificate, and package policies.

Before the automatic cleanup update described below, completed journals and text reports were retained inside each fixture's .restore-organization/runs directory. The package contains the app, optional menu component, public certificate, assets, and user instructions. Private keys, test executables, fixtures, and compiler tools are excluded.

## Explorer-style folder picker update

Package version 1.0.3.0 replaces FolderBrowserDialog with the native Windows Common Item Dialog configured for folders and filesystem paths. The existing collection path initializes the dialog when available; cancellation returns no selection, and picker failures go through the application's existing error window. No third-party runtime was added. Appearance is provided by Windows.

Three native-dialog integration checks passed on Windows 10 x64: selecting a real Unicode folder, cancelling selection, and opening/cancelling with an unavailable initial folder. The tests exercised the actual modal native dialog and COM result marshalling. Creator, full architecture packages and release ZIP were rebuilt. The restore engine and standalone restore payload were unchanged by this UI update.

The enabled context-menu copy was upgraded to version 1.0.3.0, Windows package status Ok. Windows first reported the old component in use; the update completed with its package-specific ForceApplicationShutdown option. The stable creator copy matches the new executable by SHA-256, registered COM activation still passes, and all 23 installer checks pass against the rebuilt packages.

## Automatic recovery cleanup update

Version 1.0.4.0 removes completed and recovered run folders while holding the collection lock, both after restore/recovery and when the next snapshot, restore, or recovery begins. Cleanup validates journals, refuses links, preserves nonempty staging folders and unknown entries, and uses only nonrecursive directory deletion. Unfinished and damaged journals remain available for recovery. The owner marker and operation lock remain in .restore-organization.

The report is returned in memory and displayed by Open report while the window stays open. Snapshot and undo scripts still embed their own data and code; existing scripts are unchanged and newly generated scripts include this behavior.

The engine suite passed **99 checks**, including automatic cleanup after success and rollback, older completed/recovered runs, unfinished/damaged data, leftover staged files, unknown entries, concurrent locks, locked-report cleanup retry, unchanged snapshot bytes, and embedded restore/undo cleanup under Windows PowerShell 5.1. The fixtures for the passing full run were created in a unique local temporary directory using test.ps1 -WorkDirectory. Two runs with OneDrive-backed fixtures encountered intermittent journal file-replacement failures; rollback preserved the arrangement in the reported standalone failure. This change does not alter journal write or replacement behavior.
All x64, x86, and ARM64 packages were rebuilt and signed as version 1.0.4.0; the 23 installer checks passed. The release ZIP was regenerated. The existing Explorer-menu creator copy was updated at the location recorded in context-menu.json, with its executable SHA-256 verified against dist. Previous common files were backed up under build/installed-before-cleanup.

## Complete recovery-folder removal update

Version 1.0.5.0 also removes the ownership marker, operation lock file, and recovery folder when no recovery or unknown data remains. Successful snapshot creation and restore/recovery leave only the collection's content and standalone CMD snapshot/undo scripts. Existing scripts embed their original engine; newly created scripts use complete cleanup.

A Windows named mutex, keyed by collection directory identity where available, remains held through folder removal. The legacy file lock is also held during operations, and the ownership marker is removed before releasing that file lock so older scripts cannot begin recovery work during deletion. If final cleanup fails, ownership is restored where possible and the folder is retained for a later retry. No recursive deletion is used.

The full engine and standalone suite passed **112 checks** on local temporary fixtures. Additional checks cover artifact-free snapshot creation, removal of idle metadata and legacy completed runs, separate-process contention using both the updated and legacy file locks, lock reuse after process exit, and preservation of unexpected state-folder entries. Restore, automatic rollback, and interrupted recovery all verify complete recovery-folder removal after completion. The passing standalone restore and undo test also requires the entire recovery folder to be absent.
The final release was rebuilt and signed for all three architectures as version 1.0.5.0, and all 23 installer checks passed. The updated release ZIP executable matches dist by SHA-256. The currently installed Explorer-menu creator copy was refreshed at the location recorded in context-menu.json and its executable hash verified; prior common files were backed up under build/installed-before-full-cleanup-*.
