using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;
using Herculan.Es1.Host;
using Herculan.Es1.Input;
using Herculan.Es1.Render;
using Herculan.Es1.Scene;
using Herculan.Es1.Sim;
using Herculan.Es1.View;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// The scene reads preferences options 3 to 6 live (docs/es1/simulation/preferences.md#its-rows): the backdrop's
/// options (<see cref="Es1BackdropOptions"/>) and the detail setting the frame applies again (<see cref="Es1AppliedDetail"/>),
/// each read after the option is written, as the preferences panel writes it while the view turns behind it.
/// </summary>
public class Es1LiveSceneOptionsTests {
	/// <summary>
	/// Option 3 turns the skyline off and on, and option 4 scales the clouds drawn to (count · (value + 1)) &gt;&gt; 8: none
	/// of 20 at 0, every one at 255. Catches the backdrop reading a fixed value or the options' load-time copy, and the
	/// clouds' number losing its + 1 (19 of 20 at 255) or its shift.
	/// </summary>
	[Fact]
	public void TheSkylineAndTheCloudsFollowOptions3And4AsTheyAreWritten() {
		var preferences = Es1Preferences.Default;
		var options = new Es1BackdropOptions(preferences);
		var clouds = Es1Clouds.From(new Es1WorldData { CloudCount = 20 }, new DynamixBitmapArray())!;
		var random = new Random(1);

		Assert.False(options.Skyline);
		Assert.Equal(0, clouds.Frame(default, options.CloudDetail, random).Length);

		preferences.SetOption(true, 1, Es1Preferences.HorizonOption);
		preferences.SetOption(true, 255, Es1Preferences.SkyOption);
		Assert.True(options.Skyline);
		Assert.Equal(20, clouds.Frame(default, options.CloudDetail, random).Length);

		preferences.SetOption(true, 127, Es1Preferences.SkyOption);
		Assert.Equal(10, clouds.Frame(default, options.CloudDetail, random).Length);
	}

	/// <summary>
	/// Option 5 textures the ground at 241 and bands it at 240, and only with a texture loaded; the <c>G</c> key overrides
	/// that choice until pressed again and leaves the option as it was. Catches <c>&gt;=</c> for <c>&gt;</c>, the key
	/// writing the option, and an override that never lets go.
	/// </summary>
	[Fact]
	public void TheGroundIsTexturedAbove240AndTheKeyOverridesItWithoutWritingTheOption() {
		var preferences = Es1Preferences.Default;
		var options = new Es1BackdropOptions(preferences);

		preferences.SetOption(true, 241, Es1Preferences.GroundOption);
		Assert.True(options.Textured(hasTexture: true));
		Assert.False(options.Textured(hasTexture: false));
		preferences.SetOption(true, 240, Es1Preferences.GroundOption);
		Assert.False(options.Textured(hasTexture: true));

		options.ToggleGround();
		Assert.True(options.Textured(hasTexture: true));
		Assert.Equal(240, preferences[Es1Preferences.GroundOption]);
		Assert.Equal(240, options.GroundDetail);

		options.ToggleGround();
		Assert.False(options.Textured(hasTexture: true));
	}

	/// <summary>
	/// Option 6 written between frames reaches the HERCs' detail setting on the next <c>Sim_RenderFrame</c>, the one the
	/// preferences panel's loop draws through. Catches the frame no longer applying it, and an apply that never takes the
	/// option as its last, so a change back is missed.
	/// </summary>
	[Fact]
	public void TheFrameAppliesTheDetailSettingAgainWhenOption6Changes() {
		var preferences = Es1Preferences.Default;
		var detail = new Es1AppliedDetail(preferences);
		var frames = new Es1FrameLoop(Views(), () => { }, null, detail);
		frames.StartMission();
		Assert.Equal(Es1DetailSetting.FromOption(0), detail.Setting);

		preferences.SetOption(true, 255, Es1Preferences.DetailOption);
		Assert.Equal(Es1DetailSetting.FromOption(0), detail.Setting);
		((IEs1Frame)frames).RenderFrame();
		Assert.Equal(Es1DetailSetting.FromOption(255), detail.Setting);

		preferences.SetOption(true, 0, Es1Preferences.DetailOption);
		((IEs1Frame)frames).RenderFrame();
		Assert.Equal(Es1DetailSetting.FromOption(0), detail.Setting);
	}

	/// <summary>Four views on one rect, as <see cref="Es1FramePacingTests"/> builds them.</summary>
	private static Es1ViewManager Views() {
		var records = new (int X, int Y)[] { (0, 0), (0, 197), (320, 0), (-320, 0) }
			.Select(origin => new Vue.Entry {
				ViewportX0 = 0, ViewportY0 = 0, ViewportX1 = 319, ViewportY1 = 140, CenterX = -160, CenterY = -70,
				CanvasOriginX = origin.X, CanvasOriginY = origin.Y,
			})
			.ToArray();
		var clips = records.Select(_ => Es1ClipBlock.FromRect(0, 0, 319, 140)).ToArray();
		var files = new Es1CockpitViews("TEST", records, clips, new DynamixBitmap?[4], null);
		return new Es1ViewManager(files, new Es1VideoMemory());
	}
}
