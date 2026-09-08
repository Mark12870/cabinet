---
name: gui-shot
description: Capture and inspect Cabinet's installed GTK4/libadwaita GUI.
---

# Capture a GUI screenshot

Use the script with the current installed Flatpak. Build and toolbox setup are documented by
the existing repository procedures and the script header.

```sh
scripts/gui-shot.sh About about.png
scripts/gui-shot.sh Prefixes/<existing-name> prefix.png
scripts/gui-shot.sh Library/<existing-name> library.png
```

The valid top-level pages are `Library`, `Prefixes`, `Runners`, `Doctor`, and `About`. The
`Prefixes/<existing-name>` and `Library/<existing-name>` forms capture a named row's page.

Close any obsolete Cabinet window before running: the script reuses an open window. Read the
PNG it creates and verify that it shows the requested page and current change; a successful
exit alone is not sufficient.
