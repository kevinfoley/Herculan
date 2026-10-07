using HercWorks.Core.Data.Struct.Herc;

namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - dmg\[herc].DMG — the per-chassis component table: a count and that many internal maxima,
/// then a count and that many component pieces (<see cref="HercPiece"/>, 8 bytes plus a 4-byte
/// entry per dependent internal). DBSIM builds the filename from the machine's own name string.
/// Retail mechs carry 22 internals and 29 components; SKIMMER carries 1 and 1.
///
/// <para>The component and internal index spaces, the piece record and what each piece's dependent
/// list holds per chassis are in docs/retail/formats/dmg-damage-file.md; what the simulator does with them
/// is docs/retail/simulation/component-damage.md.</para>
/// </summary>
public class HercSimDamage {
	/// <summary>
	/// Source file name. <see cref="Io.Transform.Dbsim.HercDamageFileTransformer.Write"/> checks it
	/// to tell a skimmer's .DMG (one internals slot) from a herc's (22), so a caller that wants to
	/// write must set it. The read path does not populate it.
	/// </summary>
	public string? FileName { get; set; }

	public short InternalsTotal { get; set; }

	/// <summary>Each internal's maximum, by internal index.</summary>
	public InternalsHealth[]? Internals { get; set; }

	/// <summary>The component pieces, by component index.</summary>
	public HercPiece[]? ComponentData { get; set; }

	public InternalsHealth NewInternalsHealth() => new();
	public HercPiece NewHercPiece() => new();
	public InternalsTarget NewInternalsTarget() => new();

	/// <summary>One component's record. See docs/retail/formats/dmg-damage-file.md#the-piece-record.</summary>
	public class HercPiece {
		/// <summary><c>+0x00</c> — the component's own maximum.</summary>
		public short Armor { get; set; }

		/// <summary>
		/// Record offsets <c>+0x02</c> (the debris group the piece throws) and <c>+0x03</c> (the
		/// shape sequence it drives), each <c>-1</c> for none, read as one <c>short</c>. See
		/// docs/retail/formats/dmg-damage-file.md#the-piece-record.
		/// </summary>
		public short DebrisFlags { get; set; }

		/// <summary>
		/// <c>+0x04</c>, signed — the index of the parent component this one hangs off, <c>-1</c> for
		/// none. Destroying a component cascades to every live piece naming it here.
		/// </summary>
		public byte ParentComponent { get; set; }

		/// <summary>
		/// <c>+0x05</c> — destruction flags. The bit meanings, from <c>Component_ApplyDamageAndCascade</c>
		/// (<c>0040da38</c>) and <c>Component_DestroyAndCascade</c> (<c>0040d434</c>), are in
		/// docs/retail/formats/dmg-damage-file.md#the-piece-record.
		/// </summary>
		public byte DestructionFlags { get; set; }

		/// <summary>The dependent list: the internals behind this component.</summary>
		public InternalsTarget[]? MappedInternals { get; set; }

		public HercPiece() { }

		public HercPiece(short boneCount) {
			MappedInternals = new InternalsTarget[boneCount];
		}
	}

	public class InternalsHealth {
		public short Id { get; set; }
		public short Armor { get; set; }
		public HercInternals? Name { get; set; }

		public InternalsHealth() { }

		public InternalsHealth(short id, short armor) {
			Id = id;
			Armor = armor;
		}
	}

	/// <summary>One dependent-list entry: an internal behind a component.</summary>
	public class InternalsTarget {
		/// <summary>
		/// This entry's weight in the draw that picks which internal a hit's spill lands on — 20 on
		/// nearly every entry, 1 or 5 for the pilot.
		/// </summary>
		public short SpillWeight { get; set; } = 20;

		public HercInternals? InternalsId { get; set; }

		public InternalsTarget() { }

		public InternalsTarget(short spillWeight, HercInternals internalsId) {
			SpillWeight = spillWeight;
			InternalsId = internalsId;
		}
	}
}
