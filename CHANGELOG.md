# Changelog

All notable changes to this project are documented here. This project follows [Semantic Versioning](https://semver.org/).

## [1.4.1] - 2026-10-23

### Fixed

- Standby-triggered automatic cleaning was a no-op on machines where the standby list sits in higher priorities: the low-priority purge removed 0 MB in a measured test (6.82 GB → 6.82 GB), while the full purge removed all of it (6.82 GB → 0.00 GB, +6.9 GB free).
  The standby trigger now uses the full purge when the user has enabled that area, and backs off for 10 minutes when a clean does not move the needle.
- Notification text no longer reports noise: when nothing meaningful was released it says so instead of printing "+0.04 GB".

### Added

- UI hint explaining that low-priority standby is usually a small slice.

## [1.4.0] - 2026-10-23

### Added

- Event-driven low-memory trigger via `CreateMemoryResourceNotification`.
- Top memory processes card with per-process `EmptyWorkingSet` trimming.
- Suppression of notifications while a full-screen D3D app or presentation mode is active.
- Sensor chart bounds (0-100% for memory usage).

## [1.3.0] - 2026-10-23

### Added

- Standby-size trigger (ISLC-style): clean when the standby cache exceeds N GB, even at low load.
- 100% stacked memory-composition bar (working set / free / standby / modified).
- Clicking the notification activates the Toolkit window.

## [1.2.0] / [1.2.1] - 2026-10-23

### Changed

- Notifications now go through the Windows notification area (`Shell_NotifyIcon` with a hidden tray icon) instead of the in-app toast.
- Notification body shows gigabytes instead of the number of areas.
- Working-set accounting row in the evidence table, plus an explanation when standby/modified grow because of a working-set trim.

## [1.1.0] - 2026-10-23

### Fixed

- Nested `ScrollViewer` swallowed the mouse wheel inside Toolkit's main scroll viewer.

### Added

- "Show a notification after cleaning" setting.
- `ui.toast` and `settings.read` permissions.

## [1.0.1] - 2026-10-23

### Added

- Working-set row in the evidence table with the same basis as Resource Monitor's "In use".
- Explanation line when a working-set trim moves pages into standby/modified.

## [1.0.0] - 2026-10-23

### Added

- Initial release: 7 cleanup areas, 3 memory sensors, overview row, manual clean page, auto-clean by usage threshold.
