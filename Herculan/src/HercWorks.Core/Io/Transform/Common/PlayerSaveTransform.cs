using HercWorks.Core.Data.File.Sav;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Data.Struct.Herc;
using HercWorks.Core.Data.Struct.Vshell.Hercs;
using HercWorks.Core.Data.Struct.Vshell.Sav;
using System.Text;

namespace HercWorks.Core.Io.Transform.Common;

/// <summary>Transforms byte[] data to and from a <c>sav\GAME_?.SAV</c> (see <see cref="PlayerSave"/>).</summary>
public class PlayerSaveTransform : ByteTransformer<PlayerSave> {
	private int _dbgBuffer;

	public override PlayerSave? Parse(byte[]? inputArray) {
		if (inputArray == null || inputArray.Length <= 0) {
			return null;
		}

		SetBytes(inputArray);

		var save = new PlayerSave();

		// Block 1, the armory stock: one record per catalog id, all 33, cut weapons included.
		var inventory = new Inventory {
			Items = new Inventory.InventoryItem[WeaponLUT.Values().Count]
		};
		for (int i = 0; i < WeaponLUT.Values().Count; i++) {
			byte flag = IndexByte();
			short quant = IndexShortLE();
			var entry = inventory.NewEntry();
			entry.Id = WeaponLUT.GetById(i);
			entry.UnlockFlag = flag;
			entry.Quantity = quant;

			var items = new ShellWeaponEntry[quant];
			for (int q = 0; q < quant; q++) {
				var weapon = new ShellWeaponEntry {
					Id = WeaponLUT.GetById(IndexShortLE()),
					ClassIndex = IndexShortLE(),
					FitCondition = IndexShortLE(),
					Condition = IndexShortLE(),
					Guidance = MissileType.GetById(IndexShortLE())
				};
				items[q] = weapon;
			}
			entry.Units = items;

			inventory.Items[i] = entry;
		}
		save.Inventory = inventory;

		// Block 2, the armory build queue
		save.BuildQueueFreeSlots = IndexShortLE();
		for (int w = 0; w < save.BuildQueue.Length; w++) {
			IndexShortLE(); // the slot index; Write puts back the array position
			save.BuildQueue[w] = WeaponLUT.GetById(IndexShortLE())!;
		}

		// Block 3, the career block — 76 shorts. Not the campaign flag array, which is 2000 bytes and
		// sits in the tail read below.
		for (int f = 0; f < save.CareerBlock.Length; f++) {
			save.CareerBlock[f] = IndexShortLE();
		}

		// Squadmate segment
		var squad = new PilotEntry[36]; // 36 squadmates
		for (int s = 0; s < squad.Length; s++) {
			squad[s] = IndexPilot();
		}
		save.Squadmates = squad;

		// The six shorts closing the squad block plus the two opening the player block — eight.
		for (int r = 0; r < save.SquadTailAndPlayerHead.Length; r++) {
			save.SquadTailAndPlayerHead[r] = IndexShortLE();
		}

		// Pilot segment — the same record shape as a squadmate's; the two shorts that precede it were
		// consumed with SquadTailAndPlayerHead above.
		save.PlayerPilot = IndexPilot();

		// Herc bay data
		short baySlots = IndexShortLE();
		for (int b = 0; b < baySlots; b++) {
			short bayId = IndexShortLE();
			save.HercBay[bayId] = IndexHercEntry();
		}

		// Block 7, the nine chassis availability flags — 9 shorts (18 bytes)
		int l = 0;
		while (l < HercLUT.Mongoose.Id) {
			short val = IndexShortLE();
			save.ChassisAvailability[HercLUT.GetById((short)l)!] = val;
			l += 1;
		}

		// Total available salvage
		save.SalvageTotal = IndexIntLE();

		// Tail: the 2000-byte campaign flag array, the 2-byte game state, and a 20-byte block — plus
		// whatever stale bytes the non-truncating writer left past the payload. Carried verbatim.
		using var fragmentFlags = new MemoryStream();
		while (Index < GetBytes().Length) {
			byte b = IndexByte();
			fragmentFlags.WriteByte(b);
		}
		save.CampaignStateTail = fragmentFlags.ToArray();

		return save;
	}

