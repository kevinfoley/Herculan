using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.Struct.Herc;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Sim;

/// <summary>
/// The per-component health model a mech or a flyer carries — the header of three arrays the
/// original allocates at <c>mech+0x206</c> (<c>Component_AllocDamageArrays</c>, <c>0040d2cc</c>) and
/// the four functions that read and write it. Structures have their own, much simpler one; see
/// <see cref="BaseObject.ApplyDamage"/>.
///
/// <para><b>Three arrays, and they are damage taken rather than health left</b> — every entry
/// starts at zero and climbs, and <c>-1</c> is the destroyed sentinel:</para>
/// <list type="bullet">
/// <item><see cref="Damage"/>, one entry per <i>main</i> component — 29 for a mech, 1 for a flyer,
/// both hard-coded at the constructor's call site rather than taken from the file.</item>
/// <item>A parallel <i>dependent sub-piece</i> array — 22 for a mech, 1 for a flyer. These are the
/// finer parts a main component's reading aggregates: servos, the sensor array, the shield
/// generator, the engine.</item>
/// <item>An <i>active</i> flag per main component, cleared when that component is destroyed. It is
/// what the hit test reads to stop offering a lost component as a target.</item>
/// </list>
///
/// <para>The maxima are not here: they are the <c>dmg\&lt;NAME&gt;.DMG</c> file, which supplies both
/// an 18-byte record per main component (its armour, its bone group, its destruction flags, and its
/// weighted list of dependents) and a flat maximum per dependent. That file is
/// <see cref="HercSimDamage"/>, already parsed by HercWorks.Core. A weapon mount is the exception:
/// its fitted weapon brings its own record and maximum (<see cref="FitWeapon"/>).</para>
///
/// <para><b>Overflow spills sideways.</b> A hit that finishes a main component does not stop there:
/// the excess is handed to <see cref="SpillIntoDependents"/>, which picks one of the component's
/// live dependents at random — weighted by the per-dependent figure the <c>.DMG</c> calls a crit
/// chance — and pours the excess into it, repeating for as long as each spill destroys something.
/// That is how shooting a leg off takes the servo in it with it.</para>
/// </summary>
public sealed class ComponentDamage {
	/// <summary>
	/// The sentinel a destroyed entry holds. <c>Component_AddDamage</c> (<c>0040d3ec</c>) writes it instead of the maximum, so
	/// "destroyed" is a distinct state from "damaged to exactly its maximum", and every read
	/// substitutes the maximum back in.
	/// </summary>
	public const short Destroyed = -1;

	/// <summary>How many main component slots a mech gets — <c>Mech_Constructor</c>'s literal <c>0x1d</c>.</summary>
	public const int MechComponentCount = 29;

	/// <summary>And how many dependent sub-pieces — its literal <c>0x16</c>.</summary>
	public const int MechDependentCount = 22;

	/// <summary>
	/// A flyer's counts, both literal <c>1</c> in <c>Flyer_Constructor</c> (<c>004215f4</c>). A flyer is one component with
	/// one dependent under it, which is exactly what <c>SKIMMER.DMG</c> ships.
	/// </summary>
	public const int FlyerComponentCount = 1;

	/// <inheritdoc cref="FlyerComponentCount"/>
	public const int FlyerDependentCount = 1;

	/// <summary>The damage-finished-it flag <c>Component_SpillIntoDependents</c> (<c>0040cf44</c>) pours in on destruction — its own literal.</summary>
	private const short FinishOffDependents = 32000;

	private readonly HercSimDamage.HercPiece[] _pieces;
	private readonly short[] _dependentMaxima;
	private readonly short[] _damage;
	private readonly short[] _dependentDamage;
	private readonly bool[] _active;
	private readonly List<int> _pendingCascade = new();
	private readonly SimRandom _random;

	/// <param name="model">
	/// The type's <c>dmg\&lt;NAME&gt;.DMG</c>, shared by every object of the type. Its pieces and maxima
	/// are copied, because a fitted weapon replaces its mount's in this object alone — see
	/// <see cref="FitWeapon"/>.
	/// </param>
	/// <param name="componentCount">
	/// Main component slots. <b>Not read from the file</b> — the original hard-codes 29 for a mech and
	/// 1 for a flyer at the allocation site, and a file that disagrees simply leaves slots unusable.
	/// </param>
	/// <param name="dependentCount">Dependent sub-piece slots, likewise hard-coded.</param>
	/// <param name="random">The simulation's generator, for the weighted spill pick.</param>
	public ComponentDamage(HercSimDamage model, int componentCount, int dependentCount, SimRandom random) {
		_pieces = (HercSimDamage.HercPiece[]?)model.ComponentData?.Clone() ?? Array.Empty<HercSimDamage.HercPiece>();
		_dependentMaxima = (model.Internals ?? Array.Empty<HercSimDamage.InternalsHealth>())
			.Select(internals => internals?.Armor ?? 0)
			.ToArray();
		_random = random;
		_damage = new short[componentCount];
		_dependentDamage = new short[dependentCount];
		_active = new bool[componentCount];
		Array.Fill(_active, true);
	}

