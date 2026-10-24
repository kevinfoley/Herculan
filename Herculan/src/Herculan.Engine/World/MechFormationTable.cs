using System.Buffers.Binary;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <summary>An unrotated (x, y) spread offset for one follower slot of a mech formation.</summary>
public readonly record struct MechFormationOffset(int X, int Y);

/// <summary>
/// <c>dat\MFORMS.DAT</c> — per-formation spread offsets for a mech group, so a multi-mech group (a
/// squad) does not stand every member on the group's single point. The mech equivalent of
/// <see cref="BaseFormationTable"/>, with fixed 28-byte formations of seven (x, y) <c>int16</c>
/// pairs: no slot count, no per-slot heading and no terrain trailer.
///
/// <para>Loaded by <c>Mech_LoadResources</c> (<c>0041fdb0</c>) and read through
/// <c>Mech_ApplyFormationOffset</c> (<c>00417898</c>, mech vtable <c>+0x78</c>): each member but
/// the one in member slot 0 takes <c>Formation_GetSlotOffset</c> (<c>004205cc</c>)'s offset for its
/// slot, rotated by the leader's heading. See
/// docs/retail/formats/script-dat.md#placement--the-actual-rule.</para>
/// </summary>
public sealed class MechFormationTable {
	/// <summary>VOL folder and name of the table.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The table's resource name.</summary>
	public const string ResourceName = "MFORMS.DAT";

	/// <summary>(x, y) pairs per formation record — matches the confirmed 28-byte/formation stride.</summary>
	private const int SlotsPerFormation = 7;

	private readonly MechFormationOffset[][] _formations;

	private MechFormationTable(MechFormationOffset[][] formations) {
		_formations = formations;
	}

	/// <summary>How many formations the table declares.</summary>
	public int Count => _formations.Length;

	/// <summary>
	/// The spread offset for a group's <paramref name="memberIndex"/>-th member (its position within
	/// the claiming block-11 record's <c>MemberRefs</c> array), or null when the slot takes no
	/// offset — member index 0 (the group's first-claimed member), an out-of-range formation id, or a
	/// member index past the table's 7 follower slots.
	/// </summary>
	public MechFormationOffset? OffsetFor(int formationId, int memberIndex) {
		if (memberIndex <= 0 || formationId < 0 || formationId >= _formations.Length) {
			return null;
		}

		var slots = _formations[formationId];
		int slotIndex = memberIndex - 1;
		return slotIndex < slots.Length ? slots[slotIndex] : null;
	}

	public static MechFormationTable Load(GameContent content) => Parse(content.ReadRequired(ResourceFolder, ResourceName));

	/// <summary>
	/// The table out of its bytes. VSHELL's map reads the same records from the loose
	/// <c>data\mforms.dat</c> (<c>ShellMap_ReadSquadHeader</c>, <c>00424db0</c>), which is the same file.
	/// </summary>
	public static MechFormationTable Parse(byte[] bytes) {
		int offset = 0;

		short NextInt16() {
			short value = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
			offset += 2;
			return value;
		}

		int formationCount = NextInt16();
		var formations = new MechFormationOffset[formationCount][];

		for (int f = 0; f < formationCount; f++) {
			var slots = new MechFormationOffset[SlotsPerFormation];
			for (int s = 0; s < SlotsPerFormation; s++) {
				int x = NextInt16();
				int y = NextInt16();
				slots[s] = new MechFormationOffset(x, y);
			}
			formations[f] = slots;
		}

		if (offset != bytes.Length) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{ResourceName}: walked {offset} of {bytes.Length} bytes across " +
				$"{formationCount} formations — the record shape does not match this file.");
		}

		return new MechFormationTable(formations);
	}
}
