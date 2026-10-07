namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - /SIMVOL0/OFS/PILOTn.OFS — where each frame of the matching /SIMVOL0/DBA/PILOTn.DBA
/// portrait sheet is drawn inside its comm box. No header and no count field: the file is a flat
/// array of 12-byte entries, and the loader reads a fixed 27 of them.
///
/// The layout is DBSIM's own loader,
/// <c>HddGauge_LoadPilotFrames</c> (<c>0044a7c0</c>), which per entry reads a 4-byte frame index and
/// then copies the following 8 bytes into <c>gauge + index * 8 + 0x3d</c>. So an entry is three
/// INT32s and the pair is signed, in the bank's own 320-wide space.
///
/// See docs/retail/formats/heads-down-display.md, "Squad comm boxes", for what the pair means and which
/// entries the shipped code path reaches.
/// </summary>
public class PilotOffsetFile {
	public Entry[]? Entries { get; set; }

	public class Entry {
		/// <summary>Which frame of the paired PILOTn.DBA this entry places. Sequential in every real file.</summary>
		public int Index { get; set; }

		/// <summary>Pixels right of the box origin the frame is drawn at, in the bank's own 320-wide space.</summary>
		public int X { get; set; }

		/// <inheritdoc cref="X"/>
		public int Y { get; set; }
	}
}
