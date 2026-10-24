using Herculan.Engine.Content;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>
/// The <c>0x17</c>-byte state block an energy or ammunition row exchanges with its mount, at the gauge's <c>+0x8b</c>
/// (docs/es1/simulation/cockpit-weapon-gauges.md#the-rows-fields), as far as anything reads it; the dwords
/// <c>+8</c>, <c>+0xc</c> and the bytes <c>+0x10</c>, <c>+0x13</c> are copied whole and never read.
/// </summary>
/// <param name="Value"><c>+0</c>: the energy row's charge, Q10 of a full capacitor; the ammunition row's round count.</param>
/// <param name="Power"><c>+4</c>: the energy row's charge-bar position, Q10 of the full power level.</param>
/// <param name="Destroyed"><c>+0x11</c>, the mount's destroyed byte.</param>
/// <param name="MidCharge"><c>+0x12</c>, the energy mount's mid-charge byte.</param>
/// <param name="Armed"><c>+0x14</c>, the row's mount is the armed one.</param>
/// <param name="InChain"><c>+0x15</c>, the row's mount is in the current chain.</param>
/// <param name="Ready"><c>+0x16</c>, the row's mount is ready.</param>
public record struct Es1WeaponRowState(int Value, int Power, bool Destroyed, bool MidCharge, bool Armed, bool InChain, bool Ready);

/// <summary>The pod row's two-byte state block at its <c>+0x8b</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-pod-row).</summary>
/// <param name="Switch"><c>+0x8b</c>, the ECM's switch.</param>
/// <param name="Destroyed"><c>+0x8c</c>, the destroyed byte, which nothing ported writes but the constructor's clear.</param>
public record struct Es1PodRowState(bool Switch, bool Destroyed);

/// <summary>
/// The row gauge base, <c>WeaponGauge_Ctor</c> (<c>ES1DBSIM 00036f50</c>) on <c>PanelGauge_Ctor</c>
/// (docs/es1/simulation/cockpit-weapon-gauges.md#the-row-gauge-base): the row's rect <c>+0</c>, the cockpit
/// <c>+0x10</c>, the repaint counter <c>+0x75</c>, the name <c>+0x7a</c> and the mount <c>+0x86</c>; the slots of its
/// table at <c>+0x76</c> that the cockpit and the children call as virtual members.
/// </summary>
public abstract class Es1WeaponGauge {
	/// <summary>The bytes of the name <c>strncpy</c> copies to <c>+0x7a</c>.</summary>
	public const int NameLength = 12;

	/// <summary>The fill colors the rows' labels plate their text with.</summary>
	public const byte DarkPlate = 0x3f;

	protected Es1WeaponGauge(Es1Cockpit cockpit, Es1WeaponMount mount, Es1Rect rect) {
		Cockpit = cockpit;
		Mount = mount;
		Rect = rect;
	}

	/// <summary><c>+0x10</c>, the cockpit the row draws through.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0x86</c>, the mount whose row this is.</summary>
	public Es1WeaponMount Mount { get; }

	/// <summary>
	/// <c>WeaponGauge_MountProjectileType</c> (<c>ES1DBSIM 00036f98</c>)(gauge): the <c>int16</c> <c>+0</c> of the mount's
	/// projectile record, −1 with none (docs/es1/simulation/cockpit-weapon-gauges.md#the-row-gauge-base).
	/// </summary>
	public short MountProjectileType() => Mount.Projectile is { } record ? (short)(record.Type?.Val ?? -1) : (short)-1;

	/// <summary><c>WeaponGauge_MountProjectileSubtype</c> (<c>00036fd0</c>)(gauge): the record's <c>int16</c> <c>+2</c>, its subtype, −1 with none.</summary>
	public short MountProjectileSubtype() => Mount.Projectile?.SubtypeId ?? -1;

	/// <summary><c>+0</c>–<c>+0xc</c>, the row's rect from the <c>.GAU</c>.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x7a</c>, the name's first <see cref="NameLength"/> characters.</summary>
	public string Name { get; protected init; } = "";

