namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /DBSIM/DAT/{name}_DEB.DAT — the debris table that goes with the same name's
/// <c>dts\{name}_DEB.DTS</c>. Three of these exist: <c>DEF_DEB</c> (the shared default),
/// <c>BASE_DEB</c> (structures), and one per HERC chassis named by that chassis'
/// <see cref="HercSimDat.DebrisFile"/>.
///
/// <para>The layout is <c>debris.cpp</c>'s own loader, <c>Debris_LoadDatabase</c> and
/// <c>Debris_LoadPieceList</c>: a count of <see cref="Group"/>s, each a throw count, a piece count, and
/// that many 14-byte <see cref="Piece"/> records. See docs/retail/formats/debris-dat.md.</para>
///
/// <para><b>Angles are stored in degrees.</b> <c>Debris_LoadPieceList</c> multiplies
/// <see cref="Piece.OrientationYaw"/> and <see cref="Piece.ThrowYaw"/> by 182 as it reads them
/// (<c>65536 / 360</c>), unless the raw value is the <c>-1</c> sentinel. The conversion is a
/// load-time step, not part of the file, so the values here are the file's own and a consumer
/// converts.</para>
/// </summary>
public class DebrisHerc {
	public Group[]? Data { get; set; }

	public Group NewGroup() => new();

	/// <summary>
	/// One throwable set. <c>Debris_ThrowGroup</c> either throws <see cref="Pieces"/> entire (when
	/// <see cref="ThrowCount"/> is zero) or draws <see cref="ThrowCount"/> pieces from it at random,
	/// weighted by each piece's <see cref="Piece.Weight"/>.
	/// </summary>
	public class Group {
		public short ThrowCount { get; set; }
		public Piece[] Pieces { get; set; } = System.Array.Empty<Piece>();
	}

	/// <summary>One shape the group can throw, and how it is thrown.</summary>
	public class Piece {
		/// <summary><c>+0x00</c> — which root of the matching <c>.DTS</c> this piece is.</summary>
		public short ShapeIndex { get; set; }

		/// <summary>
		/// <c>+0x02</c> — this piece's share of the group's weighted draw. The loader sums these
		/// into the group's own total; every retail piece states 10 or 20.
		/// </summary>
		public short Weight { get; set; }

		/// <summary>
		/// <c>+0x04</c> — the group index this piece bursts into when it lands or times out, or
		/// <c>-1</c> for a piece that simply comes to rest. Read through the same two-database index
		/// space the spawn site used.
		/// </summary>
		public short ChildGroup { get; set; }

		/// <summary>
		/// <c>+0x06</c> — the <c>EXPLOS.DAT</c> effect type that goes off where this piece ends, or
		/// <c>-1</c> for none.
		/// </summary>
		public short DestroyEffect { get; set; }

		/// <summary>
		/// <c>+0x08</c> — degrees of yaw applied to the spawn transform before the piece is placed,
		/// or <c>-1</c> to leave the piece's orientation alone.
		/// </summary>
		public short OrientationYaw { get; set; }

		/// <summary>
		/// <c>+0x0a</c> — degrees of yaw the piece is thrown along, relative to
		/// <see cref="OrientationYaw"/>, or <c>-1</c> to throw it on a random bearing.
		/// </summary>
		public short ThrowYaw { get; set; }

		/// <summary>
		/// <c>+0x0c</c> — the piece's mass, which divides the throw speed: a heavier piece is thrown
		/// less far. Retail values are 800 to 4000.
		/// </summary>
		public short Mass { get; set; }
	}
}
