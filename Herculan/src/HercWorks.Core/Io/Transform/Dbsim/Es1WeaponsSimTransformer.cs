using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's sim-side <c>dat\WEAPONS.DAT</c> (<see cref="Es1Weapons"/>; docs/es1/formats/weapons-dat-sim.md),
/// in file order: each template through ES2's piece, cluster and tail readers as <see cref="WeaponsSimTransformer"/> does,
/// then the projectile records through <see cref="ProjectileDataTransformer.ReadProjectile"/>. Writing a parsed file
/// gives back its bytes.
///
/// <para>A file the walk does not end exactly on throws <see cref="InvalidDataException"/>, so a file this model would not
/// give back whole is rejected.</para>
/// </summary>
public class Es1WeaponsSimTransformer : ByteTransformer<Es1Weapons> {
	private const int TailLength = 0x30;

	private readonly HercDamageFileTransformer _pieces = new();
	private readonly HercColliderTransformer _clusters = new();
	private readonly ProjectileDataTransformer _projectiles = new();

	/// <exception cref="InvalidDataException">The walk runs past the file or stops short of its end.</exception>
	public override Es1Weapons? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		try {
			int offset = 0;
			var templates = new Weapons.WeaponMountTemplate[Es1Weapons.TemplateCount];
			for (int i = 0; i < templates.Length; i++) {
				var t = new Weapons.WeaponMountTemplate {
					Piece = _pieces.ReadPiece(inputArray, ref offset),
					Cluster = _clusters.ReadCluster(inputArray, ref offset),
					Tail = inputArray.AsSpan(offset, TailLength).ToArray(),
				};
				offset += TailLength;
				templates[i] = t;
			}

			var projectiles = new ProjectileData.Projectile[Es1Weapons.ProjectileCount];
			for (int i = 0; i < projectiles.Length; i++) {
				projectiles[i] = _projectiles.ReadProjectile(inputArray, ref offset);
			}

			if (offset != inputArray.Length) {
				throw new InvalidDataException($"The walk ends at {offset} of {inputArray.Length} bytes.");
			}

			SetBytes(inputArray);
			Index = offset;
			return new Es1Weapons { Templates = templates, Projectiles = projectiles };
		} catch (ArgumentException e) {
			throw new InvalidDataException("The walk runs past the end of the file.", e);
		} catch (IndexOutOfRangeException e) {
			throw new InvalidDataException("The walk runs past the end of the file.", e);
		}
	}

	public override byte[]? Write(Es1Weapons source) {
		using var o = new MemoryStream();
		foreach (var t in source.Templates) {
			_pieces.WritePiece(o, t.Piece);
			_clusters.WriteCluster(o, t.Cluster);
			o.Write(t.Tail);
		}

		foreach (var p in source.Projectiles) {
			_projectiles.WriteProjectile(o, p);
		}

		return o.ToArray();
	}
}
