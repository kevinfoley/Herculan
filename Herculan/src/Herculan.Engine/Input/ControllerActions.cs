using System.Collections.Immutable;
using System.Text;

namespace Herculan.Engine.Input;

/// <summary>Which of a machine's actions the Controller window's action lists show (<see cref="ControllerActions.Listed"/>).</summary>
public enum ControllerActionList {
	/// <summary>The actions a controller button is usually bound to.</summary>
	Common,

	/// <summary>Common's, and the keyboard's other actions a button can usefully send.</summary>
	All,
}

/// <summary>
/// <b>This engine's own:</b> the actions a controller's Custom profile can bind, one set of names for both games, in the
/// order and the groups the Controller window lists them in: <see cref="KeyAction"/>'s, less the keys that work a
/// panel, the window or a recording rather than the machine. Each game says which of them it has. The window's lists
/// show fewer (<see cref="Listed"/>): a machine's own, and the "Common" list a short set of them.
/// </summary>
public static class ControllerActions {
	/// <summary>The actions that work a panel, the window or a recording, which no control binds.</summary>
	private static readonly ImmutableHashSet<KeyAction> NotOffered = [
		KeyAction.PanelAccept, KeyAction.PanelCancel, KeyAction.PanelNextButton, KeyAction.FullScreen,
		KeyAction.LeaveFullScreen, KeyAction.StopTape, KeyAction.MenuBack, KeyAction.CopyFrame,
	];

	// Each section is a run of KeyAction's members, which the enum keeps together in this order.
	private static readonly (KeySection Section, KeyAction First, KeyAction Last)[] Runs = [
		(KeySection.Piloting, KeyAction.ThrottleForward, KeyAction.ThrottleStepDown),
		(KeySection.Weapons, KeyAction.Fire, KeyAction.PowerDown),
		(KeySection.TargetingAndShields, KeyAction.NextTarget, KeyAction.ShieldsFront),
		(KeySection.DisplaysAndViews, KeyAction.MfdStatus, KeyAction.DropNavMarker),
		(KeySection.FlashComm, KeyAction.FlashCommAttackMyTarget, KeyAction.SendFireAtWill),
		(KeySection.CommandDisplay, KeyAction.CommandSquadmate1, KeyAction.CommandCancel),
		(KeySection.DamageDetail, KeyAction.DamageStructural, KeyAction.DamageHercRight),
		(KeySection.OutsideView, KeyAction.SwapCameraControls, KeyAction.ViewNextSquadmate),
		(KeySection.PanelsAndGame, KeyAction.MissionStatus, KeyAction.StopTape),
		(KeySection.Herculan, KeyAction.MenuBar, KeyAction.CopyFrame),
	];

	/// <summary>
	/// Every action a control can bind, with its group, in the order the Controller window lists them. Automatic turret
	/// tracking, which the Keys window has under targeting, is listed under piloting, after Center legs.
	/// </summary>
	public static ImmutableArray<(KeySection Section, KeyAction Action)> Offered { get; } = Arrange();

	/// <summary>The actions only Earthsiege 2 has.</summary>
	public static ImmutableHashSet<KeyAction> Es2Only { get; } = [
		KeyAction.CenterLegs, KeyAction.RazorThrottleUp, KeyAction.RazorThrottleDown, KeyAction.WeaponRow10,
		KeyAction.ToggleChainRow10, KeyAction.NearestTarget, KeyAction.ClearTarget, KeyAction.NextTargetComponent,
		KeyAction.ScannerNextTarget, KeyAction.OtherHercsStatus, KeyAction.DropNavMarker, KeyAction.OnlineManual,
	];

	/// <summary>The actions only Metaltech: Earthsiege has.</summary>
	public static ImmutableHashSet<KeyAction> Es1Only { get; } = [
		KeyAction.ThrottleStepUp, KeyAction.ThrottleStepDown, KeyAction.PreviousChain,
	];

	/// <summary>The actions a HERC has and a RAZOR lacks, which the RAZOR's list leaves out.</summary>
	public static ImmutableHashSet<KeyAction> HercOnly { get; } = [
		KeyAction.CenterTurret, KeyAction.CenterLegs, KeyAction.AutoTurretTracking,
	];

