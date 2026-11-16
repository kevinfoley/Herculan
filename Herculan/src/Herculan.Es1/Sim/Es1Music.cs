using Herculan.Es1.Sound;

namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Music_Tick</c> (<c>000585dc</c>) and its globals: one of three calm songs gives way to its battle song when an enemy
/// is in sight or close, and back (docs/es1/simulation/main-loop.md#the-music--music_tick-000585dc). A song is a row of
/// the simulator's catalog (<see cref="Es1Sound.Catalog"/>), a calm one from 0 to 2 and its battle one
/// <see cref="BattleOffset"/> past it.
/// </summary>
public sealed class Es1Music {
	/// <summary>The calm songs the first call picks from.</summary>
	public const short CalmSongs = 3;

	/// <summary>The rows from a calm song to its battle song.</summary>
	public const short BattleOffset = 3;

	/// <summary>The clock's counts from a check or a switch to the next check, 5 seconds at 60 a second.</summary>
	public const uint CheckInterval = 300;

	/// <summary>The volume a switch takes off the song each call.</summary>
	public const int FadeStep = 15;

	/// <summary>The distance within which an enemy is seen whether or not it can be targeted.</summary>
	public const int SeenRange = 25000;

	/// <summary>The distance past which a flyer keeps the battle song, strictly more.</summary>
	public const int FarFlyerRange = 0x49f0;

	/// <summary><c>Music_Song</c> (<c>00096d0c</c>): the song's row, −1 until the first call picks one.</summary>
	public short Song { get; set; } = -1;

	/// <summary><c>Music_SongObject</c> (<c>00096d14</c>): the song's catalog object, none with no sound manager.</summary>
	public Es1SoundObject? SongObject { get; set; }

	/// <summary><c>Music_Battle</c> (<c>00096d0e</c>): the battle song plays, or is the one a switch goes to.</summary>
	public bool Battle { get; set; }

	/// <summary><c>Music_NextCheck</c> (<c>00096d10</c>): the clock's count the next check waits to pass.</summary>
	public uint NextCheck { get; set; }

	/// <summary><c>Music_Switching</c> (<c>00096d18</c>): a switch is fading the song out.</summary>
	public bool Switching { get; set; }

	/// <summary>
	/// <c>Music_Tick</c> (<c>000585dc</c>), with <paramref name="musicOption"/> the preferences' option 0 (<c>000bd562</c>),
	/// as the doc section lays it out. The clock is <see cref="Es1SimClock.Clock"/>. A seen object is measured from
	/// <see cref="Es1SimGlobals.LocalPlayerMech"/> with <c>Math_DistanceBetweenPoints</c> (<c>00063c20</c>) before the
	/// cockpit's two target tests, and a flyer's distance the same way.
	///
	/// <para>Engine guards: with no player's machine, through which retail reads, no object is close and no flyer far;
	/// with no cockpit (<see cref="Es1SimGlobals.CockpitViewInstance"/>), which retail passes unchecked, none can be
	/// targeted; an object with no group, whose action and side retail reads through, is passed over; and with no song
	/// object, where retail calls through a null one, the switch's volume steps do nothing and
	/// the fade ends at once.</para>
	/// </summary>
	public void Tick(Es1SimGlobals globals, byte musicOption) {
		var sound = globals.Sound;
		if (sound.Manager == null || musicOption == 0) {
			return;
		}

		uint now = globals.Clock.Clock;
		if (Song == -1) {
			Song = (short)globals.PresentationRandom.NextBelow(CalmSongs);
			sound.Preload(Song);
			SongObject = sound.Catalog(Song);
			Battle = false;
			NextCheck = now + CheckInterval;
		}

		if (Switching) {
			if (SongObject is { } fading && fading.GetVolume() != 0) {
				fading.SetVolume(unchecked((sbyte)((sbyte)fading.GetVolume() - FadeStep)));
				return;
			}

			sound.Stop(Song);
			Song += Battle ? BattleOffset : (short)-BattleOffset;
			sound.Preload(Song);
			SongObject = sound.Catalog(Song);
			SongObject?.SetVolume(100);
			Switching = false;
		}

		if (now > NextCheck) {
			var player = globals.LocalPlayerMech;
			var targets = globals.CockpitViewInstance?.Targets;
			foreach (var target in globals.LiveObjects) {
				if (Switching) {
					break;
				}

				if (target.Immobilized || target.Destroyed || target.Group is not { Action: null, Side: 1 }) {
					continue;
				}

				bool close = player != null && target.Position.ApproxDistanceTo(player.Position) < SeenRange;
				if ((targets != null && targets.CanTarget(target) && targets.InForwardCone(target)) || close) {
					if (Battle) {
						return;
					}

					Switching = true;
					Battle = true;
					NextCheck = now + CheckInterval;
				}
			}

			if (Battle && !Switching) {
				Switching = true;
				foreach (var flyer in globals.Flyers.Live) {
					if (!flyer.Immobilized && !flyer.Destroyed && player != null
							&& flyer.Position.ApproxDistanceTo(player.Position) > FarFlyerRange) {
						Switching = false;
						break;
					}
				}

				if (Switching) {
					Battle = false;
					NextCheck = now + CheckInterval;
				}
			}
		}

		if (!sound.IsPlaying(Song)) {
			sound.Play(Song);
		}
	}
}
