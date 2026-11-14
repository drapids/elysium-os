# elysium

a whole new layer on top of windows. glass top bar, an arc dock on the screen edge, a shelf that drops down from the top, a launcher, AudioFlow, notifications that actually look good, and you can still throw your windows around.

## the main stuff

**top bar** sits on every monitor (or just the main one). the pill in the middle changes with what's going on: song + controls when music plays, download speed when something's downloading, progress when you copy files, a pulsing dot when an app wants you, a countdown when a timer's running. otherwise it's just the clock. click it for the control center.

**control center** has cpu / gpu / ram / network / battery, focus mode, cursor lock, desktop icons, physics, taskbar, paste stack, wi-fi and bluetooth toggles, sound output, your latest clipboard and audio profiles.

**AudioFlow** opens from the volume in the bar. volume and live levels per app, mute per app, send an app to a different output, switch outputs and mics, save setups as profiles (gaming, music, whatever) and give them hotkeys. plug in headphones and it can switch to them automatically.

**dock** fans out from the screen edge. scroll, drag, ctrl+scroll to zoom out to folders. with two monitors it only lives on the outer edges, never the seam in the middle.

**shelf** drags down from the top bar. it stays open while you go grab stuff from other windows, so you can actually drag things in. esc, the x or clicking the bar closes it. pin it and only the x does.
- drag a file up to the top bar and the bar opens into a thin slot. drop it and it tips in like mail. hold it over the bar for a sec to open the full shelf
- grab stuff back out easily: drag starts right away, ctrl or shift click (or drag a box) to pick a bunch, "drag all" grabs the whole shelf. dragging out moves by default, hold ctrl to copy. there's a bar for move to, copy to, zip, send to phone and take off shelf
- shelf: drop files, text or images to keep them close. drag them back out to move (hold ctrl to copy)
- catalog: your folders as books. click one to walk in, drop files on one to move them there, hover with a file to spring it open. inside a folder the files sit face-out on a magazine rack under the books
- collections that fill themselves: recent, downloads, images, code, docs, archives, large, unsorted, projects (anything with git, package.json, .sln...) and duplicates
- your own collections that point at files wherever they live
- sort by modified, name, last opened, size, type, color, how often you open it, or relevance
- pocket a window (ctrl + alt + p) and it hides on the shelf with a live preview
- drag something onto the bar and you get drop actions: shelf, zip, to png, to jpg, copy path, collection, inspect
- once it gets busy the same kind of stuff stacks into piles. click a pile to spread it out, drag it to take the whole thing

**quick look.** select a file in File Explorer and hit space (or space on anything in the dock, shelf or launcher). it opens right away with a big preview and a toolbar: open, show in folder, copy, rename, send to phone, delete, and ⋯ for everything else. arrows flip through the rest of the folder, delete sends it to the recycle bin, f2 renames, i shows the info panel (size, dates, related files, project commits, older versions). right-click anything on the shelf for open / quick look / copy / rename / delete, or just hit delete.

**spaces** (ctrl + alt + g) is a zoomable desktop. drag files in, double-click for a note, right-click for widgets (clock, now playing, weather, smart folders, a live portal to any window), make groups and drag them around with everything inside. scroll to zoom, drag empty space to pan, t tidies up, f fits everything, g snaps to the grid (hold alt to place freely). pictures show up as actual previews, right-click something to draw an arrow to something else, and the minimap in the corner gets you around big spaces. you can have a bunch of spaces and each one can remember which windows were open and where. with more than one virtual desktop, each desktop gets its own space.

**window stacks.** drag a window's title bar onto another window's title bar and hold for a sec. they turn into tabs. click a tab to switch, scroll the tabs to flip through, middle-click one to pull it back out.

**window lerp.** turn it on in settings > windows and dragging a window by its title bar makes it trail your mouse smoothly instead of sticking to it. sliders for how laggy and how wobbly. fling it and it still flies. hold ctrl to drag the old way.

**picture in picture.** ctrl + alt + o pops the window you're on into a small always-on-top window in the corner. scroll its little bar to resize, same keys put it back.

**send to phone.** inspect a file > send to phone (or `> share` in the launcher, or ctrl + alt + u in File Explorer). a qr code pops up, scan it and the file downloads straight to your phone over your wi-fi. nothing goes through the internet.

