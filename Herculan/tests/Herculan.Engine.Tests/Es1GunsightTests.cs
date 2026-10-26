using System.Buffers.Binary;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dts.Anim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using Herculan.Engine.Anim;
using Herculan.Engine.Numerics;
using Herculan.Es1.Gunsight;
using Herculan.Es1.Host;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Herculan.Es1.WeaponRows;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's roving gunsight (docs/es1/simulation/cockpit-gunsight-hud.md) on a constructed cockpit: the heading tape at
/// (40, 10)–(280, 25), 240 wide over a nine-frame bank, so <i>P</i> = 9 × 240 × <c>0x400</c> / <c>0x10000</c> = 33.
/// </summary>
public class Es1GunsightTests {
	private const int TapeX0 = 40, TapeY0 = 10, TapeX1 = 280, TapeY1 = 25, Frames = 9;

	/// <summary>
	/// The set-heading slot places the strip for the machine's heading negated, with the added 19/18 term and the wrap at
	/// north. Heading −<c>0x4000</c>: <i>h</i> = <c>0x4000</c>, <i>q</i> = 528, frame 2, offset 48 + (64 × 117) &gt;&gt; 8 =
	/// 77. Heading 1: <i>h</i> = 65535, <i>q</i> = 2111, frame 8, offset 191 + 116 = 307, past the width, so 67 and frame
	/// 9, wrapped to 0. Catches the heading not negated, the added term dropped, or the frame not wrapped (an index past
	/// the bank).
	/// </summary>
	[Theory]
	[InlineData(-0x4000, 2, 3, TapeX0 - 77)]
	[InlineData(1, 0, 1, TapeX0 - 67)]
	public void HeadingTapePlacesTheStripForTheHeading(short heading, int left, int right, int stripX) {
		var rig = Rig();
		rig.Player.Rotation = (0, 0, heading);

		rig.Gunsight.HeadingTape.AddDelta(0);

		Assert.Same(rig.Banks.HTick![left], rig.Gunsight.HeadingTape.LeftFrame);
		Assert.Same(rig.Banks.HTick[right], rig.Gunsight.HeadingTape.RightFrame);
		Assert.Equal(stripX, rig.Gunsight.HeadingTape.StripX);
	}

	/// <summary>
	/// <c>Gunsight_SetValues</c> hands the twist's old − new to the rotation indicator and the pitch's to the vertical bar,
	/// so each bar's value follows its angle, held to its limits; and the heading's new − old to the waypoint indicator's
	/// limits and to the tape's set-heading, which reads the machine's heading 0 and so shows frames 0 and 1. Catches a
	/// delta's sign flipped, a delta handed to the wrong child, the limits not shifted, or the tape left alone.
	/// </summary>
	[Fact]
	public void SetValuesMovesEachChildByItsAngle() {
		var rig = Rig();
		var block = rig.Gunsight.GetStateBlock().Copy();
		block.Heading = 0x100;
		block.Twist = 0x4000;
		block.Pitch = -300;

		rig.Gunsight.SetValues(block);

		Assert.Equal(Es1Gunsight.RotationLimit, rig.Gunsight.RotationIndicator.Value);
		Assert.Equal(-300, rig.Gunsight.VerticalBar.Value);
		Assert.Equal(-Es1Gunsight.HeadingLimit + 0x100, rig.Gunsight.WaypointIndicator.Min);
		Assert.Equal(Es1Gunsight.HeadingLimit + 0x100, rig.Gunsight.WaypointIndicator.Max);
		Assert.Same(rig.Banks.HTick![0], rig.Gunsight.HeadingTape.LeftFrame);
		Assert.Equal((short)0x4000, rig.Gunsight.State.Twist);
	}

