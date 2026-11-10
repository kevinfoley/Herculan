using System.Collections.Immutable;
using System.Text;

namespace Herculan.Engine.Input;

/// <summary>
/// <b>This engine's own:</b> the actions a controller's Custom profile can bind, one set of names for both games, in the
/// order and the groups the Controller window lists them in: <see cref="KeyAction"/>'s, less the keys that work a
/// panel, the window or a recording rather than the machine. Each game says which of them it has.
/// </summary>
public static class ControllerActions {
	/// <summary>The actions that work a panel, the window or a recording, which no control binds.</summary>
	private static readonly ImmutableHashSet<KeyAction> NotOffered = [
		KeyAction.PanelAccept, KeyAction.PanelCancel, KeyAction.PanelNextButton, KeyAction.FullScreen,
		KeyAction.LeaveFullScreen, KeyAction.StopTape, KeyAction.MenuBack, KeyAction.CopyFrame,
	];

	// Each section is a run of KeyAction's members, which the enum keeps together in this order.
	private static readonly (KeySection Section, KeyAction First, KeyAction Last)[] Runs = [
		(KeySection.Driving, KeyAction.ThrottleForward, KeyAction.ThrottleStepDown),
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

	/// <summary>Every action a control can bind, with its group, in the order the Controller window lists them.</summary>
	public static ImmutableArray<(KeySection Section, KeyAction Action)> Offered { get; } = [
		.. Runs.SelectMany(run => Enumerable.Range((int)run.First, run.Last - run.First + 1)
			.Select(action => (run.Section, (KeyAction)action)))
			.Where(entry => !NotOffered.Contains(entry.Item2)),
	];

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
}
