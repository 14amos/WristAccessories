# Changelog

## 1.0.3 (work in progress - released for feedback)
- Hands from other outfits blend into your wrist: your own wrist and hand stay up to where the new hand starts, its edge tucks
  under your skin, and its skin takes your colour right at the seam (tattoo ink keeps its darkness). Setting "Blend the wrist
  seam" (on). Not done on a gloved hand of your own outfit (it keeps the old behaviour).
- The ISB addon is part of the release now (folder WristAccessories-ISB, loads only with ISB-Aishi + WTT-ContentBackport).
- The tab is now called WARDROBE.
- Items made from another mod's outfits (WTT-ContentBackport) are only listed when that mod is installed (no more items
  without textures).
- PRESETS on the tab: SAVE AS (type a name, SAVE / DELETE), PRESET to load one, LOAD WHAT = everything, wrist only (items,
  watch, ring) or hands and tattoos only. Stored in BepInEx/config/com.amos.wristaccessories.presets.json.
- Fabric fingerless gloves that were listed as hands keep their own colours (no skin toning).
- ARM TATTOOS for any top (HAND 1 / HAND 2 pieces): USEC Day Off (skeleton, spider web), Gladiator, BEAR Three Stripes, USEC
  ScavTac, BEAR Vacation, QILO Topographic Green, USEC Deadly Frog, USEC Alone in Woods. Pressed onto your own arm; hidden wherever
  your arm isn't bare skin (sleeves, gloves, cuffs). Some still carry a faint band of the source outfit's own bracelet.
- Tattoo overlays: only real ink shows now (pale skin highlights were drawn as pale streaks), no more white sheen.

## 1.0.2
- Performance: the "Follow the sleeve live" setting is gone. It cost about 1 ms per frame (spikes up to 10 ms) and moved a kill
  sleeve by 0.3 mm at most; the kill sleeve fit from 1.0.1 already keeps it out of the arm.
- Outfit bracelets that come as a set are now also available one by one: USEC Special Ops (beads, DON'T TREAD ON ME band,
  Crusader band), Green Hawaii, BEAR Vacation, USEC Day Off, Orange Hawaii, QILO Topographic Blue, USEC QILO Choco Chip
  (20 new items). BEAR Vacation's four left wristbands also as one item.
- USEC Special Ops beads no longer show white.
- Cleaner cuff cuts on the hands added in 1.0.1; new USEC Special Ops picture.

## 1.0.1
- Kill sleeves no longer clip into the arm: an arm wearing one is fitted to (nearly) every wrist pose, and the sleeve follows your
  arm live in first person (setting "Follow the sleeve live").
- Tattooed hands from other outfits (BEAR Lonsdale, USEC Deadly Frog, USEC Alone in Woods, ...) as hand choices, and their tattoos
  as overlays for any bare hand (HAND 1 / HAND 2).
- 88 more hands and gloves from other outfits. Bare hands no longer bring the other outfit's cuff, watch strap or bracelet: your own
  wrist carries on into the new hand, no gap.
- Glove and hand pictures all from the back of the hand, so left and right read correctly; 2 duplicate gloves removed.
- Items without a picture show the fallback smiley; 57 item pictures that were missing from 1.0.0 are back.

## 1.0.0 - first public release
- WTT-Artem support removed (its addon, its shoes, its outfits' arm outlines and pants data).
- Everything from the 0.x test builds below.

## 0.10.2
- Shoes: no more see-through ankles. The shoe's collar is checked against the pants' hem all the way round the ankle; your own
  boot is only taken out where the new shoe covers it (a low sneaker keeps your own boot over the instep).
- EVERY SHOE option: also lists the shoes only bots wear (zombies, bosses, scavs, guards, cultists), with readable names.
- Arm fit for the meshes the game swaps onto some tops (Arena Hawaii, Deadlift), so bracelets and sleeves fit the arm actually drawn.

## 0.10.1
- Shoes cut as the other outfit's whole boot (no scraps of its pants cuff); your own boots removed instead of shrunk; the shoe
  is fitted to your pants.

## 0.10.0
- SHOES: another outfit's shoes on your feet.
- Option rows and tiles re-laid out to fit the new tile.

## 0.9.9
- WATCHES IN WRIST SLOTS option: watches can go in the LEFT / RIGHT slots too.

## 0.9.x
- ACCESSORIES tab on the character screen with live preview; LEFT / RIGHT 1-5, WATCH, RING, UPPER / LOWER and HANDS tiles.
- Gloves, other outfits' hands and hand pieces; skin tone match.
- Cleans the painted shadow of a hidden outfit watch off the skin (on outfits that have the fix).

## 0.4 - 0.8
- Watches cut from the outfits' hands (16), named after their outfit; watch face on the back or inner wrist.
- Bracelets, chains, kill sleeves, dogtag swing; Manimal-WatchAnims hookup.
