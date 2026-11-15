# SnowDock

A Mac-style dock for Windows 11 that replaces the taskbar, with the look of every Dock from Mac OS X 10.0 to
macOS 26 (Snow Leopard by default). It has the magnification wave, labels, running-app indicators, launch and
attention bounces, dock menus, folder stacks (fan, grid and list), Dock Exposé, drag and drop, auto-hide, and
left, bottom or right placement.

Everything is drawn in code (`DockSurface.Render.cs`). No Apple artwork is included. App icons and thumbnails
come from Windows and from the apps themselves.

## Build and run

Requires the .NET 8 SDK.

```
dotnet build
```

**Try it safely first.** Preview mode sits above your normal taskbar and doesn't touch it:

```
bin\Debug\net8.0-windows\SnowDock.exe --preview
```

**Real mode** hides the Windows taskbar and reserves the dock's edge of the screen:

```
bin\Debug\net8.0-windows\SnowDock.exe
```

To quit, right-click an empty part of the shelf and choose **Quit SnowDock and Restore Taskbar**.

For everyday use, publish a build that doesn't depend on your source folder:

```
dotnet publish -c Release -o publish
```

Then run `publish\SnowDock.exe`. Turn on **Start SnowDock at Sign-In** from that copy, because the sign-in entry
points at whichever exe you turn it on from.

## If the taskbar doesn't come back

If SnowDock is killed (Task Manager, crash, debugger stop), it can't restore the taskbar. Any one of these fixes it:

1. Run `SnowDock.exe --restore-taskbar`. This also restores your original auto-hide setting.
2. Restart Explorer: Task Manager → *Windows Explorer* → *Restart*.
3. Sign out and back in.

## What hiding the taskbar costs you

Hiding the taskbar also hides everything else on it:

- **The system tray.** Apps that live there (Discord, OBS, Voicemeeter, Voicemod, …) are only reachable when they
  have a window open.
- **The clock, Quick Settings and notifications.** Win+A and Win+N should still open them.
- **The Start button.** The Windows key should still open Start.

Set `"HideTaskbar": false` if you'd rather keep the taskbar and run the dock beside it.

## Styles

Right-click an empty part of the dock and choose **Dock Style…** to switch. The change is immediate and saved.
Releases that shared a look share a style:

| Style | Releases | Look |
|---|---|---|
| Cheetah & Puma | 10.0–10.1 | Pinstriped translucent white bar on the screen edge, black triangles, outlined labels |
| Jaguar, Panther & Tiger | 10.2–10.4 | The same bar without pinstripes |
| Leopard | 10.5 | 3D glass shelf with reflections, blue orb indicators, light menus |
| Snow Leopard & Lion | 10.6–10.7 | The shelf with blue-white dots and dark menus (default) |
| Mountain Lion | 10.8 | Frosted shelf, fainter reflections, horizontal light-bar indicators |
| Mavericks | 10.9 | Smokier frosted shelf, light-bar indicators |
| Yosemite to Catalina | 10.10–10.15 | Flat translucent bar on the edge, small dots, labels with a pointer; light or dark |
| Big Sur to Sequoia | 11–15 | Floating, rounded, translucent; light or dark |
| Tahoe | 26 | Liquid Glass: a clear floating slab with a bright rim and sheen; light or dark |

On the left or right, the 3D-shelf styles switch to a flat panel, as those releases did. The other styles keep
their look on the sides.

**What's approximate:**
- Everything is drawn in code to resemble each era. No Apple artwork is used.
- The indicators and overall materials follow published descriptions: triangles in 10.0–10.4, a blue dot in
  10.5–10.7, a light bar in 10.8–10.9, dots from 10.10 on, Jaguar dropping the pinstripes, Big Sur's floating
  rounded dock, and Tahoe's Liquid Glass.
- Label treatments, exact colours and Mavericks' tint are best guesses.
- The real Docks from Yosemite on blur what's behind them, and Tahoe's glass bends it. A transparent Windows
  window can't do either, so those styles are tinted instead.
