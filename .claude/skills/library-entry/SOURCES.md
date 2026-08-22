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
| tal-noisemaker, tal-filter-2, tal-reverb-4, tal-chorus-lx, tal-vocoder, tal-bitcrusher | TAL's product pages, `tal-software.com/images/products/` — `tal-noisemaker-new.jpg`, `tal-filter-2-new.jpg`, `tal-reverb-402.jpg`, `tal-chorus-lx_.png`, `vocoder-3.jpg`, `tal-bitcrusher-02.jpg` |
| couture, graillon, inner-pitch, lens, panagement, psypan, renegate, selene | Auburn Sounds' product pages, `auburnsounds.com/images/` — `couture.jpg`, `graillon3.jpg`, `innerpitch2.jpg`, `lens.jpg`, `panagement2.jpg`, `psypan2.webp`, `renegate.jpg`, `selene.jpg` |
| valhalla-freq-echo, valhalla-space-modulator, valhalla-supermassive | Valhalla's product pages, `valhalladsp.com/wp-content/uploads/` — `2014/06/ValhallaFreqEcho-1.jpg`, `2016/06/ValhallaSpaceModGUI.jpg`, `2020/05/Supermassive-GUI.jpg`; each is stamped with the version it was taken at, which is older than the one the entry pins, and Valhalla publishes no newer shot |
| sitala-1 | captured here, from Sitala 1.0's own standalone running in a Wine prefix |
| sitala-2 | `decomposer.de/images/sitala-features/4x4-layout.png`, the 4×4 pad grid version 2 is built around |

Each `<vendor>/logo.png` is that vendor's own mark on the background that keeps it legible in
both GNOME themes — Xfer's and TAL's are white, so they sit on near-black; Surge Synth Team's
has black nodes, so it sits on white; u-he's and FabFilter's are square badges that bring their
own. Digital Suburban ships none, so Dexed's page falls back to a symbolic icon.

| Vendor | Logo |
| --- | --- |
| u-he | their Mastodon avatar, `mastodon.social/@uheplugins` — the square badge; the press kit has only the wide wordmark |
| xfer-records | `xferrecords.com/assets/logo-…png` |
| fabfilter | `commons.wikimedia.org/wiki/File:FabFilter_Logo.svg`, PD-textlogo, from `fabfilter.com/press` |
| surge-synth-team | `surge-synthesizer.github.io/_astro/sst-logo…svg` |
| vital-audio | the V badge from `vital.audio/images/social.png`, which brings its own dark ground |
| tal-software | `tal-software.com/logo.svg`, a light grey wordmark, so it sits on near-black |
| auburn-sounds | the impossible-triangle A out of `auburnsounds.com/images/logo-auburn.png`; the wordmark beside it is 3:1 and unreadable at this size, and the mark is pale, so it sits on near-black |
| valhalla-dsp | the horned helmet from `valhalladsp.com/wp-content/uploads/2020/01/cropped-valhalla_helmet_black-webicon-192x192.png`, black on transparent, so it sits on white |
| decomposer | the loop badge at the left of `decomposer.de/images/decomposer-logo.png`; the wordmark beside it is 9:1, and the mark is white on transparent, so it sits on near-black |

The u-he, Xfer Records, FabFilter, Vital Audio, TAL Software, Auburn Sounds, Valhalla DSP and
Decomposer images are those companies' own artwork of their own products, used to identify the
plugin the entry installs. Dexed is GPL-3.0 and Surge XT GPL-3.0; Vital's source is GPL-3.0 and
its free binary is Vital Audio's to give away, but its presets are licensed separately and
Cabinet ships none of them. Auburn Sounds' EULA forbids redistributing the software and grants
no right to the company's logo, and Valhalla DSP's says the same — its plugins may not be
distributed without permission, and the wordmark and helmet are trademarks. Cabinet
redistributes neither: it downloads each plugin from its vendor over HTTPS, and the mark is here
to say whose it is. Ask before adding a vendor whose terms you have not read.
