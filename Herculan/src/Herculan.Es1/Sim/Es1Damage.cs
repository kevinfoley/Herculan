using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Sim;

/// <summary>
/// One component's 18-byte piece of a machine's <c>.DMG</c> table, the machine's own copy
/// (docs/es1/formats/dmg-damage-file.md#the-damage-header): the file's eight bytes and dependent list, and the anchor
/// <c>HercPiece_BindComponentAnchors</c> (<c>00009b28</c>) binds from the <c>.COL</c> model.
/// </summary>
public sealed class Es1DamagePiece {
	/// <summary><c>+0</c>, the armor.</summary>
	public short Armor { get; init; }

	/// <summary><c>+3</c>, the shape's cell sequence the component drives, negative for none.</summary>
	public sbyte Sequence { get; init; }

	/// <summary><c>+4</c>, the component it hangs off, which its loss cascades from; −1 for none.</summary>
	public sbyte Parent { get; init; }

	/// <summary><c>+5</c>, the flags: bit 0 cascades the loss, bit 1 lights smoke, bit 2 is the cascade's big explosion.</summary>
	public byte Flags { get; init; }

	/// <summary><c>+6</c> and <c>+8</c>, the dependents: each internal's spill weight and index.</summary>
	public (short Weight, short Internal)[] Dependents { get; init; } = [];

	/// <summary><c>+0xc</c>, the part id of the node the anchor rides; 0 until bound.</summary>
	public short AnchorPart { get; set; }

	/// <summary><c>+0xe</c>, the anchor, its cluster's bound center; null until bound, where retail holds a null pointer.</summary>
	public (short X, short Y, short Z)? AnchorPoint { get; set; }

	/// <summary>
	/// A copy of <paramref name="piece"/>, as <c>HercPiece_ReadRecord</c> reads it and <c>HercPiece_CopyRecord</c>
	/// (<c>000098e8</c>) copies it, with its parent replaced by <paramref name="parent"/> when one is given. A dependent
	/// whose index <c>HercWorks.Core</c> does not name reads as internal −1, which no array holds.
	/// </summary>
	public static Es1DamagePiece From(HercSimDamage.HercPiece piece, sbyte? parent = null) => new() {
		Armor = piece.Armor,
		Sequence = (sbyte)(piece.DebrisFlags >> 8),
		Parent = parent ?? (sbyte)piece.ParentComponent,
		Flags = piece.DestructionFlags,
		Dependents = (piece.MappedInternals ?? []).Select(d => (d.SpillWeight, d.InternalsId?.Id ?? (short)-1)).ToArray(),
	};
}

/// <summary>
/// A machine's damage header, <c>+0x19c</c> (docs/es1/formats/dmg-damage-file.md#the-damage-header), with the
/// functions of the damage arrays' object file (docs/es1/simulation/component-damage.md): the internals' and the
/// components' damage, the occupancy flags, the <c>.DMG</c> table's pieces and maxima and the <c>.COL</c> model. Damage
/// counts up from 0; −1 is a destroyed entry.
/// </summary>
public sealed class Es1DamageHeader {
	/// <summary>The counts <c>Mech_Constructor</c> passes <c>Component_AllocDamageArrays</c>: 21 internals and 28 components.</summary>
	public const int InternalCount = 21, ComponentCount = 28;

	/// <summary>A destroyed entry.</summary>
	public const short Destroyed = -1;

	/// <summary>What <c>Component_DestroyAndCascade</c> gives a lost component's dependents.</summary>
	public const short FinishOff = 32000;

	/// <summary>A full reading, Q8.</summary>
	public const short Full = 0x100;

	/// <summary>The cell a component whose armor is gone with dependents standing shows, and a lost component's.</summary>
	public const short ArmorGoneCell = 1, LostCell = 2;

	/// <summary>The explosion types a lost component makes: the ordinary one and its flags' bit 2's, the cascade's big one.</summary>
	public const short ComponentExplosion = 10, BigExplosion = 0x11;

	/// <summary>The debris group of <c>000bc1c0</c> every lost component throws.</summary>
	public const int LostDebrisGroup = 3;

	/// <summary>The smoke kind a lost component whose flags' bit 1 is set lights.</summary>
	public const short LostSmokeKind = 0;

	private readonly Es1DamagePiece[] _pieces;
	private readonly short[] _maxima;
	private readonly List<short> _queue = new();

