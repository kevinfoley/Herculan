using Herculan.Es1.Input;

namespace Herculan.Es1.Panels;

/// <summary>
/// What a panel takes from the input and the options of the binary it runs in: <c>alert.cpp</c>'s and the controls
/// panel's calls into the input and <c>prefs.cpp</c>, which the simulator (<see cref="Es1SimAlertInput"/>) and the shell
/// (docs/es1/shell/options.md#the-panel-base--alertpanel_ctorbase-0000222d) make on their own globals.
/// </summary>
public abstract class Es1AlertInput {
	/// <summary>The options the panels read and change.</summary>
	public abstract Es1Preferences Preferences { get; }

	/// <summary>
	/// The input a panel's pass reads: the block's command word, pointer and buttons 0 and 1 at <see
	/// cref="Es1PlayerInputBlock.Buttons"/>, the press and the next-widget buttons.
	/// </summary>
	public abstract Es1PlayerInputBlock Build();

	/// <summary><c>AlertPanel_HandleEvent</c>'s latch of <paramref name="button"/>, 1 to 4, with the value 1.</summary>
	public abstract void LatchButton(int button);

	/// <summary><c>AlertPanel_Enter</c>'s step on the input: the block kept, which <see cref="PutBack"/> gets back.</summary>
	public abstract Es1PlayerInputBlock? Keep();

	/// <summary><c>AlertPanel_Leave</c>'s step on the input: <paramref name="kept"/>, what <see cref="Keep"/> answered, put back.</summary>
	public abstract void PutBack(Es1AlertGlobals globals, Es1PlayerInputBlock? kept);

	/// <summary><c>Prefs_SetOption</c>(1, <paramref name="value"/>, <paramref name="index"/>).</summary>
	public abstract void SetOption(byte value, int index);

	/// <summary><c>Prefs_ToggleOption</c>(1, <paramref name="index"/>); returns the value.</summary>
	public abstract byte ToggleOption(int index);

	/// <summary><c>Prefs_StepOption</c>(1, <paramref name="modulus"/>, <paramref name="index"/>); returns the value.</summary>
	public abstract byte StepOption(int modulus, int index);

	/// <summary><c>Prefs_StepOptionBack</c>(1, <paramref name="modulus"/>, <paramref name="index"/>); returns the value.</summary>
	public abstract byte StepOptionBack(int modulus, int index);

	/// <summary>The controls panel's DONE commit, with the handlers applied.</summary>
	public abstract void CommitAll();

	/// <summary><c>ControlsPanel_ApplyDevices</c>' rebuild of the joystick devices for options 7 and 9.</summary>
	public abstract void RebuildJoysticks();

	/// <summary><c>ControlsPanel_ApplyDevices</c>' enable or disable of the mouse input device, when there is one.</summary>
	public abstract void SetMouseDeviceEnabled(bool enabled);

	/// <summary>
	/// <b>This engine's own:</b> whether something other than options 7, 8 and 9 says what the stick does, so the controls
	/// panel locks their rows (<see cref="Es1ControlsPanel.StickRowsLocked"/>). Never, unless a binary's input says so.
	/// </summary>
	public virtual bool StickOptionsOverridden => false;
}

/// <summary>The simulator's (docs/es1/simulation/alert-panels.md): the input build (<see cref="Es1InputBuild"/>) and the simulator's <c>prefs.cpp</c>.</summary>
public sealed class Es1SimAlertInput : Es1AlertInput {
	public Es1SimAlertInput(Es1InputBuild build) {
		InputBuild = build;
	}

	/// <summary>The input build each pass of a panel's loop runs.</summary>
	public Es1InputBuild InputBuild { get; }

	public override Es1Preferences Preferences => InputBuild.Preferences;

	/// <summary><c>Input_BuildPlayerDevice</c> (<see cref="Es1InputBuild.Build"/>).</summary>
	public override Es1PlayerInputBlock Build() => InputBuild.Build();

	/// <summary>The button latched with state 1 on the controls' latches (<see cref="Es1ButtonLatches.Latch"/>).</summary>
	public override void LatchButton(int button) => InputBuild.Controls.Latches.Latch(1, button);

	/// <summary>
	/// <c>PlayerInputBlock</c> kept, and buttons 0–3 latched with state 1. Each input build fills a new block
	/// (<see cref="Es1InputBuild.PlayerInputBlock"/>), so the block itself is kept rather than a copy of its bytes.
	/// </summary>
	public override Es1PlayerInputBlock? Keep() {
		var kept = InputBuild.PlayerInputBlock;
		for (int button = 1; button <= Es1ButtonLatches.Count; button++) {
			InputBuild.Controls.Latches.Latch(1, button);
		}

		return kept;
	}

	/// <summary><c>PlayerInputBlock</c> put back, and with a mouse device the pointer and the drawn cursor moved to its pointer.</summary>
	public override void PutBack(Es1AlertGlobals globals, Es1PlayerInputBlock? kept) {
		InputBuild.PlayerInputBlock = kept;
		if (globals.MouseDevice is { } mouse) {
			int x = kept?.PointerX ?? 0, y = kept?.PointerY ?? 0;
			mouse.SetPosition(x, y);
			globals.Display.Cursor.SyncPosition(x, y);
		}
	}

	public override void SetOption(byte value, int index) => Preferences.SetOption(true, value, index);

	public override byte ToggleOption(int index) => Preferences.ToggleOption(true, index);

	public override byte StepOption(int modulus, int index) => Preferences.StepOption(true, modulus, index);

	public override byte StepOptionBack(int modulus, int index) => Preferences.StepOptionBack(true, modulus, index);

	/// <summary><c>Prefs_CommitOptions</c> (<c>0004a83c</c>)(1) (<see cref="Es1Preferences.CommitOptions"/>).</summary>
	public override void CommitAll() => Preferences.CommitOptions(true);

	/// <summary><see cref="Es1JoystickDevices.Rebuild"/>, whose doc comment gives this engine's departure for <c>NONE</c> (plan decision 14).</summary>
	public override void RebuildJoysticks() => InputBuild.Joysticks.Rebuild(Preferences);

	/// <summary>While the controller's Custom profile drives the input build in place of option 7's configuration (<see cref="Es1InputBuild.Custom"/>).</summary>
	public override bool StickOptionsOverridden => InputBuild.Custom?.Invoke() != null;

	/// <summary><see cref="Es1InputBuild.MouseDeviceEnabled"/>.</summary>
	public override void SetMouseDeviceEnabled(bool enabled) => InputBuild.MouseDeviceEnabled = enabled;
}
