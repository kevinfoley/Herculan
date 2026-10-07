using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using Herculan.Engine.Render;
using Herculan.Engine.View;
using Herculan.Engine.World;
using System.Numerics;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The cockpit damage shake — the view band's walk, the flash's own timer, and the resources the
/// flash swaps to. See docs/retail/formats/cockpit-canopy-palette.md, "The damage shake".
///
/// <para>The walk needs no Earthsiege 2 install: it is the original's arithmetic over its own
/// constants. The palette tests do need one, and are skipped without it — what they pin is the
/// assumption the whole swap rests on, that an impact table is the same shape as the table it
/// stands in for.</para>
/// </summary>
public class CockpitDamageShakeTests {
	// ---- the view band ----------------------------------------------------------------------

	/// <summary>A machine that has not been hit sits still, whatever the frame rate.</summary>
	[Fact]
	public void AnUntouchedCockpitDoesNotMove() {
		var shake = new CockpitHitShake();

		for (int frame = 0; frame < 200; frame++) {
			shake.Update(Frame, cockpitHits: 0);
			Assert.Equal(0, shake.OffsetPixels);
			Assert.False(shake.FlashActive);
		}
	}

	/// <summary>A hit moves the view off its rest position.</summary>
	[Fact]
	public void AHitStartsTheShake() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		Assert.True(OffsetsOver(shake, frames: 20).Any(o => o > 0),
			"twenty frames of a 0-4 step should have moved the view at least once");
	}

	/// <summary>
	/// And brings the flash up during the window — but <b>not necessarily on the first frame</b>. The
	/// interval is a 0-9 tick roll, and a roll of 0 flips the palette straight back off on the frame
	/// after it was raised.
	/// </summary>
	[Fact]
	public void AHitStartsTheFlash() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		Assert.True(FlashSeenOver(shake, frames: 30, hits: 1),
			"the flash should be up for some of the window");
	}

	/// <summary>
	/// The walk stays inside the band the original seeds — <c>5 &lt;&lt; VideoMode_YCoordShift</c>,
	/// ten of the art's pixels — and never goes the other side of rest. A step that overshot a limit
	/// would put the horizon somewhere the original never puts it.
	/// </summary>
	[Fact]
	public void TheWalkStaysInsideTheBand() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		foreach (int offset in OffsetsOver(shake, frames: 500)) {
			Assert.InRange(offset, 0, CockpitHitShake.BandPixels);
		}
	}

	/// <summary>
	/// It oscillates rather than settling, and it reverses constantly: <c>CockpitView_StepShake</c>
	/// steps toward whichever limit is <i>farther</i>, and which one that is flips the moment the
	/// offset crosses the band's middle.
	/// </summary>
	[Fact]
	public void TheWalkReversesRatherThanSettling() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		var offsets = OffsetsOver(shake, frames: 120).ToList();

		Assert.True(Reversals(offsets) > 10,
			"a walk that reverses only on a limit would turn round a handful of times at most");
		Assert.True(offsets.Distinct().Count() > 3, "the view should not park on one or two offsets");
	}

	/// <summary>
	/// <b>And it never touches either limit again once it has left the low one.</b> Moving up needs
	/// the offset below the middle, so the highest reachable is the largest sub-middle offset plus
	/// the largest step, and the lowest is the middle less that step. With a ten-pixel band and steps
	/// of 0-4 that is 1 to 8 — the band is wider than the excursion it produces.
	/// </summary>
	[Fact]
	public void TheWalkNeverReachesEitherLimitAgain() {
		var shake = new CockpitHitShake();
		var reached = new HashSet<int>();

		// Several shakes on one instance, which is how the host drives it: a fresh instance's random
		// stream starts in one place and would only ever walk one path.
		for (int hit = 1; hit <= 40; hit++) {
			shake.Update(Frame, hit);
			foreach (int offset in OffsetsOver(shake, frames: 55, hits: hit)) {
				reached.Add(offset);
			}

			Advance(shake, CockpitHitShake.DurationSeconds, hits: hit);
		}

		Assert.Equal(Enumerable.Range(0, 9), reached.OrderBy(o => o));
	}

	/// <summary>
	/// The window is <c>0x3c</c> coarse ticks and then both halves go back — the view to rest and the
	/// palette to the theater's.
	/// </summary>
	[Fact]
	public void TheShakeEndsAfterItsWindow() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		Advance(shake, CockpitHitShake.DurationSeconds + Frame * 2);

		Assert.Equal(0, shake.OffsetPixels);
		Assert.False(shake.FlashActive);
	}

	/// <summary>
	/// <b>The retail quirk.</b> A second hit inside the window clears the band and then declines to
	/// seed a new one, because the flash's timer is still armed and only its expiry clears that. So
	/// the view goes still for the rest of the shake. See KNOWN_ISSUES.md.
	/// </summary>
	[Fact]
	public void ASecondHitStopsTheViewShakeInsteadOfRestartingIt() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);
		Advance(shake, CockpitHitShake.DurationSeconds / 3);

		shake.Update(Frame, cockpitHits: 2);

		Assert.All(OffsetsOver(shake, frames: 60), offset => Assert.Equal(0, offset));
	}

	/// <summary>
	/// And it extends the window all the same. Both halves of the quirk, since either alone would look
	/// like a plain restart or a plain no-op. The flash cannot show the extension, since it may have
	/// stopped too, so a third hit does: past where the first window would have closed it finds the
	/// second still running and seeds no band, where a closed window would have let it arm a fresh one.
	/// </summary>
	[Fact]
	public void ASecondHitStillExtendsTheWindow() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		double intoFirst = CockpitHitShake.DurationSeconds * 0.75;
		Advance(shake, intoFirst);
		shake.Update(Frame, cockpitHits: 2);

		// Past where the first window would have closed, and still inside the second.
		Advance(shake, CockpitHitShake.DurationSeconds - intoFirst + Frame * 2, hits: 2);
		shake.Update(Frame, cockpitHits: 3);
		Assert.All(OffsetsOver(shake, frames: 20, hits: 3), offset => Assert.Equal(0, offset));

		Advance(shake, CockpitHitShake.DurationSeconds, hits: 3);
		Assert.False(shake.FlashActive);
	}

	/// <summary>
	/// <b>The flash half of the quirk.</b> A second hit while the impact palette is up restores the
	/// theater palette without touching the toggle's saved partner, which the last toggle left holding
	/// the theater palette too — so every later toggle swaps the theater palette with itself and the
	/// flash stays off for the rest of the window.
	/// </summary>
	[Fact]
	public void ASecondHitOnAnImpactPhaseStopsTheFlash() {
		var shake = new CockpitHitShake();
		Assert.True(AdvanceToFlashPhase(shake, flashActive: true), "the flash should come up");

		shake.Update(Frame, cockpitHits: 2);

		Assert.False(shake.FlashActive);
		Assert.False(FlashSeenOver(shake, frames: 55, hits: 2),
			"the flash should stay off for the rest of the window");
	}

	/// <summary>
	/// A second hit while the theater palette is up leaves the impact palette as the toggle's partner,
	/// so the flash carries on.
	/// </summary>
	[Fact]
	public void ASecondHitOnATheaterPhaseKeepsTheFlash() {
		var shake = new CockpitHitShake();
		Assert.True(AdvanceToFlashPhase(shake, flashActive: false), "the flash should go off between toggles");

		shake.Update(Frame, cockpitHits: 2);

		Assert.True(FlashSeenOver(shake, frames: 30, hits: 2), "the flash should carry on");
	}

	/// <summary>
	/// The <c>FlashThroughSecondHit</c> tweak: the flash carries on from an impact phase as well.
	/// </summary>
	[Fact]
	public void UnderTheTweakASecondHitOnAnImpactPhaseKeepsTheFlash() {
		var shake = new CockpitHitShake { FlashSurvivesRestart = true };
		Assert.True(AdvanceToFlashPhase(shake, flashActive: true), "the flash should come up");

		shake.Update(Frame, cockpitHits: 2);

		Assert.True(FlashSeenOver(shake, frames: 30, hits: 2), "the flash should carry on");
	}

	/// <summary>
	/// A hit taken while an outside view is up is dropped, not held: <c>Cockpit_StartHitShake</c>
	/// returns at once in view mode 4, so coming back into the cockpit afterwards starts nothing.
	/// </summary>
	[Fact]
	public void AHitTakenOutsideDoesNotShakeOnReturn() {
		var shake = new CockpitHitShake();

		for (int frame = 0; frame < 10; frame++) {
			shake.Reset(cockpitHits: frame < 5 ? 0 : 1);
		}

		for (int frame = 0; frame < 60; frame++) {
			shake.Update(Frame, cockpitHits: 1);
			Assert.Equal(0, shake.OffsetPixels);
			Assert.False(shake.FlashActive);
		}
	}

	/// <summary>
	/// The flash alternates rather than staying on: <c>Palette_ToggleImpact</c> swaps the two
	/// palettes each time the 0-9 tick timer expires, so the screen strobes.
	/// </summary>
	[Fact]
	public void TheFlashAlternatesWhileTheShakeRuns() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);

		var seen = new HashSet<bool>();
		for (int frame = 0; frame < 40; frame++) {
			shake.Update(Frame, cockpitHits: 1);
			seen.Add(shake.FlashActive);
		}

		Assert.Equal(2, seen.Count);
	}

	/// <summary>
	/// Leaving the cockpit drops a shake outright rather than pausing it — the original's view mode 4
	/// clears the end tick where every other path lets it run down.
	/// </summary>
	[Fact]
	public void ResetDropsAShakeInProgress() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 1);
		Advance(shake, Frame * 5);

		shake.Reset(cockpitHits: 1);

		Assert.Equal(0, shake.OffsetPixels);
		Assert.False(shake.FlashActive);

		// And it stays down: a reset shake is not waiting to resume on the next frame.
		Assert.All(OffsetsOver(shake, frames: 30), offset => Assert.Equal(0, offset));
	}

	/// <summary>
	/// The count is an edge, not a level: holding it steady must not re-arm the shake every frame,
	/// which would make it permanent.
	/// </summary>
	[Fact]
	public void AnUnchangedCountDoesNotRetrigger() {
		var shake = new CockpitHitShake();
		shake.Update(Frame, cockpitHits: 7);

		Advance(shake, CockpitHitShake.DurationSeconds + Frame * 2, hits: 7);

		Assert.Equal(0, shake.OffsetPixels);
		Assert.False(shake.FlashActive);
	}

	// ---- the palette the flash swaps to -----------------------------------------------------

	/// <summary>
	/// Every theater's impact palette builds ramp tables the <i>same shape</i> as its ordinary ones.
	/// <see cref="SceneRenderer.SetImpactRamps"/> drops a table that does not match, because the row
	/// counts are uniforms uploaded once and the swap does not re-derive them — so a mismatch here
	/// would silently cost the whole scene half of its flash.
	/// </summary>
	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	[InlineData(4)]
	[InlineData(6)]
	[InlineData(8)]
	public void EveryTheatersImpactRampsMatchTheShapeOfItsOwn(int worldIndex) {
		if (Content() is not { } content) {
			return;
		}

		var theater = TheaterDescriptor.LoadByWorldIndex(content, worldIndex);
		if (Shading(content, theater.PaletteName, theater.PaletteName) is not { } ordinary
			|| Shading(content, theater.PaletteName, theater.ImpactPaletteName) is not { } impact) {
			return;
		}

		var ordinaryShade = SurfaceRampTable.Build(ordinary);
		var impactShade = SurfaceRampTable.Build(impact);
		Assert.NotNull(ordinaryShade);
		Assert.NotNull(impactShade);
		Assert.Equal(ordinaryShade.Height, impactShade.Height);
		Assert.Equal(ordinaryShade.GouraudBlockRow, impactShade.GouraudBlockRow);

		var ordinaryPalette = PaletteRampTable.Build(ordinary);
		var impactPalette = PaletteRampTable.Build(impact);
		Assert.NotNull(ordinaryPalette);
		Assert.NotNull(impactPalette);
		Assert.Equal(ordinaryPalette.Height, impactPalette.Height);
		Assert.Equal(ordinaryPalette.ShadeRows, impactPalette.ShadeRows);
	}

	/// <summary>
	/// And they are not the same table. A flash that swapped in an identical lookup would show
	/// nothing, and the shapes matching above is exactly the condition under which that failure would
	/// be invisible.
	/// </summary>
	[Fact]
	public void TheImpactRampIsADifferentTable() {
		if (Content() is not { } content) {
			return;
		}

		var theater = TheaterDescriptor.LoadByWorldIndex(content, 0);
		if (Shading(content, theater.PaletteName, theater.PaletteName) is not { } ordinary
			|| Shading(content, theater.PaletteName, theater.ImpactPaletteName) is not { } impact) {
			return;
		}

		Assert.NotEqual(PaletteRampTable.Build(ordinary)!.Pixels, PaletteRampTable.Build(impact)!.Pixels);
	}

	/// <summary>
	/// The cockpit's own flash palette is <c>IMPACT&lt;n&gt;.DPL</c> carrying this herc's scheme out of
	/// <c>IMPACTCP.DPL</c> — the same 24 slots step 5 fills from <c>COCKPIT.DPL</c>, from the
	/// same-index scheme table. Checked against both sources so that neither half can be silently
	/// dropped.
	/// </summary>
	[Fact]
	public void TheCockpitFlashPaletteTakesItsSchemeFromImpactcp() {
		if (Content() is not { } content) {
			return;
		}

		var flash = CockpitPalette.LoadImpact(content, "IMPACT0", cockpitSchemeIndex: 0);
		var impactCockpit = Palette(content, CockpitPalette.ImpactCockpitPaletteName);
		var theaterCockpit = Palette(content, CockpitPalette.CockpitPaletteName);
		if (flash == null || impactCockpit == null || theaterCockpit == null) {
			return;
		}

		int source = CockpitPalette.SchemeFirstEntry(0);
		bool differsFromOrdinary = false;

		for (int i = 0; i < CockpitPalette.CockpitSchemeLength; i++) {
			var installed = flash.Colors[CockpitPalette.CockpitSchemeFirstSlot + i].GetColor();
			var expected = impactCockpit.Colors[source + i].GetColor();
			Assert.Equal((expected.R, expected.G, expected.B), (installed.R, installed.G, installed.B));

			var ordinary = theaterCockpit.Colors[source + i].GetColor();
			differsFromOrdinary |= (ordinary.R, ordinary.G, ordinary.B) != (expected.R, expected.G, expected.B);
		}

		Assert.True(differsFromOrdinary,
			"IMPACTCP's scheme should not be byte-identical to COCKPIT's, or the panel would not flash");
	}

	/// <summary>
	/// The canopy's flash buffer is the same art through the other palette: a different picture, but
	/// the <i>same</i> alpha, since that channel carries the 3D viewport's hole rather than anything
	/// the palette knows about. Getting this wrong fills the viewport in for the length of a flash.
	/// </summary>
	[Fact]
	public void TheCanopysFlashBufferKeepsTheViewportHole() {
		if (Content() is not { } content
			|| CockpitArt.Load(content, "OUTLAW", "WORLD1", impactPaletteName: "IMPACT1") is not { } art) {
			return;
		}

		var front = art.Front;
		Assert.NotNull(front.ImpactPixels);
		Assert.Equal(front.Pixels.Length, front.ImpactPixels.Length);

		// A real hole, or this test would pass on a frame with no transparent pixels at all.
		int transparent = 0;
		for (int at = 3; at < front.Pixels.Length; at += 4) {
			Assert.Equal(front.Pixels[at], front.ImpactPixels[at]);
			if (front.Pixels[at] == 0) {
				transparent++;
			}
		}

		Assert.True(transparent > 0, "the forward canopy should have a viewport hole punched in it");
		Assert.NotEqual(front.Pixels, front.ImpactPixels);
		Assert.Same(front.Pixels, front.PixelsFor(flashActive: false));
		Assert.Same(front.ImpactPixels, front.PixelsFor(flashActive: true));
	}

	/// <summary>
	/// Without an impact palette the frame carries no flash buffer and falls back to its ordinary
	/// pixels, so a theater missing the file costs the flash and nothing else.
	/// </summary>
	[Fact]
	public void WithoutAnImpactPaletteTheCanopyHasNoFlashBuffer() {
		if (Content() is not { } content
			|| CockpitArt.Load(content, "OUTLAW", "WORLD1") is not { } art) {
			return;
		}

		Assert.Null(art.Front.ImpactPixels);
		Assert.Same(art.Front.Pixels, art.Front.PixelsFor(flashActive: true));
	}

	// ---- what the flash reaches -------------------------------------------------------------

	/// <summary>
	/// <b>The invariant the solid-poly path rests on.</b> A flat solid face's colour is
	/// <c>rampRow(UnlitShade)[paletteIndex]</c>, and the shader now resolves it from
	/// <see cref="PaletteRampTable"/> instead of using the colour
	/// <c>DtsMeshBuilder.ResolveSolidColors</c> baked. Those two must be the same byte, or moving the
	/// lookup to the GPU would recolour every flat face in the game — so this compares them for every
	/// palette index, in every theater, through both the ordinary palette and the flash one.
	/// </summary>
	[Fact]
	public void TheUnlitRampRowIsWhatASolidFaceWouldHaveBaked() {
		if (Content() is not { } content) {
			return;
		}

		int compared = 0;

		for (int worldIndex = 0; worldIndex < TheaterDescriptor.Count; worldIndex++) {
			var theater = TheaterDescriptor.LoadByWorldIndex(content, worldIndex);

			foreach (string paletteName in new[] { theater.PaletteName, theater.ImpactPaletteName }) {
				if (Shading(content, theater.PaletteName, paletteName) is not { } shading
					|| PaletteRampTable.Build(shading) is not { } table) {
					continue;
				}

				for (int index = 0; index < PaletteRampTable.Width; index++) {
					if (shading.Ramp.Resolve(index, ShadeRamp.UnlitShade, shading.Palette) is not { } baked) {
						continue;
					}

					int at = (table.UnlitRow * PaletteRampTable.Width + index) * 4;
					Assert.Equal(Quantise(baked.X), table.Pixels[at]);
					Assert.Equal(Quantise(baked.Y), table.Pixels[at + 1]);
					Assert.Equal(Quantise(baked.Z), table.Pixels[at + 2]);
					compared++;
				}
			}
		}

		Assert.True(compared > 1000, "the comparison should have covered every theater's whole palette");
	}

	/// <summary>
	/// And the mesh builder actually hands that index over. A flat solid face carries one; every other
	/// surface carries -1, which is what keeps a fallback colour and a lit face out of the lookup.
	///
	/// <para>The four models are the ones the class is concentrated in — in-flight ordnance and the
	/// weapon models fitted to a machine. A HERC or a building is almost entirely lit flat faces and
	/// would carry next to no index, so testing one would assert nothing.</para>
	/// </summary>
	[Theory]
	[InlineData("ROCKETS")]
	[InlineData("BULLETS")]
	[InlineData("METEOR")]
	[InlineData("MECHWPNS")]
	public void SolidFacesCarryAPaletteIndexAndNothingElseDoes(string model) {
		if (Content() is not { } content
			|| content.Read("dts", model + ".DTS") is not { } bytes
			|| new DTSModelTransformer().Parse(bytes) is not DynamixThreeSpaceModel parsed
			|| Shading(content, "WORLD0", "WORLD0") is not { } shading) {
			return;
		}

		var build = DtsMeshBuilder.BuildAll(parsed, null, shading);
		int solid = 0;

		foreach (var vertex in build.Vertices) {
			if (vertex.SolidPaletteIndex < 0) {
				continue;
			}

			solid++;

			// A real index, and a surface the shader's solid branch will actually be reached for:
			// unlit, untextured, and naming no material ramp.
			Assert.InRange(vertex.SolidPaletteIndex, 0, PaletteRampTable.Width - 1);
			Assert.Equal(0f, vertex.Textured);
			Assert.True(vertex.ShadeRamp < 0,
				"a face that names a material ramp must not also name a palette index");
		}

		Assert.True(solid > 0, $"{model} should have flat solid faces to carry an index");
	}

	/// <summary>
	/// The HUD's own colours follow the flash. <c>COLORS.DAT</c> ids and raw palette slots are both
	/// resolved at load, so without a second resolution the instruments would keep their colours while
	/// the world went red — which is the one way a partial flash reads as a fault rather than an
	/// effect. Twenty of the twenty-seven ids move under <c>IMPACT1</c>, the palette loaded here.
	/// </summary>
	[Fact]
	public void TheHudsOwnColoursFollowTheFlash() {
		if (Content() is not { } content
			|| CockpitArt.Load(content, "OUTLAW", "WORLD1", impactPaletteName: "IMPACT1") is not { } art) {
			return;
		}

		var ordinary = SampleHudColors(art);
		art.FlashActive = true;
		var flashed = SampleHudColors(art);

		Assert.NotEqual(ordinary, flashed);

		// And it is reversible — the flash ends several times a mission.
		art.FlashActive = false;
		Assert.Equal(ordinary, SampleHudColors(art));

		// Most of the table moves, not one entry of it. A single changed id would pass the check
		// above while leaving the HUD looking untouched.
		int moved = 0;
		for (int id = 0; id < HudColorTable.EntryCount; id++) {
			var before = art.LogicalColor(id);
			art.FlashActive = true;
			var after = art.LogicalColor(id);
			art.FlashActive = false;
			if (before != after) {
				moved++;
			}
		}

		Assert.True(moved >= 15, $"only {moved} of {HudColorTable.EntryCount} HUD colours moved");
	}

	/// <summary>
	/// Without an impact palette the HUD has nothing to flash to and must keep its colours rather than
	/// resolving to null or to black.
	/// </summary>
	[Fact]
	public void WithoutAnImpactPaletteTheHudKeepsItsColours() {
		if (Content() is not { } content
			|| CockpitArt.Load(content, "OUTLAW", "WORLD1") is not { } art) {
			return;
		}

		var ordinary = SampleHudColors(art);
		art.FlashActive = true;

		Assert.Equal(ordinary, SampleHudColors(art));
		Assert.Null(art.ImpactSpritePixels);
	}

	/// <summary>
	/// The plates and glyphs themselves, expanded through the flash palette from the atlas's retained
	/// indices — a different picture, the same cutout.
	/// </summary>
	[Fact]
	public void TheSpriteSheetHasAFlashExpansionWithTheSameCutout() {
		if (Content() is not { } content
			|| CockpitArt.Load(content, "OUTLAW", "WORLD1", impactPaletteName: "IMPACT1") is not { } art
			|| art.Sprites is not { Atlas: { } atlas }) {
			return;
		}

		Assert.NotNull(art.ImpactSpritePixels);
		Assert.Equal(atlas.Pixels.Length, art.ImpactSpritePixels.Length);
		Assert.NotEqual(atlas.Pixels, art.ImpactSpritePixels);

		int transparent = 0;
		for (int at = 3; at < atlas.Pixels.Length; at += 4) {
			Assert.Equal(atlas.Pixels[at], art.ImpactSpritePixels[at]);
			if (atlas.Pixels[at] == 0) {
				transparent++;
			}
		}

		Assert.True(transparent > 0, "the HUD sheet is mostly cutout and should have transparent texels");
	}

	/// <summary>A spread of what the HUD draws with: the resolved ids and the raw slots both.</summary>
	private static List<Vector3?> SampleHudColors(CockpitArt art) {
		var sampled = new List<Vector3?>();
		for (int id = 0; id < HudColorTable.EntryCount; id++) {
			sampled.Add(art.LogicalColor(id));
		}

		sampled.Add(art.GaugeColors?.FillEven);
		sampled.Add(art.HeadsDownColors?.Background);
		sampled.Add(art.TargetArrowColors?.Unlocked);
		sampled.Add(art.WeaponBarColors?.FillEven);
		sampled.Add(art.PaletteEntry(CockpitArt.WeaponBarFillEvenIndex));
		return sampled;
	}

	private static byte Quantise(float channel) =>
		(byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);

	// ---- harness ----------------------------------------------------------------------------

	/// <summary>One frame at 60 Hz, comfortably shorter than the shake's shortest timer.</summary>
	private const double Frame = 1.0 / 60.0;

	private static IEnumerable<int> OffsetsOver(CockpitHitShake shake, int frames, int hits = 1) {
		for (int frame = 0; frame < frames; frame++) {
			shake.Update(Frame, hits);
			yield return shake.OffsetPixels;
		}
	}

	private static bool FlashSeenOver(CockpitHitShake shake, int frames, int hits) {
		for (int frame = 0; frame < frames; frame++) {
			shake.Update(Frame, hits);
			if (shake.FlashActive) {
				return true;
			}
		}

		return false;
	}

	/// <summary>How many times the walk changed direction, ignoring the frames it did not move.</summary>
	private static int Reversals(IReadOnlyList<int> offsets) {
		int reversals = 0;
		int direction = 0;

		for (int i = 1; i < offsets.Count; i++) {
			int step = Math.Sign(offsets[i] - offsets[i - 1]);
			if (step == 0) {
				continue;
			}

			if (direction != 0 && step != direction) {
				reversals++;
			}

			direction = step;
		}

		return reversals;
	}

	/// <summary>
	/// Starts a shake with hit 1 and runs it until <see cref="CockpitHitShake.FlashActive"/> reads
	/// <paramref name="flashActive"/>, inside the window. False if it never does.
	/// </summary>
	private static bool AdvanceToFlashPhase(CockpitHitShake shake, bool flashActive) {
		shake.Update(Frame, cockpitHits: 1);
		for (int frame = 0; frame < 30; frame++) {
			if (shake.FlashActive == flashActive) {
				return true;
			}

			shake.Update(Frame, cockpitHits: 1);
		}

		return false;
	}

	private static void Advance(CockpitHitShake shake, double seconds, int hits = 1) {
		for (double spent = 0; spent < seconds; spent += Frame) {
			shake.Update(Frame, hits);
		}
	}

	/// <summary>
	/// A theater's ramp against a chosen palette — the pair <see cref="SurfaceRampTable"/> and
	/// <see cref="PaletteRampTable"/> are built from. Passing the impact palette with the theater's
	/// own ramp is what the engine does, because retail ships no <c>IMPACT&lt;n&gt;.RMP</c>.
	/// </summary>
	private static SurfaceShading? Shading(GameContent content, string rampName, string paletteName) =>
		ShadeRamp.Load(content, rampName) is { } ramp && Palette(content, paletteName) is { } palette
			? new SurfaceShading(ramp, palette)
			: null;

	private static DynamixPalette? Palette(GameContent content, string name) =>
		content.Read("dpl", name + ".DPL") is { } bytes
			? new DynamixPaletteTransformer().Parse(bytes) as DynamixPalette
			: null;

	private static GameContent? Content() {
		string? root = GameInstall.Locate(null);
		return root != null ? GameContent.MountSimulator(root) : null;
	}
}
