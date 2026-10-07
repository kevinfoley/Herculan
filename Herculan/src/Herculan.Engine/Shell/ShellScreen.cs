namespace Herculan.Engine.Shell;

/// <summary>
/// Which campaign the shell is running — <c>CampaignModeFlag</c> (<c>0048260c</c>), the flag that also picks
/// <c>GAME_R.SAV</c> over <c>GAME_T.SAV</c> (docs/retail/formats/save-games.md). It is what gates three of
/// the eight tabs; see <see cref="ShellScreen.ApplyTabGate"/>.
/// </summary>
public enum ShellCampaignMode {
	/// <summary>
	/// Training mode — the practice missions and <c>INSTANT ACTION</c> — which has no salvage economy and
	/// so no repair, build or armory.
	/// </summary>
	Training = 0,

	/// <summary>The real campaign, where every tab is live.</summary>
	Campaign = 1,
}

/// <summary>
/// One shell screen: the strip's widgets and which tab is up. What the pointer does to them is
/// <see cref="ShellPointer"/>'s.
///
/// <para>What is built here is the <b>frame</b> — the backdrop-textured root, the square button at the
/// far left of the strip, and the eight tabs. Every tab screen in VSHELL builds its own copy of that
/// strip at the same coordinates and then adds its own content beneath it, so the frame is the part
/// that is the same on all eight and the part worth having first. The content panels are not built
/// yet; see Herculan/ROADMAP.md.</para>
///
/// <para><b>The tabs name themselves.</b> Each caption is an <c>estext.bin</c> entry, consecutive from
/// <see cref="ShellLayout.FirstTabCaption"/>, exactly as the builder fetches them — so nothing here
/// hardcodes what the tabs are called or has to be corrected when the string table is read properly.
/// A tab with no caption available draws its plate and no text.</para>
///
/// <para><b>Three tabs are gated in training mode.</b> The builder clears <c>+0x49</c> on tab
/// 5 as it constructs it, and the strip's refresh (<c>0043b0c8</c>) rewrites that flag on tabs 2 to 6
/// from <c>CampaignModeFlag</c> (<c>0048260c</c>) — REPAIR, BUILD and ARMORY off in training, everything on in the
/// campaign. See <see cref="ApplyTabGate"/>.</para>
///
/// <para><b>Tabs 0 and 1 never latch.</b> Every handler starts by clearing the lit flag on all nine
/// strip buttons (<c>00439dcb</c>); the six from WEAPONS on then write their own back to 1, and the
/// main menu's and the save screen's do not — <see cref="SelectedTab"/> is <c>CurrentTabIndex</c> (<c>0047581c</c>), which
/// screen is up, and the latch is the lit flag, which only six of the eight ever take. Those two
/// hide the strip outright instead, and <see cref="StripVisible"/> carries that; see
/// <see cref="SelectTab"/>.</para>
/// </summary>
public sealed class ShellScreen {
	/// <summary>Id of the square button at the left of the strip, past the eight tab ids.</summary>
	public const int MenuButtonId = ShellLayout.TabCount;

	/// <summary>The square button's caption, the two bytes <c>3f 00</c> at <c>00475dcd</c>.</summary>
	private const string MenuButtonCaption = "?";

	private readonly List<ShellButton> _buttons = new();

	private ShellScreen() { }

	/// <summary>Every button on the screen, in the order the builder constructs them.</summary>
	public IReadOnlyList<ShellButton> Buttons => _buttons;

	/// <summary>Which tab is up, 0-7, or <see cref="NoTab"/> after <see cref="ReturnToFrame"/>.</summary>
	public int SelectedTab { get; private set; }

	/// <summary>
	/// <c>CurrentTabIndex</c> (<c>0047581c</c>)'s parked value, <c>0xffff</c>: no tab is up, so whichever is clicked next is
	/// never mistaken for the one already showing.
	/// </summary>
	public const int NoTab = -1;