	/// <summary>The actions a RAZOR has and a HERC lacks, which the HERC's list leaves out.</summary>
	public static ImmutableHashSet<KeyAction> RazorOnly { get; } = [KeyAction.RazorThrottleUp, KeyAction.RazorThrottleDown];

	// Offered, but in neither list, since nobody binds them to a controller: the chain toggles, the views that step back,
	// and picking an order on FLASH COMM or the command display by name.
	private static readonly ImmutableHashSet<KeyAction> Unlisted = [
		.. Range(KeyAction.ToggleChainRow1, KeyAction.ToggleChainRow10),
		KeyAction.LookLeft, KeyAction.LookRight, KeyAction.ViewBack,
		.. Range(KeyAction.FlashCommAttackMyTarget, KeyAction.FlashCommFireAtWill),
		.. Range(KeyAction.CommandDisengage, KeyAction.CommandEmcon),
	];

	// In the "All" list alone: what an axis usually works, the numbered weapon rows, the MFD's and the Heads-Down Display's
	// screens by name, Other HERCs on STATUS and the online manual; and the sections in AllSections.
	private static readonly ImmutableHashSet<KeyAction> AllOnly = [
		.. Range(KeyAction.ThrottleForward, KeyAction.SteerRight),
		.. Range(KeyAction.TurretLeft, KeyAction.TurretDown),
		.. Range(KeyAction.RazorThrottleUp, KeyAction.DiagonalBackRight),
		KeyAction.ThrottleStepUp, KeyAction.ThrottleStepDown,
		.. Range(KeyAction.WeaponRow1, KeyAction.WeaponRow10),
		.. Range(KeyAction.MfdStatus, KeyAction.DamageDetail),
		KeyAction.OtherHercsStatus, KeyAction.OnlineManual,
	];

	private static readonly ImmutableHashSet<KeySection> AllSections = [
		KeySection.FlashComm, KeySection.CommandDisplay, KeySection.DamageDetail, KeySection.OutsideView,
	];

	// Sections a gamepad's list leaves out even in "All".
	private static readonly ImmutableHashSet<KeySection> JoystickSections = [KeySection.CommandDisplay, KeySection.DamageDetail];

	/// <summary>
	/// The actions the Controller window lists for a machine, in <see cref="Offered"/>'s order: the RAZOR's, or the HERC's,
	/// without the other machine's own; in <paramref name="list"/>; and, for a gamepad, without the command display's and
	/// the damage detail's keys.
	/// </summary>
	public static IEnumerable<(KeySection Section, KeyAction Action)> Listed(ControllerActionList list, bool razor, bool gamepad) =>
		Offered.Where(entry => !Unlisted.Contains(entry.Action)
			&& !(razor ? HercOnly : RazorOnly).Contains(entry.Action)
			&& !(gamepad && JoystickSections.Contains(entry.Section))
			&& (list == ControllerActionList.All || !(AllOnly.Contains(entry.Action) || AllSections.Contains(entry.Section))));

	/// <summary>The <c>.lang</c> key of the action's name in the Controller window: <c>controller.action.</c> and its name in snake case.</summary>
	public static string LabelKey(KeyAction action) {
		string name = action.ToString();
		var key = new StringBuilder("controller.action.");
		for (int i = 0; i < name.Length; i++) {
			char c = name[i];
			// A word starts at a capital, and a number at its first digit.
			bool wordStart = i > 0 && (char.IsUpper(c) || (char.IsDigit(c) && !char.IsDigit(name[i - 1])));
			if (wordStart) {
				key.Append('_');
			}

			key.Append(char.ToLowerInvariant(c));
		}

		return key.ToString();
	}

	private static ImmutableArray<(KeySection Section, KeyAction Action)> Arrange() {
		var offered = Runs.SelectMany(run => Range(run.First, run.Last).Select(action => (run.Section, action)))
			.Where(entry => !NotOffered.Contains(entry.action))
			.Where(entry => entry.action != KeyAction.AutoTurretTracking)
			.ToList();
		int after = offered.FindIndex(entry => entry.action == KeyAction.CenterLegs);
		offered.Insert(after + 1, (KeySection.Piloting, KeyAction.AutoTurretTracking));
		return [.. offered];
	}

	private static IEnumerable<KeyAction> Range(KeyAction first, KeyAction last) =>
		Enumerable.Range((int)first, last - first + 1).Select(action => (KeyAction)action);
}