	/// <summary>
	/// One pilot record. <b>The player's is the same shape as a squadmate's</b> — VSHELL reads both
	/// with <c>Pilot_Read</c> (<c>0040fefc</c>) and writes both with <c>Pilot_Write</c> (<c>0040fd5f</c>), roster id included. What
	/// makes the player's segment look different is that two shorts of its own precede the record;
	/// those belong to the surrounding block and are read with <c>SquadTailAndPlayerHead</c>.
	///
	/// <para>Three shorts precede the name — roster id, esnames index, then the length — and eleven
	/// follow the on-strength byte. Every count here matters: taking the esnames index for the length
	/// desynchronizes the whole squad segment, reading a twelfth trailing short eats into the block
	/// that follows, and dropping the roster id for the player alone lands its name two bytes early.
	/// See <c>docs/retail/formats/save-games.md</c>.</para>
	/// </summary>
	private PilotEntry IndexPilot() {
		var entry = new PilotEntry();

		entry.RosterId = IndexShortLE();
		entry.NameIndex = IndexShortLE();

		short nameLen = IndexShortLE();
		byte[] name = IndexSegment(nameLen);
		entry.Name = nameLen > 0
			? BytesToLatin1String(name).Substring(0, nameLen - 1)
			: string.Empty;

		entry.Bay = IndexShortLE();
		entry.OnStrength = IndexByte();
		entry.Skill = PilotSkill.GetById(IndexShortLE());
		entry.SquadPosition = IndexShortLE();
		entry.Rank = PilotRank.GetById(IndexShortLE());
		entry.Condition = IndexShortLE();
		entry.HercKills = IndexShortLE();
		entry.FlyerKills = IndexShortLE();
		entry.BaseKills = IndexShortLE();
		entry.TotalHercKills = IndexShortLE();
		entry.TotalFlyerKills = IndexShortLE();
		entry.TotalBaseKills = IndexShortLE();
		entry.MissionsFlown = IndexShortLE();

		return entry;
	}

	private HercBayEntry IndexHercEntry() {
		var herc = new HercBayEntry {
			ChassisType = HercLUT.GetById(IndexShortLE()),
			ChassisIndex = IndexShortLE(),
			ExternalConditions = new Dictionary<HercExternals, ShellHercPart>()
		};

		foreach (var e in HercExternals.Values()) {
			herc.ExternalConditions[e] = new ShellHercPart(e.Id, e.Label, IndexShortLE());
		}

		// Ids 0-9 only: 0-8 are the nine named components and 9 is the machine's overall condition.
		// Ids 10-21 are not in the file at all, so stopping short of them is the format, not a gap.
		// See HercInternals.
		herc.InternalConditions = new Dictionary<HercInternals, ShellHercPart>();
		foreach (var internalPart in HercInternals.Values()) {
			if (internalPart.Id < HercInternals.ServosLegLeftRear.Id) {
				herc.InternalConditions[internalPart] = new ShellHercPart(internalPart.Id, internalPart.Label, IndexShortLE());
			}
		}

		for (int h = 0; h < herc.HardpointConditions.Length; h++) {
			herc.HardpointConditions[h] = new ShellHercPart((short)h, "hardpoint_" + h, IndexShortLE());
		}

		herc.BuildPercent = IndexShortLE();
		herc.BuildMissionsLeft = IndexShortLE();

		herc.MountCapacity = IndexShortLE();
		herc.MountsOccupied = IndexShortLE();
		for (int h = 0; h < herc.MountsOccupied; h++) {
			short socketId = IndexShortLE();
			var weapon = new ShellWeaponEntry {
				Id = WeaponLUT.GetById(IndexShortLE()),
				ClassIndex = IndexShortLE(),
				FitCondition = IndexShortLE(),
				Condition = IndexShortLE(),
				Guidance = MissileType.GetById(IndexShortLE())
			};
			herc.Mounts[socketId] = weapon;
		}

		return herc;
	}

