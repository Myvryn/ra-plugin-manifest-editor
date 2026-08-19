# RA Plugin Manifest Editor

A companion tool for [RA Control](https://rocksolidaudio.com) that lets you
curate the plugin list its **Insert Plugins** activity shows you — instead
of hand-editing XML, check off the plugins you don't want cluttering that
list and remove them with one click.

## What it is

RA Control keeps track of every plugin it finds on your system in a file
called `HostMode.props`, sitting in:

```
Windows:  %APPDATA%\Rocksolid Audio\RA Control\Host Mode\HostMode.props
macOS:    ~/Library/Application Support/Rocksolid Audio/RA Control/Host Mode/HostMode.props
```

That file is what populates the "Insert Plugins" list in Host Mode. It's a
plain XML file (JUCE's `KnownPluginList` format), and if you've got a lot of
duplicate, broken, or shell/placeholder plugin entries in there, there was
previously no way to clean it up except editing XML by hand.

This app gives you a proper interface for that: open the file, check the
plugins you want gone, remove them, save. It remembers what you've removed,
so if RA Control's own scanner ever re-adds one, this app will flag it as
already-removed the next time you open it.

## How to use it

1. **Launch the app** (`RA Plugin Manifest Editor.exe`).
2. Click **Open different file…** (or **Open HostMode.props…** if nothing's
   loaded yet). The file picker opens straight into RA Control's own
   `Host Mode` folder — just double-click `HostMode.props`.
3. **Browse the list.** Use the search box to filter by name, manufacturer,
   format, or category, or narrow it down with the format dropdown
   (VST3, AAX, etc). Click any column header to sort by it.
4. **Check the plugins you want removed.** Use **Check all filtered** to
   select everything currently shown by your search/filter, or
   **Uncheck all** to clear selections.
5. Click **Remove checked (N)**. You'll get a confirmation prompt showing
   how many plugins are about to be removed — this only changes what's held
   in memory so far, nothing is written to disk yet.
6. Click **Save changes** to write it back to `HostMode.props`. A backup of
   the previous version is written automatically first (see below). If you
   still have plugins checked but never actually clicked "Remove checked"
   (easy to do by accident), Save will stop and ask whether to remove them
   first — so you can't check off a bunch of plugins, hit Save, and end up
   with an unchanged file without realizing it.
7. Restart RA Control (or reopen Host Mode) to see the trimmed list.

### Other things worth knowing

- **Reload from disk** re-reads the current file without re-opening the
  file picker — useful if RA Control's plugin scanner has run again since
  you last loaded it.
- **Removal history**: every plugin you remove is remembered even after you
  close the app. If a rescan brings a removed plugin back into
  `HostMode.props`, the next time you open or reload the file, that plugin
  is automatically pre-checked and tagged **history** — so clearing it out
  again is just one more click of "Remove checked" + "Save changes." Click
  **Removal history** in the toolbar to see everything you've ever removed.
  Each entry has two options: **Restore** puts that plugin straight back
  into the file you currently have open — no need to reopen the original
  or redo any other removals you've already made — and **Forget** just
  stops it from being auto-selected in the future without adding it back.
  Either way, remember to click **Save changes** afterward to write it to
  disk.
- **Auto-resume**: the app remembers the last file you had open and reloads
  it automatically the next time you launch it — no need to browse to the
  same folder every time.
- **Save edited copy as…** exports your changes to a new file instead of
  overwriting the original — handy for testing or sharing a curated list.

## Does it back up your file automatically?

**Yes.** Every time you click **Save changes**, the app copies whatever is
currently on disk to a timestamped backup —
`HostMode.props.bak-YYYYMMDD-HHmmss` — in the same folder, *before* it
writes anything. This happens automatically, every save, with no setup or
toggle required. If an edit goes wrong or you change your mind after the
fact, the previous version is sitting right next to the file, and you can
just rename it back to `HostMode.props`.

These backups accumulate over time (one per save) and aren't
auto-deleted, so it's worth clearing out old ones from the `Host Mode`
folder occasionally if you save often.

## Where things live

| What | Where |
|---|---|
| The manifest itself | `...\RA Control\Host Mode\HostMode.props` (RA Control's folder — untouched except when you hit Save) |
| Backups of the manifest | Same folder, as `HostMode.props.bak-<timestamp>` |
| This app's settings (last opened file) | `%APPDATA%\RAPluginManifestEditor\settings.json` |
| Removal history | `%APPDATA%\RAPluginManifestEditor\removal-history.json` |

The app's own settings/history are stored separately from RA Control's
files, so deleting them resets this tool without touching RA Control itself.

## Why a native app instead of a web page

The first version of this was a browser-based tool, but Chromium blocks
the File System Access API from opening files under `%APPDATA%` (it's
treated as a protected system location), so it couldn't open
`HostMode.props` directly. This version is a native desktop app instead,
which has no such restriction.

It's built with Avalonia UI (.NET) specifically so the same source can be
built for Windows or macOS — see the "Building" section below.

---

## Building

Requires the .NET 9/10 SDK.

```
dotnet build
dotnet run
```

### Publishing a standalone Windows .exe

```
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/win-x64
```

Produces a single `RAPluginManifestEditor.exe` (~100MB, self-contained —
no .NET install required on the target machine). Two large native debug
symbol files (`libSkiaSharp.pdb`, `libHarfBuzzSharp.pdb`) get copied into
the output folder too; they're safe to delete before distributing.

### Publishing for macOS

The project uses Avalonia UI specifically so it's portable — no Windows-only
APIs are used anywhere in the codebase. To build a macOS version:

```
dotnet publish -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=true -o publish/osx-arm64
# or osx-x64 for Intel Macs
```

The only platform-specific code is `Services/AppPaths.cs`, which already
branches for macOS (`~/Library/Application Support/RAPluginManifestEditor`)
vs. Windows (`%APPDATA%`) vs. Linux (XDG). Everything else — UI, XML
handling, file dialogs — is cross-platform Avalonia/.NET out of the box.
A macOS build hasn't been tested on real hardware; the app icon
(`Assets/app.ico`) and `.app` bundle metadata would also need attention
for a polished macOS release.

## Theming

Colors in `Styles/RATheme.axaml` were sampled directly from RA Control
screenshots (dark background `#1E1E1E`, panels `#282828`, gold accent
`#DCA34C`/`#DCB96E`, green `#68C1A0`, red/salmon `#E6827D`) to feel at
home next to RA Control itself. It's a close approximation, not a
pixel-perfect asset match — RA's actual icon/fonts weren't extracted from
the binary.
