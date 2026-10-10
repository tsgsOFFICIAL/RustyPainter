# Changelog

All notable changes to Rusty Painter are documented here.

## 0.1.0

### App

- First build with the sidebar, a Home page and a Settings page.

### Updates

- The app checks GitHub for a newer version when it starts and shows what changed.
- It can download and install the new version itself. Installed copies run the new installer, portable copies swap their files, and both restart afterwards.
- Downloads retry on a bad connection and are checked against the checksum GitHub publishes. If an update fails, your installed version is left as it was.
