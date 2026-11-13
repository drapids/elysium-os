# heft

a whole new layer on top of windows. glass top bar, an arc dock on the screen edge, a shelf that drops down from the top, a launcher, AudioFlow, notifications that actually look good, and you can still throw your windows around.

## the main stuff

**top bar** sits on every monitor (or just the main one). the pill in the middle changes with what's going on: song + controls when music plays, download speed when something's downloading, progress when you copy files, a pulsing dot when an app wants you, a countdown when a timer's running. otherwise it's just the clock. click it for the control center.

**control center** has cpu / gpu / ram / network / battery, focus mode, cursor lock, desktop icons, physics, taskbar, paste stack, wi-fi and bluetooth toggles, sound output, your latest clipboard and audio profiles.

**AudioFlow** opens from the volume in the bar. volume and live levels per app, mute per app, send an app to a different output, switch outputs and mics, save setups as profiles (gaming, music, whatever) and give them hotkeys. plug in headphones and it can switch to them automatically.

**dock** fans out from the screen edge. scroll, drag, ctrl+scroll to zoom out to folders. with two monitors it only lives on the outer edges, never the seam in the middle.

**shelf** drags down from the top bar.
- shelf: drop files, text or images to keep them close. drag them back out to move (hold ctrl to copy)
- catalog: your folders as books. click one to walk in, drop files on one to move them there, hover with a file to spring it open
- collections that fill themselves: recent, downloads, images, code, docs, archives, large, unsorted
- your own collections that point at files wherever they live
- sort by modified, name, last opened, size, type, color, how often you open it, or relevance
- pocket a window (ctrl + alt + p) and it hides on the shelf with a live preview
- drag something onto the bar and you get drop actions: shelf, zip, to png, to jpg, copy path, collection, inspect

**inspect anything.** space or right-click on anything in the dock, shelf or launcher and it zooms into a card with a preview, info, related files and what you can do with it. ctrl + alt + i does it for whatever is selected in File Explorer.

**launcher** is alt + space.

| type | does |
|---|---|
| anything | apps, files, commands, settings, math, all fuzzy (vsco finds VS Code) |
| `?` | web search |
| `>` | run a command, or `> alias yt = https://youtube.com` to make your own |
| `r ` | file search |
| `@` | apps |
| `#` | windows settings |
| `=` | calculator + units (`5 km to mi`) |
| `!` | system stuff (lock, sleep, restart, empty recycle bin...) |
| `:` | clipboard history, enter pastes it |
| `&` | heft stuff |
| `/` | walk folders, tab goes in |

plain english works too: `pdfs from this week`, `big videos in downloads`, `screenshots from yesterday`.

pipes: `r screenshots > collection Memes`, `pdfs this week > zip`, `@ vscode > open`, `big files in downloads > delete` (asks twice).

reminders and timers: `remind me in 10 min to stretch`, `remind me at 5pm to call grandma`, `timer 25m`, `pomodoro`.

ctrl + p pins a result. stuff you use a lot floats up.

your own commands can chain steps: `study = &focus ; spotify: ; wait 2 ; https://docs.google.com`

**notifications.** heft shows Windows notifications its own way and turns off Windows' popups for those apps so you don't get both. there's a searchable history grouped by app, and per-app rules: pop up, silent, or blocked. focus mode mutes the apps you pick and holds notifications.

**start.** tap the windows key and a fullscreen glass start fades in over everything: a big clock, search (just start typing), your real Windows Start pins with their groups and sizes, the apps you use most, recent files, your taskbar apps along the bottom, and widgets on the side for weather, what's playing, quick toggles, cpu / ram / gpu, reminders and power. "all apps" has every app A to Z. ctrl + esc still opens the old start.

**overview.** ctrl + alt + w (or the top-left corner) zooms out to every window on that screen as a live preview. click one to jump to it, type to filter, middle-click to close it.

**hot corners.** push the mouse into a corner of any screen: top-left is overview, bottom-left is start by default. you can set each corner to start, overview, control center or show desktop. fullscreen games are left alone.

**everything else**
- hide desktop icons (nothing gets deleted). double-click the desktop or ctrl + alt + e to peek
- taskbar: clear / blur / acrylic, auto-hide or gone, centered icons, hide start and tray
- scroll a window's title bar to make it see-through, middle-click it to pin it on top
- scroll the screen edge for brightness, scroll the top bar for volume, middle-click the bar to play/pause
- cursor lock keeps your mouse on one screen for games (ctrl + alt + l, or automatic in fullscreen)
- paste stack: ctrl + alt + c, copy a bunch of things, then ctrl + alt + v pastes them in order
- share your setup as a `.heftpack` file and import a friend's

## keys

| | |
|---|---|
| alt + space | launcher |
| ctrl + alt + space | dock |
| ctrl + alt + s | shelf |
| ctrl + alt + a | AudioFlow |
| ctrl + alt + n | notifications |
| ctrl + alt + k | control center |
| ctrl + alt + h | settings |
| ctrl + alt + l | cursor lock |
| ctrl + alt + f | focus |
| ctrl + alt + p | pocket the current window |
| ctrl + alt + c / v | paste stack on, paste next |
| ctrl + alt + i | inspect what's selected in Explorer |
| ctrl + alt + d | next audio output |
| ctrl + alt + e | peek desktop icons |
| ctrl + alt + w | overview |
| windows key | start |

all of them can be changed in settings > keys.

## build

needs the .NET 8 SDK, windows 10 or 11.

```
dotnet run
```

to make the exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish
```

put `publish\heft.exe` somewhere it'll stay, open it, then settings > general > start with windows. that sets up a task that runs heft the second you sign in, before the normal startup apps.

settings live in `%AppData%\heft\settings.json`, and `%AppData%\heft\log.txt` has what heft saw if something acts weird.

## heads up

- windows of apps running as admin can't be thrown or pocketed unless heft runs as admin too
- if heft crashes while it's handling notifications, Windows' popups stay off until you open heft again (or flip the switch off in settings > alerts)
- per-app audio routing uses the same windows feature as the sound settings page, so it works on windows 10 2004 and newer
- making GIFs and shrinking videos for discord shows up in inspect when ffmpeg is installed

made by mason arcieri
