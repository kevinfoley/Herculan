# Handoff: test review — what needs the retail data, and what no test covers

A mutation-backed review of `Herculan.Engine.Tests` (and a lighter one of `HercWorks.Disc.Tests`,
`HercWorks.Help.Tests` and `HercWorks.Video.Tests`) removed or rewrote every test that could not fail
for the bug it claimed. Synthetic tests were checked by mutating the engine and seeing them fail. The
tests below read retail files, so in the cloud environment they returned early and were only reasoned
about. Each entry says what is suspect and what would settle it.

## Never run against the data since being changed

- **`CockpitDamageShakeTests.EveryTheatersImpactRampsMatchTheShapeOfItsOwn`** — now also asserts each
  theater's impact ramp table differs from its ordinary one, for all five theaters. If a row fails, that
  theater's `IMPACT<n>.DPL` builds an identical table and its flash shows nothing.
- **`HardpointOverlayTests.ArmingFileCarriesOneAreaPerMount`** — matches each `ARM_HOTS.DAT` group to
  its chassis by `HercId` and expects `HercLUT.HardpointMax` areas. Assumes the file's ids are the
  catalog's 0–8.
- **`SkeletonPoseTests`** — the camera-joint test now asserts the camera part resolves (it returned
  silently before), and the parent test asserts every ground HERC has joints. Both rest on
  mech-locomotion.md's "every ground HERC lands on the same chain".
- **`ClipRegionFileTests.EdgFileIsOneSpanBlock`** — the literal row counts are gone; the consumer's row
  count is compared with the block's.

## Snapshots and gaps that the data would settle

- **`SkyGradientTests.EveryRetailTheaterBuildsItsSky`** — the 16/15 band split, horizon colour 223/222,
  band height 6, gap 1 and offset 0 are a snapshot of theater data, and the horizon-colour line restates
  the band count. It is the only test that `WorldData`'s header accessors read the right shorts from
  real files: the synthetic header uses the same indices, so it cannot catch an index shift. If a
  `WorldData` header test elsewhere covers those indices, trim this one to "every theater builds a sky".
- **`WeaponMountFitTests.TemplateFrontBlockIsAPieceAndACluster`** — mostly a verbatim snapshot of
  ATC20's template (armor 1500, flags 1, spill 20, component 19, 6 spheres, max 500).
  `FittedMountTakesItsWeaponsPieceAndEmptyOnesDropOut` already checks armor and internal max through
  the consumer. The closing round trip compares only a prefix (`bytes.AsSpan(0, written.Length)`), so a
  writer that stops early still passes. Check whether a fitted mount's collision model carries component
  19's spheres offset by the mount; if so, replace the snapshot with that check and compare full length.
- **`ShellRepairScreenTests.RetailValues()`** — hand-written, not read from `gam\damage.dat`. The 72.5%
  rebuild figure and the 7628 unit value depend on it. Parse the retail file and compare its scales and
  shares with `RetailValues()`.
- **`MissionWalkTests.SquadmatesStartOnTheirFormationPosts`** — reads whatever mission the install's
  `script.dat` currently holds, and asserts nothing when the player's group has no followers. Load a
  fixed mission that has a squad and assert at least one follower was checked.
- **`ComputerWarningTests.TheKillAnnouncementNeedsTheVictimToBeTheSelectedTarget`** — the player is the
  attacker in both phases, so only the selected-target half of the predicate is varied. A third phase,
  a non-player killing the player's selected target (expect silence), would cover the other half.
- **`HercWorks.Help.Tests` `HostileInputTests.CorruptBytesNeverThrow`** — corrupts the retail manual
  2000 times but never checks that any corrupted copy still parses, so it could pass with every round
  refused at the header. Count the rounds that parse and the ones refused and assert both clear a
  floor, as `HercWorks.Disc.Tests`' `SurvivesRandomCorruption` and `SurvivesRandomCueSheets` do.
- **`ShellHangarStoreTests`** — `Store` writes 1 for every available chassis where it read any nonzero
  flag. The synthetic save only carries 0 and 1; the retail sweep catches it if a retail save carries
  another value.

## Doc and test disagree

- **`MechLocomotionTests.TheEyeRidesTheCameraNode`** — its summary says the fleet's eyes sit at
  3.2–11.2 m *without* the type's eye offset and 8.2–13.2 m with it, and asserts 7–15 m.
  mech-locomotion.md, "Cockpit eye and bob", gives 3.2–11.2 m as the standing eye height. One of the two
  is stale; a retail run of the eye heights with and without the offset settles which.

## Engine paths no test reaches

Found while mutating; none has a test claiming it, so nothing was weakened. Each could get a synthetic
test.

- `JoystickBindings.CombineForCamera`'s `HasThrottle` gate (lever and rudder zeroed on a stick without
  a lever).
- `SkyGradient.BandAt`'s level `+0.5`, which only matters for fractional distances.
- `MechObject.ExchangeCockpitThrottle`'s flyer branch.
- `MechObject.ApplyThrottleInput`'s lever-dependent clamp never runs: the lever path zeroes the axis the
  rate path steps from.
- `MechDetailRootRemap`'s `TSBSPPart` per-node branch and its exact-type `TSGroup` test.
- `ShellRepairCosts.ItemCost`'s early return: every caller prices to 100.
- `ShellPointer.Release`'s right-button arm fires on a tab with no press there first.
- `CdRipMusicSource.UncachedTracks`' zero-length-track filter, `GameContent.MaxArchives`,
  `StreamedCdAudio.GetPosition`'s fallback and `ResumeAt(0)` returning false.
- `PlayerSaveRoundTripTests.BuildSave` gives `ChassisType` and `ChassisIndex` the same value, and the
  pilot's rank, on-strength byte and Herc kills all 1, so a reader and writer that swapped either set
  would pass. `ShellHangarStoreTests` depends on the fixture, so changing it ripples there.
