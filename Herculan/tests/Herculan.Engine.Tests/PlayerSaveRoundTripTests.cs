using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Install;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Byte-for-byte round trips through <see cref="PlayerSaveTransform"/>.
///
/// <para>The synthetic cases build the bytes by hand from the layout in
/// <c>docs/retail/formats/save-games.md</c> rather than from the transformer's own writer, so a
/// disagreement between the two shows up as a failure instead of cancelling out. What they pin is
/// the pilot record's field count — three shorts before the name (roster id, esnames index, length)
/// and eleven after the on-strength byte — and the 152-byte career block. Getting either wrong
/// desynchronizes the 36-record squad segment and everything after it.</para>
///
/// <para>The retail case sweeps a real install's save slots and skips silently without one, as the
/// rest of the suite does.</para>
/// </summary>
public class PlayerSaveRoundTripTests {
	private const int WeaponCatalogSize = 33;
	private const int CareerBlockShorts = 76;
	private const int SquadSize = 36;
	private const int PrePlayerShorts = 8;
	private const int HercUnlockCount = 9;

	[Fact]
	public void RoundTripsASyntheticSaveByteForByte() {
		byte[] original = BuildSave();
		var transformer = new PlayerSaveTransform();

		PlayerSave parsed = Assert.IsType<PlayerSave>(transformer.Parse(original));

		Assert.Equal(original, transformer.Write(parsed));
	}

	/// <summary>
	/// Skill and rank are separate 0-3 fields two shorts apart, with the squad slot between them.
	/// Distinct values here would come back equal if the reader collapsed them, and a reader that
	/// mistook the esnames index for the name length would not reach them at all.
	/// </summary>
	[Fact]
	public void ReadsSkillAndRankAsSeparateFields() {
		var transformer = new PlayerSaveTransform();

		PlayerSave parsed = Assert.IsType<PlayerSave>(transformer.Parse(BuildSave()));

		Assert.NotNull(parsed.Squadmates);
		PilotEntry first = parsed.Squadmates[0];
		Assert.Equal("DUGGAN", first.Name);
		Assert.Equal(7, first.NameIndex);
		Assert.Equal(PilotSkill.Veteran.Id, first.Skill!.Id);
		Assert.Equal(PilotRank.Captain.Id, first.Rank!.Id);
		Assert.Equal(11, first.SquadPosition);

		// The three kill counters and their career totals, in file order.
		Assert.Equal(1, first.HercKills);
		Assert.Equal(2, first.FlyerKills);
		Assert.Equal(3, first.BaseKills);
		Assert.Equal(40, first.TotalHercKills);
		Assert.Equal(50, first.TotalFlyerKills);
		Assert.Equal(60, first.TotalBaseKills);
		Assert.Equal(9, first.MissionsFlown);
	}

	/// <summary>The career block is 76 shorts; 77 leaves the squad segment two bytes adrift.</summary>
	[Fact]
	public void CareerBlockIs152Bytes() {
		var transformer = new PlayerSaveTransform();

		PlayerSave parsed = Assert.IsType<PlayerSave>(transformer.Parse(BuildSave()));

		Assert.Equal(CareerBlockShorts, parsed.CareerBlock.Length);
		Assert.Equal(PrePlayerShorts, parsed.SquadTailAndPlayerHead.Length);
		Assert.Equal(SquadSize, parsed.Squadmates!.Length);
	}

	/// <summary>
	/// Every save slot of a real install, parsed and written back. Byte equality has to hold even for
	/// the slots carrying a stale tail past their last field — VSHELL's save stream does not truncate,
	/// so two retail slots have one, and the transformer keeps those bytes verbatim.
	/// </summary>
	[Fact]
	public void RoundTripsEveryRetailSaveByteForByte() {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		string savDir = Path.Combine(root, "SAV");
		if (!Directory.Exists(savDir)) {
			return;
		}

		string[] slots = Directory.GetFiles(savDir, "GAME_*.SAV");
		if (slots.Length == 0) {
			return;
		}

		foreach (string slot in slots) {
			byte[] original = File.ReadAllBytes(slot);
			var transformer = new PlayerSaveTransform();

			PlayerSave? parsed = transformer.Parse(original);
			Assert.NotNull(parsed);

			string diag = $"{Path.GetFileName(slot)}: workshop={parsed.BuildQueueFreeSlots} " +
				$"sq0={parsed.Squadmates![0].Name}/{parsed.Squadmates[0].NameIndex} " +
				$"sq1={parsed.Squadmates[1].Name} " +
				$"player={parsed.PlayerPilot?.Name} bays={parsed.HercBay.Count} " +
				$"salvage={parsed.SalvageTotal} tail={parsed.CampaignStateTail?.Length}";

			Assert.Equal(SquadSize, parsed.Squadmates.Length);
			Assert.Equal(HercUnlockCount, parsed.ChassisAvailability.Count);
			Assert.True(parsed.HercBay.Count > 0, diag);

			foreach (PilotEntry pilot in parsed.Squadmates) {
				Assert.NotNull(pilot.Skill);
				Assert.NotNull(pilot.Rank);
				Assert.InRange(pilot.NameIndex, (short)0, (short)35);
				Assert.False(string.IsNullOrEmpty(pilot.Name),
					$"{Path.GetFileName(slot)}: a squadmate parsed with an empty name");
				Assert.All(pilot.Name!, c => Assert.InRange(c, ' ', '~'));
			}

			Assert.Equal(original, transformer.Write(parsed));
		}
	}

