# Changelog

All notable changes to Rusty Painter are documented here.

## 0.9.0

### Animated neon signs

- The three animated neon signs can be painted, with up to five frames. Every frame has its own picture and its own place on the canvas, and a new frame starts as a copy of the open one.
- Studio plays the frames as a preview at three speeds: slow shows a frame every two seconds, medium every second and fast twice a second.
- The picture over the game steps through the frames too, at the speed you picked.
- Painting opens each frame in turn, clears it and paints it. The frames after the last picture are cleared as well, so nothing old is left in them. If a frame will not open, painting stops and says so.
- Saved projects keep all their frames.

### About

- A new About page shows the version, the build time, the operating system and the license, so a bug report has what it needs.
- It opens the changelog in its own window, with images and animated GIFs, and it can check for updates.
- It has buttons for GitHub, Discord and Ko-fi.
- The changelog and the notes shown with an update come in English, Danish and Russian. A version that is not translated yet is shown in English.

### Layout

- Every page was checked at several window sizes, in all three languages and both looks. The smallest window is now 1240 pixels wide, because Studio needs that much room for its preview.
- Fixed the Studio header, which squeezed the name of the sign to a few letters, long names that were cut off, the progress bar color, buttons that were hard to read in the light theme, and a search box that cut off its text.

## 0.8.0

### Languages and looks

- The app speaks English, Danish and Russian. It starts in the language of Windows, and the language can be changed in Settings at once, with a flag for each.
- A light theme, next to the dark one. It can follow Windows or be set by hand in Settings.
- Words that depend on a number, such as "1 stroke" and "5 strokes", have the right form in every language, and numbers and dates are written the way each language writes them.

## 0.7.0

### Settings

- A bigger Settings page. The start speed, the picture over the game, how solid it is, the progress box and the painting keys can all be set once and are used every time.
- A button opens the folder where the Library keeps its projects, and a button puts the painting settings back as they were at the start.
- Switches and sliders use the orange of the app.

## 0.6.0

### Library

- Studio can save everything about a picture: the surface, the placement, the colors and the brush. Opening it again gives the same result.
- The Library page shows the saved projects as cards with a small picture of the result. You can search, open, rename, copy and delete them, and a delete asks first.
- Projects are kept as ordinary folders, so they are easy to back up or move to another computer.

## 0.5.0

### Over the game

- The picture can be shown over the game where it will be painted, so the position can be checked before anything is painted. How solid it is can be changed. It never takes a click or the focus.
- A small box at the top of the game shows how far the painting has come, so the app does not have to be in view.
- The footer tells whether Rust is running.
- The painting keys only work while Rust is the window in front, so no other program loses a key.

## 0.4.0

### Painting

- Studio paints the picture into Rust. The painter sets up the paint screen, picks every color and paints the strokes, using the mouse and the keyboard.
- Cells that share a color are painted as one drag, along a row or down a column, whichever is longer. Single cells are clicked.
- A get-ready page shows what is about to be painted, how long it will take and what to do in the game, and the painting page shows the progress.
- Space starts, pauses and resumes. Esc stops. Both can be changed in Settings. Moving the mouse or leaving the game pauses the painting too, and nothing is sent to the game until Rust is in front.
- The canvas can be cleared first, the speed can be chosen, and the position can be fine tuned.
- An optional background color fills the parts of the picture that would be left empty.

## 0.3.0

### Studio

- A new Studio page. Load a picture by dragging it in, browsing, pasting it or pasting a link to it, or type some text and make a picture of it, with its own colors for the letters and the background.
- Place the picture on the canvas with fit, fill or stretch. Zoom, move, rotate and flip it, by the sliders or by dragging and using the wheel on the canvas.
- Colors are matched to the game's palette or to any color, with up to 256 colors, smooth or pattern dithering, and filters: black and white, sepia, invert, brighten and posterize. Brightness, contrast and saturation can be adjusted.
- The brush size can be chosen, from fine to coarse, and the neon signs have their four fixed brushes.
- The result is shown as it would be painted, with a spinner while it is made. A split view puts the original beside it, with a divider you can move.
- The number of strokes, the colors used and a rough time are shown.

## 0.2.0

### Surfaces

- Thirteen surfaces can be painted: four wooden signs, five artist canvases, a portrait picture frame and three neon signs. The size and the place of every canvas were measured in the game.
- The app finds the game window and works at any 16:9 resolution.
- Both of the game's color modes are supported: its palette and any color typed as a hex code.
- Canvases that open zoomed in are zoomed out for you.

## 0.1.1

### Updates

- Test release to check that an installed 0.1.0 can update itself. There are no other changes.

## 0.1.0

### App

- First build with the sidebar, a Home page and a Settings page.

### Updates

- The app checks GitHub for a newer version when it starts and shows what changed.
- It can download and install the new version itself. Installed copies run the new installer, portable copies swap their files, and both restart afterwards.
- Downloads retry on a bad connection and are checked against the checksum GitHub publishes. If an update fails, your installed version is left as it was.
