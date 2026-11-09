using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Es2.Content;

namespace Herculan.Es2.Sim;

/// <summary>
/// One shape a debris group can throw, and how it is thrown — the 14-byte record
/// <c>Debris_LoadPieceList</c> reads, with its two angles already converted out of degrees.
/// </summary>
/// <param name="ShapeIndex">Which root of the database's own <c>.DTS</c> this piece is.</param>
/// <param name="Weight">Its share of the group's weighted draw.</param>
/// <param name="ChildGroup">
/// The group index this piece bursts into where it ends, or <c>-1</c>. It is read through the same
/// two-database index space the original spawn used, which is why a piece carries the database that
/// was installed when it was thrown — see <see cref="DebrisObject"/>.
/// </param>
/// <param name="DestroyEffect">The <c>EXPLOS.DAT</c> effect that goes off there, or <c>-1</c>.</param>
/// <param name="OrientationYaw">
/// Yaw applied to the spawn frame before the piece is placed, in BAM, or <c>-1</c> for "leave the
/// frame alone". The file states degrees; the loader's own <c>x182</c> is applied here.
/// </param>
/// <param name="ThrowYaw">
/// The bearing the piece is thrown along, relative to <paramref name="OrientationYaw"/>, in BAM, or
/// <c>-1</c> to throw it on a random bearing.
/// </param>
/// <param name="Mass">What divides the throw speed — a heavier piece goes less far.</param>
public readonly record struct DebrisPiece(
	short ShapeIndex, short Weight, short ChildGroup, short DestroyEffect,
	int OrientationYaw, int ThrowYaw, short Mass);

/// <summary>
/// One throwable set. <c>Debris_ThrowGroup</c> reads it two ways: a group whose
/// <see cref="ThrowCount"/> is zero throws every piece it holds, and any other throws exactly
/// <see cref="ThrowCount"/> pieces drawn from it at random, weighted by
/// <see cref="DebrisPiece.Weight"/> against <see cref="TotalWeight"/> — so the same group can throw
/// the same shape twice.
/// </summary>
public sealed class DebrisGroup {
	internal DebrisGroup(short throwCount, DebrisPiece[] pieces) {
		ThrowCount = throwCount;
		Pieces = pieces;

		foreach (var piece in pieces) {
			TotalWeight += piece.Weight;
		}
	}

	/// <inheritdoc cref="DebrisGroup" />
	public short ThrowCount { get; }

	/// <summary>The pieces, in file order — which is the order the weighted walk consumes them in.</summary>
	public IReadOnlyList<DebrisPiece> Pieces { get; }

	/// <summary>
	/// The sum of every piece's weight, which is the bound the draw is taken under. The original
	/// accumulates it in the loader rather than storing it in the file.
	/// </summary>
	public int TotalWeight { get; }
}

/// <summary>
/// One <c>{name}_DEB</c> pair — <c>dat\{name}_DEB.DAT</c>'s groups and the roots of
/// <c>dts\{name}_DEB.DTS</c> they name. This is the 12-byte struct <c>Debris_LoadDatabase</c> fills: the
/// group list, its count, the shape array and its count.
///
/// <para>Three kinds exist and all three are loaded by that one function: <c>DEF_DEB</c>, the shared
/// default every index space starts in; <c>BASE_DEB</c>, loaded by <c>Base_LoadResources</c>; and
/// one per HERC chassis, loaded by <c>MechType_InitOne</c> from the chassis' own
/// <see cref="HercSimDat.DebrisFile"/> and stored on its type record at <c>+0x212</c>.</para>
/// </summary>
public sealed class DebrisDatabase {
	private DebrisDatabase(string name, DebrisGroup[] groups) {
		Name = name;
		Groups = groups;
	}

	/// <summary>The base name, e.g. <c>DEF_DEB</c> — both the table's and the shape file's.</summary>
	public string Name { get; }

	/// <summary>The groups, indexed by the local half of a debris index.</summary>
	public IReadOnlyList<DebrisGroup> Groups { get; }

	/// <summary>
	/// Reads one database's table. Returns null when the install has no such <c>.DAT</c>, in which
	/// case nothing that names it throws anything — the same silence an unported branch gives.
	/// </summary>
	public static DebrisDatabase? Load(GameContent content, string name) {
		byte[]? bytes = content.Read(ResourceFolder, name + ".DAT");
		if (bytes == null
				|| new DebrisHercTransformer().Parse(bytes) is not DebrisHerc { Data: { } groups }) {
			return null;
		}

		var built = new DebrisGroup[groups.Length];
		for (int i = 0; i < groups.Length; i++) {
			var pieces = new DebrisPiece[groups[i].Pieces.Length];
			for (int j = 0; j < pieces.Length; j++) {
				var piece = groups[i].Pieces[j];
				pieces[j] = new DebrisPiece(
					piece.ShapeIndex, piece.Weight, piece.ChildGroup, piece.DestroyEffect,
					ToBam(piece.OrientationYaw), ToBam(piece.ThrowYaw), piece.Mass);
			}

			built[i] = new DebrisGroup(groups[i].ThrowCount, pieces);
		}

		return new DebrisDatabase(name, built);
	}

