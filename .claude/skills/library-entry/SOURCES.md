# Where the Library's artwork came from

Every image in `data/library/<vendor>/`, and what it is.

Cabinet ships these so the Library works offline. Each `<vendor>/<id>.jpg` is the plugin's own
interface and each `<vendor>/<id>.png` a square crop of it, resized here; nothing was redrawn.

| Entry | Source |
| --- | --- |
| bazille-cm, podolski, protoverb, triple-cheese, tyrell-n6, zebra-cm, zebralette | u-he's product pages, `u-he.com/products/<product>/assets/images/` |
| surge-xt | `surge-synthesizer.github.io/images/hero_dark.png` |
| dexed | captured here, from Dexed 1.0.1's own standalone build |
| serum | `xferrecords.com/assets/products-large/serum2_promo…png` |
| fabfilter-total-bundle | `cdn-b.fabfilter.com/img/products/pro-q-4-screenshot.jpg` — Pro-Q 4, the bundle's flagship |
| vital | a frame of `vital.audio/videos/full_screen.mp4`, the interface tour on their own front page |

Each `<vendor>/logo.png` is that vendor's own mark on the background that keeps it legible in
both GNOME themes — Xfer's is white, so it sits on near-black; Surge Synth Team's has black
nodes, so it sits on white; u-he's and FabFilter's are square badges that bring their own.
Digital Suburban ships none, so Dexed's page falls back to a symbolic icon.

| Vendor | Logo |
| --- | --- |
| u-he | their Mastodon avatar, `mastodon.social/@uheplugins` — the square badge; the press kit has only the wide wordmark |
| xfer-records | `xferrecords.com/assets/logo-…png` |
| fabfilter | `commons.wikimedia.org/wiki/File:FabFilter_Logo.svg`, PD-textlogo, from `fabfilter.com/press` |
| surge-synth-team | `surge-synthesizer.github.io/_astro/sst-logo…svg` |
| vital-audio | the V badge from `vital.audio/images/social.png`, which brings its own dark ground |

The u-he, Xfer Records, FabFilter and Vital Audio images are those companies' own artwork of
their own products, used to identify the plugin the entry installs. Dexed is GPL-3.0 and Surge
XT GPL-3.0; Vital's source is GPL-3.0 and its free binary is Vital Audio's to give away, but its
presets are licensed separately and Cabinet ships none of them. Ask before adding a vendor whose terms you have not read.