	public override byte[]? Write(PlayerSave save) {

		using var outStream = new MemoryStream();

		// ARMORY STOCK
		foreach (var item in save.Inventory!.Items!) {
			outStream.WriteByte((byte)item.UnlockFlag);
			_dbgBuffer += 1;
			WriteAndCount(outStream, WriteShortLE(item.Quantity));

			foreach (var entry in item.Units!) {
				WriteAndCount(outStream, WriteShortLE((short)entry.Id!.Id));
				WriteAndCount(outStream, WriteShortLE(entry.ClassIndex));
				WriteAndCount(outStream, WriteShortLE(entry.FitCondition));
				WriteAndCount(outStream, WriteShortLE(entry.Condition));
				WriteAndCount(outStream, WriteShortLE((short)entry.Guidance!.Id));
			}
		}

		// BUILD QUEUE
		WriteAndCount(outStream, WriteShortLE(save.BuildQueueFreeSlots));
		for (int w = 0; w < save.BuildQueue.Length; w++) {
			WriteAndCount(outStream, WriteShortLE((short)w));
			WriteAndCount(outStream, WriteShortLE((short)save.BuildQueue[w].Id));
		}

		// CAREER BLOCK
		foreach (var f in save.CareerBlock) {
			WriteAndCount(outStream, WriteShortLE(f));
		}

		// PILOT DATA
		foreach (var pilot in save.Squadmates!) {
			WritePilotData(pilot, outStream);
		}

		// SQUAD BLOCK TAIL + PLAYER BLOCK HEAD
		foreach (var unk in save.SquadTailAndPlayerHead) {
			WriteAndCount(outStream, WriteShortLE(unk));
		}

		// PLAYER PILOT
		WritePilotData(save.PlayerPilot!, outStream);

		// HERC DATA — keyed by the bay id read from the file: retail saves have sparse bay ids
		// (e.g. no bay 2 or bay 7), so the bays cannot be walked as 0..Count-1.
		outStream.Write(WriteShortLE((short)save.HercBay.Count), 0, 2);
		foreach (var kv in save.HercBay) {
			WriteHercEntry(kv.Key, kv.Value, outStream);
		}

		// CHASSIS AVAILABILITY — the same nine ids the read path walks (0 until HercLUT.Mongoose.Id).
		for (short l = 0; l < HercLUT.Mongoose.Id; l++) {
			var herc = HercLUT.GetById(l)!;
			short val = save.ChassisAvailability.TryGetValue(herc, out var unlockVal) ? unlockVal : (short)0;
			WriteAndCount(outStream, WriteShortLE(val));
		}

		// SALVAGE
		WriteAndCount(outStream, WriteIntLE(save.SalvageTotal));

		// TAIL SEGMENT
		foreach (var b in save.CampaignStateTail!) {
			outStream.WriteByte(b);
			_dbgBuffer += 1;
		}

		return outStream.ToArray();
	}

