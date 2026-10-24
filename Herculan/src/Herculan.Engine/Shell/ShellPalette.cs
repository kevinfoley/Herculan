namespace Herculan.Engine.Shell;

/// <summary>
/// Which of the mission tab's three faces is up. It is the shell's <c>MissionScreenView</c> (<c>0048106c</c>), and its three
/// values are the ones the palette switch and the screen builder both test for; nothing has been read
/// that uses 2 or 3.
/// </summary>
public enum ShellMissionView {
	/// <summary>The campaign map, which is where the tab lands at the start of a stage.</summary>
	Map = 0,

	/// <summary>The pre-mission briefing.</summary>
	Briefing = 1,

	/// <summary>The post-mission debrief, which the campaign layer selects rather than the tab.</summary>
	Debriefing = 4,
}

/// <summary>
/// The shell's palettes, and which one each screen is drawn through.
///
/// <para>A screen changes palette through a palette-scope widget whose <c>+0x45</c> is the index,
/// <c>Shell_SetPaletteScope(index)</c> (<c>00439da0</c>). <see cref="Names"/> is the table
/// <c>Shell_InstallPalette(index)</c> (<c>004075b2</c>) indexes: past the first five entries are three
/// runs of five — briefing, debrief and theater palettes — indexed by the campaign stage, 1-5, of
/// which stage 5 is the Moon. See docs/retail/shell/screen-layout.md#the-palette.</para>
/// </summary>
public static class ShellPalette {
	/// <summary>
	/// The pointer table at <c>0046dcdc</c>, as resource names — <c>dpl\</c> and <c>.dpl</c> stripped,
	/// since <see cref="ShellArt"/> composes both back on. Index into it exactly as
	/// <c>Shell_InstallPalette</c> (<c>004075b2</c>) does.
	/// </summary>
	public static readonly string[] Names = {
		"INTR_PT1", "PALETTE", "ARMING", "CAM_ER", "CAM_MOON",
		"BR_W1", "BR_W2", "BR_W3", "BR_W4", "BR_W5",
		"DB_W1", "DB_W2", "DB_W3", "DB_W4", "DB_W5",
		"ALPH", "DELT", "OMIC", "BRAV", "LUNA",
	};

	/// <summary>The intro sequence's palette, and the only entry nothing in the tab layer selects.</summary>
	public const int Interstitial = 0;

	/// <summary>
	/// The bay palette: what the shell installs on entry, what <c>0043b23d</c> re-installs whenever the
	/// service bay is shown, and what the main menu, the save screen and the repair screen are drawn
	/// through.
	/// </summary>
	public const int ServiceBay = 1;

	/// <summary>The weapons, build, armory and crew screens' palette.</summary>
	public const int Arming = 2;

	/// <summary>The campaign map's two palettes, picked on whether the stage is the lunar one.</summary>
	public const int CampaignMapEarth = 3;
	public const int CampaignMapMoon = 4;

	/// <summary>Stage 1's briefing palette; the five run consecutively from here.</summary>
	public const int FirstBriefing = 5;

	/// <summary>Stage 1's debrief palette; likewise five consecutive.</summary>
	public const int FirstDebriefing = 10;

	/// <summary>Stage 1's theater palette; likewise five consecutive, ending on <c>LUNA</c>.</summary>
	public const int FirstTheater = 15;

	/// <summary>How many stages the campaign has, and so how long each of the three per-stage runs is.</summary>
	public const int StageCount = 5;

	/// <summary>The stage the campaign moves to the Moon on — the last one.</summary>
	public const int LunarStage = StageCount;

	/// <summary>
	/// The palette a tab is drawn through, or null where the original installs none. That case is real
	/// rather than defensive: <c>Shell_SelectTabPalette</c> (<c>0043b162</c>)'s mission arm is three bare <c>if</c>s against
	/// <c>MissionScreenView</c> (<c>0048106c</c>) with no <c>else</c>, so a fourth value leaves whatever palette was up.
	///
	/// <para><paramref name="stage"/> is the campaign stage as the save holds it, 1-5 for the campaign.
	/// The arithmetic is the original's and is unguarded there: it is reproduced unguarded here, so a
	/// stage outside 1-5 gives an index outside the briefing or debrief run exactly as it does in
	/// retail, and <see cref="Name"/> is what refuses it.</para>
	/// </summary>
	public static int? ForTab(int tab, ShellMissionView view = ShellMissionView.Map, int stage = 1) => tab switch {
		// Main menu and save do not go through the tab switch at all — their handlers install this
		// directly, and 0043b23d installs it again on the way in.
		0 or 1 => ServiceBay,
		3 => ServiceBay,
		2 or 4 or 5 or 6 => Arming,
		7 => view switch {
			ShellMissionView.Map => stage >= LunarStage ? CampaignMapMoon : CampaignMapEarth,
			ShellMissionView.Briefing => FirstBriefing - 1 + stage,
			ShellMissionView.Debriefing => FirstDebriefing - 1 + stage,
			_ => null,
		},
		_ => null,
	};

	/// <summary>
	/// Whether switching to <paramref name="tab"/> leaves the canvas below the strip filled with the
	/// shell's background colour. Tabs 2-7 install their palette through the scope (<c>Shell_SelectTabPalette</c> (<c>0043b162</c>)
	/// cases 2-7 all call <c>Shell_SetPaletteScope</c> (<c>00439da0</c>)), and showing the scope posts its paint,
	/// <c>ESPal_Paint</c> (<c>0040cb40</c>), which fills its whole rect with <c>0x10</c> before the hide installs the
	/// palette. Nothing repaints the backdrop-textured root afterwards, so a tab screen is drawn over
	/// that fill, and wherever its widgets leave the canvas bare, retail shows black. The main menu and
	/// the save screen go through the scope too, but then put up their own backdrop-textured root over
	/// it.
	/// </summary>
	public static bool FillsScope(int tab) => tab >= 2 && tab <= 7;

	/// <summary><c>ESPal_Paint</c> (<c>0040cb40</c>): the scope's rect filled with <see cref="ShellChrome.InteriorColor"/>.</summary>
	public static void PaintScope(ShellSurface surface) {
		var scope = ShellLayout.PaletteScope;
		surface.Fill(scope.X0, scope.Y0, scope.X1, scope.Y1, ShellChrome.InteriorColor);
	}

	/// <summary>
	/// The theater palette for a campaign stage — <c>Shell_InstallPalette(stage + 0xe)</c> (<c>004075b2</c>) in
	/// <c>maybe_Mission_UpdateLocationTab</c>. Stage 5 never reaches it in the original, which branches
	/// to the lunar cutscene before the call, so <c>LUNA</c> is in the table and unused by this path.
	/// </summary>
	public static int ForStage(int stage) => FirstTheater - 1 + stage;

	/// <summary>The resource name at an index, or null when the index is outside the table.</summary>
	public static string? Name(int index) =>
		index >= 0 && index < Names.Length ? Names[index] : null;

	/// <summary>The index a resource name sits at, or null when the table does not carry it.</summary>
	public static int? IndexOf(string name) {
		for (int i = 0; i < Names.Length; i++) {
			if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) {
				return i;
			}
		}

		return null;
	}
}
