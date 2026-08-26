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
| decent-sampler | Decent Samples' product page, `decentsamples.com/wp-content/uploads/2020/06/Screen-Shot-2022-08-06-at-7.11.22-AM.png` — the plugin with their own Basic Piano loaded, a 1.x build and the shot they still sell it with; 1.25.0's standalone was run here and draws an empty panel until an instrument is in it |
| ik-product-manager | IK's product page, `ikmultimedia.com/products/productmanager/images/1.0/ik_pm_gui_software@2x.jpg` — the manager's Software tab, the list every IK plugin is installed and authorised from |
| helix-native | the *GUI Overview* figure in Line 6's own *Helix Native Pilot's Guide*, which the installer leaves at `ProgramData/Line 6/Helix Native/res/` and Line 6 also publish; the page draws its red callout lines as vector overlays, so the raster underneath comes out clean. Their product page has only a marketing composite with the plugin too small to crop |
| drumgizmo | `drumgizmo.org/wiki/lib/exe/fetch.php?media=drumgizmo-0.9.15.png`, the official DrumGizmo Wiki interface screenshot |
| neural-amp-modeler | `github.com/mikeoliphant/neural-amp-modeler-lv2/releases/tag/v0.2.3`, the LV2 port's supplied `resources/modgui/screenshot-nam.png`; its icon is the project's `NeuralAmpModeler/resources/Images.xcassets/AppIcon.appiconset/icon_512x512.png` |

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
| decent-samples | the `ds` mark, their own favicon at `decentsamples.com/wp-content/uploads/2018/09/cropped-Favicon_512x512.png`, 512×512, black on an opaque white square, keyed off it and recoloured white |
| ik-multimedia | the hexagonal IK mark at the left of `ikmultimedia.com/images/layout/IK_LOGO_DL_RGB_WHITE_FFF.svg`, vector and already white; the MULTIMEDIA wordmark beside it is 3.6:1 and unreadable at this size. The manager's icon is not a crop of its screenshot: a product list is unreadable at the 32px a row draws, so it is the app's own 256px icon, the same hexagon filled red, out of the shortcut its installer leaves in the prefix |
| line6 | the LINE 6 pill mark, `commons.wikimedia.org/wiki/File:Line_6_logo.svg`, PD-textlogo; solid black ink on transparent, recoloured white |
| drumgizmo | `drumgizmo-0.9.20.tar.gz` from `drumgizmo.org/releases/drumgizmo-0.9.20/`, `plugingui/resources/logo.png`, recoloured white |
| neural-amp-modeler | `neuralampmodeler.com`, the project's 192px favicon containing the NAM mark |
