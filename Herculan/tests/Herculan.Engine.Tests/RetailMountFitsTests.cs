using HercWorks.Core.Data.File.Msn;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RetailMountFits"/> over two constructed missions: what a Cybrid's hardpoint offers in the editor is what the
/// records of that chassis fit in that slot, a template's included, and a weapon chosen takes the ammunition type
/// those records carry with it there.
/// </summary>
public class RetailMountFitsTests {
	private const short Scarab = 19;
	private const short Diablo = 17;
	private const short Atc50 = 3;
	private const short Atc100 = 5;
	private const short L300 = 10;
	private const short Msl10 = 15;
	private const short Arh = 1;

	/// <summary>
	/// Catches a scan keyed by slot alone or by chassis alone (a hardpoint offered another slot's or another chassis's
	/// weapons), a template (GUID -1) left out, and a record that picks from a random table, type -1, counted as a fit.
	/// </summary>
	[Fact]
	public void AHardpointOffersWhatThatChassisFitsInThatSlot() {
		var fits = RetailMountFits.Scan([
			new MissionFile { Mechs = [Herc(1, Scarab, Atc100, L300), Herc(-1, Scarab, Atc50), Herc(2, -1, Msl10)] },
			new MissionFile { Mechs = [Herc(1, Diablo, L300, 0)] },
		]);

		Assert.Equal([Atc50, Atc100], fits.Weapons(Scarab, 0));
		Assert.Equal([L300], fits.Weapons(Scarab, 1));
		Assert.Equal([L300], fits.Weapons(Diablo, 0));
		Assert.Equal([0], fits.Weapons(Diablo, 1));
		Assert.Empty(fits.Weapons(Scarab, 2));
	}

	/// <summary>
	/// Catches a weapon chosen with an ammunition type other than the one retail records carry with it on that hardpoint:
	/// the filler for every weapon, or the first record's where most carry another.
	/// </summary>
	[Fact]
	public void AWeaponTakesTheAmmunitionTypeItsRecordsCarryMostOften() {
		var fits = RetailMountFits.Scan([
			new MissionFile {
				Mechs = [
					Herc(1, Diablo, Msl10), Herc(2, Diablo, Msl10, ammo: [Arh]), Herc(3, Diablo, Msl10, ammo: [Arh]),
					Herc(4, Diablo, L300),
				],
			},
		]);

		Assert.Equal(Arh, fits.Ammo(Diablo, 0, Msl10));
		Assert.Equal(RosterFit.Filler, fits.Ammo(Diablo, 0, L300));
		Assert.Null(fits.Ammo(Diablo, 1, Msl10));
	}

	// A herc of <type> with <weapons> from slot 0, every later slot -1, and <ammo> from slot 0, every later slot the filler.
	private static MechRosterEntry144 Herc(short guid, short type, params short[] weapons) => Herc(guid, type, weapons, []);

	private static MechRosterEntry144 Herc(short guid, short type, short weapon, short[] ammo) => Herc(guid, type, [weapon], ammo);

	private static MechRosterEntry144 Herc(short guid, short type, short[] weapons, short[] ammo) {
		var refs = Enumerable.Repeat((short)-1, RosterFit.Slots).ToArray();
		var types = Enumerable.Repeat(RosterFit.Filler, RosterFit.Slots).ToArray();
		weapons.CopyTo(refs, 0);
		ammo.CopyTo(types, 0);
		return new MechRosterEntry144 { GUID = guid, TypeIndex = type, RandomTableKey = (short)(type == -1 ? 4 : -1), WeaponRefs = refs, WeaponSecondary = types };
	}
}
