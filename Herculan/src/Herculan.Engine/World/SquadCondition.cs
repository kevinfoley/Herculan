using System.Buffers.Binary;
using HercWorks.Core.Data.File.Sav;

namespace Herculan.Engine.World;

/// <summary>
/// The condition a machine of the player's squad carries into a mission — its <c>player.mec</c>
/// entry's three condition spans, read as <c>int16</c> percentages. A squad machine has no roster
/// record and so no starting condition; this is what it starts in instead. See
/// docs/retail/simulation/component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068;
/// <see cref="Sim.ComponentDamage.ApplySquadCondition"/> writes it.
/// </summary>
/// <param name="External"><inheritdoc cref="MecEntry.ExternalConditions"/></param>
/// <param name="Internal"><inheritdoc cref="MecEntry.InternalConditions"/></param>
/// <param name="Hardpoint"><inheritdoc cref="MecEntry.HardpointConditions"/></param>
public sealed record SquadCondition(IReadOnlyList<short> External, IReadOnlyList<short> Internal,
		IReadOnlyList<short> Hardpoint) {
	/// <summary>The three spans of one <c>player.mec</c> entry.</summary>
	public static SquadCondition Of(MecEntry entry) =>
		new(Shorts(entry.ExternalConditions), Shorts(entry.InternalConditions), Shorts(entry.HardpointConditions));

	private static short[] Shorts(byte[] span) {
		var values = new short[span.Length / 2];
		for (int i = 0; i < values.Length; i++) {
			values[i] = BinaryPrimitives.ReadInt16LittleEndian(span.AsSpan(i * 2));
		}

		return values;
	}
}
