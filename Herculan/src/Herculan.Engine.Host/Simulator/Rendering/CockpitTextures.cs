using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator.Rendering;

/// <summary>
/// The cockpit's art on the GPU: the three canopy panels, the Heads-Down Display's, the HUD sprite sheet and
/// the heads-down map's relief. All of them are re-uploaded rather than re-bound when the damage flash or the
/// shield rings change them.
/// </summary>
sealed class CockpitTextures : IDisposable {
	private readonly CockpitArt? _art;
	private readonly HddCommandScreen? _hddCommand;
	private readonly HddMapRaster? _hddMapFlashRaster;

	public CockpitTextures(GL gl, CockpitArt? art, HddCommandScreen? hddCommand, HddMapRaster? hddMapFlashRaster) {
		_art = art;
		_hddCommand = hddCommand;
		_hddMapFlashRaster = hddMapFlashRaster;
		if (art == null) {
			return;
		}

		Front = new GpuTexture(gl, art.Front.Pixels, art.Front.Width, art.Front.Height);
		Side = new GpuTexture(gl, art.Side.Pixels, art.Side.Width, art.Side.Height);

		if (art.HeadsDown is { } headsDownFrame) {
			HeadsDown = new GpuTexture(gl, headsDownFrame.Pixels, headsDownFrame.Width, headsDownFrame.Height);
		}

		if (art.Sprites is { } hudSprites) {
			HudSprites = hudSprites.Atlas.Upload(gl);
		}

		// Nearest, like everything else: the original blits it through its palettized texture mapper.
		if (hddCommand?.Raster is { } mapRaster) {
			HddMap = new GpuTexture(gl, mapRaster.Pixels, mapRaster.Width, mapRaster.Height);
		}
	}

	public GpuTexture? Front { get; }
	public GpuTexture? Side { get; }
	public GpuTexture? HeadsDown { get; }
	public GpuTexture? HudSprites { get; }
	public GpuTexture? HddMap { get; }

	/// <summary>
	/// Puts every cockpit texture through the theater's impact palette or back: the canopy's three panels, the
	/// HUD sheet and the heads-down map.
	/// </summary>
	public void ShowFlash(bool active) {
		if (_art is { } art && Front != null) {
			Front.Update(art.Front.PixelsFor(active), art.Front.Width, art.Front.Height);
			Side?.Update(art.Side.PixelsFor(active), art.Side.Width, art.Side.Height);
			if (art.HeadsDown is { } headsDown && HeadsDown != null) {
				HeadsDown.Update(headsDown.PixelsFor(active), headsDown.Width, headsDown.Height);
			}

			// And the plates and glyphs themselves. One handle re-uploaded rather than a second texture
			// bound per draw: the sheet goes into a dozen overlay calls, and a flash is a one-second event.
			if (art.Sprites is { Atlas: { } atlas } && art.ImpactSpritePixels is { } flashPixels
					&& HudSprites != null) {
				HudSprites.Update(active ? flashPixels : atlas.Pixels, atlas.Width, atlas.Height);
			}
		}

		// And the heads-down map's relief, for a player who takes a hit while panned down to it.
		if (HddMap != null && _hddMapFlashRaster is { } flashRaster
				&& _hddCommand?.Raster is { } raster) {
			var shown = active ? flashRaster : raster;
			HddMap.Update(shown.Pixels, shown.Width, shown.Height);
		}
	}

	/// <summary>
	/// The shield meter's rings are canopy pixels, not HUD geometry, and the original relights them by rewriting
	/// six palette slots every frame. Decoding the art at load baked that palette in, so the live version repaints
	/// those pixels and re-uploads — which UpdateShieldRings only asks for on the frames where a ring's colour
	/// actually changed, so a settled array costs one comparison.
	/// </summary>
	public void RefreshShieldRings(MechObject pilotMech, CockpitPowerUp powerUp, long coarseTicks, bool flashShown) {
		if (_art is not { } cockpitArt || Front == null) {
			return;
		}

		// While the cockpit is powering up the rings fill from dark instead — see CockpitPowerUp.
		var shieldRings = pilotMech.Shields;
		var (frontRings, rearRings) = powerUp.ShieldCharges(
			CockpitPalette.ShieldFacingCharge(shieldRings.Front, shieldRings.BaseMax),
			CockpitPalette.ShieldFacingCharge(shieldRings.Rear, shieldRings.BaseMax),
			coarseTicks);
		bool repainted = cockpitArt.UpdateShieldRings(frontRings, rearRings);

		if (repainted) {
			// Through whichever buffer the damage flash is currently showing — the repaint writes the
			// rings into both, and re-uploading the other one here would cancel a flash mid-shake.
			var frontFrame = cockpitArt.Front;
			Front.Update(frontFrame.PixelsFor(flashShown), frontFrame.Width, frontFrame.Height);
			Side?.Update(cockpitArt.Side.PixelsFor(flashShown), cockpitArt.Side.Width, cockpitArt.Side.Height);
			if (cockpitArt.HeadsDown is { } headsDownFrame && HeadsDown != null) {
				HeadsDown.Update(headsDownFrame.PixelsFor(flashShown), headsDownFrame.Width, headsDownFrame.Height);
			}
		}
	}

	public void Dispose() {
		Front?.Dispose();
		Side?.Dispose();
		HeadsDown?.Dispose();
		HudSprites?.Dispose();
		HddMap?.Dispose();
	}
}

/// <summary>
/// The damage flash: the scene, the HUD's colours and the cockpit's art all swapped to the theater's impact
/// palette while the cockpit shake holds it up. Tracked so the swap costs nothing on the frames — the great
/// majority — where it did not change: the scene half is two texture handles, but the canopy half re-uploads
/// three full-panel textures.
/// </summary>
sealed class DamageFlash(MissionScene scene, CockpitArt? art, SceneRenderer renderer, CockpitTextures textures) {
	/// <summary>Whether the scene and the canopy are currently showing the theater's impact palette.</summary>
	public bool Shown { get; private set; }

	public void Apply(bool active) {
		if (active == Shown) {
			return;
		}

		Shown = active;
		renderer.ImpactPaletteActive = active;

		// The HUD's own colours — the resolved COLORS.DAT ids and raw palette slots every widget draws
		// through. Most of the twenty-seven move under the impact palette, so without this the
		// instruments would be the one part of the screen refusing to flash.
		if (art != null) {
			art.FlashActive = active;
		}

		// The sky and the fog colour come out of the palette too, and in an open zone they are most of
		// what is on screen — see Scene.ImpactFlash.
		if (scene.ImpactFlash is { } flash) {
			(active ? flash.Atmosphere : scene.Atmosphere).ApplyTo(renderer);
		}

		textures.ShowFlash(active);
	}
}