	/// <summary>The folder a database's table lives under.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>And the folder its shapes live under, under the same base name.</summary>
	public const string ShapeFolder = "dts";

	/// <summary>The default database's name, <c>Debris_LoadResources</c>'s literal <c>def_deb</c>.</summary>
	public const string DefaultName = "DEF_DEB";

	/// <summary>The structure database's name, <c>Base_LoadResources</c>' literal <c>base_deb</c>.</summary>
	public const string StructureName = "BASE_DEB";

	/// <summary>
	/// <c>Debris_LoadPieceList</c>'s <c>x182</c>, applied to both of a piece's angles unless the raw value is
	/// the <c>-1</c> sentinel. <c>65536 / 360</c> is about 182.04, so it is degrees to BAM.
	/// </summary>
	private static int ToBam(short degrees) => degrees == NoAngle ? NoAngle : degrees * BamPerDegree;

	/// <inheritdoc cref="ToBam" />
	public const int BamPerDegree = 182;

	/// <summary>The sentinel both angle fields use for "not stated", and the value they keep.</summary>
	public const int NoAngle = -1;

	/// <summary>The group at a local index, or null when the index is outside the table.</summary>
	public DebrisGroup? Group(int index) =>
		index >= 0 && index < Groups.Count ? Groups[index] : null;
}

/// <summary>
/// The two-database index space every debris spawn site addresses — <c>Debris_Resolve</c>.
///
/// <para>The original keeps the default database in a fixed global and a <i>currently installed</i>
/// alternate in another (<c>g_DebrisAlternate</c>), which each spawn site writes immediately before it
/// spawns: <c>Mech_ComponentDamageWrite</c> installs the machine's own chassis database, the
/// structure death sequence installs <c>BASE_DEB</c>, and a piece bursting into its child group
/// re-installs whichever database it was thrown out of. An index below the default's group count
/// reads the default; anything higher has that count subtracted and reads the alternate. So group 2
/// is always <c>DEF_DEB</c>'s third group, and group 10 is <c>BASE_DEB</c>'s fifth or a chassis'
/// fifth depending on what was installed a moment earlier.</para>
///
/// <para>Nothing bounds the alternate half: a site that names a high index with nothing installed
/// throws nothing, which is what the original's null pointer would have done had it ever
/// happened.</para>
/// </summary>
public sealed class DebrisCatalog {
	private readonly Dictionary<string, DebrisDatabase?> _databases = new(StringComparer.OrdinalIgnoreCase);
	private readonly GameContent _content;

	private DebrisCatalog(GameContent content, DebrisDatabase defaultDatabase) {
		_content = content;
		Default = defaultDatabase;
		_databases[defaultDatabase.Name] = defaultDatabase;
	}

	/// <summary>
	/// Loads the catalog, which means loading <c>DEF_DEB</c>. Returns null when that is missing:
	/// every index space starts in it, so without it no site can resolve anything at all.
	/// </summary>
	public static DebrisCatalog? Load(GameContent content) =>
		DebrisDatabase.Load(content, DebrisDatabase.DefaultName) is { } table
			? new DebrisCatalog(content, table)
			: null;

	/// <summary><c>DEF_DEB</c> — the low half of every index.</summary>
	public DebrisDatabase Default { get; }

	/// <summary>Every database loaded so far, the default included.</summary>
	public IEnumerable<DebrisDatabase> Databases => _databases.Values.Where(d => d != null)!;

	/// <summary>
	/// Loads one named database, caching by name. Every machine of one chassis shares its, exactly as
	/// the original holds one copy per type record.
	/// </summary>
	public DebrisDatabase? Database(string name) {
		if (_databases.TryGetValue(name, out var cached)) {
			return cached;
		}

		var table = DebrisDatabase.Load(_content, name);
		_databases[name] = table;
		return table;
	}

	/// <summary>
	/// <c>Debris_Resolve</c> — splits a debris index into the database that owns it and its group
	/// inside that database. Null when the index falls in the alternate half and
	/// <paramref name="installed"/> is nothing, or when it names a group neither table has.
	/// </summary>
	/// <param name="index">The index a spawn site names.</param>
	/// <param name="installed">What that site had installed as the alternate.</param>
	public (DebrisDatabase Database, DebrisGroup Group)? Resolve(short index, DebrisDatabase? installed) {
		var database = Default;
		int local = index;

		if (local >= Default.Groups.Count) {
			if (installed == null) {
				return null;
			}

			local -= Default.Groups.Count;
			database = installed;
		}

		return database.Group(local) is { } group ? (database, group) : null;
	}
}