	/// <summary>How many main component slots this object has.</summary>
	public int Count => _damage.Length;

	/// <summary>Damage taken by each main component. <see cref="Destroyed"/> for one that has been lost.</summary>
	public IReadOnlyList<short> Damage => _damage;

	/// <summary>
	/// Whether a main component is still standing — the original's active-flag array, which the hit
	/// test consults before it will even test that component's spheres.
	/// </summary>
	public bool IsActive(int index) => index >= 0 && index < _active.Length && _active[index];

	/// <summary>
	/// Clears one slot's active flag on its own, leaving its damage reading alone — the bare
	/// <c>mech+0x20e[i] = 0</c> write <c>Mech_ApplyDirectFireDamage</c> makes when a weapon-mount
	/// component is knocked out. It is not <see cref="DestroyAndCascade"/>: the slot stops being
	/// shootable and stops taking further writes through the damage pathways, but nothing under it or
	/// mounted on it comes apart.
	/// </summary>
	public void Deactivate(int index) {
		if (index >= 0 && index < _active.Length) {
			_active[index] = false;
		}
	}

	/// <summary>
	/// The <c>.DMG</c> record for one main component, or null when the file has no such slot — or, for
	/// a weapon mount, the record its fitted weapon put there (<see cref="FitWeapon"/>).
	/// </summary>
	public HercSimDamage.HercPiece? Piece(int index) =>
		index >= 0 && index < _pieces.Length ? _pieces[index] : null;

	/// <summary>
	/// The damage half of <c>Mech_ConfigureLoadout</c> (<c>004175dc</c>)'s per-mount step: the weapon
	/// template's own piece replaces the mount component's (<c>HercPiece_ReplacePieces</c>,
	/// <c>0040d240</c>), keeping the chassis piece's parent, with its first internal pointed at the
	/// fit slot's own, and that internal takes the template's maximum
	/// (<c>HercPiece_SetInternalMaxima</c>, <c>0040d128</c>). Every retail <c>.DMG</c> ships the ten
	/// mount pieces as placeholders — armour 1, no internals — so this is where a mount's armour comes
	/// from. See docs/retail/formats/dmg-damage-file.md#a-fitted-weapon-replaces-its-mounts-piece.
	/// </summary>
	/// <param name="component">The mount's component, its fit slot plus 19.</param>
	/// <param name="weapon">The template's piece, <see cref="Weapons.WeaponMountTemplate.Piece"/>.</param>
	/// <param name="fitSlot">The hardpoint's fit slot, <c>.GL +0x17</c>.</param>
	/// <param name="internalMaximum">The template's <see cref="Weapons.WeaponMountTemplate.InternalMaximum"/>.</param>
	internal void FitWeapon(int component, HercSimDamage.HercPiece weapon, int fitSlot, short internalMaximum) {
		if (component < 0 || component >= _pieces.Length) {
			return;
		}

		short internalSlot = (short)(HercInternals.FirstWeaponMountId + fitSlot);
		var internals = (weapon.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>())
			.Select((entry, i) => new HercSimDamage.InternalsTarget {
				SpillWeight = entry.SpillWeight,
				InternalsId = i == 0 ? HercInternals.GetById(internalSlot) : entry.InternalsId,
			})
			.ToArray();

		_pieces[component] = new HercSimDamage.HercPiece {
			Armor = weapon.Armor,
			DebrisFlags = weapon.DebrisFlags,
			ParentComponent = _pieces[component].ParentComponent,
			DestructionFlags = weapon.DestructionFlags,
			MappedInternals = internals,
		};

		if (internalSlot < _dependentMaxima.Length) {
			_dependentMaxima[internalSlot] = internalMaximum;
		}
	}

	/// <summary>
	/// <c>Mech_ConfigureLoadout</c>'s occupancy pass, after the mounts are fitted: every component of
	/// the first 29 whose piece lists no internal has its active flag cleared, so it is never struck,
	/// targeted or written to. That takes out every empty hardpoint, and on retail data the
	/// shoulders and every chassis' unused slots. See
	/// docs/retail/simulation/component-damage.md#the-component-damage-system.
	/// </summary>
	internal void ClearUnoccupied() {
		for (int i = 0; i < MechComponentCount && i < _active.Length && i < _pieces.Length; i++) {
			if ((_pieces[i].MappedInternals?.Length ?? 0) == 0) {
				_active[i] = false;
			}
		}
	}

