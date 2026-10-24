using System.Buffers.Binary;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <summary>An unrotated (x, y) spread offset for one follower slot of a mech formation.</summary>
public readonly record struct MechFormationOffset(int X, int Y);

/// <summary>
/// <c>dat\MFORMS.DAT</c> — per-formation spread offsets for a <c>script.dat</c> block-7 mech group.
/// The mech equivalent of <see cref="BaseFormationTable"/>; stops a multi-mech group (a squad) from
/// placing every member on the group's single point.
///
/// <para>Load site: <c>Mech_LoadResources</c> (<c>0041fdb0</c>) opens <c>dat\mforms</c> (string
/// <c>"mforms"</c> at <c>0049a46b</c>, joined via the shared folder-prefix helper
/// <c>ResourcePath_BuildFolderName</c> (<c>00492ae0</c>)), reads a 2-byte record count into <c>DAT_004a9dec</c>, allocates that many
/// 28-byte vector elements (<c>Rtl_VectorNew(..., 0x1c, count, ...)</c>), stores the vector pointer
/// into <c>_DAT_004a9df0</c> — the global <c>Formation_GetSlotOffset</c> (<c>004205cc</c>) reads —
/// then reads <c>count * 28</c> bytes from the file into it. Registered into DBSIM's
/// subsystem-loader table via a thunk at <c>00420654</c> (<c>RegisterSubsystemLoader(Mech_LoadResources, 2)</c>, <c>00401d64</c>).
/// Prior analysis had no reference to <c>_DAT_004a9df0</c>'s write because <c>Mech_LoadResources</c>
/// itself had never been disassembled (no call-graph edge into it from anything already found).</para>
///
/// <para>Byte-exact against the retail file: 2-byte count (5) + 5 fixed 28-byte formations (seven
/// (x, y) <c>int16</c> pairs each) consumes all 142 content bytes, nothing left over. Unlike
/// <see cref="BaseFormationTable"/>, mech formations are fixed-size records — no per-formation slot
/// count and no terrain trailer.</para>
///
/// <para>Consuming side mirrors <see cref="BaseFormationTable"/>: <c>Mech_ApplyFormationOffset</c>
/// (<c>00417898</c>, mech vtable <c>+0x78</c>) no-ops for member-index 0 (the leader) and otherwise
/// looks up <c>Formation_GetSlotOffset(formationId, memberIndex)</c>, rotated into world space by
/// <c>Formation_RotateAndAddOffset</c> (<c>00411d64</c>) — same plain 2D rotation
/// <see cref="MissionLoader"/> already implements for bases.</para>
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