	/// <summary>
	/// Whether the strip is drawn and answers clicks — the hidden bit of the full-screen panel every
	/// strip button is parented to (<c>ShellPanelWidget</c> (<c>0048d448</c>)).
	/// </summary>
	public bool StripVisible { get; private set; } = true;

	/// <summary>
	/// Builds the shell frame: the tab strip, captioned from <paramref name="text"/>, with
	/// <paramref name="selectedTab"/> current and <paramref name="mode"/>'s tabs gated.
	/// </summary>
	public static ShellScreen CreateFrame(ShellText? text, int selectedTab = 0,
			ShellCampaignMode mode = ShellCampaignMode.Campaign) {
		var screen = new ShellScreen();

		// The strip's leftmost button, whose two faces come from the ONLINE bank rather than the tab
		// plate's — which is why it is built separately rather than as a tab. Its caption is the one on
		// the strip that is not an estext.bin entry: the builder passes the literal at 00475dcd.
		screen._buttons.Add(new ShellButton(MenuButtonId, ShellLayout.MenuButton,
			unlit: new ShellSprite(ShellArt.MenuButtonBank, 0),
			lit: new ShellSprite(ShellArt.MenuButtonBank, 1),
			caption: MenuButtonCaption));

		for (int i = 0; i < ShellLayout.TabCount; i++) {
			screen._buttons.Add(new ShellButton(i, ShellLayout.Tab(i),
				unlit: new ShellSprite(ShellArt.ButtonBank, UnlitFrame),
				lit: new ShellSprite(ShellArt.ButtonBank, LitFrame),
				caption: text?.Text(ShellLayout.FirstTabCaption + i)));
		}

		screen.ApplyTabGate(mode);
		screen.SelectTab(selectedTab);
		return screen;
	}

	/// <summary>
	/// The strip refresh, <c>0043b0c8</c>: REPAIR, BUILD and ARMORY answer only in the campaign, while
	/// WEAPONS and CREW answer in both. It writes those five and no others — the two leftmost tabs,
	/// MISSION and the square button are never gated. The rest of the refresh — showing the strip and
	/// parking <c>CurrentTabIndex</c> (<c>0047581c</c>) — is <see cref="ReturnToFrame"/>'s.
	///
	/// <para><b>The flag is <c>+0x49</c>, the enable flag.</b> A cleared tab is still hit by the
	/// pointer, and its handler, <c>ESButtonBitmap_HandleEvent</c> (<c>00409df2</c>), then ignores the
	/// click, so it swallows it; nothing else sits under the strip, so leaving it out of
	/// <see cref="ButtonAt"/> comes to the same thing.</para>
	/// </summary>
	public void ApplyTabGate(ShellCampaignMode mode) {
		bool economy = mode == ShellCampaignMode.Campaign;
		SetTabEnabled(WeaponsTab, true);
		SetTabEnabled(RepairTab, economy);
		SetTabEnabled(BuildTab, economy);
		SetTabEnabled(ArmoryTab, economy);
		SetTabEnabled(CrewTab, true);
	}

	private void SetTabEnabled(int tab, bool enabled) {
		if (Button(tab) is { } button) {
			button.Enabled = enabled;
		}
	}

	/// <summary>
	/// The tabs the gate and the palette switch name. The captions they carry are
	/// <c>estext.bin</c>'s and are fetched, not hardcoded — these are only the indices.
	/// </summary>
	public const int MainMenuTab = 0;
	public const int SaveTab = 1;
	public const int WeaponsTab = 2;
	public const int RepairTab = 3;
	public const int BuildTab = 4;
	public const int ArmoryTab = 5;
	public const int CrewTab = 6;
	public const int MissionTab = 7;

	/// <summary>
	/// The two tabs whose handlers leave the whole strip unlit — see the class remarks. Everything from
	/// here up writes its own lit flag back after the clear.
	/// </summary>
	public const int FirstLatchingTab = WeaponsTab;

	/// <summary>
	/// The tab plate's two faces in <see cref="ShellArt.ButtonBank"/>. The builder hands the class a
	/// third pointer, frame 3, which neither of its paints reads — see <see cref="ShellButton"/>.
	/// </summary>
	private const int UnlitFrame = 1;
	private const int LitFrame = 2;

