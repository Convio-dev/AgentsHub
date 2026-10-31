# Icon brief, third edition

The `NEW` folder is cut and wired. This file now lists only what is still wrong or still missing,
so nothing here repeats art that already exists.

## What is already in the app

Cut from your sheets, in `ui/src/assets/icons`, all referenced through `ui/src/lib/toolIcons.ts`:

| group | count | where it renders |
|---|---|---|
| `engines` | 8 | new-conversation tiles, dev tool rows, shortcuts |
| `sections` | 8 | Settings left nav |
| `tools` | 11 | tab headers, dashboard cards, dev tools, fallbacks |
| `ai` | 7 | chat header, avatars, dev tool rows |
| `actions` | 10 | refresh, add, search, copy, download, external, key, lock, pencil, trash across Settings, Profiles, Dev Tools and Dashboard |
| `mascot` | 2 | chat empty state, assistant avatar |
| `brand` | 1 | header mark, favicon, app icon, CLI icon |
| `objects` | 1 of 15 | `browser` on the dashboard hosts card |
| `states` | 0 of 4 | mapped, no home yet |

The 14 idle `objects` icons and 4 `states` icons stay mapped on purpose: every remaining slot for
them is 14 px or less, where a cut sticker turns into a blue smudge. They are not missing art, they
are waiting for a bigger slot. Do not redraw them.

## Needs a redraw (3)

These three exist but break the style. Same rules as below, same subject.

- `sections/appearance`: currently sits on a blue rounded plate with a white border, unlike every
  other free-cut section mark. Draw the palette and brush alone, no plate, no background square.
- `states/running`: currently a dark navy rounded square with a play triangle inside. Draw only the
  play triangle with the standard white outline, sitting on empty canvas.
- `objects/package`: currently a tan cardboard box, the only warm colour in the set. Draw the same
  open box and cube in blue, ink and white only.

## Genuinely missing (4)

- `brand/logo-white`: the app mark as flat white on transparent, one solid shape, no blue, no
  outline. Used at 16 px in the tray and in the installer header where the blue mark disappears.
- `tools/wifi`: the on-state twin of `tools/wifi-off`: three nested signal arcs, no slash. Used in
  the Remote Access card next to the off state.
- `installer/app-3d.png` and `installer/cli-3d.png`: the desktop shortcut and installer graphics
  still carry the old logo. Same mark as `brand/logo`, drawn on a rounded plate with a soft edge
  shadow, on transparent, 1024x1024. Only if the installer is still shipping these.
- `actions/close`: a thick X inside nothing else, for modal close buttons. Optional; the vector X
  is legible at 14 px, so this one is a nice-to-have rather than a need.

## The spec these must follow

Matches everything already delivered, so the new pieces drop into the same set.

- Canvas 1024x1024, background flat `#050505`, subject at 76 to 84 percent, centred, equal margins.
- Fill `#3FB8F5`, deep shade `#0E6B9C`, one highlight `#9BE0FF` at most, ink `#0B2030`, white
  details.
- Continuous white outline 22 to 26 px wide with a 6 to 8 px ink line inside it, one unbroken
  trace around the silhouette.
- Minimum stroke 14 px so it survives being drawn at 20 to 24 px.
- No sparkles, dots, dashes, floating decorations, shadows, glow, plates, plates-with-borders,
  warm colours, text or multiple subjects.
- One icon per file, named as its path, for example `wifi.png` and `logo-white.png`.

## Nothing else

Every other icon in the app comes from `NEW` or from the earlier sheets, is cut, centred and
placed. If you add new screens later, the gap will be in this file, not in your inbox.
