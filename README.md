# SnowDock

A Mac-style dock for Windows 11 that replaces the taskbar, with the look of every Dock from Mac OS X 10.0 to
macOS 26 (Snow Leopard by default). It has the magnification wave, labels, running-app indicators, launch and
attention bounces, dock menus, folder stacks (fan, grid and list), Dock Exposé, live widgets, drag and drop,
auto-hide, and top, bottom, left or right placement.

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

### Install and uninstall

Right-click the dock and choose **Install SnowDock**, or run `SnowDock.exe --install`. This:
- copies SnowDock to `%LocalAppData%\Programs\SnowDock` (no admin rights needed)
- adds a Start menu shortcut
- lists SnowDock in **Settings → Apps → Installed apps**

To remove it, choose **Uninstall** there (or run `SnowDock.exe --uninstall`). This brings the taskbar back and
removes everything except your settings in `%AppData%\SnowDock`.

### Updates

SnowDock checks this repository's GitHub Releases a minute after it starts and every six hours after that. When a
newer version is out, an **Update** button appears above the dock (and in the dock menu).
- **Installed copy:** one click downloads the release zip, swaps the files once SnowDock has closed, and starts
  the new version.
- **Any other copy:** the button opens the release page.

Only GitHub's public releases API is contacted.

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

## Widgets

Widgets are live tiles in their own section of the dock, between your apps and the stacks. Right-click the dock
and choose **Widgets…** to open the picker: browse by category or search, and click a widget to add it. Click it
again to take it off. While the picker is open, each widget in the dock shows a **−** badge you can click to
remove it.

Drag a widget along the dock to reorder it, or drag it off to remove it. Click a widget to use it. Most open a
card with more detail or controls; Esc or a click elsewhere closes the card.

There are 96 widgets.