	/// <summary>
	/// <c>Component_ReadDamagePercent</c> (<c>0040dbc0</c>) — one component's accumulated damage as a
	/// Q8 fraction, <b>0 pristine and 256 destroyed</b>. Not health — reading it as health inverts
	/// the meaning of every caller.
	///
	/// <para>It is an aggregate. The component's own damage and maximum are only the first term; the
	/// reading then folds in every dependent the component's record lists, each with its own damage
	/// and its own maximum, before dividing. So a leg reads as its own armour <i>plus</i> the servo
	/// inside it, which is why a component can show damage no shot ever put on it directly.</para>
	/// </summary>
	public int DamagePercent(int index) {
		if (Piece(index) is not { } piece) {
			return 0;
		}

		int maximum = piece.Armor;
		int taken = _damage[index] < 0 ? maximum : _damage[index];

		foreach (var dependent in piece.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>()) {
			int slot = dependent.InternalsId?.Id ?? -1;
			if (slot < 0 || slot >= _dependentDamage.Length) {
				continue;
			}

			short dependentMax = DependentMaximum(slot);
			taken += _dependentDamage[slot] < 0 ? dependentMax : _dependentDamage[slot];
			maximum += dependentMax;
		}

		// The original divides unguarded; a zero total is only reachable on hand-edited data, and
		// pristine is the honest answer for a component with nothing to lose.
		return maximum == 0 ? 0 : (taken << 8) / maximum;
	}

	/// <summary>
	/// <c>Component_TotalArmor</c> (<c>0040dc58</c>) — everything a component has to lose: its own armour plus the maximum of
	/// every internal mapped onto it. It is <see cref="DamagePercent"/>'s denominator, exposed
	/// because the impact roll scales its damage by it — see
	/// <see cref="MechObject.SpreadImpactDamage"/>, which is what makes a heavy component take a
	/// heavier share of the same fall.
	/// </summary>
	public int TotalArmor(int index) {
		if (Piece(index) is not { } piece) {
			return 0;
		}

		int maximum = piece.Armor;
		foreach (var dependent in piece.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>()) {
			int slot = dependent.InternalsId?.Id ?? -1;
			if (slot >= 0 && slot < _dependentDamage.Length) {
				maximum += DependentMaximum(slot);
			}
		}

		return maximum;
	}

	/// <summary>
	/// <c>Component_ReadOverallDamage</c> (<c>0040db2c</c>) - the whole machine's damage as one Q8 fraction, 0 pristine and 256
	/// destroyed. It is the object's vtable <c>+0x40</c> for a HERC (<c>Mech_GetOverallDamage</c>, <c>00415504</c>), and it is
	/// what the MFD status screen's structural-integrity readout prints.
	///
	/// <para>Every main component and every dependent slot is weighed once, each against its own
	/// maximum, and a destroyed one counts as its full maximum. Unlike <see cref="DamagePercent"/> a
	/// dependent is <b>not</b> also folded into its parent here, so nothing is double-counted.</para>
	/// </summary>
	public int OverallDamage {
		get {
			int taken = 0;
			int maximum = 0;

			for (int i = 0; i < _damage.Length; i++) {
				int armor = Piece(i)?.Armor ?? 0;
				taken += _damage[i] < 0 ? armor : _damage[i];
				maximum += armor;
			}

			for (int slot = 0; slot < _dependentDamage.Length; slot++) {
				short dependentMax = DependentMaximum(slot);
				taken += _dependentDamage[slot] < 0 ? dependentMax : _dependentDamage[slot];
				maximum += dependentMax;
			}

			return maximum == 0 ? 0 : (taken << 8) / maximum;
		}
	}

	/// <summary>
	/// One dependent sub-piece's damage on its own, as a Q8 fraction — <b>not</b> the aggregate
	/// <see cref="DamagePercent"/> computes. <c>Mech_ComponentDamageWrite</c> spells this reading out
	/// inline, by literal slot, four or six times over: the leg servos at 0 and 1 (plus 10 and 11 on
	/// a four-legged chassis), the shield generator at 4, the reactor at 5, and life support and the
	/// pilot at 8 and 9. A destroyed entry reads as a full 256.
	/// </summary>
	public int DependentPercent(int slot) {
		if (slot < 0 || slot >= _dependentDamage.Length) {
			return 0;
		}

		short maximum = DependentMaximum(slot);
		if (_dependentDamage[slot] < 0 || maximum == 0) {
			return _dependentDamage[slot] < 0 ? 0x100 : 0;
		}

		return (_dependentDamage[slot] << 8) / maximum;
	}

