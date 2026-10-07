using System.Buffers.Binary;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.World;

/// <summary>
/// <c>dat\FFORMS.DAT</c> — per-formation station offsets for a <c>script.dat</c> block-8 flyer
/// group, the flyer twin of <see cref="MechFormationTable"/> and <see cref="BaseFormationTable"/>.
///
/// <para>The layout, the accessor's arithmetic and how a flight uses the offset are
/// docs/retail/formats/script-dat.md, section "The flyer formation table".</para>
///
/// <para>Byte-exact against the retail file: 2-byte count (5) + 5 × 0x12 consumes all 92 content
/// bytes with nothing left over.</para>
/// </summary>
public sealed class FlyerFormationTable {
	/// <summary>VOL folder and name of the table.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The table's resource name.</summary>
	public const string ResourceName = "FFORMS.DAT";

	/// <summary>
	/// Follower slots per formation record — <c>0x12 / 6</c>, which is what fixes the stride against
	/// the accessor's <c>slot * 6 - 6</c>.
	/// </summary>
	private const int SlotsPerFormation = 3;

	private readonly Vec3i[][] _formations;

	private FlyerFormationTable(Vec3i[][] formations) {
		_formations = formations;
	}

	/// <summary>How many formations the table declares.</summary>
	public int Count => _formations.Length;

	/// <summary>
	/// The station offset for a group's <paramref name="memberIndex"/>-th flyer, or null when the
	/// slot takes none — member 0 (the leader), an out-of-range formation id, or a member index past
	/// the table's three follower slots.
	/// </summary>
	public Vec3i? OffsetFor(int formationId, int memberIndex) {
		if (memberIndex <= 0 || formationId < 0 || formationId >= _formations.Length) {
			return null;
		}

		var slots = _formations[formationId];
		int slotIndex = memberIndex - 1;
		return slotIndex < slots.Length ? slots[slotIndex] : null;
	}

	public static FlyerFormationTable Load(GameContent content) {
		byte[] bytes = content.ReadRequired(ResourceFolder, ResourceName);
		int offset = 0;

		short NextInt16() {
			short value = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
			offset += 2;
			return value;
		}

		int formationCount = NextInt16();
		var formations = new Vec3i[formationCount][];

		for (int f = 0; f < formationCount; f++) {
			var slots = new Vec3i[SlotsPerFormation];
			for (int s = 0; s < SlotsPerFormation; s++) {
				int x = NextInt16();
				int y = NextInt16();
				int z = NextInt16();
				slots[s] = new Vec3i(x, y, z);
			}
			formations[f] = slots;
		}

		if (offset != bytes.Length) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{ResourceName}: walked {offset} of {bytes.Length} bytes across " +
				$"{formationCount} formations — the record shape does not match this file.");
		}

		return new FlyerFormationTable(formations);
	}
}
