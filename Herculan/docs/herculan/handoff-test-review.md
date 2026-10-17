# Handoff: test review — what needs the retail data, and what no test covers

A mutation-backed review of `Herculan.Engine.Tests` (and a lighter one of `HercWorks.Disc.Tests`,
`HercWorks.Help.Tests` and `HercWorks.Video.Tests`) removed or rewrote every test that could not fail
for the bug it claimed. Synthetic tests were checked by mutating the engine and seeing them fail. Each
entry below says what is suspect and what would settle it.

## Gaps that the data would settle

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

- **`MechLocomotionTests.TheEyeRidesTheCameraNode`** — asserts the eye, node plus the type's eye
  offset, sits 7-15 m up, and passes for every HERC against the retail data. mech-locomotion.md,
  "Cockpit eye and bob", gives the standing eye height as 3.2 m (STINGRAY) to 11.2 m (SAMSON), which
  are the heights without the offset. Either the doc leaves out the offset or the engine applies one
  retail does not; reading where DBSIM adds the eye offset settles which.

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
- The cockpit's world viewport riding the pan vertically, `CockpitInput.PopUp`, and the press
  tracking's surface check.
- `CdRipMusicSource.UncachedTracks`' zero-length-track filter, `GameContent.MaxArchives`,
  `StreamedCdAudio.GetPosition`'s fallback and `ResumeAt(0)` returning false.
- `PlayerSaveRoundTripTests.BuildSave` gives `ChassisType` and `ChassisIndex` the same value, and the
  pilot's rank, on-strength byte and Herc kills all 1, so a reader and writer that swapped either set
  would pass. `ShellHangarStoreTests` depends on the fixture, so changing it ripples there.
