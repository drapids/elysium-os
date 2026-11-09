# heft

throw your windows around.

grab any window, fling it, and it keeps going. it bounces off the screen edges, slides to a stop, and with gravity on it falls and stacks on top of other windows. shake a window while dragging it to minimize it.

there's a small handle on the right edge of the screen. hover it and a panel slides out with every setting. right-click it (or hit ctrl + alt + space) and you get a spin wheel of your apps and projects. hangar29 gets found and pinned automatically.

## controls

| | |
|---|---|
| ctrl + alt + space | spin wheel at your cursor |
| ctrl + alt + h | open / close the panel |
| right-click the edge handle | spin wheel |
| tray icon | panel, wheel, pause, quit |

on the wheel: drag it to spin, scroll or use the arrow keys to step, click an app or hit enter to open the one at the top, 1-9 to open by slot, esc to close.

## settings

- **feel**: chill / bouncy / chaos presets, or tune throw strength, friction, bounciness, and gravity yourself
- **behavior**: stack on other windows, shake to minimize (with sensitivity), fly between monitors
- **wheel**: favorites, your projects, and apps you use a lot. star something to pin it, x to take it off. "my projects" is the list of names it looks for on your pc
- **general**: system / light / dark, start with windows, hide the edge handle, apps to leave alone

settings live in `%AppData%\heft\settings.json`.

## build

needs the .NET 8 SDK on windows 10/11.

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
