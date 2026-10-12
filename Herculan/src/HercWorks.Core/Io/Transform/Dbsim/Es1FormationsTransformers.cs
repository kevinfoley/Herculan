using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>Reads and writes ES1's <c>dat\MFORMS.DAT</c> (<see cref="Es1MechFormations"/>; docs/es1/formats/formations-dat.md).</summary>
public class Es1MechFormationsTransformer : Es1CountedTableTransformer<Es1MechFormations, Es1MechFormation> {
	protected override int RecordLength => Es1MechFormation.Length;

	protected override Es1MechFormation ReadRecord() {
		var slots = new Es1FormationSlot16[Es1MechFormation.SlotCount];
		for (int i = 0; i < slots.Length; i++) {
			slots[i] = new Es1FormationSlot16 { X = IndexShortLE(), Y = IndexShortLE() };
		}

		return new Es1MechFormation { Slots = slots };
	}

	protected override void WriteRecord(BinaryWriter w, Es1MechFormation record) {
		foreach (var slot in record.Slots) {
			w.Write(slot.X);
			w.Write(slot.Y);
		}
	}

	protected override Es1MechFormations Create(Es1MechFormation[] records) => new() { Formations = records };

	protected override IReadOnlyList<Es1MechFormation> RecordsOf(Es1MechFormations file) => file.Formations;
}

/// <summary>Reads and writes ES1's <c>dat\FFORMS.DAT</c> (<see cref="Es1FlyerFormations"/>; docs/es1/formats/formations-dat.md).</summary>
public class Es1FlyerFormationsTransformer : Es1CountedTableTransformer<Es1FlyerFormations, Es1FlyerFormation> {
	protected override int RecordLength => Es1FlyerFormation.Length;

	protected override Es1FlyerFormation ReadRecord() {
		var slots = new Es1FormationSlot16[Es1FlyerFormation.SlotCount];
		for (int i = 0; i < slots.Length; i++) {
			slots[i] = new Es1FormationSlot16 { X = IndexShortLE(), Y = IndexShortLE(), Z = IndexShortLE() };
		}

		return new Es1FlyerFormation { Slots = slots };
	}

	protected override void WriteRecord(BinaryWriter w, Es1FlyerFormation record) {
		foreach (var slot in record.Slots) {
			w.Write(slot.X);
			w.Write(slot.Y);
			w.Write(slot.Z);
		}
	}

	protected override Es1FlyerFormations Create(Es1FlyerFormation[] records) => new() { Formations = records };

	protected override IReadOnlyList<Es1FlyerFormation> RecordsOf(Es1FlyerFormations file) => file.Formations;
}

/// <summary>Reads and writes ES1's <c>dat\BFORMS.DAT</c> (<see cref="Es1BaseFormations"/>; docs/es1/formats/formations-dat.md).</summary>
public class Es1BaseFormationsTransformer : Es1CountedTableTransformer<Es1BaseFormations, Es1BaseFormation> {
	protected override int RecordLength => Es1BaseFormation.Length;

	protected override Es1BaseFormation ReadRecord() {
		var slots = new Es1FormationSlot32[Es1BaseFormation.SlotCount];
		for (int i = 0; i < slots.Length; i++) {
			slots[i] = new Es1FormationSlot32 { X = IndexIntLE(), Y = IndexIntLE() };
		}

		return new Es1BaseFormation { Slots = slots };
	}

	protected override void WriteRecord(BinaryWriter w, Es1BaseFormation record) {
		foreach (var slot in record.Slots) {
			w.Write(slot.X);
			w.Write(slot.Y);
		}
	}

	protected override Es1BaseFormations Create(Es1BaseFormation[] records) => new() { Formations = records };

	protected override IReadOnlyList<Es1BaseFormation> RecordsOf(Es1BaseFormations file) => file.Formations;
}
