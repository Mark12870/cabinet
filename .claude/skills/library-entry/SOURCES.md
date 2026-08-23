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
| aalto, aaltoverb, kaivo, sumu, virta | Madrona Labs' product pages, `madronalabs.com/assets/site/` — `size_911_aalto_ui….png`, `size_911_aaltoverb_ui….png`, `size_1151_kaivo_ui….png`, `size_911_sumu_ui….jpg`, `size_937_virta_ui….png` |
| sitala-1 | captured here, from Sitala 1.0's own standalone running in a Wine prefix |
| sitala-2 | `decomposer.de/images/sitala-features/4x4-layout.png`, the 4×4 pad grid version 2 is built around |
| spitfire-audio | `spitfireaudio.com/cdn/shop/files/spitfire-app-product-view.jpg`, the app's own library grid. Its icon is not a crop of that: four dark sleeves are unreadable at the 32px a row draws, so it repeats the roundel below, cropped tighter than the logo |

Each `<vendor>/logo.png` is that vendor's own mark on transparency, cut from the largest copy
the vendor publishes and recoloured white where the original was too dark to read on `#222226`.
FabFilter's is a square badge that brings its own ground, so it keeps it.
Digital Suburban ships none, so Dexed's page falls back to a symbolic icon.

| Vendor | Logo |
| --- | --- |
| u-he | the wordmark out of their own press kit, `press-cdn.u-he.com/company/u-he_company_epk.zip`, the flat 800×464 PNG, keyed off the near-black plate it sits on |
| xfer-records | `xferrecords.com/assets/logo-…png`, 276×194, already white on transparent |
| fabfilter | `commons.wikimedia.org/wiki/File:FabFilter_Logo.svg`, PD-textlogo, from `fabfilter.com/press` |
| surge-synth-team | `logo_sst_white.svg` from `github.com/surge-synthesizer/surge-synth-team.org`, `src/images/` — their own dark-background variant; the mark is the left 100 units of a 655×100 lockup |
| vital-audio | the V badge out of `vital.audio/images/social.png`, 1200×630, keyed off the dark ground behind it |
| tal-software | `tal-software.com/logo.svg`, a light grey wordmark, which reads as it is |
| auburn-sounds | the impossible-triangle A out of `auburnsounds.com/images/logo-auburn.png`, 579×175; the wordmark beside it is 3:1 and unreadable at this size, and the gradient reads on the dark ground |
| valhalla-dsp | the horned helmet from `valhalladsp.com/wp-content/uploads/2020/01/cropped-valhalla_helmet_black-webicon.png`, the 512px original behind the 192px webicon, black on transparent, recoloured white |
| madrona-labs | the tree-ring mark at the left of `madronalabs.com/assets/site/logotype….svg`; the wordmark beside it is 3:1 and unreadable at this size, and the mark is near-black, recoloured white |
| decomposer | the loop badge, their YouTube channel avatar at 400px, `youtube.com/channel/UCFD4oFUHDIEexdF-pU1vzaw` — the badge in their own header is 40px; white, keyed off its black ground |
| spitfire-audio | the circular SPITFIRE AUDIO roundel, their YouTube channel avatar at 900px, `youtube.com/user/spitfireaudiollp` — the favicon they serve is capped at 64px; white, keyed off its near-black ground |

The u-he, Xfer Records, FabFilter, Vital Audio, TAL Software, Auburn Sounds, Valhalla DSP,
Decomposer and Madrona Labs images are those companies' own artwork of their own products, used
to identify the plugin the entry installs. Dexed is GPL-3.0 and Surge XT GPL-3.0; Vital's source
is GPL-3.0 and its free binary is Vital Audio's to give away, but its presets are licensed
separately and Cabinet ships none of them. Auburn Sounds' EULA forbids redistributing the
software and grants no right to the company's logo, and Valhalla DSP's says the same — its
plugins may not be distributed without permission, and the wordmark and helmet are trademarks.
Madrona Labs sell every plugin they make, and the tree-ring mark is theirs. Spitfire Audio sell
their libraries and licence them per seat; Cabinet ships neither, only their manager's own
screenshot and roundel to say whose catalogue it opens. Cabinet redistributes
none of it: it downloads each plugin, and each Madrona demo installer, from its vendor over
HTTPS, and the mark is here to say whose it is. Ask before adding a vendor whose terms you have
not read.
