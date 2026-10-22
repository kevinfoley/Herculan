namespace Herculan.Engine.Input;

/// <summary>
/// Everything a key does in the simulator, one value per thing, grouped as docs/retail/key-bindings.md groups them and
/// then HERCULAN's own (docs/herculan/herculan-key-bindings.md). A numbered set is one value per member, in order, so a
/// handler can count from its first. Which keys each takes is a <see cref="KeyMap"/>'s.
/// </summary>
public enum KeyAction {
	// Driving.
	ThrottleForward,
	ThrottleBack,
	SteerLeft,
	SteerRight,
	AllStop,
	TurretLeft,
	TurretRight,
	TurretUp,
	TurretDown,
	CenterTurret,
	CenterLegs,
	RazorThrottleUp,
	RazorThrottleDown,

	/// <summary>Throttle forward and steer left together, overriding the other driving keys of its group.</summary>
	DiagonalForwardLeft,
	DiagonalForwardRight,
	DiagonalBackLeft,
	DiagonalBackRight,

	// Weapons.
	Fire,
	WeaponRow1,
	WeaponRow2,
	WeaponRow3,
	WeaponRow4,
	WeaponRow5,
	WeaponRow6,
	WeaponRow7,
	WeaponRow8,
	WeaponRow9,
	WeaponRow10,

	/// <summary>Adds weapon row 1 to the current firing chain, or takes it out.</summary>
	ToggleChainRow1,
	ToggleChainRow2,
	ToggleChainRow3,
	ToggleChainRow4,
	ToggleChainRow5,
	ToggleChainRow6,
	ToggleChainRow7,
	ToggleChainRow8,
	ToggleChainRow9,
	ToggleChainRow10,
	NextWeapon,
	PreviousWeapon,
	LinkWeapon,
	NextChain,

	/// <summary>Raises the selected energy weapon's power level a step.</summary>
	PowerUp,
	PowerDown,

	// Targeting and shields.
	NextTarget,
	NearestTarget,
	ClearTarget,
	NextTargetComponent,

	/// <summary>Switches the radar between active and passive.</summary>
	RadarMode,
	RadarRange,
	ScannerNextTarget,
	AutoTurretTracking,
	ShieldsRear,
	ShieldsFront,

	// Displays and views.
	MfdStatus,
	MfdFlashComm,
	MfdNavMap,
	MfdScanner,
	MfdTarget,
	MfdMissileCam,
	CommandDisplay,
	DamageDetail,
	LookLeft,
	LookRight,

	/// <summary>
	/// From the forward view down to the Heads-Down Display; otherwise back to the forward view, or to the cockpit.
	/// </summary>
	ViewBack,
	OutsideView,

	/// <summary>The MFD's SELECT: the other HERCs' status on STATUS, the next target on TARGET STATUS.</summary>
	OtherHercsStatus,
	DropNavMarker,

	// FLASH COMM, with the screen up: pick an order, in the screen's row order.
	FlashCommAttackMyTarget,
	FlashCommIgnoreMyTarget,
	FlashCommHelpMeOut,
	FlashCommJoinOnMe,
	FlashCommScanForHostiles,
	FlashCommEmcon,
	FlashCommFireAtWill,
	FlashCommNextOrder,
	FlashCommPreviousOrder,
	FlashCommTransmit,

	// FLASH COMM from any screen: pick and transmit an order, in the same order.
	SendAttackMyTarget,
	SendIgnoreMyTarget,
	SendHelpMeOut,
	SendJoinOnMe,
	SendScanForHostiles,
	SendEmcon,
	SendFireAtWill,

	// The Heads-Down Display's command display.
	CommandSquadmate1,
	CommandSquadmate2,
	CommandSquadmate3,

	/// <summary>The first of the eight orders, in the screen's list order (<see cref="Cockpit.HddOrder"/>).</summary>
	CommandDisengage,
	CommandAttackEnemy,
	CommandDefendPosition,
	CommandPatrolGridpoint,
	CommandGotoGridpoint,
	CommandJoinOnMe,
	CommandScanForHostiles,
	CommandEmcon,
	CommandPreviousOrder,
	CommandNextOrder,
	CommandNextUnit,
	CommandPick,
	CommandZoomIn,
	CommandZoomOut,
	CommandScrollUp,
	CommandScrollDown,
	CommandScrollLeft,
	CommandScrollRight,
	CommandRecenter,
	CommandTransmit,
	CommandCancel,

	// The Heads-Down Display's damage detail.
	DamageStructural,
	DamageInternal,
	DamageWeapons,
	DamageSystemsUp,
	DamageSystemsDown,
	DamageHercLeft,
	DamageHercRight,

	// The outside view.
	SwapCameraControls,
	ViewNextSquadmate,

	// Panels and the game.
	MissionStatus,
	Pause,
	QuitGame,
	Objectives,
	Preferences,
	OnlineManual,

	// A modal panel's own keys.
	PanelAccept,
	PanelCancel,
	PanelNextButton,
	FullScreen,
	LeaveFullScreen,
	StopTape,

	// HERCULAN's own.
	MenuBar,

	/// <summary>Backs out of the menu bar or one of its windows while one is up.</summary>
	MenuBack,

	/// <summary>Copies the frame to the clipboard while full screen.</summary>
	CopyFrame,
}
