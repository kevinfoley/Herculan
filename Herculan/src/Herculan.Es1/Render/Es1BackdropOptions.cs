using Herculan.Es1.Input;
using Herculan.Es1.Scene;

namespace Herculan.Es1.Render;

/// <summary>
/// The preferences options the backdrop reads each frame (docs/es1/simulation/preferences.md#its-rows): option 3, whether
/// the skyline is drawn; option 4, how many clouds; option 5, the textured or banded ground and the scattered points.
/// With a simulation running they are the simulator's live options, which the preferences panel changes while the
/// outside view turns behind it, read on every draw as retail's readers read the bytes, so a change shows on the next
/// pass. With none, the zone viewer alone, they are the viewer's own fixed values, the user's choice: the skyline on,
/// every cloud and the textured ground.
///
/// <para><b>This engine's own:</b> the viewer's <c>G</c> key (<see cref="ToggleGround"/>), which retail has no
/// counterpart for.</para>
/// </summary>
public sealed class Es1BackdropOptions {
	/// <summary>Option 4 and option 5 with no simulation: their largest value, every cloud and the textured ground.</summary>
	public const byte ViewerDetail = 255;

	/// <summary>Option 5 the viewer alone switches to for the banded ground: the largest value below 240, which draws 248 of 250 points.</summary>
	public const byte ViewerBandedDetail = Es1GroundSpecks.DetailLimit - 1;

	private readonly Es1Preferences? _live;
	private byte _viewerGround = ViewerDetail;
	private bool? _texturedOverride;

	/// <param name="live">The simulator's options, or null with no simulation.</param>
	public Es1BackdropOptions(Es1Preferences? live) {
		_live = live;
	}

	/// <summary>
	/// Option 3 (<c>000bd565</c>) not 0: <c>Hzline_DrawWithOffset</c> draws the skyline
	/// (docs/es1/rendering/ground-and-sky.md#the-horizon-skyline). Always with no simulation.
	/// </summary>
	public bool Skyline => _live == null || _live[Es1Preferences.HorizonOption] != 0;

	/// <summary>
	/// Option 4 (<c>000bd566</c>), which the number of clouds drawn scales by
	/// (docs/es1/rendering/ground-and-sky.md#each-frame, <see cref="Es1Clouds.Frame"/>); <see cref="ViewerDetail"/> with
	/// no simulation.
	/// </summary>
	public int CloudDetail => _live?[Es1Preferences.SkyOption] ?? ViewerDetail;

	/// <summary>
	/// Option 5 (<c>000bd567</c>), which textures the ground above 240 (docs/es1/rendering/ground-and-sky.md#the-ground)
	/// and below it scales the scattered points (<see cref="Es1GroundSpecks.Update"/>); with no simulation
	/// <see cref="ViewerDetail"/>, or <see cref="ViewerBandedDetail"/> while <see cref="ToggleGround"/> has banded it.
	/// </summary>
	public int GroundDetail => _live?[Es1Preferences.GroundOption] ?? _viewerGround;

	/// <summary>
	/// Whether the ground is textured: <c>Hzline_FillGround</c>'s test, a ground texture loaded (<paramref name="hasTexture"/>,
	/// the <c>hzline</c>'s <c>+0x70</c>) and <see cref="GroundDetail"/> above 240, unless <see cref="ToggleGround"/>
	/// overrides the second.
	/// </summary>
	public bool Textured(bool hasTexture) =>
		hasTexture && (_texturedOverride ?? GroundDetail > Es1GroundSpecks.DetailLimit);

	/// <summary>
	/// The viewer's <c>G</c> key, an engine aid. With no simulation it switches <see cref="GroundDetail"/> between
	/// <see cref="ViewerDetail"/> and <see cref="ViewerBandedDetail"/>, so the specks come and go with the bands. With a
	/// simulation running it overrides option 5's choice of textured or banded ground with the other one, until it is
	/// pressed again; it never writes the option, so the scattered points still follow option 5 and the preferences panel
	/// shows and saves what the player set.
	/// </summary>
	public void ToggleGround() {
		if (_live == null) {
			_viewerGround = _viewerGround == ViewerDetail ? ViewerBandedDetail : ViewerDetail;
			return;
		}

		_texturedOverride = _texturedOverride == null ? GroundDetail <= Es1GroundSpecks.DetailLimit : null;
	}
}
