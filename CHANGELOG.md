# Changelog

All notable changes to Rusty Painter are documented here.

## 0.1.0

### Elsewhere

- First build: the application shell (sidebar, themed title bar, Home and Settings) and the in-app **update flow**. The app checks GitHub Releases on startup, shows what changed, and can download and apply the update itself - via the installer for Setup installs, or by swapping the files for portable installs - then relaunches. Downloads retry on a flaky connection, are checked against the SHA-256 GitHub publishes for the release, and never touch the installed copy unless the whole update downloaded intact.
