using Herculan.Engine.Content;
using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// One blip on the scanner: where it sits on the display and what colour it is drawn in.
/// </summary>
/// <param name="X">
/// Offset right of the display centre, in <b>world units</b>, already rotated into the viewing
/// machine's own frame. The paint divides it by
/// <see cref="MfdScannerState.WorldUnitsPerPixel"/> to reach device pixels, which is where the
/// original does that divide too.
/// </param>
/// <param name="Y">Offset <i>down</i> the display from its centre, same units — the plot's y is
/// already negated, so ahead of the machine is negative.</param>
/// <param name="ColorId">
/// <c>COLORS.DAT</c> logical id, from <see cref="MfdScanner.ContactColorId"/>.
/// </param>
public readonly record struct MfdContact(int X, int Y, int ColorId);

/// <summary>
/// Everything the SCANNER screen draws that comes from the simulation rather than from the layout —
/// what <c>MfdRadarScreen_Update</c> (<c>0043ebe0</c>), the screen's update slot, leaves in the screen object for
/// <c>MfdRadarScreen_Paint</c> (<c>0043eecc</c>), its paint, to read.
/// </summary>
/// <param name="Contacts">
/// The plotted contacts, screen object <c>+0x68</c>, a 100-entry vector of
/// <c>{x, y, colourId}</c> triples with the live count at <c>+0x518</c>.
/// </param>
/// <param name="TargetContact">
/// Which entry of <paramref name="Contacts"/> is the selected target, or -1 — the original keeps a
/// pointer to it at <c>+0x51c</c> and blits the bracket sprite over it after the loop.
/// </param>
/// <param name="TargetRangeMetres">
/// The <c>TRG:</c> readout, <c>+0x520</c>. <b>Only written while the selection is actually being
/// plotted</b>, so it holds its last value once the target leaves scanner range or the selection is
/// cleared; the field starts at zero, which is the <c>0000</c> a cockpit powers up showing.
/// </param>
/// <param name="RangeIndex">Zoom setting, <c>MfdDisplay+0xc5</c> — index into <see cref="MfdScanner.Ranges"/>.</param>
/// <param name="Passive">
/// Whether the viewing machine's scanner is off — <c>mech+0x96 == 0</c>, which the screen mirrors
/// into <c>+0x34</c> every update and uses both to light PASS or ACTIVE and to decide whether to
/// draw the passive-range ring.
/// </param>
public readonly record struct MfdScannerState(
	IReadOnlyList<MfdContact> Contacts,
	int TargetContact,
	int TargetRangeMetres,
	int RangeIndex,
	bool Passive) {

	/// <summary>What the display holds before a machine is flown: no contacts, longest range, passive.</summary>
	public static MfdScannerState Empty { get; } = new(
		Contacts: Array.Empty<MfdContact>(),
		TargetContact: -1,
		TargetRangeMetres: 0,
		RangeIndex: MfdScanner.DefaultRangeIndex,
		Passive: true);

	/// <summary>
	/// <see cref="Contacts"/> with a default-constructed record's null standing in for an empty list,
	/// so a caller that never filled one in draws an empty screen rather than throwing.
	/// </summary>
	public IReadOnlyList<MfdContact> Plotted => Contacts ?? Array.Empty<MfdContact>();

	/// <summary>The display range in world units — <c>MfdDisplay+0xbd</c>.</summary>
	public int Range => MfdScanner.Ranges[Math.Clamp(RangeIndex, 0, MfdScanner.Ranges.Length - 1)];

	/// <summary>
	/// World units one device pixel of the plot covers: the paint's own
	/// <c>range / 25 &gt;&gt; XCoordShift</c>, integer-divided in that order. A contact at the display
	/// range therefore lands <see cref="MfdScanner.PlotRadiusGau"/> GAU units out, inside the dish
	/// art's own 27-unit radius.
	/// </summary>
	public int WorldUnitsPerPixel =>
		Math.Max(Range / MfdScanner.PlotRadiusGau / (int)CockpitArt.GauToPixelScale, 1);
}

