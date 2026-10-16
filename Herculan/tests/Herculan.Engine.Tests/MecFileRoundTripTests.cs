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

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ReadsTheEntryFieldsEitherWay(bool withWeaponFlags) {
		var transformer = new MecFileTransformer();

		MecFile parsed = Assert.IsType<MecFile>(transformer.Parse(BuildMec(withWeaponFlags)));

		Assert.Equal(0, parsed.PlayerEntryIndex);
		MecEntry entry = Assert.Single(parsed.Entries);
		Assert.Equal(7, entry.PilotNameIndex);
		Assert.Equal(2, entry.Skill);
		Assert.Equal(5, entry.MechType);
		Assert.Equal(2, entry.SlotCount);
		Assert.Equal(new short[] { 3, 0 }, entry.WeaponRefs);
		Assert.Equal(new short[] { 1, 5 }, entry.WeaponAmmoTypes);
		Assert.Equal(26, entry.ExternalConditions.Length);
		Assert.Equal(20, entry.InternalConditions.Length);
		Assert.Equal(20, entry.HardpointConditions.Length);
	}

	/// <summary>
	/// One entry with two weapon slots, laid out exactly as VSHELL's <c>FUN_004106b7</c> writes it:
	/// the pilot's two fields, the mech type, the slot count, the two per-slot arrays, a literal
	/// zero, then the three blocks that are one contiguous 66-byte span in the HERC record. An empty
	/// hardpoint is weapon id 0 paired with ammo type 5, which is why slot 1 reads that way.
	/// </summary>
	private static byte[] BuildMec(bool withWeaponFlags) {
		var bytes = new List<byte>();

		AddShort(bytes, 0); // PlayerEntryIndex
		AddShort(bytes, 1); // entry count

		AddShort(bytes, 7); // pilot name index into esnames.bin
		AddShort(bytes, 2); // pilot skill tier
		AddShort(bytes, 5); // mech type
		AddShort(bytes, 2); // slot count
		AddShort(bytes, 3);
		AddShort(bytes, 0); // empty hardpoint
		AddShort(bytes, 1);
		AddShort(bytes, 5); // the empty hardpoint's filler ammo type
		AddShort(bytes, 0); // Unk3A, a literal zero in every export

		for (int i = 0; i < 26 + 20 + 20; i++) {
			bytes.Add((byte)(i + 1));
		}

		if (withWeaponFlags) {
			AddShort(bytes, WeaponCatalogSize);
			for (int i = 0; i < WeaponCatalogSize; i++) {
				bytes.Add((byte)(i + 1));
			}
		}

		return bytes.ToArray();
	}

	private static void AddShort(List<byte> bytes, short value) {
		bytes.Add((byte)(value & 0xFF));
		bytes.Add((byte)((value >> 8) & 0xFF));
	}
}
