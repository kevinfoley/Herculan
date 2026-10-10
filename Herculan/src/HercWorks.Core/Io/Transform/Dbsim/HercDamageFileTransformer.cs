using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Ported from org.hercworks.core.io.transform.dbsim.HercDamageFileTransformer.
///
/// Three format rules, decoded against real <c>.DMG</c> files from a retail install
/// (<c>ES2\VOL\simvol0\dmg\{SKIMMER,SPIDER,OUTLAW}.DMG</c>). Each is easy to get wrong by assuming
/// every machine is shaped like a HERC — SKIMMER is the one that proves otherwise:
/// <list type="number">
/// <item><b>The component count is variable.</b> Loop the file's own <c>totalComponents</c>. A
/// normal HERC has 29 (SPIDER, OUTLAW), so a hardcoded 29 looks right until SKIMMER, which has 1
/// and overruns.</item>
/// <item><b>So is the internals count, and nothing pads it.</b> Hercs store 22 and SKIMMER 1; the
/// components follow the last stored internal directly.</item>
/// <item><b><c>SpillWeight</c> is written raw, not scaled.</b> It reads as exactly <c>20</c>
/// (<c>0x14</c>) for the large majority of components across all three files, so a <c>* 100</c> on
/// write would not round-trip.</item>
/// </list>
/// </summary>
public class HercDamageFileTransformer : ByteTransformer<HercSimDamage> {
	public override HercSimDamage? Parse(byte[]? inputArray) {
		Index = 0;

		if (inputArray == null || inputArray.Length <= 0) {
			// TODO (carried over from Java): null input
			return null;
		}

		var data = new HercSimDamage();

		SetBytes(inputArray);

		var internals = new HercSimDamage.InternalsHealth[IndexShortLE()];
		data.InternalsTotal = (short)internals.Length;

		for (int i = 0; i < data.InternalsTotal; i++) {
			// Every slot is kept, not just the first ten. DBSIM reads this array flat and indexes it
			// by the dependent index a component's own record names, and two of those indices are
			// past ten: slots 10 and 11 are the rear leg servos of a four-legged chassis, which
			// Mech_ComponentDamageWrite reads by literal offset alongside slots 0 and 1. Dropping
			// them lost PITBULL's rear legs, and left the write path dereferencing nulls for every
			// 22-slot file it round-tripped.
			var system = data.NewInternalsHealth();
			system.Id = (short)i;
			system.Armor = IndexShortLE();
			if (system.Armor != 0) {
				system.Name = HercInternals.GetById((short)i);
			}

			internals[i] = system;
		}
		data.Internals = internals;

		short totalComponents = IndexShortLE();

		data.ComponentData = new HercSimDamage.HercPiece[totalComponents];
		for (int i = 0; i < totalComponents; i++) {
			data.ComponentData[i] = ParseHercPiece(data);
		}

		return data;
	}

	public override byte[]? Write(HercSimDamage data) {
		using var outStream = new MemoryStream();

		var internals = data.Internals ?? Array.Empty<HercSimDamage.InternalsHealth>();
		Emit(outStream, WriteShortLE((short)internals.Length));
		foreach (var system in internals) {
			Emit(outStream, WriteShortLE(system.Armor));
		}

		Emit(outStream, WriteShortLE((short)data.ComponentData!.Length));

		foreach (var piece in data.ComponentData) {
			WritePiece(outStream, piece);
		}

		return outStream.ToArray();
	}

	/// <summary>
	/// One piece record — <c>HercPiece_ReadRecord</c> (<c>0040cff8</c>) itself, which the sim
	/// <c>WEAPONS.DAT</c> reader runs too (see <see cref="WeaponsSimTransformer"/>). Advances
	/// <paramref name="offset"/> past everything it read.
	/// </summary>
	public HercSimDamage.HercPiece ReadPiece(byte[] bytes, ref int offset) {
		SetBytes(bytes);
		Index = offset;
		var piece = ParseHercPiece(new HercSimDamage());
		offset = Index;
		return piece;
	}

	/// <summary>The write side of <see cref="ReadPiece"/>: 8 bytes, then 4 per dependent.</summary>
	public void WritePiece(Stream outStream, HercSimDamage.HercPiece piece) {
		var internals = piece.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>();

		Emit(outStream, WriteShortLE(piece.Armor));
		Emit(outStream, WriteShortLE(piece.DebrisFlags));
		outStream.WriteByte(piece.ParentComponent);
		outStream.WriteByte(piece.DestructionFlags);
		Emit(outStream, WriteShortLE((short)internals.Length));

		foreach (var t in internals) {
			Emit(outStream, WriteShortLE(t.SpillWeight));
			Emit(outStream, WriteShortLE(t.InternalsId!.Id));
		}
	}

	private HercSimDamage.HercPiece ParseHercPiece(HercSimDamage data) {
		var piece = data.NewHercPiece();
		piece.Armor = IndexShortLE();
		piece.DebrisFlags = IndexShortLE();
		piece.ParentComponent = IndexByte();
		piece.DestructionFlags = IndexByte();

		piece.MappedInternals = new HercSimDamage.InternalsTarget[IndexShortLE()];
		for (int i = 0; i < piece.MappedInternals.Length; i++) {
			var internalComp = data.NewInternalsTarget();
			internalComp.SpillWeight = IndexShortLE();
			internalComp.InternalsId = HercInternals.GetById(IndexShortLE());
			piece.MappedInternals[i] = internalComp;
		}
		return piece;
	}

	private static void Emit(Stream outArr, byte[] data) => outArr.Write(data, 0, data.Length);
}