/// <summary>
/// The MFD's SCANNER screen (F4, mode 3) — <c>MfdRadarScreen_Ctor</c> (<c>0043e70c</c>), its update
/// slot <c>MfdRadarScreen_Update</c> (<c>0043ebe0</c>) and its paint <c>MfdRadarScreen_Paint</c> (<c>0043eecc</c>). The rest of the display — the F-key
/// column, the four aux buttons, the background chrome and the title — is <see cref="MfdLayout"/>.
///
/// <para><b>What the screen is.</b> A plan view centred on the machine being flown, rotated so its
/// own nose is up, with the turret's 90-degree arc drawn as a filled wedge over it. Everything on it
/// is <c>MFD</c> bank art the rest of the display never touches — frames 14-18.</para>
///
/// <list type="table">
/// <item><term>14, 110x110</term><description>the dish: opaque screen colour in the corners, a
/// grey ring, and a transparent interior the wedge shows through</description></item>
/// <item><term>15, 51x50</term><description>the wedge — a filled quarter disc whose <i>corner</i> is
/// its pivot</description></item>
/// <item><term>16, 10x10</term><description>the bracket drawn over the selected target</description></item>
/// <item><term>17, 6x54</term><description>the fixed 12-o'clock reference line, top edge to centre</description></item>
/// <item><term>18, 14x7</term><description>the player marker, an up-pointing triangle whose apex is
/// the centre</description></item>
/// </list>
///
/// <para><b>Coordinates.</b> As in <see cref="MfdLayout"/>, GAU (320-wide) units relative to the
/// inset screen origin; multiply by <see cref="CockpitArt.GauToPixelScale"/> for device pixels.</para>
///
/// <para>The contact list is rebuilt every frame while F4 is up, rather than by whichever of the two
/// scanners is up, which is the same result by a shorter route. The ghost-contact branch is
/// transcribed as the skip it is.</para>
/// </summary>
public static class MfdScanner {
	/// <summary>The bank every frame here comes from — the display's own.</summary>
	public const string Bank = MfdLayout.Bank;

	/// <summary>Top-left of the 110x110 dish, the constructor's <c>origin + (0x26, 1)</c>.</summary>
	public static readonly (int X, int Y) DiscOrigin = (0x26, 1);

	/// <summary>
	/// The plot's centre, <c>origin + (0x41, 0x1c)</c>. It is not the dish art's exact midpoint —
	/// that would be (65.5, 28.5) — because the constructor states it as whole GAU units.
	/// </summary>
	public static readonly (int X, int Y) Center = (0x41, 0x1c);

	/// <summary>Top-left of the reference line, <c>origin + (0x40, 3)</c>.</summary>
	public static readonly (int X, int Y) ReferenceLineOrigin = (0x40, 3);

	/// <summary>
	/// GAU units the player marker is blitted left of the centre — the constructor's
	/// <c>3 &lt;&lt; XCoordShift</c>. Its y is the centre's exactly, so the triangle's apex sits on
	/// the centre and its body hangs below.
	/// </summary>
	public const int PlayerMarkerOffsetX = 3;

	/// <summary>
	/// The divisor the plot scale is built from, screen object <c>+0x28</c>: a contact at the display
	/// range plots this many GAU units from the centre.
	/// </summary>
	public const int PlotRadiusGau = 0x19;

	/// <summary>GAU units the target bracket is blitted up and left of its contact — the paint's <c>2 &lt;&lt; XCoordShift</c>.</summary>
	public const int TargetBracketOffset = 2;

	/// <summary>Device pixels a contact blip covers, the paint's own <c>(x, y)-(x + 1, y + 1)</c> fill.</summary>
	public const int BlipSize = 2;

	/// <inheritdoc cref="DiscOrigin"/>
	public const int DiscFrame = 14;

	/// <inheritdoc cref="DiscOrigin"/>
	public const int WedgeFrame = 15;

	/// <inheritdoc cref="DiscOrigin"/>
	public const int TargetBracketFrame = 16;

	/// <inheritdoc cref="DiscOrigin"/>
	public const int ReferenceLineFrame = 17;