	/// <summary>
	/// <c>Component_AllocDamageArrays</c> (<c>00009b8c</c>) with <paramref name="internals"/> and
	/// <paramref name="components"/>, a HERC's <see cref="InternalCount"/> and <see cref="ComponentCount"/> unless given:
	/// the damage zeroed and every occupancy flag set; then <c>Damage_LoadMechDmgFile</c>'s table,
	/// <c>Collision_RegisterObject</c>'s model and <c>Component_LinkMaxRefData</c> (<c>00009c28</c>) binding them to
	/// <paramref name="owner"/>.
	/// </summary>
	/// <remarks>
	/// The maxima array holds at least <see cref="Internals"/> entries, the file's own beyond, zeros past them: retail's
	/// holds the file's count and every reader walks the header's count, so on a file with fewer (<c>SPIDER</c>, 12) it
	/// reads, and the loadout's join writes, past the array; here those land in the zeros, an engine guard.
	/// </remarks>
	public Es1DamageHeader(IEs1SimObject owner, HercSimDamage table, ColliderNode[] model, int internals = InternalCount,
			int components = ComponentCount) {
		Owner = owner;
		Model = model;
		Internals = internals;
		Components = components;
		InternalDamage = new short[internals];
		ComponentDamage = new short[components];
		Occupied = new bool[components];
		_pieces = (table.ComponentData ?? []).Select(piece => Es1DamagePiece.From(piece)).ToArray();
		var maxima = (table.Internals ?? []).Select(i => i.Armor).ToArray();
		_maxima = new short[Math.Max(maxima.Length, internals)];
		maxima.CopyTo(_maxima, 0);
		Array.Fill(Occupied, true);
	}

	/// <summary><c>+0x1a</c>, the internals' count.</summary>
	public int Internals { get; }

	/// <summary><c>+0x18</c>, the components' count.</summary>
	public int Components { get; }

	/// <summary><c>+0</c>, the internals' damage.</summary>
	public short[] InternalDamage { get; }

	/// <summary><c>+4</c>, the components' damage.</summary>
	public short[] ComponentDamage { get; }

	/// <summary><c>+8</c>, the occupancy flags: a component whose flag is clear is not struck, placed or written.</summary>
	public bool[] Occupied { get; }

	/// <summary><c>+0x10</c>, the <c>.COL</c> model, the machine's own; the loadout's join replaces the mounts' clusters.</summary>
	public ColliderNode[] Model { get; private set; }

	/// <summary><c>+0x14</c>, the object the header belongs to.</summary>
	public IEs1SimObject Owner { get; }

	/// <summary>
	/// <see cref="Owner"/> as a HERC, which the cascade's cell frames, smoke and part nodes are read on. PLACEHOLDER: a
	/// flyer's header (part F3 ports its damage write) reaches none of them here; what retail's cascade does on a
	/// flyer's static instance is unread.
	/// </summary>
	private Es1Mech? Machine => Owner as Es1Mech;

	/// <summary>
	/// <c>ComponentCascade_EffectLatch</c> (<c>000af140</c>): one fire and one explosion a cascade. Retail's is one global;
	/// <see cref="ApplyDamageAndCascade"/> clears it at each cascade's head and nothing else is known to read it, so one per
	/// header holds the same.
	/// </summary>
	public bool EffectLatch { get; private set; }

	/// <summary>Component <paramref name="component"/>'s piece, or null past the table, where retail reads past it.</summary>
	public Es1DamagePiece? Piece(int component) =>
		component >= 0 && component < _pieces.Length ? _pieces[component] : null;

	/// <summary>Internal <paramref name="index"/>'s maximum, the table's <c>+4</c> array; 0 outside it, an engine guard.</summary>
	public short Maximum(int index) => index >= 0 && index < _maxima.Length ? _maxima[index] : (short)0;

	private bool IsOccupied(int component) => component >= 0 && component < Components && Occupied[component];

	private short Internal(int index) => index >= 0 && index < Internals ? InternalDamage[index] : (short)0;

	private short Component(int index) => index >= 0 && index < Components ? ComponentDamage[index] : (short)0;

