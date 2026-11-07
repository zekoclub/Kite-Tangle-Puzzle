# Kite Tangle UI assets

svg/ = vector sources. png/ = Unity-ready sprites (4× for small sprites; backgrounds are 1080×1920).

- kites/: one sprite per color, plus kite_shadow (offset it +4,+8 in design px) and kite_dock_empty
- spools/: charkhi per color
- rooftops/: per color (9-slice: border 12px)
- ui/: slot_empty, slot_ad_locked, coin, button_primary/secondary (9-slice 20px), hint_ring
- backgrounds/: sky_day, sky_dusk, sky_night, win_yellow, roof_band

Strings: draw with LineRenderer in 3 passes: edge #FBF6EC width 7, dark shade width 3.6, base color width 2.2 (at 390px design width). See palette.json.
Fonts: Baloo 2 + Material Symbols Rounded (both free on Google Fonts).