	/// <inheritdoc cref="DiscOrigin"/>
	public const int PlayerMarkerFrame = 18;

	/// <summary>
	/// What the wedge sprite is rotated by on top of the turret's twist. The sprite is a quarter disc
	/// filling the quadrant right and below its pivot, so its bisector starts at 45 degrees; turning
	/// it back 135 degrees puts that bisector straight up, which is where a centred turret belongs.
	/// The rotation is applied about the sprite's own <c>(0, 0)</c> corner — <c>Bitmap_BlitRotatedScaled</c>
	/// rotates the destination quad's corners and then translates by the caller's position, so that
	/// position is the pivot rather than a top-left.
	/// </summary>
	public const short WedgeAngleOffset = -0x6000;

	/// <summary>
	/// The three display ranges in world units, <c>MfdScannerRanges</c> (<c>004d1cf4</c>), which
	/// <c>MfdDisplay_Ctor</c> writes as literals: 300 / 600 / 1200 metres at 1000 units = 6 m.
	/// </summary>
	public static readonly int[] Ranges = { 50000, 100000, 200000 };

	/// <summary>The zoom index a display is built at — the constructor's own <c>+0xc5 = 2</c>.</summary>
	public const int DefaultRangeIndex = 2;

	/// <summary>
	/// RANGE (button 8) steps the zoom index and wraps — <c>MfdDisplay_CycleScannerRange</c> (<c>00446fc8</c>): <c>+1</c>, and back to 0
	/// at 3. It steps <i>outwards</i> from the default, so the first press goes from 1200 m to 300 m.
	/// </summary>
	public static int NextRangeIndex(int index) => index + 1 == Ranges.Length ? 0 : index + 1;

	/// <summary>
	/// The radius the passive-range ring is drawn at, the paint's own literal. It is the range at
	/// which a scanner paints something that is not emitting back (the same figure the detection
	/// sweep uses), and it is only drawn when the machine is passive <i>and</i> the display range
	/// exceeds it — so it appears on the 1200 m setting alone.
	/// </summary>
	public const int PassiveRingRange = 140000;

	/// <summary>
	/// Background the disc rect is flooded with before anything else goes down, and the background
	/// each of the four readout labels paints behind itself.
	///
	/// <para><b>A raw palette index, not a <c>COLORS.DAT</c> id.</b> The paint states it as an
	/// immediate, and the original's rule is that a colour arriving in a data file is a logical id
	/// while one a constructor writes as a literal is already a palette index — the same rule the
	/// weapon panel's raw 32/34/46 follow. It resolves to (12,12,12), which is exactly what the dish
	/// art's own corner pixels are, so the flood and the corners read as one flat dark face and the
	/// four readout boxes are invisible against it. Reading it as an id instead lands on palette 24,
	/// a mid grey that would fill the whole dish.</para>
	/// </summary>
	public const int BackgroundPaletteIndex = 0x11;

	/// <summary>The passive-range ring's colour.</summary>
	public const int PassiveRingColorId = 11;

	/// <summary>
	/// Contact colours, from the paint's switch on the contact's target class and its side. The
	/// original reads eight separate <c>HudColorTable</c> entries here; laid out as a table the
	/// pattern is one pair per class plus a fallback.
	/// </summary>
	public const int HercFriendlyColorId = 16;

	/// <inheritdoc cref="HercFriendlyColorId"/>
	public const int HercHostileColorId = 9;

	/// <inheritdoc cref="HercFriendlyColorId"/>
	public const int StructureFriendlyColorId = 6;

	/// <inheritdoc cref="HercFriendlyColorId"/>
	public const int StructureHostileColorId = 12;

	/// <inheritdoc cref="HercFriendlyColorId"/>
	public const int FlyerFriendlyColorId = 17;

	/// <inheritdoc cref="HercFriendlyColorId"/>
	public const int FlyerHostileColorId = 15;

	/// <summary>
	/// What a contact whose class the switch does not name is drawn in. Unreachable with retail data —
	/// every constructed object states one of the four classes — but it is the original's own default
	/// arm and costs nothing to carry.
	/// </summary>
	public const int UnknownColorId = 18;