	/// <summary>
	/// <c>Component_FillDamageReadouts</c> (<c>004151a4</c>) — the display buffer every damage screen
	/// reads, in one pass, because that is how the original hands it to them: a flat array of Q8
	/// fractions the screens then index by fixed offset rather than calling back into the model.
	///
	/// <list type="bullet">
	/// <item><see cref="FirstArmorReadout"/>..+18: each of the first 19 main components against its
	/// own armour alone — <b>not</b> <see cref="DamagePercent"/>, which also folds in that
	/// component's dependents.</item>
	/// <item><see cref="FirstDependentReadout"/>..+11: the twelve dependents on their own, the same
	/// reading <see cref="DependentPercent"/> gives.</item>
	/// <item><see cref="FirstCombinedReadout"/>..+9: components 19-28 — the weapon mounts — each
	/// weighed together with the dependent at the matching offset, which is dependent 12 upward.
	/// Pairing is positional here, not through the component's own dependent list.</item>
	/// </list>
	///
	/// <para>Entry 0 is the type record's <c>+0x6c</c> (<c>Mech_ReadDamageReadouts</c>,
	/// <c>0041b4ec</c>) and stays zero: no screen's paint reads it.</para>
	/// </summary>
	public short[] ReadDamageReadouts() {
		var readouts = new short[ReadoutCount];

		for (int i = 0; i < ArmorReadoutCount; i++) {
			short armor = Piece(i)?.Armor ?? 0;
			readouts[FirstArmorReadout + i] = i >= _damage.Length ? (short)0
				: _damage[i] < 0 ? (short)0x100
				: armor == 0 ? (short)0
				: (short)((_damage[i] << 8) / armor);
		}

		for (int slot = 0; slot < DependentReadoutCount; slot++) {
			readouts[FirstDependentReadout + slot] = (short)DependentPercent(slot);
		}

		for (int i = 0; i < CombinedReadoutCount; i++) {
			int component = ArmorReadoutCount + i;
			int slot = DependentReadoutCount + i;
			int armor = Piece(component)?.Armor ?? 0;
			short dependentMax = DependentMaximum(slot);
			int total = armor + dependentMax;
			if (total == 0 || component >= _damage.Length || slot >= _dependentDamage.Length) {
				continue;
			}

			int taken = (_damage[component] < 0 ? armor : _damage[component])
				+ (_dependentDamage[slot] < 0 ? dependentMax : _dependentDamage[slot]);
			readouts[FirstCombinedReadout + i] = (short)((taken << 8) / total);
		}

		return readouts;
	}

	/// <summary>Entries in the buffer <see cref="ReadDamageReadouts"/> fills — the original's own 42.</summary>
	public const int ReadoutCount = 42;

	/// <summary>Where the 19 per-armour component readings start.</summary>
	public const int FirstArmorReadout = 1;

	/// <inheritdoc cref="FirstArmorReadout"/>
	public const int ArmorReadoutCount = 19;

	/// <summary>Where the 12 standalone dependent readings start.</summary>
	public const int FirstDependentReadout = 20;

	/// <inheritdoc cref="FirstDependentReadout"/>
	public const int DependentReadoutCount = 12;

	/// <summary>Where the 10 combined weapon-mount readings start.</summary>
	public const int FirstCombinedReadout = 32;

	/// <inheritdoc cref="FirstCombinedReadout"/>
	public const int CombinedReadoutCount = 10;

	/// <summary>
	/// <c>Mech_WeightedArmorRemaining</c> (<c>0041537c</c>) — what is left of the machine, as a weighted sum
	/// that a wreck's salvage value is scaled from (<see cref="MechObject.SalvageValue"/>). The first 19 main
	/// components each add <c>(armour - damage) * weight / armour</c> against <see cref="ComponentSalvageWeights"/>,
	/// and the first 12 dependents the same against their own maxima and <see cref="DependentSalvageWeights"/>.
	///
	/// <para>A component counts only while it is active and its raw damage is 0 to 149 — so anything worn past
	/// 150 points is worth nothing, however much armour it had — and a dependent only while its maximum is not
	/// zero. The original divides by a main component's armour unguarded; a zero here is skipped.</para>
	/// </summary>
	public int WeightedArmorRemaining() {
		int total = 0;

		for (int i = 0; i < ComponentSalvageWeights.Length && i < _damage.Length; i++) {
			int armor = Piece(i)?.Armor ?? 0;
			if (_active[i] && _damage[i] >= 0 && _damage[i] < SalvageDamageLimit && armor != 0) {
				total += (armor - _damage[i]) * ComponentSalvageWeights[i] / armor;
			}
		}

		for (int slot = 0; slot < DependentSalvageWeights.Length && slot < _dependentDamage.Length; slot++) {
			int maximum = DependentMaximum(slot);
			if (maximum != 0 && _dependentDamage[slot] >= 0 && _dependentDamage[slot] < SalvageDamageLimit) {
				total += (maximum - _dependentDamage[slot]) * DependentSalvageWeights[slot] / maximum;
			}
		}

		return total;
	}

	/// <summary>The raw damage at which a component stops counting towards <see cref="WeightedArmorRemaining"/> — the literal <c>0x96</c>.</summary>
	private const short SalvageDamageLimit = 0x96;

	/// <summary>The weights of the first 19 main components, <c>00499fe0</c>.</summary>
	private static readonly short[] ComponentSalvageWeights = {
		3000, 3000, 1000, 1000, 2000, 2000, 1000, 2000, 2000, 1000, 1000, 500, 500, 2000, 2000, 1000, 1000, 500, 500,
	};

	/// <summary>The weights of the first 12 dependents, <c>0049a006</c>, straight after the components'.</summary>
	private static readonly short[] DependentSalvageWeights = {
		1500, 1500, 0, 0, 1000, 3000, 1000, 1000, 1000, 0, 1500, 1500,
	};