**screenshots, text and colors** (ctrl + alt + x). the screen freezes and dims, a little toolbar shows up up top, and 1-7 switches what it does:
- shot: drag a box or just click a window. then copy, save, pin it on top, mark it up, send it to the shelf, a space or your phone
- text: box anything and the text gets copied (windows' own text reading, works offline)
- color: a zoomed loupe follows your mouse, click copies the hex, shift+click saves it to your colors
- palette: box something and get its main colors, copy them as hex, css variables or an image
- qr: box a qr code to open or copy it
- measure: drag to see the distance in pixels
- record: box an area and record it as a gif or mp4 (needs ffmpeg)

pinned shots float on top. drag them around, scroll to resize, ctrl+scroll for see-through, double-click to close. markup has arrows, boxes, a highlighter, a pen, blur and text.

**snap zones.** hold shift while you drag a window and zones show up. drop it in one and it fills it. pick a layout per monitor (halves, quarters, thirds, focus, mix, or auto which picks by screen shape) and the gap in settings > windows.

**window rules.** put an app where you want it and run "remember where this window goes" (or the button in settings > windows). next time it opens it goes right there, maximized, or centered, on the same monitor. can keep it on top too.

**reopen closed apps.** ctrl + alt + z brings back the last app you closed. start has a recently closed card too.

**game mode.** when something goes fullscreen elysium goes quiet: no sounds, slower polling, no gestures, notifications wait in the bell and you get one summary after. can ignore the windows key while you play.

**modes.** a mode flips a bunch at once: light or dark, accent, focus, sounds, audio profile. switch with a hotkey, from the launcher, at a time of day, or when an app opens (like gaming mode when valorant starts). settings > alerts.

**text shortcuts.** type `;date` anywhere and it turns into the date. make your own in settings > keys. `{date}` `{time}` `{day}` `{iso}` `{clipboard}` fill themselves in.

**mouse gestures** (off by default). hold right click, draw, let go: up for overview, down minimizes, left / right go back and forward, up-down refreshes. set them all in settings > keys. a normal right click still works.

**scratchpad.** ctrl + alt + q or the note icon in the bar. a little pad that saves as you type.

**last download.** ctrl + alt + j pops your newest download right at your mouse, drag it straight into discord or an upload box. it also sits in the bar for a bit after something downloads.

**from your phone.** "send files from your phone" in the launcher shows a qr code. scan it, pick photos or whatever, they land in Downloads\from phone and on the shelf. same wi-fi, nothing in the cloud.

**desktops.** dots in the bar for each virtual desktop, click one to jump there.

**look.** settings > look: accent from windows, your wallpaper, or any color. how see-through the glass is, how round the corners are, how fast stuff animates, the font, and a different bar color per monitor. icon packs too: drop pngs named after apps (chrome.png, code.png) in a folder in `%AppData%\elysium\icons` and pick it.

**bar editor.** settings > bar & desktop: move anything in the bar left or right, reorder it, or turn it off.

**extensions.** settings > extensions. an extension is a folder with a `manifest.json` and a `main.js`, it runs inside elysium and can only do what its manifest asks for. it can add launcher commands, shortcuts, stuff in the top bar, cards on start, shelf drop actions, react to songs, windows, downloads and the clipboard, and fetch things from the web. save the file and elysium reloads it. share one by zipping the folder and renaming it to `.elysiumext`. comes with pomodoro, github notifications, world clock, what's playing and synced lyrics to start from.

**sounds.** off by default. turn them on in settings > general, pick glass, soft or retro, set the volume, pick which things make noise. they get quieter while music plays. make your own pack by putting wavs named open, close, drop, notify, chime, throw, stack and tick in a folder in `%AppData%\elysium\sounds`.

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
| `&` | elysium stuff |
| `/` | walk folders, tab goes in |

plain english works too: `pdfs from this week`, `big videos in downloads`, `screenshots from yesterday`.

rename a bunch at once: `r screenshots > rename trip-{n}`. `{n}` counts up, `{name}` is the old name, `{date}` is when it changed. "undo rename" puts them back.

pipes: `r screenshots > collection Memes`, `pdfs this week > zip`, `@ vscode > open`, `big files in downloads > delete` (asks twice).

reminders and timers: `remind me in 10 min to stretch`, `remind me at 5pm to call grandma`, `timer 25m`, `pomodoro`.

ctrl + p pins a result. stuff you use a lot floats up.

your own commands can chain steps: `study = &focus ; spotify: ; wait 2 ; https://docs.google.com`

**the dock** shows a little red count on apps with unread notifications.

**notifications.** elysium shows Windows notifications its own way and turns off Windows' popups for those apps so you don't get both. there's a searchable history grouped by app, and per-app rules: pop up, silent, or blocked. focus mode mutes the apps you pick and holds notifications.

**start.** tap the windows key and a fullscreen glass start fades in over everything: a big clock, search (just start typing), your pinned apps, the apps you use most, recent files, your taskbar apps along the bottom, and widgets on the side for weather, what's playing, quick toggles, cpu / ram / gpu, reminders and power. "all apps" has every app A to Z. ctrl + esc still opens the old start.
- a row of everything you have open with live previews. click to switch, hover for the x, middle-click to close, right-click for minimize / close / end task
- taskbar apps get a little dot when they're running. click to jump to it, right-click for a new window, every window it has open, or close them all
- pins are elysium's own now (it copies your Windows pins the first time). drag to reorder, drop one on another to make a folder, right-click for run as admin, open file location, unpin or uninstall
- a tools card for task manager, file explorer, settings, terminal, run and overview
- what's playing updates live when the song changes
- optional screen time card that shows how long you were in each app today (stays on your pc)

**overview.** ctrl + alt + w (or the top-left corner) zooms out to every window on that screen as a live preview. click one to jump to it, type to filter, middle-click to close it.
- right-click a window: move it to the other screen (works on fullscreen games too, it flips fullscreen off and back on if it has to), borderless fullscreen, snap to a zone, maximize / minimize, always on top, picture in picture, pocket it, tab it with another window, mute it, screenshot or record it, remember where it goes, add a note, show the app's file, inspect, close or end task
- frozen windows say "not responding", windows making sound get a speaker you can click to mute
- the screens along the top: click one to look at that screen, drag a window onto one to send it there
- keys: arrows to move around, shift + arrows sends it to the next screen, delete closes it, 1-9 jumps straight to one, space zooms in, ctrl-click a few and press t to tile them
- minimized windows show up dimmed at the end, "group by app" keeps the same app together, and recently closed apps sit along the bottom

**workspaces.** drag a window onto another one's title bar and they become tabs. the tabs sit right above the window so they never cover anything. + adds a tab (stuff you have open, apps you usually pair with this one, or your favorites), double-click the name to rename it, and the ⋯ menu can minimize the whole thing into a floating pill you can drag around, keep it on top of everything or under everything, ungroup it or close every tab. scroll the tabs to flip through them.

**themes.** settings > look has theme packs that restyle the bar, the dock, start, the glass, the accent and the corners all at once. it comes with frutiger aero, midnight, paper, y2k chrome, terminal and sunset. they can change your wallpaper and cursor too (turn that off right there, and picking the elysium look puts your old ones back). you can save your current look as a theme, share it as a `.elysiumtheme` file, install other people's, or grab them from the gallery. a theme is just a folder with a `theme.json` in `%AppData%\elysium\themes`, so you can go wild with gradients in there. drop a `cursors` folder with `arrow.cur`, `hand.cur` and so on in it and the theme uses those.

**gallery.** settings > look and settings > extensions can browse themes and extensions from a github repo (`drapids/elysium-gallery` by default, `gallery/` in here has the format). free, no account.

**stuck windows.** ctrl + alt + r rescues the window you're in: first press sends it to the other screen, second flips fullscreen, third forces it borderless. windows that end up off-screen come back on their own when a monitor goes away (or run "bring back off-screen windows"). ctrl + alt + shift + m frees the mouse if a game crashed and left it trapped.

**hot corners.** push the mouse into a corner of any screen: top-left is overview, bottom-left is start by default. you can set each corner to start, overview, control center or show desktop. fullscreen games are left alone.

**everything else**
- hide desktop icons (nothing gets deleted). double-click the desktop or ctrl + alt + e to peek
- taskbar: clear / blur / acrylic, auto-hide or gone, centered icons, hide start and tray
- scroll a window's title bar to make it see-through, middle-click it to pin it on top
- scroll the screen edge for brightness, scroll the top bar for volume, middle-click the bar to play/pause
- cursor lock keeps your mouse on one screen for games (ctrl + alt + l, or automatic in fullscreen)
- paste stack: ctrl + alt + c, copy a bunch of things, then ctrl + alt + v pastes them in order
- share your setup as a `.elysiumpack` file and import a friend's
- sort Downloads into folders by type on its own (off by default)
- version shelf keeps older copies of files in the folders you pick so you can go back (off by default)
- notifications pop up on whatever screen your mouse is on
- ctrl + alt + y shows every shortcut, click one to run it
- settings has a search box up top
- shortcuts can turn one app up or down (or mute it), like spotify louder without touching anything else
- the clipboard history in the launcher shows color swatches, link domains, file icons and image thumbnails
- the launcher finds your open windows too ("switch to ..."), shift + enter closes one
- game mode still lets urgent stuff through, like defender finding something
- animations run at your monitor's full refresh rate now, and look settings apply the second you change them

**the island.** the pill in the bar morphs between whatever's going on: music, downloads, copies, timers, recording (click to stop), and quick pops for volume, brightness and caps lock if you pick "in the bar" for popups. or pick glass popups instead.

**calendar.** click the clock (or the pill when nothing's playing) for a month view with your reminders, timers and calendars. add google, apple or outlook calendars in settings > calendar with their ical link. read-only, stays on your pc.

**moments.** a boot screen when elysium starts (or only when the pc starts), a glass shutting down / restarting screen, a lock animation, the bar sliding back in when the pc wakes, and a "match the lock screen" button that puts a themed version of your wallpaper on windows' lock screen. (nothing can draw over the real lock screen, that part's windows.)

**standby.** after a few minutes away the screen turns into a big clock with your music, weather, notifications and what's next on your calendar. pick the clock, background, dim, which screens, what shows. move the mouse and it's gone. it won't kick in over a fullscreen game or a video.

**more optional stuff** (also off until you turn it on)
- minimize into the dock: windows shrink into the dock edge and grow back out
- open and close animations for every app, zooming out of the dock icon if you launched it there
- themed window borders: the window you're in gets an accent border, title bars match light / dark
- accent from the app you're in (settings > look > accent)
- roll a window up into its title bar, a little tab hangs off it to drop it back down (give "roll a window up / down" a key)
- auto-tiling: new windows get arranged, big one on the left, the rest stacked on the right
- dock previews: hover an open app in the dock to see its windows
- bounce when an app wants you: its icon pops out at the dock edge and the bar glows
- background apps in the bar: the stuff in your tray in a menu (open it, quit it, end task). turn on the slot in the bar editor
- glass desktop menu: right-click empty desktop for elysium's menu, icons still get windows' one
- desktop piles: desktop files stacked by type on the right, click a pile to fan it out, drag stuff out of it
- start greeting with your name and the weather, and live pins with unread counts and what spotify's playing
- notifications can show on the mouse's screen, the main one, a specific one, or all of them
- settings > about has your specs and the version, and what's new pops up after updates

**coming from heft?** elysium moves your settings, themes and shelf over the first time it opens and closes the old heft if it's still running.

**optional stuff** (all off until you turn it on)
- focus border: a soft glow around the window you're typing in
- spotlight (ctrl + alt + shift + s): dims everything except the window you're in
- remember window spots per monitor setup, so unplugging a screen or waking the pc puts everything back
- pause music for videos: start a video and spotify pauses, then picks back up when the video stops
- pause when headphones come out
- mic: ctrl + alt + m mutes it everywhere and a red mic shows in the bar. push to talk or push to mute on any key or a mouse side button, with a little blip when it turns on and off
- paste plain (ctrl + shift + v) pastes without the formatting
- notes on apps: a sticky note that shows up every time you're in that app

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
| ctrl + alt + g | spaces |
| ctrl + alt + o | picture in picture |
| ctrl + alt + u | send Explorer selection to phone |
| ctrl + alt + y | every shortcut |
| ctrl + alt + x | screenshot / text / colors |
| ctrl + alt + z | reopen the last closed app |
| ctrl + alt + q | scratchpad |
| ctrl + alt + j | grab your last download |
| ctrl + alt + r | rescue a stuck window |
| ctrl + alt + m | mute / unmute your mic |
| ctrl + alt + shift + s | spotlight |
| ctrl + alt + shift + m | free the mouse |
| ctrl + shift + v | paste plain (when it's on) |
| (pick one) | roll a window up / down, standby, tile windows now |
| shift while dragging a window | snap zones |
| space (in File Explorer) | inspect |
| windows key | start |

all of them can be changed in settings > keys. click the keys and press a new combo. you can also make your own shortcuts there (open an app, a file, a folder or a site, type out text, run any elysium action, or chain steps) and remap keys, like caps lock as escape. it warns you when something else already uses a combo.

## build

needs the .NET 8 SDK, windows 10 or 11.

```
dotnet run
```

to make the exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish
```

put `publish\elysium.exe` somewhere it'll stay, open it, then settings > general > start with windows. "start before other apps" (on by default) sets up a task that runs elysium the second you sign in, ahead of the normal startup list. turn it off and elysium goes in the regular startup list instead. if you move the exe later elysium fixes the startup entry itself next time it opens.

settings live in `%AppData%\elysium\settings.json`, and `%AppData%\elysium\log.txt` has what elysium saw if something acts weird.

## heads up

- windows of apps running as admin can't be thrown or pocketed unless elysium runs as admin too
- if elysium crashes while it's handling notifications, Windows' popups stay off until you open elysium again (or flip the switch off in settings > alerts)
- per-app audio routing uses the same windows feature as the sound settings page, so it works on windows 10 2004 and newer
- making GIFs and shrinking videos for discord shows up in inspect when ffmpeg is installed
- send to phone needs windows to let elysium through the firewall the first time (allow it on private networks)
- window lerp and stacks skip admin windows unless elysium runs as admin too
- text shortcuts and gestures don't reach admin windows unless elysium runs as admin too
- desktop dots read the desktop list windows keeps in the registry, and switching sends ctrl + win + arrows

made by mason arcieri