	/// <summary>The colour a contact of this class and side plots in.</summary>
	public static int ContactColorId(TargetClass targetClass, MissionSide side) {
		bool hostile = side != MissionSide.Human;
		return targetClass switch {
			TargetClass.Herc => hostile ? HercHostileColorId : HercFriendlyColorId,
			TargetClass.Structure or TargetClass.GroundVehicle =>
				hostile ? StructureHostileColorId : StructureFriendlyColorId,
			TargetClass.Flyer => hostile ? FlyerHostileColorId : FlyerFriendlyColorId,
			_ => UnknownColorId,
		};
	}

	/// <summary>
	/// Font all four readouts are written in — <c>ColorSchemePanels[13]</c>, which the constructor
	/// reaches by its absolute address <c>0049b0e0</c>.
	/// </summary>
	public const string ReadoutFont = "GREEN";

	/// <summary>
	/// <c>STRINGS0.STR</c> group holding <c>TRG:</c>, the bottom-left caption — the paint's
	/// <c>DAT_004d16b4</c>. It is a one-string group of its own, as is
	/// <see cref="RangeCaptionGroup"/>; the two are adjacent <c>.bss</c> arrays, which is what makes
	/// the pair identifiable.
	/// </summary>
	public const int TargetCaptionGroup = 30;

	/// <summary>And <c>RNG:</c>, the top-left caption — <c>DAT_004d16b8</c>.</summary>
	public const int RangeCaptionGroup = 31;

	/// <summary>
	/// Rects for the four readouts, GAU relative to the inset origin, each stated by the constructor
	/// as a top-left plus a size. The two captions are left-aligned and the two values right-aligned
	/// (<c>Label_SetRect</c> alignment 1 and 4), which is what parks them in the disc's four corners
	/// where the circle leaves room.
	/// </summary>
	public static readonly (int X0, int Y0, int X1, int Y1) RangeCaptionRect = (0x26, 1, 0x26 + 0xc, 1 + 5);

	/// <inheritdoc cref="RangeCaptionRect"/>
	public static readonly (int X0, int Y0, int X1, int Y1) RangeValueRect = (0x4f, 1, 0x4f + 0x10, 1 + 5);

	/// <inheritdoc cref="RangeCaptionRect"/>
	public static readonly (int X0, int Y0, int X1, int Y1) TargetCaptionRect = (0x26, 0x33, 0x26 + 0xc, 0x33 + 5);

	/// <inheritdoc cref="RangeCaptionRect"/>
	public static readonly (int X0, int Y0, int X1, int Y1) TargetValueRect = (0x4f, 0x33, 0x4f + 0x10, 0x33 + 5);

	/// <summary>
	/// How the two value readouts are formatted. The paint runs <c>_itoa</c> into a four-byte field
	/// and then walks the digits back to its far end, filling what it passes with <c>'0'</c> — a
	/// right-aligned, zero-padded four-character field, which is why an undamaged power-up reads
	/// <c>1200</c> and <c>0000</c>.
	/// </summary>
	public const int ReadoutWidth = 4;

	/// <inheritdoc cref="ReadoutWidth"/>
	public static string Readout(int metres) =>
		metres.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(ReadoutWidth, '0');