	/// <summary>
	/// <c>Component_IsFullyDestroyed</c> (<c>0040d9f8</c>) — whether a main component is destroyed <b>and</b> every dependent under it
	/// is too (<c>Component_AllDependentsDestroyed</c>, <c>0040cf10</c>). It is the stricter of the two "is this gone" questions, and the
	/// one the mech's death test asks of its two cockpit sections.
	/// </summary>
	public bool FullyDestroyed(int index) {
		if (Piece(index) is not { } piece || index >= _damage.Length || _damage[index] >= 0) {
			return false;
		}

		foreach (var dependent in piece.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>()) {
			int slot = dependent.InternalsId?.Id ?? -1;
			if (slot >= 0 && slot < _dependentDamage.Length && _dependentDamage[slot] >= 0) {
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// <c>Component_ApplyDamageAndCascade</c> (<c>0040da38</c>) — the write half, and the endpoint
	/// both damage pathways converge on.
	///
	/// <list type="number">
	/// <item>Add the damage to the component. Under its maximum, that is the whole of it.</item>
	/// <item>At or over, the component is destroyed (<see cref="Destroyed"/>) and the
	/// <i>excess</i> carries on into its dependents — see <see cref="SpillIntoDependents"/>.</item>
	/// <item>If the spill ran out of live dependents <b>and</b> the record's destruction flags say so,
	/// the component cascades: it comes apart, and so does everything mounted on it.</item>
	/// </list>
	///
	/// <para><b>The active flag is not this function's gate.</b> It gates
	/// <see cref="DestroyAndCascade"/> and each pathway's own entry point
	/// (<c>Mech_ComponentDamageWrite</c> tests it and returns before reaching here); the write itself
	/// runs on an inactive slot. That distinction is what lets the weapon-mount destruction roll
	/// clear the slot's flag and <i>then</i> finish the component off with a flat 10000 without the
	/// bone group coming apart with it — see <see cref="Deactivate"/>.</para>
	/// </summary>
	/// <param name="index">Which component.</param>
	/// <param name="damage">How much.</param>
	/// <param name="world">
	/// The world the destruction's own effects go into, or null to run the damage model alone. The
	/// original has no such option — the effects come off globals — but a component can be written
	/// off from places that have no world to hand, and the arithmetic is the same either way.
	/// </param>
	/// <param name="owner">What the components belong to, which is what a fire burns on.</param>
	/// <param name="debris">
	/// The debris table to install as the alternate database for everything this cascade throws —
	/// the owner's own. See <see cref="DebrisCatalog"/>.
	/// </param>
	/// <returns>Whether the component cascaded — which is what a caller reads as "this thing is gone".</returns>
	public bool ApplyDamage(int index, short damage, SimWorld? world = null, SimObject? owner = null,
			DebrisDatabase? debris = null) {
		if (Piece(index) is not { } piece) {
			return false;
		}

		if (!AddDamage(ref _damage[index], piece.Armor, ref damage)) {
			return false;
		}

		if (!SpillIntoDependents(piece, damage) || (piece.DestructionFlags & 1) == 0) {
			return false;
		}

		// One destruction event, one big explosion: the latch is cleared here, at the head of the
		// group, and the first component in it whose flags claim the large effect sets it and
		// silences every effect the rest of the group would have spawned. See DestroyAndCascade.
		_majorEffectFired = false;

		DestroyAndCascade(index, world, owner, debris);

		// The original queues the bone-group siblings on a shared list and drains it here rather
		// than recursing, so a cycle in the data cannot blow the stack. Same shape, own list.
		while (_pendingCascade.Count > 0) {
			int next = _pendingCascade[^1];
			_pendingCascade.RemoveAt(_pendingCascade.Count - 1);
			DestroyAndCascade(next, world, owner, debris);
		}

		return true;
	}

	/// <summary>
	/// <c>DAT_004a98b0</c> — the "the big one has already gone off" latch, cleared at the head of a
	/// cascade group and set by the first component in it whose
	/// <see cref="DestructionFlagMajorEffect"/> is up. While it is set every remaining component in
	/// the group destroys silently: no fire, no explosion. It is what stops a machine that loses
	/// eight parts at once from spawning eight explosions.
	/// </summary>
	private bool _majorEffectFired;

	/// <summary>
	/// <c>Component_DestroyAndCascade</c> (<c>0040d434</c>) — takes one component out: the entry goes
	/// to <see cref="Destroyed"/>, the active flag clears, everything under it is finished off, and
	/// every <i>other</i> component whose record names this one as its parent is queued to follow.
	///
	/// <para><b>The <c>.DMG</c> piece's <c>ParentComponent</c> byte is a component index</b>, not a model bone: the
	/// original compares it against the index of the component that just died. That is the whole of
	/// the dependency graph between main components — losing a shoulder takes the arm on it.</para>
	///
	/// <para><b>It is also where a part coming off is made visible</b>, and the record's own
	/// <c>DebrisFlags</c> pair drives all of it. The high byte
	/// (<see cref="SubShapeSequenceOf"/>) is the shape cell-animation this component drives, stepped
	/// to its blank third cell so the part stops being drawn; a component that drives one also
	/// catches fire, of one of two kinds, on its <c>DestructionFlags</c>. The low byte
	/// (<see cref="DebrisGroupOf"/>) is the wreckage the part throws — or
	/// <see cref="DefaultDebrisGroup"/> when it names none.</para>
	///
	/// <para>The blank cell is stepped to in <see cref="CellFrames"/>, which the renderer reads to
	/// pick which cell of each sequence is on screen. See docs/retail/rendering/mech-shape-drawing.md.</para>
	/// </summary>
	private void DestroyAndCascade(int index, SimWorld? world, SimObject? owner,
			DebrisDatabase? debris) {
		if (Piece(index) is not { } piece || !IsActive(index)) {
			return;
		}

		_damage[index] = Destroyed;
		_active[index] = false;

		// Whatever is left under it goes with it. The original's literal 32000 is simply more than
		// any dependent's maximum.
		SpillIntoDependents(piece, FinishOffDependents);

		DestructionEffects(index, piece, world, owner, debris);

		// The field is a signed byte in the original and 0xff means "no parent", so it is read as one
		// here: as an unsigned byte the sentinel would simply never match, which is the right answer
		// by accident rather than the right comparison.
		for (int i = 0; i < _active.Length && i < _pieces.Length; i++) {
			if (_active[i] && (sbyte)_pieces[i].ParentComponent == index) {
				_pendingCascade.Add(i);
			}
		}
	}

	/// <summary>
	/// The visual half of <c>Component_DestroyAndCascade</c> — everything a part coming off puts on
	/// screen, in the original's own order.
	///
	/// <list type="number">
	/// <item><b>Fire</b>, but only for a component that drives a shape sequence — the same byte, and
	/// the original's own gate. Which fire is the destruction flags: <see cref="DestructionFlagWholeObjectFire"/> puts out every fire already
	/// burning on the object and lights <see cref="FireEffect.WholeObjectShape"/> in their place —
	/// this is the machine going up as a whole — while
	/// <see cref="DestructionFlagComponentFire"/> simply adds
	/// <see cref="FireEffect.ComponentShape"/> to whatever is already alight.</item>
	/// <item><b>The part stops being drawn</b>, by stepping that sequence to its blank cell.</item>
	/// <item><b>An explosion</b> at the component, of one of two types:
	/// <see cref="ComponentExplosion"/> normally, or <see cref="MajorExplosion"/> for a component
	/// whose flags claim the big one — which also latches every remaining effect in this cascade
	/// group off.</item>
	/// <item><b>Debris</b>, the group the record's low byte names, thrown from the component's own
	/// position; a component that names none throws <see cref="DefaultDebrisGroup"/>.</item>
	/// </list>
	///
	/// <para>The explosion and the debris are thrown from where the component <i>is</i> — its
	/// collision cluster resolved through the machine's posed node, the same point a blast measures
	/// its falloff from. A component the <c>.COL</c> never names has no such point and falls back to
	/// the machine's own origin, which is at its feet.</para>
	/// </summary>
	private void DestructionEffects(int index, HercSimDamage.HercPiece piece, SimWorld? world,
			SimObject? owner, DebrisDatabase? debris) {
		if (world == null || owner == null) {
			return;
		}

		var mech = owner as MechObject;
		var point = mech?.ComponentWorldPosition((short)index) ?? owner.Position;
		int sequence = SubShapeSequenceOf(piece.DebrisFlags);

		if (sequence >= 0) {
			if (!_majorEffectFired) {
				if ((piece.DestructionFlags & DestructionFlagWholeObjectFire) != 0) {
					world.Effects.ReleaseFires(owner);
					world.Effects.SpawnFire(owner, (short)index, default, FireEffect.WholeObjectShape);
				} else if ((piece.DestructionFlags & DestructionFlagComponentFire) != 0) {
					world.Effects.SpawnFire(owner, (short)index, default, FireEffect.ComponentShape);
				}
			}

			CellFrames[sequence] = DestroyedCell;
		}

		if (!_majorEffectFired) {
			short type = ComponentExplosion;
			if ((piece.DestructionFlags & DestructionFlagMajorEffect) != 0) {
				_majorEffectFired = true;
				type = MajorExplosion;
			}

			world.Effects.SpawnImpactEffect(type, point, owner);
		}

		short group = DebrisGroupOf(piece.DebrisFlags);
		world.Effects.SpawnDebris(group < 0 ? DefaultDebrisGroup : group,
			mech?.ComponentThrowFrame((short)index) ?? owner.WorldFrame, debris);
	}

	/// <summary>
	/// The <c>.DMG</c> record's <c>+0x02</c> byte, as a signed value — which debris group this
	/// component throws, or <c>-1</c> for none. <c>HercSimDamage</c> reads <c>+0x02</c> and
	/// <c>+0x03</c> as one <c>short</c>; the original reads them as two independent signed bytes.
	/// </summary>
	public static short DebrisGroupOf(short debrisFlags) => (sbyte)(debrisFlags & 0xff);

	/// <summary>
	/// And the record's <c>+0x03</c> byte — which <c>TSCellAnimPart</c> sequence of the machine's
	/// shape this component drives, <c>-1</c> for a component with no geometry of its own. It is the
	/// gate on the fire as well as the map to the blank cell; see
	/// docs/retail/rendering/mech-shape-drawing.md.
	/// </summary>
	public static short SubShapeSequenceOf(short debrisFlags) => (sbyte)((debrisFlags >> 8) & 0xff);

	/// <summary>
	/// The machine's shape instance's per-sequence cell-frame array, which is what a destroyed
	/// component's geometry disappears through — see <see cref="ShapeCellFrames"/>.
	/// </summary>
	public ShapeCellFrames CellFrames { get; } = new();

	/// <summary>
	/// The cell a destroyed component's sequence is stepped to — the original's literal
	/// <c>shapeInstance[+8][record+3] = 2</c>. Every body part of every retail chassis is a
	/// three-cell flipbook whose third cell is a <c>TSPoly</c> that draws nothing, so arriving there
	/// is how the part comes off.
	/// </summary>
	public const short DestroyedCell = 2;

	/// <summary><see cref="DestructionFlags"/> bit 1 — the whole object goes up, and its other fires go out.</summary>
	public const int DestructionFlagWholeObjectFire = 0x2;

	/// <summary><see cref="DestructionFlags"/> bit 3 — this part alone catches fire.</summary>
	public const int DestructionFlagComponentFire = 0x8;

	/// <summary>
	/// <see cref="DestructionFlags"/> bit 2 — this component's loss is the big explosion, and the one
	/// that silences the rest of its cascade group.
	/// </summary>
	public const int DestructionFlagMajorEffect = 0x4;

	/// <summary>The <c>EXPLOS.DAT</c> type an ordinary component's loss spawns — the literal 10.</summary>
	public const short ComponentExplosion = 10;

	/// <summary>And the one a <see cref="DestructionFlagMajorEffect"/> component spawns — the literal 0x11.</summary>
	public const short MajorExplosion = 0x11;

	/// <summary>
	/// The debris group a component that names none throws — the literal 2, which is
	/// <c>DEF_DEB</c>'s and therefore the same three shapes off any machine.
	/// </summary>
	public const short DefaultDebrisGroup = 2;

	/// <summary>
	/// <c>Component_SpillIntoDependents</c> (<c>0040cf44</c>) — pours a component's overflow damage into its dependents, one at a time.
	///
	/// <para>The pick is <b>weighted and random</b>: each live dependent contributes the record's own
	/// per-dependent figure (<see cref="HercSimDamage.InternalsTarget.SpillWeight"/>, 20 on most mech
	/// pieces and 150 on the skimmer's one) to a total, a number is drawn under that total, and the
	/// entry whose band it lands in takes the hit. If that spill destroys the dependent, its own
	/// excess goes round again — so one large hit can strip several internals in sequence.</para>
	/// </summary>
	/// <returns>
	/// Whether the component has no live dependents left. It is that answer, not the damage, that the
	/// caller turns into a cascade — a component only comes apart once everything inside it has.
	/// </returns>
	private bool SpillIntoDependents(HercSimDamage.HercPiece piece, short damage) {
		var dependents = piece.MappedInternals ?? Array.Empty<HercSimDamage.InternalsTarget>();

		while (true) {
			int total = 0;
			foreach (var dependent in dependents) {
				int slot = dependent.InternalsId?.Id ?? -1;
				if (slot >= 0 && slot < _dependentDamage.Length && _dependentDamage[slot] >= 0) {
					total += dependent.SpillWeight;
				}
			}

			if (total == 0) {
				return true;
			}

			// The original walks the list subtracting each live entry's weight from the draw and stops
			// on the first that takes it below zero, falling off the end onto the last entry when the
			// weights do not cover the draw. Reproduced, landing included.
			int draw = _random.NextBelow((short)total);
			int chosen = -1;
			foreach (var dependent in dependents) {
				int slot = dependent.InternalsId?.Id ?? -1;
				chosen = slot;
				if (slot < 0 || slot >= _dependentDamage.Length || _dependentDamage[slot] < 0) {
					continue;
				}

				draw -= dependent.SpillWeight;
				if (draw < 0) {
					break;
				}
			}

			if (chosen < 0 || chosen >= _dependentDamage.Length) {
				return false;
			}

			if (!AddDamage(ref _dependentDamage[chosen], DependentMaximum(chosen), ref damage)) {
				return false;
			}
		}
	}

	/// <summary>
	/// <c>Component_AddDamage</c> (<c>0040d3ec</c>) — adds damage to one entry, capping it at the maximum.
	///
	/// <para>Two things it does that a plain accumulate would not. An entry already holding
	/// <see cref="Destroyed"/> absorbs nothing at all, so a lost part cannot be shot again. And an
	/// entry the damage finishes stores the sentinel rather than the maximum and <b>writes the excess
	/// back into <paramref name="damage"/></b>, which is the mechanism the whole spill is built
	/// on.</para>
	/// </summary>
	/// <returns>Whether the entry is now destroyed.</returns>
	private static bool AddDamage(ref short entry, short maximum, ref short damage) {
		if (entry < 0) {
			return true;
		}

		int total = entry + damage;
		if (total < maximum) {
			entry = (short)total;
		} else {
			damage = (short)(total - maximum);
			entry = Destroyed;
		}

		return entry < 0;
	}

	/// <summary>
	/// Writes a dependent's accumulated damage directly, bypassing the cascade and the death gate.
	/// The one caller is <see cref="MechObject.ApplyStartingCondition"/>, which needs to state a
	/// machine's condition as it is spawned rather than damage it into that condition — the original
	/// writes the array element in exactly the same way, for the same reason.
	/// </summary>
	internal void SetDependentDamage(int slot, short damage) {
		if (slot >= 0 && slot < _dependentDamage.Length) {
			_dependentDamage[slot] = damage;
		}
	}

	/// <summary>
	/// <c>Mech_ApplySquadCondition</c> (<c>00415068</c>) — writes a squad machine's carried-over
	/// condition straight into the main and dependent arrays, each entry becoming
	/// <c>(100 - condition) * maximum / 100</c>. Which entry comes from which span, and what is
	/// zeroed, is docs/retail/simulation/component-damage.md#a-squad-machines-condition--mech_applysquadcondition-00415068.
	///
	/// <para>Like <see cref="SetDependentDamage"/> it states the condition rather than damaging the
	/// machine into it: no cascade, no death gate, no announcement, and the active flags are left
	/// alone.</para>
	/// </summary>
	internal void ApplySquadCondition(World.SquadCondition condition) {
		// Components 0-18: the 13 external facets against each piece's own armour, the rest zeroed.
		for (int i = 0; i < SquadArmorComponents; i++) {
			short value = i < SquadExternalFacets
				? Share(Entry(condition.External, i), Piece(i)?.Armor ?? 0)
				: (short)0;
			SetComponent(i, value);
		}

		// Dependents 0-11: the first nine internal entries against each internal's maximum; the
		// pilot's slot and 10-11 are zeroed.
		for (int slot = 0; slot < SquadDependentSlots; slot++) {
			short value = slot == PilotSlot || slot >= SquadInternalEntries
				? (short)0
				: Share(Entry(condition.Internal, slot), DependentMaximum(slot));
			SetDependentDamage(slot, value);
		}

		// Components 19-28, the mounts: each hardpoint's condition against the mount's armour plus
		// the maximum of dependent 12 + n beside it, the excess past the armour going to that
		// dependent.
		for (int n = 0; n < SquadMountComponents; n++) {
			int component = SquadArmorComponents + n;
			int slot = SquadDependentSlots + n;
			short armor = Piece(component)?.Armor ?? 0;
			short dependentMax = DependentMaximum(slot);
			short taken = Share(Entry(condition.Hardpoint, n), (short)(dependentMax + armor));
			if (armor < taken) {
				SetComponent(component, Destroyed);
				SetDependentDamage(slot, dependentMax < (short)(taken - armor) ? Destroyed : (short)(taken - armor));
			} else {
				SetComponent(component, taken);
			}
		}
	}

	/// <summary>
	/// One entry's share of a maximum — the original's
	/// <c>(short)(((100 - condition) * maximum) / 100)</c>, truncating toward zero as C does.
	/// </summary>
	private static short Share(short condition, short maximum) => (short)((100 - condition) * maximum / 100);

	/// <summary>
	/// One condition entry. Each span is a fixed size in <c>player.mec</c>, so a missing entry is
	/// only reachable on a hand-built <see cref="World.SquadCondition"/>; it reads as pristine.
	/// </summary>
	private static short Entry(IReadOnlyList<short> span, int index) =>
		index < span.Count ? span[index] : (short)100;

	private void SetComponent(int index, short damage) {
		if (index >= 0 && index < _damage.Length) {
			_damage[index] = damage;
		}
	}

	/// <summary><c>Mech_ApplySquadCondition</c>'s three loop bounds: 19 components, 12 dependents, 10 mounts.</summary>
	private const int SquadArmorComponents = 19;

	/// <inheritdoc cref="SquadArmorComponents"/>
	private const int SquadDependentSlots = 12;

	/// <inheritdoc cref="SquadArmorComponents"/>
	private const int SquadMountComponents = 10;

	/// <summary>How many of the 19 components read a condition — the external span's 13 facets.</summary>
	private const int SquadExternalFacets = 13;

	/// <summary>How many of the 12 dependents read one — the original's <c>&lt; 10</c>, less the pilot.</summary>
	private const int SquadInternalEntries = 10;

	/// <summary>The pilot's dependent slot, which the condition zeroes rather than reads.</summary>
	private const int PilotSlot = 9;

	/// <summary>The most damage a dependent can take before it reads destroyed.</summary>
	internal short DependentMax(int slot) => DependentMaximum(slot);

	/// <summary>
	/// One dependent's maximum, out of the <c>.DMG</c>'s flat leading array, or the fitted weapon's
	/// for a mount's internal (<see cref="FitWeapon"/>). Every retail HERC states 22 of these and the
	/// skimmer one, matching the slot counts the constructors allocate.
	/// </summary>
	private short DependentMaximum(int slot) =>
		slot >= 0 && slot < _dependentMaxima.Length ? _dependentMaxima[slot] : (short)0;
}
