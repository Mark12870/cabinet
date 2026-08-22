---
name: palette
description: Cabinet's colours and the rule about when they may be used at all. Use when picking a colour for anything Cabinet draws rather than themes — the app icon, the site, a diagram, an SVG — or when tempted to put a hex into the GUI.
---

# Cabinet's palette

**GNOME's colours win wherever GNOME has one.** The GUI follows the user's system accent, and
`success`, `warning` and `error` are Adwaita style classes rather than hexes from here — a
`data/style.css` mapping these onto `--accent-bg-color` was written and removed, because
overriding the accent someone chose is the one case this rule excludes.

Use the palette only where there is no GNOME colour to inherit: the app icon (espresso and
amber-flame today), the site, a diagram, anything drawn rather than themed.

```
espresso      #663322
amber-flame   #ffbb00
jungle-teal   #227d66
pale-sky      #bfdbf7
watermelon    #ee4266
```