	/// <summary>
	/// <c>Component_AddDamage</c> (<c>00009c88</c>)(<paramref name="damage"/>, <paramref name="maximum"/>,
	/// <paramref name="entry"/>): an entry not already negative takes the damage as an <c>int16</c> sum, and one that
	/// reaches the maximum writes the excess back through <paramref name="damage"/> and becomes <see cref="Destroyed"/>;
	/// answers whether the entry is negative. A sum past 32767 wraps negative with neither written
	/// (docs/es1/formats/dmg-damage-file.md#the-damage-header).
	/// </summary>
	public static bool AddDamage(ref short damage, short maximum, ref short entry) {
		if (entry >= 0) {
			entry = unchecked((short)(entry + damage));
			if (maximum <= entry) {
				damage = unchecked((short)(entry - maximum));
				entry = Destroyed;
			}
		}

		return entry < 0;
	}

	/// <summary>
	/// <c>Component_SpillIntoDependents</c> (<c>0000979c</c>)(<paramref name="piece"/>, maxima, <paramref name="damage"/>,
	/// internals): the weights of the dependents not yet negative summed as an <c>int16</c>, the one
	/// <c>Math_RandomBelow</c> of the sum falls on given the damage (<see cref="AddDamage"/>), and the draw repeated with
	/// the excess while that destroys it; answers whether no dependent was left standing. <paramref name="random"/> is the
	/// simulation's generator, <c>000b5bd4</c>.
	/// </summary>
	private bool SpillIntoDependents(Es1DamagePiece piece, short damage, SimRandom random) {
		while (true) {
			short sum = 0;
			foreach (var (weight, index) in piece.Dependents) {
				if (Internal(index) >= 0) {
					sum = unchecked((short)(sum + weight));
				}
			}

			if (sum == 0) {
				return true;
			}

			short draw = (short)random.NextBelow(sum);
			int chosen = -1;
			foreach (var (weight, index) in piece.Dependents) {
				if (Internal(index) >= 0) {
					draw = unchecked((short)(draw - weight));
					if (draw < 0) {
						chosen = index;
						break;
					}
				}
			}

			// Retail's walk always ends on an entry, the draw being under the sum; an index outside the array stops here.
			if (chosen < 0 || chosen >= Internals
					|| !AddDamage(ref damage, Maximum(chosen), ref InternalDamage[chosen])) {
				return false;
			}
		}
	}

	/// <summary>
	/// <c>Component_ApplyDamageAndCascade</c> (<c>00009f7c</c>)(header, <paramref name="damage"/>,
	/// <paramref name="component"/>) (docs/es1/simulation/component-damage.md#write-and-cascade--component_applydamageandcascade-00009f7c):
	/// <see cref="AddDamage"/> on the component against its armor, and when that answers destroyed the excess spilt into
	/// its dependents. A dependent left standing puts the piece's sequence on <see cref="ArmorGoneCell"/>; none left, with
	/// the flags' bit 0, clears <see cref="EffectLatch"/> and loses the component and then each queued one, last first
	/// (<see cref="DestroyAndCascade"/>), answering 1. The occupancy flag is not tested. The spill draws on
	/// <paramref name="globals"/>' generator, and a lost component's explosion goes into its pool.
	/// </summary>
	public bool ApplyDamageAndCascade(short damage, short component, Es1SimGlobals globals) {
		var random = globals.Random;
		if (Piece(component) is not { } piece || component >= Components) {
			return false;
		}

		if (!AddDamage(ref damage, piece.Armor, ref ComponentDamage[component])) {
			return false;
		}

		if (!SpillIntoDependents(piece, damage, random)) {
			if (piece.Sequence >= 0) {
				Machine?.SetCellFrame(piece.Sequence, ArmorGoneCell);
			}

			return false;
		}

		if ((piece.Flags & 1) == 0) {
			return false;
		}

		EffectLatch = false;
		DestroyAndCascade(component, globals);
		while (_queue.Count > 0) {
			short next = _queue[^1];
			_queue.RemoveAt(_queue.Count - 1);
			DestroyAndCascade(next, globals);
		}

		return true;
	}

