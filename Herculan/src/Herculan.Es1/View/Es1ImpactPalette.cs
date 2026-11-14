using HercWorks.Core.Data.File.Dyn;

namespace Herculan.Es1.View;

/// <summary>
/// The impact palette and its three switches (docs/es1/simulation/cockpit-panel.md#the-hit-shake), which the hit shake
/// runs (<see cref="Es1HitShake"/>): <c>Palette_ActivateImpact</c> (<c>ES1DBSIM 00025ed0</c>),
/// <c>Palette_ToggleImpact</c> (<c>00025ef8</c>) and <c>Palette_RestoreFromImpact</c> (<c>00025f1c</c>), on
/// <c>ImpactPaletteObject</c> (<c>000928f0</c>), the palette the toggles swap with (<c>000afdcc</c>) and the one the
/// restore makes active (<c>000afdd0</c>). Each makes a palette active through <c>Palette_SetActive</c>
/// (<c>00027e7c</c>), which here sends it as the live palette (<see cref="SetLivePalette"/>), and sets, flips or clears
/// the pitch gradient's <c>000928eb</c> (<see cref="Es1PitchGradient.ImpactShowing"/>); each does nothing while no
/// impact palette is loaded.
///
/// <para>Retail's installs write into the active palette in place (<c>Palette_InstallRange</c>), so the impact palette
/// keeps what was installed into it while it showed, the pitch gradient's entries among them, and a palette made active
/// again shows what was installed into it before. This engine's installs send a changed copy as a new live palette each
/// time (<see cref="Es1ScreenPresenter.InstallRange"/>), so each palette the switches hold is kept here as its latest colors: at
/// each switch the active one takes the live palette as it then stands (<see cref="LivePalette"/>).</para>
/// </summary>
public sealed class Es1ImpactPalette {
	private PaletteObject? _impact, _kept, _restore, _active;

	/// <summary>Whether <c>ImpactPaletteObject</c> is loaded, without which the switches do nothing.</summary>
	public bool Loaded => _impact != null;

	/// <summary>The live palette as it stands, which the palette a switch replaces takes; with none, it takes none.</summary>
	public Func<DynamixPalette?>? LivePalette { get; set; }

	/// <summary>Sends a palette as the live palette, the one every index on the screen shows through.</summary>
	public Action<DynamixPalette?>? SetLivePalette { get; set; }

	/// <summary>
	/// <c>World_LoadTheater</c>'s store of <c>Palette_LoadDpl</c>'s palette into <c>ImpactPaletteObject</c>
	/// (docs/es1/formats/wld-world.md#the-load); null, for a world file that names none or one the install lacks, loads none.
	/// </summary>
	public void Load(DynamixPalette? palette) {
		_impact = palette != null ? new PaletteObject(palette) : null;
		_kept = _restore = _active = null;
	}

	/// <summary>
	/// <c>Palette_ActivateImpact</c>: the impact palette made active, the one it replaces kept for the toggles and the
	/// restore, and <see cref="Es1PitchGradient.ImpactShowing"/> set in <paramref name="gradient"/>.
	/// </summary>
	public void Activate(Es1PitchGradient gradient) {
		if (_impact == null) {
			return;
		}

		_kept = SetActive(_impact);
		gradient.ImpactShowing = true;
		_restore = _kept;
	}

	/// <summary>
	/// <c>Palette_ToggleImpact</c>: the kept palette made active and the one it replaces kept in its stead, and
	/// <see cref="Es1PitchGradient.ImpactShowing"/> flipped in <paramref name="gradient"/>.
	/// </summary>
	public void Toggle(Es1PitchGradient gradient) {
		if (_impact == null) {
			return;
		}

		_kept = SetActive(_kept);
		gradient.ImpactShowing = !gradient.ImpactShowing;
	}

	/// <summary>
	/// <c>Palette_RestoreFromImpact</c>: the palette <see cref="Activate"/> replaced made active again, and
	/// <see cref="Es1PitchGradient.ImpactShowing"/> cleared in <paramref name="gradient"/>.
	/// </summary>
	public void Restore(Es1PitchGradient gradient) {
		if (_impact == null) {
			return;
		}

		SetActive(_restore);
		gradient.ImpactShowing = false;
	}

	/// <summary>
	/// <c>Palette_SetActive</c>(<paramref name="palette"/>): it made active and sent as the live palette, answering the
	/// one it replaced, which first takes the live palette's colors. Retail's slots are never null when a switch reads
	/// them, since <see cref="Toggle"/> and <see cref="Restore"/> follow an <see cref="Activate"/>; a null one, which
	/// retail would make active as a null palette, sends nothing here, an engine guard.
	/// </summary>
	private PaletteObject SetActive(PaletteObject? palette) {
		var previous = _active ?? new PaletteObject(null);
		previous.Colors = LivePalette?.Invoke();
		_active = palette;
		if (palette != null) {
			SetLivePalette?.Invoke(palette.Colors);
		}

		return previous;
	}

	/// <summary>One of retail's palette objects, as the colors it last held.</summary>
	private sealed class PaletteObject(DynamixPalette? colors) {
		public DynamixPalette? Colors { get; set; } = colors;
	}
}
