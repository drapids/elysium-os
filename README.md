# heft

throw your windows around.

grab any window, fling it, and it keeps going. it bounces off the screen edges, slides to a stop, and with gravity on it falls and stacks on top of other windows. shake a window while dragging it to minimize it.

push your mouse into the edge of the screen (right by default, or left / top / bottom) and a dock fans out in a half circle. no outline, just your apps. scroll or drag to spin through them, hover to magnify, click to open. hangar29 gets found and pinned first, and if you have a fresh win-unpacked build it uses whichever exe is newest.

ctrl + scroll (or pinch) zooms out to places like desktop, downloads, documents and your drives. click a folder to dive in, zoom out to go back up. right-click anything to show it in file explorer.

## controls

| | |
|---|---|
| push into the edge | dock |
| ctrl + alt + space | dock |
| ctrl + alt + h | settings |
| right-click the edge handle | settings |
| tray icon | settings, dock, pause, quit |

on the dock: scroll, drag or arrow keys to spin, enter to open, ctrl + scroll / pinch to zoom, backspace to go back, esc to close.

## settings

- **physics**: chill / bouncy / chaos presets, or tune throw strength, friction, bounciness and gravity yourself
- **behavior**: stack on other windows, shake to minimize, fly between monitors
- **dock**: which side, icon size, magnification, edge trigger on/off
- **apps on the dock**: favorites, your projects and stuff you use a lot. star to pin, x to take it off
- **places**: the folders you zoom out to, plus drives
- **general**: auto / light / dark, start with windows, apps to leave alone, project names to look for

settings live in `%AppData%\heft\settings.json`.

## build

needs the .NET 8 SDK on windows 10/11. the glass panel uses real acrylic on windows 11 22H2+, older builds get a blur fallback.

```
dotnet run
```

for a single exe:

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## heads up

- windows from apps running as admin can't be thrown unless heft is also running as admin
- windows 11 has its own "title bar window shake" setting. if it's on, turn it off so it doesn't fight with shake to minimize
- maximized and snapped windows are left alone

made by mason arcieri