	/// <summary>
	/// <c>Component_DestroyAndCascade</c> (<c>00009cd4</c>)(header, <paramref name="component"/>)
	/// (docs/es1/simulation/component-damage.md#losing-a-component--component_destroyandcascade-00009cd4): nothing for a
	/// component whose occupancy flag is clear; otherwise its entry <see cref="Destroyed"/>, its flag cleared and
	/// <see cref="FinishOff"/> spilt into its dependents; a piece with a sequence lights smoke (<see cref="LostSmokeKind"/>)
	/// for flags bit 1 with the latch clear and shows <see cref="LostCell"/>; the explosion at the anchor with the latch
	/// clear; <see cref="LostDebrisGroup"/> thrown from the anchor; and each occupied component whose parent it is queued.
	/// Retail's queue holds 10 and is not bounded; this one is not either.
	/// </summary>
	private void DestroyAndCascade(short component, Es1SimGlobals globals) {
		var random = globals.Random;
		var (cluster, part) = Es1HitSpheres.FindClusterForComponent(Model, component);
		if (!IsOccupied(component) || Piece(component) is not { } piece) {
			return;
		}

		ComponentDamage[component] = Destroyed;
		Occupied[component] = false;
		SpillIntoDependents(piece, FinishOff, random);

		// Retail reads the bound of a component with no cluster through a null pointer; (0, 0, 0) here, an engine guard.
		var center = cluster?.Bound ?? default;
		if (piece.Sequence >= 0) {
			if (!EffectLatch && (piece.Flags & 2) != 0 && Machine is { } machine) {
				// Smoke_Construct (000069ec)(null, kind 0, the bound center, the part id, the machine), its first shape drawn
				// on the generator 000b5c48.
				globals.Smoke.Construct(LostSmokeKind, (center.X, center.Y, center.Z), part, machine, globals.PresentationRandom);
			}

			Machine?.SetCellFrame(piece.Sequence, LostCell);
		}

		// The anchor: the bound center, through the part's node unless the part id is negative, then the machine's frame.
		var anchor = new Vec3i(center.X, center.Y, center.Z);
		if (part >= 0) {
			// Retail passes a null node for a part the shape lacks; the frame alone here, an engine guard.
			anchor = (Machine?.PartNodeTransform(part) ?? Transform3.Identity).TransformPoint(center.X, center.Y, center.Z);
		}

		anchor = Owner.Frame.TransformPoint(anchor.X, anchor.Y, anchor.Z);
		if (!EffectLatch) {
			short type = ComponentExplosion;
			if ((piece.Flags & 4) != 0) {
				EffectLatch = true;
				type = BigExplosion;
			}

			// Explosion_Construct (00005af0)(null, anchor, type).
			globals.Explosions.Construct(anchor, type, globals.Random);
		}

		globals.Debris.ThrowGroup(LostDebrisGroup, anchor, random);

		for (short i = 0; i < Components; i++) {
			if (Occupied[i] && Piece(i)?.Parent == component) {
				_queue.Add(i);
			}
		}
	}

	/// <summary>
	/// <c>Component_IsFullyDestroyed</c> (<c>00009f30</c>): the component's entry negative and every dependent's too
	/// (<c>Component_AllDependentsDestroyed</c>, <c>00009734</c>).
	/// </summary>
	public bool IsFullyDestroyed(int component) =>
		Component(component) < 0 && Piece(component) is { } piece && piece.Dependents.All(d => Internal(d.Internal) < 0);

	/// <summary>
	/// <c>Component_ReadOverallDamage</c> (<c>0000a07c</c>, docs/es1/simulation/component-damage.md#the-readings): every
	/// component's damage, a negative one at its armor, and every internal's, a negative one at its maximum, over the sum of
	/// the armors and maxima, Q8. A sum of 0, which retail divides by, reads 0, an engine guard.
	/// </summary>
	public short ReadOverallDamage() {
		int damage = 0, maximum = 0;
		for (int i = 0; i < Components; i++) {
			short armor = Piece(i)?.Armor ?? 0;
			damage += ComponentDamage[i] < 0 ? armor : ComponentDamage[i];
			maximum += armor;
		}

		for (int i = 0; i < Internals; i++) {
			damage += InternalDamage[i] < 0 ? Maximum(i) : InternalDamage[i];
			maximum += Maximum(i);
		}

		return maximum == 0 ? (short)0 : (short)((damage << 8) / maximum);
	}

	/// <summary>
	/// <c>Component_ReadDamagePercent</c> (<c>0000a16c</c>): the component's damage and its dependents', each −1 at its
	/// maximum, over its armor and their maxima, Q8. Only −1 itself is replaced, so another negative entry is added as it
	/// stands (docs/es1/simulation/component-damage.md#the-readings). A total of 0 reads 0, an engine guard.
	/// </summary>
	public short ReadDamagePercent(int component) {
		if (Piece(component) is not { } piece) {
			return 0;
		}

		int maximum = piece.Armor;
		int damage = Component(component) == Destroyed ? maximum : Component(component);
		foreach (var (_, index) in piece.Dependents) {
			short entry = Internal(index);
			damage += entry == Destroyed ? Maximum(index) : entry;
			maximum += Maximum(index);
		}

		return maximum == 0 ? (short)0 : (short)((damage << 8) / maximum);
	}

