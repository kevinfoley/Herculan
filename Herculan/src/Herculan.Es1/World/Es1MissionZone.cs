using HercWorks.Core.Data.File.Msn.Es1;

namespace Herculan.Es1.World;

/// <summary>
/// The zone a mission's load leaves in header word 1, which the simulator loads the zone's shapes from
/// (docs/es1/formats/msn-mission-file.md#header-patches).
///
/// <para>The load starts the word at 0, and <c>Msn_ApplyHeaderPatch</c> (<c>ES1GO 00031c84</c>) copies a passing
/// patch's word 1 over it when the patch's is not 0. The conditions that gate a patch are not ported, so this applies
/// only the patches with no condition and refuses a mission where a conditional patch sets the zone. In the 75 retail
/// missions in the loader's layout, every patch that sets word 1 has no condition.</para>
/// </summary>
public static class Es1MissionZone {
	/// <summary>Word 1's index among the ten header words.</summary>
	public const int ZoneWord = 1;

	/// <summary>
	/// The zone <paramref name="mission"/> names. A file in the other layout
	/// (docs/es1/formats/msn-mission-file.md#a-file-in-another-layout) has no patches, and its own word 1 is taken:
	/// that is a reading of the bytes, since what loads such a file is Open.
	/// </summary>
	/// <exception cref="NotSupportedException">A patch with a condition sets the zone.</exception>
	public static short Of(Es1MissionFile mission) {
		if (mission.HeaderWords is { } words) {
			return words[ZoneWord];
		}

		short zone = 0;
		foreach (var patch in mission.HeaderPatches) {
			short value = patch.HeaderWords[ZoneWord];
			if (value == 0) {
				continue;
			}

			if (patch.ConditionRef >= 0) {
				throw new NotSupportedException(
					$"A header patch under condition {patch.ConditionRef} sets the zone to {value}; mission conditions are not ported.");
			}

			zone = value;
		}

		return zone;
	}
}