	/// <summary><c>+0x75</c>, the repaint counter the update steps from 2.</summary>
	public byte RepaintCounter { get; protected set; }

	/// <summary>Slot <c>+4</c>, a child's click with its value.</summary>
	public abstract void OnChildClick(short value, Es1Widget child);

	/// <summary>Slot <c>+8</c>, the paint, which <c>CockpitView_InvalidateWidgets</c> runs.</summary>
	public abstract void Paint();

	/// <summary>Slot <c>+0xc</c>, the per-frame update, which <c>Cockpit_DrawFrame</c> runs.</summary>
	public abstract void Update();

	/// <summary>Slot <c>+0x10</c>, <c>PanelGauge_KeyDispatchNoneWeaponGaugeCopy</c> (<c>00036f8b</c>)(gauge, code): 0, taking no key.</summary>
	public static short KeyDispatch(ushort code) {
		_ = code;
		return 0;
	}

	/// <summary><c>strncpy</c>(<c>+0x7a</c>, <paramref name="name"/>, 12), as a string.</summary>
	protected static string Truncate(string? name) =>
		name == null ? "" : name.Length > NameLength ? name[..NameLength] : name;

	/// <summary>Font <paramref name="index"/> of <c>ColorSchemePanels</c>; null when it is missing.</summary>
	internal static HudFont? Font(IReadOnlyList<HudFont?> fonts, int index) =>
		index >= 0 && index < fonts.Count ? fonts[index] : null;
}

/// <summary>
/// The energy and ammunition rows' shared part (docs/es1/simulation/cockpit-weapon-gauges.md#the-rows-fields): child 0,
/// the chained number button <c>+0x14</c>; the state block <c>+0x8b</c>; and the row <c>+0xa2</c>.
/// </summary>
public abstract class Es1WeaponRowGauge : Es1WeaponGauge {
	protected Es1WeaponRowGauge(Es1Cockpit cockpit, Es1WeaponMount mount, short row, Es1Rect rect) : base(cockpit, mount, rect) {
		Row = row;
	}

	/// <summary><c>+0xa2</c>, the row, the low half of the dword <c>WeaponMounts_ToggleChainMember</c> is handed.</summary>
	public short Row { get; }

	/// <summary><c>+0x14</c>, child 0, the number button.</summary>
	public Es1ChainedWeaponSelectButton SelectButton { get; protected init; } = null!;

	/// <summary><c>+0x8b</c>, the state block.</summary>
	public Es1WeaponRowState State { get; protected set; }

	/// <summary><c>EnergyWeaponGauge_GetState</c> (<c>000372d8</c>) or <c>AmmoWeaponGauge_GetState</c> (<c>00037778</c>): a copy of the state block.</summary>
	public Es1WeaponRowState GetState() => State;

	/// <summary>Slot <c>+0x14</c>, the set-state the mount's gauge push calls.</summary>
	public abstract void SetState(Es1WeaponRowState block);

	/// <summary>
	/// Child 0's click, the two rows' shared case: with bit 1 of <paramref name="value"/> set (the right button),
	/// <c>WeaponMounts_ToggleChainMember</c> (<c>0000d5e4</c>)(the weapon manager of <c>CockpitViewInstance</c>'s machine,
	/// the row); otherwise <c>CockpitView_ArmWeaponByGauge</c> (<c>0002d098</c>)(cockpit, gauge).
	/// </summary>
	protected void OnSelectClick(short value) {
		if ((value & 2) != 0) {
			Cockpit.Machine?.Mounts?.ToggleChainMember(Row);
			return;
		}

		Cockpit.ArmWeaponByGauge(this);
	}

	/// <summary>Whether the chained number button's state, <c>+0x17</c> of child 0, is not 2, the state the destroyed byte sets.</summary>
	protected bool SelectButtonShown => SelectButton.State != Es1Widget.Hidden;
}
