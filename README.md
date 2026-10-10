# Rusty Painter

**Turn any image, link or piece of text into Rust art.**

Load a picture by dragging it in, pasting a link or typing some text, pick a surface, and Rusty Painter paints it onto signs, frames and banners in Rust, or carves it into a pumpkin.

[![Last Build](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2FtsgsOFFICIAL%2FRustyPainter%2Fbadges%2Flatest-build.json)](https://github.com/tsgsOFFICIAL/RustyPainter/releases)
[![Nightly Build](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/nightly.yml/badge.svg)](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/nightly.yml)
[![Release Build](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/release.yml/badge.svg)](https://github.com/tsgsOFFICIAL/RustyPainter/actions/workflows/release.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-blueviolet)](https://dotnet.microsoft.com/download)
[![Avalonia](https://img.shields.io/badge/Avalonia-12-blue?logo=avaloniaui&logoColor=f5f5f5)](https://avaloniaui.net/)
[![Discord](https://img.shields.io/discord/227048721710317569?logo=discord&logoColor=FFF&label=Discord&labelColor=5865F2)](https://discord.gg/Cddu5aJ)
[![Support on Ko-fi](https://img.shields.io/badge/Support%20me%20on%20Ko--fi-F16061?logo=ko-fi&logoColor=white)](https://ko-fi.com/tsgsOFFICIAL)

## Features

- Paint pictures, links and typed text onto 13 surfaces: wooden signs, artist canvases, a picture frame and neon signs.
- Studio with a live preview, a split view against the original, filters, up to 256 colors and a choice between the game's swatches and any color.
- A hologram of the picture over the game, and a small progress box, so the app does not have to be in view while it paints.
- Hotkeys that only work while Rust is in front: Space starts, pauses and resumes, Esc stops. Both can be changed.
- A Library that keeps your projects, with search, rename, copy and delete.
- English, Danish and Russian, and a light or dark theme (or follow Windows).

## Requirements

Windows 10 or 11 (64-bit)

## Quick Start

Download the latest build from the [Releases](https://github.com/tsgsOFFICIAL/RustyPainter/releases) page. There are three files to choose from:

- `*-Setup.exe` is the guided installer. *(Recommended for most)*
- `*-self-contained.zip` is a portable copy that needs nothing installed.
- `*-framework-dependent.zip` is a smaller portable copy that needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

The app checks GitHub for a newer version when it starts and can update itself.

## Building from Source

The icon library is a git submodule, so clone with submodules:

```bash
git clone --recurse-submodules https://github.com/tsgsOFFICIAL/RustyPainter.git
cd RustyPainter
dotnet run --project RustyPainter.csproj
```

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The tests run with `dotnet test tests/RustyPainter.Tests`.
