using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Transforms byte[] data to and from simvol0/dat/WEAPONS.DAT (see <see cref="Weapons"/> and
/// docs/retail/formats/weapons-dat-sim.md for the full field-by-field writeup). No Java equivalent existed
/// beyond a bare Total field — cracked from DBSIM.EXE disassembly. Records are variable-length:
/// each opens with a <c>.DMG</c> piece and a <c>.COL</c> cluster, which this reads and writes through
/// <see cref="HercDamageFileTransformer"/> and <see cref="HercColliderTransformer"/>, the formats'
/// own walks, exactly as <c>Weapons_LoadResourceTables</c> (<c>0040fc8c</c>) calls
/// <c>HercPiece_ReadRecord</c> and <c>Collision_ReadCluster</c>. Round-trips byte-exact against the
/// retail file.
/// </summary>
public class WeaponsSimTransformer : ByteTransformer<Weapons> {
	private readonly HercDamageFileTransformer _pieces = new();
	private readonly HercColliderTransformer _clusters = new();

	public override Weapons? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		var data = new Weapons();

		data.Total = IndexShortLE();
		data.Templates = new Weapons.WeaponMountTemplate[data.Total];

		for (int i = 0; i < data.Total; i++) {
			var t = data.NewWeaponMountTemplate();

			int offset = Index;
			t.Piece = _pieces.ReadPiece(inputArray, ref offset);
			t.Cluster = _clusters.ReadCluster(inputArray, ref offset);
			Index = offset;

			t.Tail = IndexSegment(0x30);

			data.Templates[i] = t;
		}

		return data;
	}

	public override byte[]? Write(Weapons data) {

		using var outStream = new MemoryStream();

		var total = WriteShortLE(data.Total);
		outStream.Write(total, 0, total.Length);

		for (int i = 0; i < data.Templates!.Length; i++) {
			var t = data.Templates[i];

			_pieces.WritePiece(outStream, t.Piece);
			_clusters.WriteCluster(outStream, t.Cluster);
			outStream.Write(t.Tail, 0, t.Tail.Length);
		}

		return outStream.ToArray();
	}
}