| Category | Widgets |
|---|---|
| Clocks | Clock, Flip Clock, Segment Clock, Analog Clock, Minimal Analog, World Time, World Clocks (three cities), Date, Calendar Page, 7 Days, Today, Clock & Weather, Day / Month / Year Progress |
| Productivity | Timer, Stopwatch (with laps), Pomodoro, To-Dos, Notes, Calendar (your next event), Currency, Screen Time, Day Timeline |
| System | Tray (your notification-area icons), CPU, Memory, Top CPU, Top Memory, Network, Storage, Temperature, Battery (five styles), Quick Toggles (Wi-Fi, Bluetooth, hotspot, airplane), Brightness, Do Not Disturb, Start, Show Desktop, Notifications, Quick Settings, On-Screen Keyboard, Screenshot, Light / Dark, Keyboard Language |
| Media & Audio | Now Playing (full, compact, mini, bar) for whatever's playing in any app, Volume, Microphone & Sound mute |
| Apps & Files | Search (web, or a path to open), Commands (Start-menu apps and system actions), Recently Opened |
| Weather | Weather, Details, Forecast, Status, Hourly, Sunrise & Sunset (for a city you type; [Open-Meteo](https://open-meteo.com), no key) |
| Stocks | Stock Chart, Stock Watchlist ([Finnhub](https://finnhub.io) key), Crypto ([Binance](https://www.binance.com) public prices, no key) |
| Finance | 30-day revenue and a 14-day chart for Stripe, Polar, Dodo Payments, Shopify, Lemon Squeezy, Paddle and Gumroad, plus Total Revenue with a monthly goal |
| Analytics | Plausible, DataFast, Dub, Search Console |
| Developer | Resend, Linear, Supabase |
| Online & Mail | GitHub notifications, GitHub Activity (contribution grid), GitHub Overview, LeetCode, Gmail |
| Social | YouTube, Instagram, X |

- **Where data lives:** to-dos, notes, screen time and the weather city are saved next to the settings file.
- **Side docks:** widgets only show on top and bottom docks. A left or right dock hides them, because there's no
  room for wide tiles.

### Connecting accounts

Widgets that read an online account show a sample, marked **SAMPLE**, until you connect them. Click the widget:
its card says what to create in that service (usually a read-only key) and where to paste it.
- **Keys stay on this PC:** they're encrypted for your Windows account (DPAPI) in `keys.json`, used only for the
  service they belong to, and never written to the settings file or the log. **Disconnect** in the card forgets
  a key.
- **Gentle refreshing:** most account widgets refresh every 10 to 60 minutes. A store's revenue tile, its chart and
  Total Revenue share one request.
- **Your own Google client:** Gmail and Search Console need a free Google Cloud OAuth client (Desktop app). The card
  walks you through it, and you sign in once in your browser.
- **Paid access:** X's API is billed per request, so that widget refreshes only every two hours. Dub's analytics
  API needs Dub's Pro plan.
- **Resend:** it has no read-only keys, so its widget needs a full-access key.
- **Not tested against real accounts:** every account widget follows that service's official API docs (the links
  are in the code), but none has been run against a real account. LeetCode, Open-Meteo, Binance and the currency
  rates were checked live.

### The tray

The **Tray** widget shows the icons that normally sit in the taskbar's notification area, including hidden ones.
Click or right-click an icon as you would on the taskbar. The card lists them all.
- **How it works:** it listens for tray icons alongside Explorer, the way open-source shells such as RetroBar do,
  and passes every message on, so Explorer stays in sync. Nothing is injected into Explorer.
- **Not on Windows 11:** network, volume, battery and the clock are drawn by Explorer itself, so they don't
  appear. Use Quick Settings (Win+A) or the Quick Toggles and Volume widgets.
- **Real mode only:** clicks only reach apps in real mode, not in `--preview`.
- **Untested:** clicks and menus haven't been tried with real apps yet.

**Not included:** AI plan-usage widgets, TikTok, and WhatsApp unread counts.

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

On the left, right or top, the 3D-shelf styles switch to a flat panel, as those releases did away from the bottom.
The other styles keep their look on every edge.

There are also four plain looks that aren't tied to a Mac release: **Black**, **White**, **Midnight** and
**Frosted Glass**.

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
- **Hiding** on or off: auto-hide slides the dock off the screen edge. Push the mouse against that edge, or open
  Start, to bring it back. While hiding, the dock doesn't reserve screen space.
- **Widgets…**: the widget picker (see Widgets above)
- **Size: Small / Medium / Large**
- **24-Hour Clock**: for the clock widgets. It follows your Windows time format until you change it.
- **Position on Left / Bottom / Right / Top**. You can also drag an empty spot of the dock and let go near
  another edge (or on another monitor), and it moves there.
  - Side docks use the flat 2D look, with labels pointing into the screen.
  - Away from the bottom, stacks open as lists, since fans and the grid's pointer only work from the bottom.
- **Show on All Displays** (with more than one display): a dock on every display, with each display's taskbar
  hidden. Otherwise use **Show on Display N** to pick one.
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
- [ ] Widgets…: add a clock, the CPU ring and a timer; remove one with its − badge; drag one to a new spot;
      click the timer (it starts) and the to-dos (a card opens)
- [ ] Drag an empty spot of the dock to the top of the screen and back; try Small and Large sizes
- [ ] In real mode, add the Tray widget: left-click and right-click Discord's or Voicemeeter's icon
- [ ] Now Playing's buttons, the Volume widget (click, wheel), Quick Toggles and Brightness
- [ ] Type a city into a weather widget; paste one real key (GitHub is easy) and see its SAMPLE mark go away
- [ ] With more than one display: Show on All Displays
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
| `Edge` | `Bottom` | `Left`, `Bottom`, `Right` or `Top` |
| `Widgets` | none | The widgets in the dock, in order, each with its own `Settings` |
| `Clock24h` | (your Windows format) | `true` or `false` for the clock widgets |
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
SnowDock.exe --snapshot out.png --widgets clock,cpu,timer [--edit]   # show these widgets (not saved); --edit adds remove badges
SnowDock.exe --snapshot out.png --picker | --card TYPE      # the widget picker, or one widget's card
SnowDock.exe --snapshot out.png --menus                     # dark and light dock menus side by side
SnowDock.exe --snapshot out.txt --switch-test               # switch styles live (not saved), log the window size
SnowDock.exe --snapshot out.png --drag FROM:TO [--drop]     # --drop really saves the new order
SnowDock.exe --snapshot out.png --fan K | --grid K | --list K  # the K-th stack (from 0)
SnowDock.exe --snapshot out.png --expose                    # Exposé layout with placeholders
SnowDock.exe --snapshot out.txt --drop-plans "a|b|c"        # what dropping those paths would do, per spot
SnowDock.exe --poof-preview out.png
```

## Known limits

- **Side docks:** stacks open as lists, not fans or grids, and widgets are hidden.
- **Mixed scaling:** if your displays use different scaling, the dragged icon may flicker or resize when it crosses
  onto the other one.
- **Windows won't let apps switch some things:** airplane mode and Do Not Disturb. Those widgets open the
  matching Windows panel instead.
- **CPU temperature:** many PCs only report it to administrators, and some never do. The Temperature widget then
  shows n/a.
- **Stock charts:** Finnhub's free plan has no intraday history, so the stock line builds up while the dock runs.

## Third-party notices

The tray listener follows the approach of [ManagedShell](https://github.com/cairoshell/ManagedShell)
(Apache-2.0). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
