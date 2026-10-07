namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /DBSIM/DAT/BEAM.DAT — a <c>UINT16</c> count, then six-byte records of half-width, colour
/// index and <c>BEAMTEX.DBA</c> frame. Indexed by the firing <see cref="ProjectileData"/> record's
/// SubtypeId when its type is Beam:
///   0 PBW I, 1 ELF I, 2 BPBW, 3 LAS100, 4 LAS200/LAS400, 5 LAS300/LAS500, 6 PBW II, 7 ELF II,
///   8 and 9 unused.
/// Layout and the retail records: docs/retail/formats/beam-dat.md.
/// </summary>
public class BeamData {
	public short Total { get; set; }
	public Entry[]? Data { get; set; }

	public BeamData() { }

	public BeamData(short total) {
		Total = total;
		Data = new Entry[total];
	}

	public Entry NewEntry(short halfWidth, short colorId, short dbaFrameNum) => new(halfWidth, colorId, dbaFrameNum);

	public class Entry {
		/// <summary>Half-width of the beam, in world units.</summary>
		public short HalfWidth { get; set; }

		/// <summary>Palette index. Only the jagged (ELF) path uses it, as a flat fill.</summary>
		public short ColorId { get; set; }

		/// <summary><c>BEAMTEX.DBA</c> frame index.</summary>
		public short DBAFrameNum { get; set; }

		public Entry(short halfWidth, short colorId, short dbaFrameNum) {
			HalfWidth = halfWidth;
			ColorId = colorId;
			DBAFrameNum = dbaFrameNum;
		}
	}
}