	/// <summary>
	/// The waypoint straight up the map from the machine is at bearing 0. With the heading 0 the marker sits at the tape's
	/// middle, <i>x1</i> − <c>0xe38</c> × 240 / <c>0x1c70</c> = 160, its tip at row <i>y0</i> + 8 filled in colour 1;
	/// with the heading <c>0x800</c> handed through <c>Gunsight_SetValues</c> the lower limit is −<c>0x638</c> and it sits
	/// at 280 − 52 = 228; past an error of <c>0xe38</c> either way it is the arrow at that end. The caption reads the
	/// cursor + 1 and 10000 units as 60 metres. Catches the marker placed from the error instead of the shifted limit, the
	/// sides swapped, or the caption numbering the cursor.
	/// </summary>
	[Theory]
	[InlineData(0, 160)]
	[InlineData(0x800, 228)]
	[InlineData(0xe39, -1)]
	[InlineData(-0xe39, -2)]
	public void WaypointMarkerFollowsTheShiftedLimits(short heading, int x) {
		var rig = Rig();
		rig.Player.Group = new Es1MissionGroup { Route = [new Vec3i(0, 0, 0), new Vec3i(0, 10000, 0)] };
		rig.Player.Rotation = (0, 0, heading);
		var block = rig.Gunsight.GetStateBlock().Copy();
		block.Heading = heading;
		rig.Gunsight.SetValues(block);
		var indicator = rig.Gunsight.WaypointIndicator;

		indicator.Paint();

		Assert.Equal(x == -1, indicator.OffRight);
		Assert.Equal(x == -2, indicator.OffLeft);
		var marker = indicator.Marker();
		if (x >= 0) {
			Assert.Equal([(x, TapeY0 + 8), (x - 2, TapeY0 + 5), (x + 2, TapeY0 + 5)], marker);
			var display = rig.Views.Display;
			Assert.Equal(Es1HudWaypointIndicator.MarkerColor, display.Cell(display.DrawPage, x, TapeY0 + 8));
			Assert.Equal(0, display.Cell(display.DrawPage, x - 1, TapeY0 + 8));
		} else {
			Assert.Equal(x == -1 ? TapeX1 + 2 : TapeX0 - 2, marker[x == -1 ? 1 : 2].X);
		}

		Assert.Equal("WP 1: 60 M.", indicator.CaptionText);
	}

	/// <summary>
	/// <c>CockpitView_RectCornerVisible</c> passes a rect either of whose corners lies in columns 0–319 and rows 0–200 of
	/// the canvas, and none while the widgets are off. Catches the rows' bound made 199 like the columns' 319, the
	/// second corner not tested, or the widgets-off test dropped.
	/// </summary>
	[Fact]
	public void RectCornerVisibleTestsEitherCorner() {
		var cockpit = Rig().Views.Cockpit;

		Assert.True(cockpit.RectCornerVisible(new Es1Rect(319, 200, 999, 999)));
		Assert.True(cockpit.RectCornerVisible(new Es1Rect(-5, -5, 0, 0)));
		Assert.False(cockpit.RectCornerVisible(new Es1Rect(320, 0, 999, 999)));
		Assert.False(cockpit.RectCornerVisible(new Es1Rect(-1, 0, 100, 201)));
		cockpit.DisableWidgets();
		Assert.False(cockpit.RectCornerVisible(new Es1Rect(0, 0, 0, 0)));
	}

	/// <summary>
	/// The click surface's gadget is the root's 23rd widget, after the console's five buttons, the shield display's two,
	/// the MFD's 13 buttons and its click surface, and the throttle's slider, which <c>Cockpit_LoadPanel</c> builds first,
	/// and ahead of the Heads-Down Display's 15 buttons and the strips the first frame builds; and a click arms the pick only with the left button. Catches the gadget registered after the strips (which
	/// take an overlapping click first) or the button's bit misread.
	/// </summary>
	[Fact]
	public void ClickGadgetComesBeforeTheStripsAndArmsOnTheLeftButton() {
		var rig = Rig();
		new Es1FrameLoop(rig.Views, () => { }).StartMission();
		var root = rig.Views.Cockpit.Root;

		Assert.Equal<Es1Widget>(rig.Views.Cockpit.ConsoleButtons.Buttons,
			root.Clickables.Take(Es1ConsoleButtons.ButtonCount));
		Assert.Same(rig.Views.Cockpit.Shields.FrontButton, root.Clickables[5]);
		Assert.Same(rig.Views.Cockpit.Shields.RearButton, root.Clickables[6]);
		Assert.Equal<Es1Widget>(rig.Views.Cockpit.Mfd.Buttons, root.Clickables.Skip(7).Take(13));
		Assert.Same(rig.Views.Cockpit.Mfd.ClickSurface, root.Clickables[20]);
		Assert.Same(rig.Views.Cockpit.Throttle.Slider, root.Clickables[21]);
		Assert.Same(rig.Gunsight.ClickSurface.Gadget, root.Clickables[22]);
		Assert.Equal<Es1Widget>(rig.Views.Cockpit.Hdd.Buttons, root.Clickables.Skip(23).Take(15));
		Assert.Same(rig.Views.Cockpit.BottomStrip, root.Clickables[38]);
		rig.Gunsight.ClickSurface.Gadget.OnClick(2);
		Assert.False(rig.Gunsight.PickArmed);
		rig.Gunsight.ClickSurface.Gadget.OnClick(1);
		Assert.True(rig.Gunsight.PickArmed);
	}

