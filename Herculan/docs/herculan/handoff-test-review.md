# Handoff: Herculan.Engine.Tests review — tests that need the retail data to settle

A review of `Herculan/tests/Herculan.Engine.Tests` removed or rewrote the tests that could not catch a
regression. These are the ones it kept but could not settle without a retail install, because the
cloud environment has none and every retail-data case there returns early. Each entry names what is
suspect and what check would settle it.

## Retail-data snapshots that may be replaceable by a cross-file check

- **`ClipRegionFileTests.EdgFileIsOneSpanBlock`** — the structural assertions (no rects, one span block
  from row 0, read through `CockpitClipRegions.Parse`) are worth keeping. The row counts 101 and 61 are
  literals copied from the files. If the HDD and MFD display heights are held anywhere else (cockpit art,
  or a constant the cutout is drawn against), compare against that, so two retail sources are checked
  against each other instead of the bytes being restated.
- **`SkyGradientTests.EveryRetailTheaterBuildsItsSky`** — the 16/15 band split, horizon colour 223/222,
  band height 6, gap 1 and offset 0 are a snapshot of theater data, and the horizon-colour line restates
  the band count (`ZenithColor + SkyBandCount - 1`). It is the only test that `WorldData`'s header
  accessors (`SkyBandHeight`, `HorizonGap`, `HorizonOffset`, `SkyBandCount`) read the right shorts from
  real files: the synthetic header uses the same indices, so it cannot catch an index shift. If a
  `WorldData` header or round-trip test elsewhere covers those indices, trim this one to "every theater
  builds a sky".
- **`WeaponMountFitTests.TemplateFrontBlockIsAPieceAndACluster`** — mostly a verbatim snapshot of
  ATC20's template (armor 1500, flags 1, spill 20, component 19, 6 spheres, max 500), which was evidence
  that the front block is a `.DMG` piece plus a `.COL` cluster.
  `FittedMountTakesItsWeaponsPieceAndEmptyOnesDropOut` already checks armor and internal max through
  the consumer. The closing round trip compares only a prefix (`bytes.AsSpan(0, written.Length)`), so a
  writer that stops early still passes. With the install: check whether a fitted mount's collision
  model carries component 19's spheres offset by the mount. If so, replace the snapshot with that check
  and make the round trip compare full length (or say in the test why the tail is excluded).
- **`CockpitDamageShakeTests.TheImpactRampIsADifferentTable`** — checks theater 0 only, and tests the
  retail palettes rather than engine code. If every theater's `IMPACT<n>.DPL` gives a different
  `PaletteRampTable`, fold the inequality into `EveryTheatersImpactRampsMatchTheShapeOfItsOwn` (the
  condition that makes that shape test meaningful) and delete this one.

## Tests that round-trip hand-written data instead of the retail file

- **`ShellRepairScreenTests.RoundTripsTheComponentValueTable`** — round-trips the hand-written
  `RetailValues()` (with an all-zero weapon table), not `gam\damage.dat`. It pins the record length, but
  a byte-for-byte round trip of the retail file, plus a check that `RetailValues()` equals the parsed
  file, would tie every cost figure in that class to the real data.

## Kept, with assertions that do less than their comments say

- **`MechLocomotionTests.TheEyeRidesTheCameraNode`** — the summary quotes fleet statistics (3.2–11.2 m
  without the offset, 8.2–13.2 m with it) that the 7–15 m assertion never checks. Either tighten it to
  per-chassis bounds from the data or drop the figures from the comment.
- **`PlayerSaveRoundTripTests.RoundTripsEveryRetailSaveByteForByte`** — the sanity checks (`NameIndex`
  0–35, printable names, 9 chassis unlocks, 36 squadmates) partly restate fixed sizes, but they diagnose
  a desync the round trip alone could hide. Only worth revisiting with the saves to hand.
