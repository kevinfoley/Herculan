namespace HercWorks.Core.Data.File.Dat.Shell;

/// <summary>
/// FILE - <c>/SHELL/GAM/ARM_HOTS.DAT</c> and <c>/SHELL/GAM/RPR_HOTS.DAT</c> — the clickable regions
/// laid over a chassis picture, one group per chassis. <c>ARM_HOTS</c> covers the weapon hardpoints
/// on the arming screen; <c>RPR_HOTS</c> covers the six damage locations on the repair screen.
///
/// <code>
/// int16 groupCount              -- 9, one per chassis; the reader asserts it
/// per group:
///   int16 hercId                -- 0-8, sequential in both retail files
///   int16 areaCount
///   areaCount x { int32 x0, y0, x1, y1 }
/// </code>
///
/// <para><b>Both retail files parse exactly to EOF.</b> <c>ARM_HOTS</c> carries 60 areas over counts
/// 3, 5, 5, 8, 9, 9, 10, 4, 7 — a chassis's mount capacity, and the 10 the widest chassis needs is
/// exactly the length of the arming screen's hotspot handler table. <c>RPR_HOTS</c> carries six for
/// every chassis, the HERC's damage locations; the Tomahawk's and the Maverick's hold all-zero rects
/// in areas 1 and 2.
/// </para>
///
/// <para><b>The four int32s are an inclusive rect, not a position and a size.</b>
/// <c>Hotspots_BuildOverlay</c> (<c>0043c1a0</c>, <c>wsquadi.cpp</c>) hands the 16 bytes straight to
/// <c>ESRect_Ctor</c> as its rect argument, which everywhere else in the executable is
/// <c>{x0, y0, x1, y1}</c> with both corners inclusive. The geometry says the same on its own: the
/// first chassis's first two arming areas are <c>(37, 94, 70, 125)</c> and <c>(157, 94, 191, 125)</c>,
/// a left and right hardpoint mirrored about x≈114, which is only true read as two corners.</para>
///
/// See docs/retail/formats/herc-catalogs.md#gamarm_hotsdat-and-gamrpr_hotsdat--the-clickable-regions.
/// </summary>
public class HardpointOverlayConfig {
	public Herc[]? Entries { get; set; }

	public Herc NewEntry() => new();

	public class Herc {
		/// <summary>Chassis index, 0-8. Sequential in both retail files, and read rather than assumed.</summary>
		public short HercId { get; set; }

		public OverlayArea[]? Areas { get; set; }

		public Herc() { }

		public Herc(short uid, int coordSize) {
			HercId = uid;
			Areas = new OverlayArea[coordSize];
		}

		public OverlayArea NewSegment() => new();

		/// <summary>
		/// One clickable box over the chassis picture, in the same inclusive-rect convention as every
		/// widget rect in the shell. Its index within the group is what selects the hotspot's handler,
		/// so <see cref="Id"/> is ordering rather than data — the file does not carry it.
		/// </summary>
		public class OverlayArea {
			public int Id { get; set; }
			public int X0 { get; set; }
			public int Y0 { get; set; }
			public int X1 { get; set; }
			public int Y1 { get; set; }

			/// <summary>Width in pixels, inclusive of both edges.</summary>
			public int Width => X1 - X0 + 1;

			/// <summary>Height in pixels, inclusive of both edges.</summary>
			public int Height => Y1 - Y0 + 1;
		}
	}
}
