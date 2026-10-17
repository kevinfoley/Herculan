using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Io.Transform.Common;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Byte-for-byte round trips through <see cref="MecFileTransformer"/>, over synthetic files rather
/// than a retail one so the test needs no Earthsiege 2 install.
///
/// <para>What is being pinned is the trailing armory flag table — <c>int16 count</c> then one byte
/// per weapon catalog id — which VSHELL writes after the last entry. Both shapes have to survive: a
/// file that carries the table (everything the shell writes) and one that does not, which DBSIM
/// accepts.</para>
/// </summary>
public class MecFileRoundTripTests {
	private const int WeaponCatalogSize = 33;

	[Fact]
	public void PreservesTheTrailingWeaponFlagTable() {
		byte[] original = BuildMec(withWeaponFlags: true);
		var transformer = new MecFileTransformer();

		MecFile parsed = Assert.IsType<MecFile>(transformer.Parse(original));

		Assert.Equal(WeaponCatalogSize, parsed.WeaponFlags.Length);
		Assert.Equal(1, parsed.WeaponFlags[0]);
		Assert.Equal(WeaponCatalogSize, parsed.WeaponFlags[WeaponCatalogSize - 1]);

		Assert.Equal(original, transformer.Write(parsed));
	}

	/// <summary>
	/// A file with no table round-trips without one. The flags cannot be reconstructed from anything
	/// else in the file, so writing a fabricated table would state something false about the armory.
	/// </summary>
	[Fact]
	public void WritesNoTableWhenTheSourceHadNone() {
		byte[] original = BuildMec(withWeaponFlags: false);
		var transformer = new MecFileTransformer();

		MecFile parsed = Assert.IsType<MecFile>(transformer.Parse(original));

		Assert.Empty(parsed.WeaponFlags);
		Assert.Equal(original, transformer.Write(parsed));
	}

	/// <summary>
	/// The entries' fields, read against the hand-built values. The two entries have different slot counts,
	/// so each is read to its own length, and the three condition blocks are checked at their edges.
	/// </summary>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ReadsTheEntryFieldsEitherWay(bool withWeaponFlags) {
		var transformer = new MecFileTransformer();

		MecFile parsed = Assert.IsType<MecFile>(transformer.Parse(BuildMec(withWeaponFlags)));

		Assert.Equal(1, parsed.PlayerEntryIndex);
		Assert.Equal(2, parsed.Entries.Length);

		MecEntry squadmate = parsed.Entries[0];
		Assert.Equal((7, 2, 5, 2), (squadmate.PilotNameIndex, squadmate.Skill, squadmate.MechType, squadmate.SlotCount));
		Assert.Equal(new short[] { 3, 0 }, squadmate.WeaponRefs);
		Assert.Equal(new short[] { 1, 5 }, squadmate.WeaponAmmoTypes);
		Assert.Equal((1, 26), (squadmate.ExternalConditions[0], squadmate.ExternalConditions[^1]));
		Assert.Equal((27, 46), (squadmate.InternalConditions[0], squadmate.InternalConditions[^1]));
		Assert.Equal((47, 66), (squadmate.HardpointConditions[0], squadmate.HardpointConditions[^1]));

		MecEntry player = parsed.Entries[1];
		Assert.Equal((9, 3, 8, 3), (player.PilotNameIndex, player.Skill, player.MechType, player.SlotCount));
		Assert.Equal(new short[] { 4, 6, 2 }, player.WeaponRefs);
		Assert.Equal(new short[] { 5, 5, 5 }, player.WeaponAmmoTypes);
		Assert.Equal(101, player.ExternalConditions[0]);
	}

	/// <summary>
	/// Two entries laid out exactly as VSHELL's <c>FUN_004106b7</c> writes them, the player's second: the
	/// pilot's two fields, the mech type, the slot count, the two per-slot arrays, a literal zero, then the
	/// three blocks that are one contiguous 66-byte span in the HERC record. An empty hardpoint is weapon id 0
	/// paired with ammo type 5, which is why the first entry's slot 1 reads that way.
	/// </summary>
	private static byte[] BuildMec(bool withWeaponFlags) {
		var bytes = new List<byte>();

		AddShort(bytes, 1); // PlayerEntryIndex
		AddShort(bytes, 2); // entry count

		AddEntry(bytes, pilotNameIndex: 7, skill: 2, mechType: 5, weapons: [3, 0], ammo: [1, 5], firstCondition: 1);
		AddEntry(bytes, pilotNameIndex: 9, skill: 3, mechType: 8, weapons: [4, 6, 2], ammo: [5, 5, 5], firstCondition: 101);

		if (withWeaponFlags) {
			AddShort(bytes, WeaponCatalogSize);
			for (int i = 0; i < WeaponCatalogSize; i++) {
				bytes.Add((byte)(i + 1));
			}
		}

		return bytes.ToArray();
	}

	private static void AddEntry(List<byte> bytes, short pilotNameIndex, short skill, short mechType, short[] weapons,
			short[] ammo, int firstCondition) {
		AddShort(bytes, pilotNameIndex);
		AddShort(bytes, skill);
		AddShort(bytes, mechType);
		AddShort(bytes, (short)weapons.Length);
		foreach (short weapon in weapons) {
			AddShort(bytes, weapon);
		}

		foreach (short type in ammo) {
			AddShort(bytes, type);
		}

		AddShort(bytes, 0); // Unk3A, a literal zero in every export

		for (int i = 0; i < 26 + 20 + 20; i++) {
			bytes.Add((byte)(firstCondition + i));
		}
	}

	private static void AddShort(List<byte> bytes, short value) {
		bytes.Add((byte)(value & 0xFF));
		bytes.Add((byte)((value >> 8) & 0xFF));
	}
}
