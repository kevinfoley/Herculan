using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The hit shake (docs/es1/simulation/cockpit-panel.md#the-hit-shake): <c>Cockpit_StartHitShake</c>
/// (<c>ES1DBSIM 0002e17c</c>) and <c>Cockpit_HitShakeTick</c> (<c>0002e1d8</c>) on <c>HitShakeEndTick</c>
/// (<c>00092d74</c>) and <c>HitShakeNextToggleTick</c> (<c>00092d78</c>), which step the view shake
/// (<see cref="Es1ViewManager.StepShake"/>) and flash the palette (<see cref="Es1SimGlobals.ImpactPalette"/>). Its clock
/// is <c>000981c4</c>, 60 counts a second, and its random numbers the low word of
/// <see cref="Es1SimGlobals.PresentationRandom"/>.
/// </summary>
public sealed class Es1HitShake {
	/// <summary>How long a hit shakes, in clock counts.</summary>
	public const uint Length = 0x3c;

	/// <summary>The band <see cref="Start"/> arms the view shake with.</summary>
	public const int Band = 5;

	/// <summary>The spread of the palette's toggles, and of the shake's steps, in clock counts and rows.</summary>
	public const int ToggleSpread = 10, StepSpread = 5;

	private readonly Es1ViewManager _views;
	private readonly Es1SimGlobals _globals;

	internal Es1HitShake(Es1ViewManager views, Es1SimGlobals globals) {
		_views = views;
		_globals = globals;
	}

	/// <summary><c>HitShakeEndTick</c>, the clock count the shake ends at, 0 for none.</summary>
	public uint EndTick { get; private set; }

	/// <summary><c>HitShakeNextToggleTick</c>, the clock count after which the palette next toggles, 0 while no shake runs.</summary>
	public uint NextToggleTick { get; private set; }

	/// <summary>
	/// <c>Cockpit_StartHitShake</c>, outside view 4: the end <see cref="Length"/> counts from now; and when no shake runs
	/// (<see cref="NextToggleTick"/> 0), the first toggle 0 to 9 counts from now, the impact palette made active
	/// (<see cref="Es1ImpactPalette.Activate"/>) and the view shake armed with <see cref="Band"/> (<see cref="Es1ViewManager.SetShakeBand"/>). So a hit while the shake runs
	/// only makes it last longer.
	/// </summary>
	public void Start() {
		if (_views.CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}

		uint now = _globals.Clock.Clock;
		EndTick = now + Length;
		if (NextToggleTick == 0) {
			NextToggleTick = (uint)((ushort)_globals.PresentationRandom.Next() % ToggleSpread) + now;
			_globals.ImpactPalette.Activate(_globals.PitchGradient);
			_views.SetShakeBand(Band);
		}
	}

	/// <summary>
	/// <c>Cockpit_HitShakeTick</c>, once a frame from <c>Cockpit_DrawFrame</c>: view 4 ends the shake. While it runs and
	/// the clock is short of its end, each time the clock has passed <see cref="NextToggleTick"/> the palette toggles
	/// (<see cref="Es1ImpactPalette.Toggle"/>) and the next toggle comes 0 to 9 counts on, and every frame the view shake
	/// steps by 0 to 4 (<see cref="Es1ViewManager.StepShake"/>); at its end the end is cleared; either way it answers 1.
	/// Once ended with a toggle pending, the theater's palette is put back (<see cref="Es1ImpactPalette.Restore"/>), the
	/// toggle cleared and the view shake cleared
	/// (<see cref="Es1ViewManager.ClearShake"/>), answering 1; otherwise 0. The clock's compares are unsigned.
	/// </summary>
	public short Tick() {
		uint now = _globals.Clock.Clock;
		if (_views.CurrentView == Es1CockpitViews.ExternalView) {
			EndTick = 0;
		}

		if (EndTick != 0) {
			if (now < EndTick) {
				if (NextToggleTick < now) {
					_globals.ImpactPalette.Toggle(_globals.PitchGradient);
					NextToggleTick = (uint)((ushort)_globals.PresentationRandom.Next() % ToggleSpread) + now;
				}

				_views.StepShake((ushort)_globals.PresentationRandom.Next() % StepSpread);
			} else {
				EndTick = 0;
			}

			return 1;
		}

		if (NextToggleTick != 0) {
			_globals.ImpactPalette.Restore(_globals.PitchGradient);
			NextToggleTick = 0;
			_views.ClearShake();
			return 1;
		}

		return 0;
	}
}