	/// <summary>Mirror of <see cref="IndexPilot"/>, field for field, player and squadmate alike.</summary>
	private void WritePilotData(PilotEntry pilot, MemoryStream outArr) {
		WriteAndCount(outArr, WriteShortLE(pilot.RosterId));
		WriteAndCount(outArr, WriteShortLE(pilot.NameIndex));

		// Latin-1 to match the read side: the shell's own names are ASCII, but a player-typed name
		// with a high byte has to come back as the same single byte it went out as.
		var nameBytes = Encoding.Latin1.GetBytes(pilot.Name ?? string.Empty);
		var arr = new byte[nameBytes.Length + 1];
		Array.Copy(nameBytes, arr, nameBytes.Length);
		arr[^1] = 0x00;

		WriteAndCount(outArr, WriteShortLE((short)arr.Length));
		outArr.Write(arr, 0, arr.Length);
		_dbgBuffer += arr.Length;

		WriteAndCount(outArr, WriteShortLE(pilot.Bay));
		outArr.WriteByte(pilot.OnStrength);
		_dbgBuffer += 1;
		WriteAndCount(outArr, WriteShortLE(pilot.Skill?.Id ?? PilotSkill.Rookie.Id));
		WriteAndCount(outArr, WriteShortLE(pilot.SquadPosition));
		WriteAndCount(outArr, WriteShortLE(pilot.Rank?.Id ?? PilotRank.Lieutenant.Id));
		WriteAndCount(outArr, WriteShortLE(pilot.Condition));
		WriteAndCount(outArr, WriteShortLE(pilot.HercKills));
		WriteAndCount(outArr, WriteShortLE(pilot.FlyerKills));
		WriteAndCount(outArr, WriteShortLE(pilot.BaseKills));
		WriteAndCount(outArr, WriteShortLE(pilot.TotalHercKills));
		WriteAndCount(outArr, WriteShortLE(pilot.TotalFlyerKills));
		WriteAndCount(outArr, WriteShortLE(pilot.TotalBaseKills));
		WriteAndCount(outArr, WriteShortLE(pilot.MissionsFlown));
	}

	private void WriteHercEntry(short bayId, HercBayEntry herc, MemoryStream outArr) {
		WriteAndCount(outArr, WriteShortLE(bayId));
		WriteAndCount(outArr, WriteShortLE(herc.ChassisType!.Id));
		WriteAndCount(outArr, WriteShortLE(herc.ChassisIndex));

		foreach (var external in HercExternals.Values()) {
			WriteAndCount(outArr, WriteShortLE(herc.ExternalConditions![external].Health));
		}

		foreach (var internalPart in HercInternals.Values()) {
			if (internalPart.Id < HercInternals.ServosLegLeftRear.Id) {
				WriteAndCount(outArr, WriteShortLE(herc.InternalConditions![internalPart].Health));
			}
		}

		foreach (var part in herc.HardpointConditions) {
			if (part == null) {
				WriteAndCount(outArr, WriteShortLE(100));
			} else {
				WriteAndCount(outArr, WriteShortLE(part.Health));
			}
		}

		WriteAndCount(outArr, WriteShortLE(herc.BuildPercent));
		WriteAndCount(outArr, WriteShortLE(herc.BuildMissionsLeft));

		WriteAndCount(outArr, WriteShortLE(herc.MountCapacity));
		WriteAndCount(outArr, WriteShortLE(herc.MountsOccupied));

		// Keyed by mount slot: mounts serialize sparsely, like the bays above.
		foreach (var kv in herc.Mounts) {
			WriteAndCount(outArr, WriteShortLE(kv.Key));
			var weapon = kv.Value;
			WriteAndCount(outArr, WriteShortLE((short)weapon.Id!.Id));
			WriteAndCount(outArr, WriteShortLE(weapon.ClassIndex));
			WriteAndCount(outArr, WriteShortLE(weapon.FitCondition));
			WriteAndCount(outArr, WriteShortLE(weapon.Condition));
			WriteAndCount(outArr, WriteShortLE((short)weapon.Guidance!.Id));
		}
	}

	private void WriteAndCount(MemoryStream outArr, byte[] data) {
		outArr.Write(data, 0, data.Length);
		_dbgBuffer += data.Length;
	}

	/// <summary>Same zero-extend-per-byte string decode used by IndexString/NameFromListBytes.</summary>
	private static string BytesToLatin1String(byte[] data) {
		var chars = new char[data.Length];
		for (int i = 0; i < data.Length; i++) {
			chars[i] = (char)data[i];
		}
		return new string(chars);
	}
}