	/// <summary>
	/// The click surface's paint gives the forward slot the gunsight's cursor while the pointer is in its rect, its edge
	/// included, and the arrow outside it. Catches the two cursors swapped or the rect's edge left out.
	/// </summary>
	[Fact]
	public void ClickSurfaceSetsTheForwardCursor() {
		var rig = Rig();
		var surface = rig.Gunsight.ClickSurface;
		var cursor = rig.Views.Display.Cursor;

		cursor.SyncPosition(surface.Rect.X1, surface.Rect.Y1);
		surface.Paint();
		Assert.Same(rig.Cursors[Es1CockpitCursors.Gunsight], rig.Views.Cockpit.Cursors.Forward);

		cursor.SyncPosition(surface.Rect.X1 + 1, surface.Rect.Y1);
		surface.Paint();
		Assert.Same(rig.Cursors[Es1CockpitCursors.Arrow], rig.Views.Cockpit.Cursors.Forward);
	}

	/// <summary>
	/// The view manager on a 320 × 200 canvas whose <c>.GAU</c> puts the tape at (40, 10)–(280, 25), the reticle at (160,
	/// 70) and the click surface at (53, 0)–(266, 117), with a nine-frame tick bank and seven distinct cursors, for a
	/// player at the origin, or <paramref name="player"/> in <paramref name="globals"/>. Every view's window is rows 0–140
	/// of the screen, its clip region the same, and its projection centre (160, 70).
	/// </summary>
	internal static TestRig Rig(Es1SimGlobals? globals = null, Es1Mech? player = null) {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var layout = new Es1GauFile {
			Gunsight = new Es1GauGunsightBlock {
				Int2 = 319, Int3 = 199,
				HeadingTape = new Es1GauRect(TapeX0, TapeY0, TapeX1, TapeY1),
				SlideBar = new Es1GauRect(300, 40, 305, 120),
				ReticleCentre = new PixelPoint(160, 70),
				ReticleHalfSize = 10,
				ClickSurface = new Es1GauRect(53, 0, 266, 117),
			},
		};
		BinaryPrimitives.WriteInt32LittleEndian(layout.Bytes8.AsSpan(0), 319);
		BinaryPrimitives.WriteInt32LittleEndian(layout.Bytes8.AsSpan(4), 199);
		var banks = new Es1GunsightBanks { HTick = Enumerable.Range(0, Frames).Select(_ => new DynamixBitmap()).ToArray() };
		var cursors = Enumerable.Range(0, Es1CockpitViews.CursorNames.Length).Select(_ => Es1CursorImage.Of(1, 1, 0, 0)).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, new DynamixBitmap?[4], null, layout, cursors,
			gunsightBanks: banks);
		player ??= NewMech();
		var views = new Es1ViewManager(files, new Es1VideoMemory(), player, globals: globals);
		return new TestRig(views, views.Cockpit.Gunsight, player, banks, cursors);
	}

	private static Es1Mech NewMech() {
		var chassis = new Es1HercSimDat {
			AnimId_StopMove = 0,
			AnimId_StopReverse = 0,
			AnimId_TorsoTwist = -1,
			AnimId_TorsoPitch = -1,
		};
		var animation = ShapeAnimation.From(new ANAnimList {
			Sequences = [new ANSequence { Frames = [new ANSequenceFrame { Duration = 100 }], PartIds = [0], TransformIndices = [0] }],
			Transforms = [new ANAnimListTransform()],
			DefaultTransforms = [0],
		})!;
		return new Es1Mech(new Es1MechType(chassis, animation), new Es1DwellJitter()) { IsPlayer = true };
	}

	internal sealed record TestRig(Es1ViewManager Views, Es1Gunsight Gunsight, Es1Mech Player, Es1GunsightBanks Banks,
		Es1CursorImage[] Cursors);
}