	/// <summary>
	/// Rebuilds the contact list — <c>MfdRadarScreen_Update</c> (<c>0043ebe0</c>), which the display runs and then paints every
	/// frame while the scanner is up (mode 3's dirty flag is never cleared, unlike the status
	/// screens', which refresh on a 30-tick timer).
	///
	/// <para>The filter, in the original's own order: skip the viewing machine itself, anything out
	/// of the fight (<c>+0x99</c>/<c>+0xa4</c>), anything whose group is still waiting on a deployment
	/// action, and any ordinary structure whose type is invulnerable — which is what keeps the three
	/// scenery types off the display. Then <b>a Cybrid that is not currently radar-visible is
	/// dropped</b>: friendlies always plot, hostiles only once something has painted them, which is
	/// what makes the PASS/ACTIVE choice visible on this screen.</para>
	///
	/// <para><b>The blinking last-known-position contact is dead code.</b> The original's hostile
	/// branch does not simply skip: on every other coarse tick it looks for a stored position at
	/// <c>obj+0x1aa</c>, gated on <c>obj+0xa7 == 0</c>. All three constructors — <c>Mech_Constructor</c>,
	/// <c>Flyer_Constructor</c> and the base-object prologue <c>Base_Construct</c> inlines five times —
	/// set that byte to 1 the moment the object is built, and nothing in the image ever clears it, so
	/// the branch always falls through to the skip. Nothing writes <c>+0x1aa</c> either. It is
	/// transcribed here as the skip it actually is.</para>
	///
	/// <para>Range is the ground-plane approximation, not the 3D one, and the test is against the
	/// current display range — so zooming in genuinely drops distant contacts rather than just
	/// rescaling them.</para>
	/// </summary>
	/// <param name="viewer">The machine being flown — <c>CockpitView+0x203</c>.</param>
	/// <param name="objects">The live object list.</param>
	/// <param name="selected">The current target, whose blip gets the bracket and whose range fills <c>TRG:</c>.</param>
	/// <param name="previous">Last frame's state, for the range index and the sticky <c>TRG:</c> value.</param>
	public static MfdScannerState Build(SimObject? viewer, IReadOnlyList<SimObject>? objects,
			SimObject? selected, MfdScannerState previous) {
		if (viewer == null || objects == null) {
			return previous with { Contacts = Array.Empty<MfdContact>(), TargetContact = -1 };
		}

		int range = previous.Range;
		short inverse = unchecked((short)-(short)viewer.Heading);
		short cos = SimTrig.Cos(inverse);
		short sin = SimTrig.Sin(inverse);

		var contacts = new List<MfdContact>();
		int targetContact = -1;
		int targetRange = previous.TargetRangeMetres;

		for (int i = 0; i < objects.Count; i++) {
			var candidate = objects[i];
			if (candidate == viewer || candidate.Removed || candidate.Neutralised
					|| candidate.AwaitingDeployment) {
				continue;
			}

			// The observer camera is not in DBSIM's live-object list at all; SimWorld's holds it. It is
			// excluded by class here for the same reason Detection excludes it — see that type.
			if (candidate.TargetClass == TargetClass.None) {
				continue;
			}

			if (candidate is BaseObject { TargetClass: TargetClass.Structure, Type.Invulnerable: true }) {
				continue;
			}

			if (!candidate.RadarVisible && candidate.Side != MissionSide.Human) {
				continue;
			}

			int dx = candidate.Position.X - viewer.Position.X;
			int dy = candidate.Position.Y - viewer.Position.Y;
			int distance = Math.Max(SimMath.FastMagnitude2D(dx, dy), 1);
			if (range <= distance) {
				continue;
			}

			// Math_Rotate2DPoint, at Q14 with the original's own rounding. The plot's y is negated
			// because the display's y runs down the screen while the rotated frame's runs ahead of the
			// machine.
			int plotX = (int)(((long)dx * cos - (long)dy * sin + 0x2000) >> 14);
			int plotY = (int)(((long)dx * sin + (long)dy * cos + 0x2000) >> 14);

			if (candidate == selected) {
				targetContact = contacts.Count;
				targetRange = WorldUnitsToMetres(distance);
			}

			contacts.Add(new MfdContact(plotX, -plotY,
				ContactColorId(candidate.TargetClass, candidate.Side)));
		}

		return previous with {
			Contacts = contacts,
			TargetContact = targetContact,
			TargetRangeMetres = targetRange,
			Passive = !viewer.ScannerActive,
		};
	}

	/// <summary>
	/// <c>Hud_WorldUnitsToMetres</c> (<c>00434228</c>) — the game's own unit scale, stated as
	/// <c>(units / 1000) * 6</c>. The integer divide is the original's: the readout moves in steps of
	/// six metres.
	/// </summary>
	public static int WorldUnitsToMetres(int worldUnits) => worldUnits / 1000 * 6;
}
