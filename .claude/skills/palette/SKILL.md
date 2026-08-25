---
name: palette
description: Apply Cabinet's palette rules to artwork and other non-theme output. Use when choosing colours for the app icon, site, diagrams, SVGs, or GUI elements that might use hex values.
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
