
namespace Herculan.Engine.Shell;

/// <summary>
/// The campaign debrief's grants over the flag array a mission left: chassis
/// (<c>Herc_GrantUnlocks</c>, <c>004118c5</c>) and weapons (<c>Armory_GrantCampaignWeapons</c>,
/// <c>004126be</c>).
/// </summary>
public sealed partial class ShellHangar {
	/// <summary>
	/// <c>WeaponGrant_UnlockSlots</c> (<c>0046fa82</c>) — per weapon id, the campaign-flag slot whose value
	/// unlocks it, <c>-1</c> for none.
	/// </summary>
	private static readonly short[] WeaponUnlockSlots = {
		-1, -1, -1, -1, 0x34, 0x35, -1, -1, -1, -1, -1, 0x32, 0x33, -1, -1, -1, -1,
		-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0x39, 0x38, 0x3a, 0x3b,
	};

	/// <summary><c>WeaponGrant_UnlockValues</c> (<c>0046fa40</c>) — per weapon id, the value that slot must hold.</summary>
	private static readonly short[] WeaponUnlockValues = {
		0, 0, 0, 0, 2, 1, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 1,
	};

	/// <summary>The first campaign-flag slot the unit-grant pass reads.</summary>
	private const int FirstUnitGrantFlag = 0x15;

	/// <summary>
	/// <c>WeaponGrant_UnitIds</c> (<c>0046f8e0</c>) — the weapon each flag from <see cref="FirstUnitGrantFlag"/> stocks.
	/// Retail's table is the first 22 words; its loop runs to slot <c>0x31</c>, so the last seven are the
	/// words that follow it in the image, read as ids just as the original reads them.
	/// </summary>
	private static readonly short[] WeaponUnitGrantIds = {
		18, 3, 4, 5, 10, 11, 12, 15, 27, 26, 17, 7, 6, 24, 23, 22, 25, 28, 29, 30, 31, 32,
		10169, 65, 8199, 0, -4, -1, 0,
	};

	/// <summary>
	/// <c>Armory_GrantCampaignWeapons</c> (<c>004126be</c>), the campaign debrief's weapon grants over the flag
	/// array the mission left (docs/retail/formats/weapons-dat.md#campaign-grants--armory_grantcampaignweapons-004126be):
	/// each still-locked weapon with a flag slot is unlocked when the slot holds its value, and the slot is
	/// zeroed either way; then each flag from <c>0x15</c> to <c>0x31</c> adds that many units of its weapon to
	/// stock at condition 100, leaving the flag set. Returns how many units were added.
	///
	/// <para>An id past the catalog in the table's overrun appends, in retail, to a list outside the
	/// weapon record array. This engine cannot reproduce that write, so it counts the unit and stocks
	/// nothing; no retail save holds a nonzero flag there.</para>
	/// </summary>
	public int GrantCampaignWeapons(short[] flags) {
		for (int id = 0; id < WeaponUnlockSlots.Length; id++) {
			int slot = WeaponUnlockSlots[id];
			if (slot != -1 && !IsWeaponUnlocked(id)) {
				if (flags[slot] == WeaponUnlockValues[id]) {
					Stock(id).UnlockFlag = 1;
				}

				flags[slot] = 0;
			}
		}

		int granted = 0;
		for (int k = 0; k < WeaponUnitGrantIds.Length; k++) {
			int weaponId = WeaponUnitGrantIds[k];
			for (int n = 0; n < flags[FirstUnitGrantFlag + k]; n++) {
				granted++;
				if (weaponId >= 0 && weaponId < ShellMissionLaunch.WeaponCatalogCount) {
					// Armory_AddNewUnit(id, 100) (0041229d): WeaponUnit_Init(unit, id, 100, 100, 5), then Armory_AddUnit.
					AddUnit(new ShellWeaponUnit(weaponId, fitCondition: 100, condition: 100, guidance: ShellWeaponUnit.NoGuidance));
				}
			}
		}

		return granted;
	}

	/// <summary>The number of <c>herc_inf.dat</c> records <c>Herc_GrantUnlocks</c> walks; record <c>i</c> is chassis type <c>i</c>.</summary>
	private const int ChassisTypeCount = 9;

	/// <summary>
	/// <c>Herc_GrantUnlocks</c> (<c>004118c5</c>), the campaign debrief's chassis grant, run just before
	/// <see cref="GrantCampaignWeapons"/> (docs/retail/formats/herc-catalogs.md#chassis-unlocks--herc_grantunlocks-004118c5):
	/// each still-unavailable Raptor II, Ogre, Maverick or Razor becomes available when its flag slot holds
	/// the expected value, and the slot is zeroed either way. The expected value is the original's
	/// loop-carried local — 2, set to 1 in the Razor's branch — so the Razor, last in type order, is the
	/// only chassis that tests 1.
	///
	/// <para>The debrief that calls this is not ported (ROADMAP), so nothing calls it yet.</para>
	/// </summary>
	public void GrantChassis(short[] flags) {
		int expected = 2;
		for (int type = 0; type < ChassisTypeCount; type++) {
			if (IsChassisAvailable(type)) {
				continue;
			}

			int slot = type switch {
				1 => 0x3c,
				6 => 0x3e,
				7 => 0x3d,
				8 => 0x3f,
				_ => -1,
			};
			if (type == 8) {
				expected = 1;
			}

			if (slot != -1) {
				if (flags[slot] == expected) {
					_availableChassis.Add(type);
				}

				flags[slot] = 0;
			}
		}
	}
}