	/// <summary>
	/// <c>Component_TotalArmor</c> (<c>0000a24c</c>): the component's armor and its dependents' maxima, an <c>int16</c> sum.
	/// </summary>
	public short TotalArmor(int component) {
		if (Piece(component) is not { } piece) {
			return 0;
		}

		short total = piece.Armor;
		foreach (var (_, index) in piece.Dependents) {
			total = unchecked((short)(total + Maximum(index)));
		}

		return total;
	}

	/// <summary>
	/// Internal <paramref name="index"/>'s own reading as <c>Mech_ComponentDamageWrite</c> works it in line: <see cref="Full"/>
	/// for a negative entry, otherwise the entry over its maximum, Q8. A maximum of 0 reads 0, an engine guard.
	/// </summary>
	public short InternalReading(int index) {
		short entry = Internal(index);
		short maximum = Maximum(index);
		return entry < 0 ? Full : maximum == 0 ? (short)0 : (short)((entry << 8) / maximum);
	}

	/// <summary>
	/// <c>Component_FillDamageReadouts</c> (<c>000109bc</c>)(header, <paramref name="buffer"/>)
	/// (docs/es1/simulation/damage-readouts.md#the-functions), ES2's: entries 1 to 19 components 0 to 18 against their
	/// armor; 32 to 41 component 19 + <i>n</i> and internal 12 + <i>n</i> together; 20 to 31 internals 0 to 11, 0 for a
	/// maximum of 0. A negative entry reads full. Entry 41, component 28 and internal 21, lies past ES1's arrays, which
	/// retail reads past; it and every division by 0 read 0 here, an engine guard.
	/// </summary>
	public void FillDamageReadouts(short[] buffer) {
		for (int i = 0; i < 19; i++) {
			short armor = Piece(i)?.Armor ?? 0;
			buffer[1 + i] = ComponentDamage[i] < 0 ? Full : armor == 0 ? (short)0 : (short)((ComponentDamage[i] << 8) / armor);
		}

		for (int n = 0; n < 10; n++) {
			int component = 19 + n, index = 12 + n;
			int armor = Piece(component)?.Armor ?? 0;
			short maximum = Maximum(index);
			int taken = (Component(component) < 0 ? armor : Component(component))
				+ (Internal(index) < 0 ? maximum : Internal(index));
			int total = armor + maximum;
			buffer[32 + n] = total == 0 ? (short)0 : (short)(taken * 0x100 / total);
		}

		for (int i = 0; i < 12; i++) {
			short maximum = Maximum(i);
			buffer[20 + i] = maximum == 0 ? (short)0 : InternalDamage[i] < 0 ? Full : (short)((InternalDamage[i] << 8) / maximum);
		}
	}

	/// <summary>
	/// <c>Component_ReadEntryDamage</c> (<c>00010b7c</c>)(header, <paramref name="index"/>, <paramref name="kind"/>): kind 0
	/// internal <paramref name="index"/> against its maximum; 1 component <paramref name="index"/> against its armor; 2
	/// component <paramref name="index"/> + 19 against component <paramref name="index"/>'s armor. A negative entry or a
	/// zero maximum reads full. Retail reads through unset pointers for another kind; full here, an engine guard.
	/// </summary>
	public short ReadEntryDamage(int index, int kind) {
		(short entry, short maximum) = kind switch {
			0 => (Internal(index), Maximum(index)),
			1 => (Component(index), Piece(index)?.Armor ?? 0),
			2 => (Component(index + 19), Piece(index)?.Armor ?? 0),
			_ => (Destroyed, (short)0),
		};
		return entry < 0 || maximum == 0 ? Full : (short)((entry << 8) / maximum);
	}

