using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.Struct;
using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>Ported from org.hercworks.core.io.transform.dbsim.ProjectileDataTransformer.</summary>
public class ProjectileDataTransformer : ByteTransformer<ProjectileData> {
	/// <summary>Bytes one <see cref="ProjectileData.Projectile"/> record occupies.</summary>
	public const int RecordLength = 0x24;

	public override ProjectileData? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		var data = new ProjectileData();

		data.Total = IndexShortLE();
		data.Data = new ProjectileData.Projectile[data.Total];

		for (int p = 0; p < data.Total; p++) {
			data.Data[p] = ParseProjectile(data);
		}

		return data;
	}

	/// <summary>
	/// One 36-byte record, for a file that carries the records without this file's count: ES1's <c>WEAPONS.DAT</c>
	/// (see <see cref="Es1WeaponsSimTransformer"/>). Advances <paramref name="offset"/> past it.
	/// </summary>
	public ProjectileData.Projectile ReadProjectile(byte[] bytes, ref int offset) {
		SetBytes(bytes);
		Index = offset;
		var proj = ParseProjectile(new ProjectileData());
		offset = Index;
		return proj;
	}

	public override byte[]? Write(ProjectileData data) {

		using var outStream = new MemoryStream();
		outStream.Write(WriteShortLE(data.Total));

		for (int p = 0; p < data.Data!.Length; p++) {
			WriteProjectile(outStream, data.Data[p]);
		}

		return outStream.ToArray();
	}

	/// <summary>The write side of <see cref="ReadProjectile"/>.</summary>
	public void WriteProjectile(Stream outStream, ProjectileData.Projectile proj) {
		void WriteShort(short s) => outStream.Write(WriteShortLE(s));

		WriteShort(proj.Type!.Val);
		WriteShort(proj.SubtypeId);
		WriteShort(proj.DamageShield);
		WriteShort(proj.DamageArmor);
		WriteShort(proj.SplashFactor);
		WriteShort(proj.Speed);

		foreach (short[] effects in new[] { proj.ImpactFXShield, proj.ImpactFXGround, proj.ImpactFXArmor }) {
			foreach (short effect in effects) {
				WriteShort(effect);
			}
		}
	}

	private ProjectileData.Projectile ParseProjectile(ProjectileData data) {
		var proj = data.NewProjectile();

		proj.Type = ProjectileType.ForId(IndexShortLE());
		proj.SubtypeId = IndexShortLE();
		proj.DamageShield = IndexShortLE();
		proj.DamageArmor = IndexShortLE();
		proj.SplashFactor = IndexShortLE();
		proj.Speed = IndexShortLE();

		// The file order is shield, ground, armour (docs/retail/formats/proj-dat.md#layout).
		foreach (short[] effects in new[] { proj.ImpactFXShield, proj.ImpactFXGround, proj.ImpactFXArmor }) {
			for (int i = 0; i < effects.Length; i++) {
				effects[i] = IndexShortLE();
			}
		}

		return proj;
	}
}
