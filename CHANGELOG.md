# Changelog

Changes to the **RA Plugin Manifest Editor**.

Versioning is semantic: the minor bumps below add features without changing how
existing manifests or saved settings load. Dates are the release date; earlier
detail is in the git history.

---

## 1.2.0 — 2026-09-11

- **Real Windows installer**, replacing the raw self-contained `.exe` (104 MB,
  no shortcuts, no uninstall entry). Two flavours: Web (small, fetches the
  .NET 10 Runtime at install time if missing) and Offline (bundles it, no
  internet needed). Start Menu + optional desktop shortcut, proper
  Add/Remove Programs entry, and an Update/Reinstall/Uninstall choice when a
  prior install is found.
- Switched Windows to a **framework-dependent** publish (104 MB → ~30 MB
  payload) — needs the .NET 10 Runtime, which the installer above handles.
  macOS is unaffected: still a self-contained, portable app.
- Fixed: three `SemaphoreSlim`s (in `LiveMapAvailabilityService` and
  `HostMapDownloadService`) were never disposed. Managed-only leak, low
  impact, now fixed.
- Six Walls branding: app icon, maker's mark, corrected assembly identity.
- **Offline HTML manual** ships next to the executable
  (`RA Plugin Manifest Editor - Manual.html`), matching the other Six Walls
  products; also published at
  `sixwalls.net/support/ra-plugin-manifest-editor-manual.html`. The in-app Help
  panel stays the primary reference.

## 1.1.0 — 2026-08-21

- **Map-availability checking.** An offline pass reads which RA Control maps you
  actually have (via Host Maps, not Parameter Tables), scoped to the selected
  RA Control devices.
- **Optional live check** via RA Control's API, run after the offline pass.
- **"Download all maps"** — fetch and apply the real Mappings through the API.
- Prompt to restore previously-removed plugins when RA Control publishes a new
  map for them.
- Saved API token is encrypted at rest with Windows DPAPI and loaded at
  startup, not only when Settings opens.
- In-app Help panel.
- Toolbar and header layout cleaned up.
- CI: `gitleaks` check to catch accidentally committed secrets.
- The live/API feature is undocumented and its token field is hidden behind a
  flag.

## 1.0.0 — 2026-08-19

- First release. Edit RA (Reason) plugin manifests: browse, add, remove and
  reconcile plugin entries.
- PolyForm Noncommercial 1.0.0 licence. Unofficial — no affiliation with
  Reason Studios / RA.