	// ------------------------------------------------------------------ fixture

	/// <summary>
	/// A save with the retail block order and sizes: 33 inventory records, the workshop queue, the
	/// career block, 36 squadmates, the player, one hangar bay, the chassis unlocks, the salvage
	/// total, and a short tail standing in for the campaign flag array.
	/// </summary>
	internal static byte[] BuildSave() {
		var b = new List<byte>();

		// Block 1 — inventory, one record per catalog id. Id 3 owns two units, the rest none.
		for (int id = 0; id < WeaponCatalogSize; id++) {
			b.Add((byte)(id % 2));                       // unlock flag
			short qty = id == 3 ? (short)2 : (short)0;
			Short(b, qty);
			for (int q = 0; q < qty; q++) {
				Short(b, (short)id);                     // weapon id
				Short(b, (short)(q + 1));                // derived class index
				Short(b, 100);                           // health a
				Short(b, 90);                            // health b
				Short(b, (short)MissileType.None.Id);    // ammo type
			}
		}

		// Block 2 — the armory build queue: free slots, then five { index, weapon id } pairs.
		Short(b, 4);
		for (short slot = 0; slot < 5; slot++) {
			Short(b, slot);
			Short(b, slot == 0 ? (short)25 : (short)0);
		}

		// Block 3 — the career block.
		for (int i = 0; i < CareerBlockShorts; i++) {
			Short(b, (short)i);
		}

		// Block 4 — 36 squadmates, then the six shorts closing the block.
		Pilot(b, rosterId: 0, nameIndex: 7, name: "DUGGAN");
		for (int s = 1; s < SquadSize; s++) {
			Pilot(b, rosterId: (short)s, nameIndex: (short)(s % 36), name: "PILOT" + s);
		}

		// Block 4's tail plus block 5's two leading shorts.
		for (int i = 0; i < PrePlayerShorts; i++) {
			Short(b, (short)(i + 100));
		}

		// Block 5 — the player's own pilot record, the same shape as a squadmate's.
		Pilot(b, rosterId: 5, nameIndex: 12, name: "CYRIX");

		// Block 6 — the hangar.
		Short(b, 1);
		Short(b, 2);                                     // bay id
		Herc(b);

		// Block 7 — chassis availability.
		for (int i = 0; i < HercUnlockCount; i++) {
			Short(b, (short)(i % 2));
		}

		// Block 8 — the salvage pool.
		Int(b, 107000);

		// Blocks 9-11 land in the tail the transformer keeps verbatim.
		for (int i = 0; i < 64; i++) {
			b.Add((byte)(i * 3 % 251));
		}

		return b.ToArray();
	}

	private static void Pilot(List<byte> b, short rosterId, short nameIndex, string name) {
		Short(b, rosterId);
		Short(b, nameIndex);
		Short(b, (short)(name.Length + 1));
		foreach (char c in name) {
			b.Add((byte)c);
		}
		b.Add(0);

		Short(b, -1);                                    // bay id, unassigned
		b.Add(1);                                        // on strength
		Short(b, PilotSkill.Veteran.Id);
		Short(b, 11);                                    // squad slot
		Short(b, PilotRank.Captain.Id);
		Short(b, 100);                                   // condition
		Short(b, 1); Short(b, 2); Short(b, 3);           // Herc / Flyer / Base kills, this mission
		Short(b, 40); Short(b, 50); Short(b, 60);        // the same three, career totals
		Short(b, 9);                                     // missions flown
	}

	private static void Herc(List<byte> b) {
		Short(b, HercLUT.Outlaw.Id);
		Short(b, HercLUT.Outlaw.Id);                     // name index
		for (int i = 0; i < HercExternals.Values().Count; i++) {
			Short(b, 100);
		}
		for (int i = 0; i < 10; i++) {
			Short(b, 95);                                // nine internals plus the overall slot
		}
		for (int i = 0; i < 10; i++) {
			Short(b, 90);                                // per-hardpoint
		}
		Short(b, 60);                                    // build percent
		Short(b, 2);                                     // build steps remaining
		Short(b, 3);                                     // hardpoint capacity
		Short(b, 1);                                     // hardpoints occupied
		Short(b, 0);                                     // socket id
		Short(b, 3); Short(b, 1); Short(b, 100); Short(b, 100);
		Short(b, (short)MissileType.None.Id);
	}

	private static void Short(List<byte> b, short v) {
		b.Add((byte)(v & 0xff));
		b.Add((byte)((v >> 8) & 0xff));
	}

	private static void Int(List<byte> b, int v) {
		b.Add((byte)(v & 0xff));
		b.Add((byte)((v >> 8) & 0xff));
		b.Add((byte)((v >> 16) & 0xff));
		b.Add((byte)((v >> 24) & 0xff));
	}
}