	/// <summary>
	/// <c>Mech_ApplySquadCondition</c> (<c>000107f0</c>)(header, <paramref name="hardpoints"/>, <paramref name="internals"/>,
	/// <paramref name="external"/>) (docs/es1/simulation/damage-readouts.md#the-functions), ES2's: each entry written as
	/// (100 − condition) × maximum / 100: components 0 to 12 from <paramref name="external"/>, 13 to 18 zeroed; internals 0
	/// to 8 from <paramref name="internals"/> by index, 9 to 11 zeroed; components 19 + <i>n</i> from
	/// <paramref name="hardpoints"/> against their armor and internal 12 + <i>n</i>'s maximum together, the share past the
	/// armor going to the internal, each −1 past its maximum. The tenth pair lies past ES1's arrays, where retail writes;
	/// here it is not written, an engine guard, nor is a condition past its span read (100, intact).
	/// </summary>
	public void ApplySquadCondition(IReadOnlyList<short> hardpoints, IReadOnlyList<short> internals,
			IReadOnlyList<short> external) {
		static short At(IReadOnlyList<short> span, int i) => i < span.Count ? span[i] : (short)100;
		static short Share(short condition, int maximum) => (short)((100 - condition) * maximum / 100);

		for (int i = 0; i < 19; i++) {
			ComponentDamage[i] = i < 13 ? Share(At(external, i), Piece(i)?.Armor ?? 0) : (short)0;
		}

		for (int i = 0; i < 12; i++) {
			InternalDamage[i] = i == 9 || i >= 10 ? (short)0 : Share(At(internals, i), Maximum(i));
		}

		for (int n = 0; n < 10; n++) {
			int component = 19 + n, index = 12 + n;
			short armor = Piece(component)?.Armor ?? 0;
			short maximum = Maximum(index);
			short taken = Share(At(hardpoints, n), unchecked((short)(maximum + armor)));
			if (component >= Components || index >= Internals) {
				continue;
			}

			if (armor < taken) {
				ComponentDamage[component] = Destroyed;
				InternalDamage[index] = maximum < (short)(taken - armor) ? Destroyed : (short)(taken - armor);
			} else {
				ComponentDamage[component] = taken;
			}
		}
	}

	/// <summary>
	/// The damage half of <c>Mech_ConfigureLoadout</c>'s join for a mount on hardpoint slot <paramref name="slot"/>
	/// (docs/es1/formats/dmg-damage-file.md#the-loadouts-join): component <paramref name="slot"/> + 19 takes the
	/// template's piece, its parent kept and its first dependent made internal <paramref name="slot"/> + 12
	/// (<c>HercPiece_ReplacePieces</c>, <c>00009ad4</c>), whose maximum becomes the template's
	/// (<c>HercPiece_SetInternalMaxima</c>, <c>000099e4</c>); and the template's spheres, moved by
	/// <paramref name="offset"/>, replace the first cluster of that component (<c>Collision_ReplaceClusters</c>,
	/// <c>000096e8</c>, <see cref="Es1HitSpheres.ReplaceCluster"/>).
	/// </summary>
	public void FitWeapon(int slot, Weapons.WeaponMountTemplate template, Vec3i offset) {
		short component = (short)(slot + 19);
		short index = (short)(slot + 12);
		if (component >= 0 && component < _pieces.Length) {
			var piece = Es1DamagePiece.From(template.Piece, _pieces[component].Parent);
			if (piece.Dependents.Length > 0) {
				piece.Dependents[0] = (piece.Dependents[0].Weight, index);
			}

			_pieces[component] = piece;
		}

		if (index >= 0 && index < _maxima.Length) {
			_maxima[index] = template.InternalMaximum;
		}

		Model = Es1HitSpheres.ReplaceCluster(Model, component, template.Cluster.Spheres ?? [], offset);
	}

	/// <summary>
	/// The end of <c>Mech_ConfigureLoadout</c>: each cluster's node part id and bound center bound to its component's piece,
	/// in the model's order, so the last cluster of a component wins (<c>Collision_CollectComponentAnchors</c>,
	/// <c>000092e4</c>, and <c>HercPiece_BindComponentAnchors</c>); then the occupancy flag of each component whose piece
	/// lists no internal cleared.
	/// </summary>
	public void BindAnchorsAndClearUnoccupied() {
		foreach (var node in Model) {
			foreach (var cluster in node.Clusters ?? []) {
				if (Piece(cluster.ComponentIndex) is { } piece) {
					piece.AnchorPart = node.NodeIndex;
					piece.AnchorPoint = (cluster.Bound.X, cluster.Bound.Y, cluster.Bound.Z);
				}
			}
		}

		for (int i = 0; i < Components; i++) {
			if ((Piece(i)?.Dependents.Length ?? 0) == 0) {
				Occupied[i] = false;
			}
		}
	}
}
