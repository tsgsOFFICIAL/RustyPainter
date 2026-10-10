# Rusty Painter

**Turn any image, link or piece of text into Rust art.**

Load a picture (drag & drop, a URL, or just type some text), pick a surface, and Rusty Painter paints it onto signs, frames and banners in Rust - or carves it into a pumpkin.

[![Last Build](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2FtsgsOFFICIAL%2FRustyPainter%2Fbadges%2Flatest-build.json)](https://github.com/tsgsOFFICIAL/RustyPainter/releases)
[![Nightly Build](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/nightly.yml/badge.svg)](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/nightly.yml)
[![Release Build](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/release.yml/badge.svg)](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/release.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/download)
[![Avalonia](https://img.shields.io/badge/Avalonia-12-blue?logo=avaloniaui&logoColor=f5f5f5)](https://avaloniaui.net/)
[![Discord](https://img.shields.io/discord/227048721710317569?logo=discord&logoColor=FFF&label=Discord&labelColor=5865F2)](https://discord.gg/Cddu5aJ)
[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

> **Status:** early development. The app shell and the update flow are in place; Studio (loading images, picking a surface, painting) is being built next.

## Requirements

- Windows 10/11 (64-bit)

## Quick Start

Grab the latest build from the [Releases](https://github.com/tsgsOFFICIAL/RustyPainter/releases) page:

- **`*-Setup.exe`** - guided install, registers with Windows' Installed apps.
- **`*-self-contained.zip`** - portable copy, nothing to install.
- **`*-framework-dependent.zip`** - portable and much smaller, needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Rusty Painter keeps itself up to date: it checks GitHub on startup and offers to update. Both install types update in place.

## Building from Source

The icon library is a git submodule, so clone with submodules:

```bash
git clone --recurse-submodules https://github.com/tsgsOFFICIAL/RustyPainter.git
cd RustyPainter
dotnet run --project RustyPainter.csproj
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

## Releasing

- Bump `<FileVersion>` in `RustyPainter.csproj` and add a section to `CHANGELOG.md`.
- Push a tag like `v0.1.0`: the *Release Build* workflow publishes the Setup.exe and both zips.
- Every push to `master` also publishes a rolling **nightly** prerelease (not offered to update checks).