- Fans, grids, stack lists and Exposé keep the same look in every style.

## Using it

### Apps
- **Click** an app that isn't running to launch it. It bounces until its first window appears.
- **Click** a running app to bring its windows forward. If they're all minimized, the most recent one is restored.
  Like the Mac, clicking doesn't minimize.
- **Click and hold** a running app for **Dock Exposé**: its windows spread out as live previews over the dimmed
  screen. Click one to bring it forward. Click elsewhere or press Esc to go back.
- Apps that flash for attention bounce.
- **Right-click** an app for its window list, **Keep in Dock**, **Show in File Explorer**, **Hide** and **Quit**.
- File Explorer plays the Finder's part: it is always first and can't be removed or moved.

### Rearranging
- **Drag** an icon along the dock to reorder it. The other icons part to show where it will land.
  - Dropping a running app that isn't in the Dock pins it at that spot.
  - Dragging a pinned icon off the dock (it fades when you're far enough away) removes it. If the app isn't running,
    it disappears in a puff of smoke. If it is running, it stays until the app quits.
  - Esc cancels a drag. File Explorer, stacks and the Recycle Bin can't be dragged.

### Dropping files from File Explorer
| Drop | onto | does |
|---|---|---|
| apps (`.exe`, `.lnk`) | a gap between icons | pins them there |
| files | the middle of an app | opens them with that app |
| files | the middle of a stack | moves them in (same drive) or copies them (other drive); hold Ctrl to copy, Shift to move |
| files | the middle of the Recycle Bin | moves them to the Recycle Bin |
| folders | anywhere else | adds them as stacks |
| anything | File Explorer | shows it in File Explorer |

The target icon darkens and shows its name while you hover. Copies, moves and recycling go through Windows itself,
so you get its usual progress, "replace or skip" and confirmation dialogs.

Some files can't go to the Recycle Bin: files on network shares, on some USB drives, or too big for the bin.
Windows would delete those permanently, so it warns you first and you can cancel.

Drops on launcher shortcuts that carry their own arguments (like Discord's) and on Store apps are ignored.

### Stacks
Documents and Downloads sit beside the Recycle Bin as **stacks**. Their dock icon is a little pile of the newest
items. **Click** a stack to open it:
- **Fan:** the newest items arc up out of the dock, newest at the bottom. The top row opens the folder
  ("N More in File Explorer" when the fan can't show everything).
- **Grid:** a dark panel of icons that scrolls. Click a folder to step into it, and use **Back** to return.
- **List:** a dark menu of the folder's items. Folders open as submenus.
- **Automatic:** a fan when everything fits, otherwise a grid.

Close a stack by clicking anywhere else, clicking the stack again, or pressing Esc (Backspace goes back in the
grid). **Right-click** a stack to choose its view, sort it (Name, Date Added, Date Modified, Kind), show it as a
pile or as the folder's icon, open it, or remove it. Stacks update on their own when files arrive or change.

Windows doesn't record when a file was added to a folder, so **Date Added** uses the file's creation time.
Downloading or copying a file sets that time, so it works the same way.

### The dock menu
Right-click an empty part of the shelf for:
- **Magnification** on or off
- **Dock Style…**: pick an era (see Styles above)
- **Hiding** on or off: auto-hide slides the dock off the screen edge. Push the mouse against that edge to bring it
  back. While hiding, the dock doesn't reserve screen space.
- **Position on Left / Bottom / Right**: side docks use the flat 2D look, with labels pointing into the screen.
  Stacks open as lists there, since fans and the grid's pointer only work from the bottom.
- **Show on Display N** (with more than one monitor)
- **Start SnowDock at Sign-In** (off until you turn it on)
- **Add Folder Stack…**, **Dock Settings…**, **Quit**

The dock hides itself while a full-screen app or game is in front.

## First-run checklist

Hover is confirmed with a real mouse. Everything else has only been checked in offscreen renders, plus one short
live Exposé run. Check these in `--preview` first:

- [ ] Clicking a running app brings it forward; clicking a pinned app launches it with a bounce
- [ ] Right-click opens the dark menu, and clicking elsewhere (or Esc) closes it
- [ ] Dragging an icon opens a gap under it and drops it there; dragging one off removes it
- [ ] Click and hold a running app opens Exposé, and its tiles show the live window content (not just the app's
      icon); clicking a window brings it forward; Esc goes back
- [ ] Downloads opens as a fan; switch it to Grid and List from its right-click menu and try each
- [ ] Drag a file from File Explorer onto an app, onto Downloads, and a folder onto an empty spot
- [ ] Turn hiding on, then off; move the dock left and right, then back to the bottom; switch displays
- [ ] Switch through a few Dock Styles (one shelf, one flat, one floating): the dock redraws, menus change between
      light and dark, and in real mode maximized windows still stop short of it
- [ ] Then in real mode: the taskbar disappears, maximized windows stop short of the dock, and Win, Win+A and
      Win+N still work
- [ ] In real mode with Hiding on: pushing the mouse against the bottom edge brings back the dock, not the Windows
      taskbar (SnowDock switches the taskbar to auto-hide, so it may flash briefly)

## Settings

The settings file is `%AppData%\SnowDock\config.json`. Most settings are also in the dock menu. If you edit the
file, restart the dock.

| Setting | Default | |
|---|---|---|
| `Style` | `snow-leopard` | `cheetah`, `jaguar`, `leopard`, `snow-leopard`, `mountain-lion`, `mavericks`, `yosemite`, `yosemite-dark`, `big-sur`, `big-sur-dark`, `tahoe`, `tahoe-dark` |
| `IconSize` | 52 | Icon size at rest (DIPs) |
| `Magnification` | true | Magnify icons under the mouse |
| `MagnifiedSize` | 96 | Largest magnified size |
| `HideTaskbar` | true | Hide the Windows taskbar in real mode |
| `ReserveScreenSpace` | true | Maximized windows stop short of the dock |
| `AutoHide` | false | Slide the dock off the edge until the mouse touches it |
| `Edge` | `Bottom` | `Left`, `Bottom` or `Right` |
| `Monitor` | (main) | Device name of the display to use, e.g. `\\.\DISPLAY2` |
| `ShowRecycleBin` | true | Recycle Bin after the separator |
| `Pinned` | your taskbar pins | Imported once from your taskbar pins |
| `Stacks` | Documents, Downloads | `Path`, optional `Name`, `Sort`, `Display` (`Stack`/`Folder`), `View` (`Fan`/`Grid`/`List`/`Automatic`) |
| `FanMaxItems` | 14 | Most items a fan shows (fewer if the screen is short) |

The log is `%AppData%\SnowDock\snowdock.log`.

## Debugging

All of these render or plan offscreen. They never move the mouse, and nothing is saved unless noted.

```
SnowDock.exe --snapshot out.png [--hover N] [--style id] [--backdrop light] [--edge Left|Right] [--hide 0..1] [--dump-icons dir]
SnowDock.exe --snapshot out.png --menus                     # dark and light dock menus side by side
SnowDock.exe --snapshot out.txt --switch-test               # switch styles live (not saved), log the window size
SnowDock.exe --snapshot out.png --drag FROM:TO [--drop]     # --drop really saves the new order
SnowDock.exe --snapshot out.png --fan K | --grid K | --list K  # the K-th stack (from 0)
SnowDock.exe --snapshot out.png --expose                    # Exposé layout with placeholders
SnowDock.exe --snapshot out.txt --drop-plans "a|b|c"        # what dropping those paths would do, per spot
SnowDock.exe --poof-preview out.png
```

## Known limits

- **Side docks:** stacks open as lists, not fans or grids.
- **Mixed scaling:** if your displays use different scaling, the dragged icon may flicker or resize when it crosses
  onto the other one.
- **System tray:** there's no replacement for the tray (see above).