	/// <summary>
	/// Makes one tab the screen that is up, and latches it if it is one of the six that latch: the
	/// handler's <c>00439dcb</c> clears the lit flag on all nine strip buttons and repaints them, and a
	/// latching tab's handler then writes its own back to 1 and repaints it. Out-of-range indices are
	/// ignored.
	///
	/// <para><b>The main menu and the save tab hide the strip.</b> Their handlers call <c>0043b23d</c>,
	/// which hides the strip's parent panel, so each screen stands alone and is left only through its
	/// own buttons; the ones that reach the other tabs end in <see cref="ReturnToFrame"/>. The shell also
	/// starts with the strip hidden, since <c>ServiceBay_BuildScreen</c> hides its panel as it builds it,
	/// so a frame built on the main menu has none.</para>
	/// </summary>
	public void SelectTab(int index) {
		if (index < 0 || index >= ShellLayout.TabCount) {
			return;
		}

		SelectedTab = index;
		StripVisible = index >= FirstLatchingTab;
		foreach (var button in _buttons) {
			button.Lit = false;
			button.Repaint();
		}

		if (index >= FirstLatchingTab && Button(index) is { } tab) {
			tab.Lit = true;
			tab.Repaint();
		}
	}

	/// <summary>
	/// Puts the bare frame back up with no tab current — the pair the save screen's EXIT and RESTORE
	/// both end with: <c>0043b162(8)</c>, which shows the frame's root, then the strip refresh
	/// <c>0043b0c8</c>, which shows the strip's panel, regates it and parks <c>CurrentTabIndex</c> (<c>0047581c</c>) at
	/// <c>0xffff</c>. Neither writes a lit flag, and nothing is lit: the save tab's handler cleared all
	/// nine and latched none.
	/// </summary>
	public void ReturnToFrame(ShellCampaignMode mode) {
		StripVisible = true;
		ApplyTabGate(mode);
		SelectedTab = NoTab;
	}

	/// <summary>
	/// The strip's lit flags alone moved to one tab: every button unlit and <paramref name="index"/>
	/// lit, with no change to which tab is current — what <c>Movie_PlayQueue</c> (<c>0041e368</c>) does
	/// to MISSION before each movie while the frame is up.
	/// </summary>
	public void LightOnly(int index) {
		foreach (var button in _buttons) {
			button.Lit = button.Id == index;
			button.Repaint();
		}
	}

	/// <summary>
	/// The current tab's button unlit and no tab current (<c>CurrentTabIndex = 0xffff</c> (<c>0047581c</c>)), the strip left
	/// up — what <c>Movie_PlayQueue</c> does after <c>Mission_Leave</c> ahead of the location picture.
	/// </summary>
	public void LeaveTab() {
		if (Button(SelectedTab) is { } tab) {
			tab.Lit = false;
			tab.Repaint();
		}

		SelectedTab = NoTab;
	}

	/// <summary>The button at a canvas point, or null. Disabled buttons, and a hidden strip, do not answer.</summary>
	public ShellButton? ButtonAt(float canvasX, float canvasY) {
		if (!StripVisible) {
			return null;
		}

		foreach (var button in _buttons) {
			if (button.Enabled && button.Rect.Contains(canvasX, canvasY)) {
				return button;
			}
		}

		return null;
	}

	/// <summary>The button with this id, or null.</summary>
	public ShellButton? Button(int id) {
		foreach (var button in _buttons) {
			if (button.Id == id) {
				return button;
			}
		}

		return null;
	}

	/// <summary>
	/// What the pointer hits on the strip, or null. Each button's caption is a <c>Text</c> child
	/// covering the whole button, so the hit never changes within one.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) =>
		ButtonAt(canvasX, canvasY) is { } button
			? new ShellHit(new ShellWidget(ShellWidgetKind.StripButton, button.Id), ShellHandler.ButtonIcon)
			: null;
}
