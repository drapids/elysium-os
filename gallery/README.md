# elysium gallery

elysium reads a list of themes and extensions from `index.json` in a github repo (`drapids/elysium-gallery` by default, change it with `galleryUrl` in settings.json).

each entry:

- `kind`: `theme` or `extension`
- `name`, `author`, `description`
- `download`: a direct link to a `.elysiumtheme` / `.elysiumext` (both are just zips)
- `colors`: optional, a couple of hex colors for the little swatch

to add yours, open a pull request that adds an entry.
