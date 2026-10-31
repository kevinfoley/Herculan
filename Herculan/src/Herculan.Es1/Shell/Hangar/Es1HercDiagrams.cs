using System.Globalization;
using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dat.Shell;
using Herculan.Es1.Panels;
using Herculan.Es1.Shell.Menus;

namespace Herculan.Es1.Shell.Hangar;

/// <summary>
/// The parts of a HERC the HERC's file keeps for its diagram (docs/es1/formats/herc-diagrams.md#the-hercs-diagram): the
/// current mount <c>+0x70</c>, the body <c>+0x72</c>, which <c>Herc_Ctor</c> makes and <c>Herc_Dtor</c> destroys, and
/// what <see cref="Es1HercDiagrams.Build"/> makes and <see cref="Es1HercDiagrams.Free"/> frees.
/// </summary>
public sealed class Es1HercDiagram {
	/// <summary><c>+0x70</c>, the current mount, −1 for none.</summary>
	public short CurrentMount { get; set; } = -1;

	/// <summary><c>+0x72</c>, the body.</summary>
	public Es1Sprite Body { get; } = new();

	/// <summary><c>+0x94</c>, a sprite per mount: the mount's outline; null when not built.</summary>
	public Es1Sprite[]? Outlines { get; set; }

	/// <summary><c>+0x98</c>, on chassis 7 the piece over the shoulders; null on the others and when not built.</summary>
	public Es1Sprite? ColossusTop { get; set; }

	/// <summary><c>+0x9c</c>, the chassis' <c>WEP1</c> bank as the build took it.</summary>
	public IReadOnlyList<HercWorks.Core.Data.File.Dyn.DynamixBitmap>? WeaponBank { get; set; }

	/// <summary><c>+0xa0</c>, the chassis' <c>OUTL</c> bank as the build took it.</summary>
	public IReadOnlyList<HercWorks.Core.Data.File.Dyn.DynamixBitmap>? OutlineBank { get; set; }

	/// <summary><c>+0xa4</c>, a 38-byte mount picture per mount; null when not built.</summary>
	public Es1MountPicture[]? Mounts { get; set; }

	/// <summary>Whether a build has run since the HERC's state was made, which <see cref="Es1HercDiagrams.Of"/> reads.</summary>
	internal bool Built { get; set; }
}

/// <summary>A mount picture (<c>MountPicture_Ctor</c>, <c>00023d70</c>): the layout record at <c>+0</c> and the weapon's picture, a sprite, at <c>+4</c>.</summary>
public sealed class Es1MountPicture {
	/// <summary><c>+0</c>, the layout record <c>HercLayout_Find</c> gave.</summary>
	public Es1HercLayoutItem? Record { get; set; }

	/// <summary><c>+4</c>, the weapon's picture.</summary>
	public Es1Sprite Sprite { get; } = new();
}

/// <summary>
/// The HERC's file's functions on a HERC's diagram (docs/es1/formats/herc-diagrams.md): the build and the free, the body,
/// the mount pictures, the draws and erases, the mount highlight, the current mount's setters and getters and the status
/// lines, and the two bytes <c>Herc_MountHighlightShown</c> (<c>0007ff98</c>) and <c>Herc_ColossusTopShown</c>
/// (<c>0007ff8e</c>), clear in the image. Each HERC's diagram is kept against the HERC's record; that keeping, and the
/// build <see cref="Of"/> makes the first time, are this engine's, every function retail's.
/// </summary>
public sealed class Es1HercDiagrams {
	/// <summary>The status kinds of <c>Herc_GetStatus</c>: internal words, external areas, mounts; <c>0x781</c> lists the internal words as <c>0x784</c>.</summary>
	public const short InternalLines = 0x781, Internal = 0x784, External = 0x785, MountKind = 0x786;

	/// <summary>The condition below which a mount shows its weapon's damaged picture, <c>0x33</c>.</summary>
	private const short DamagedBelow = 0x33;

	/// <summary>The <c>WEP1</c> frame of the Colossus' shoulder piece, 43, and its point (<c>0x56</c>, <c>0x19</c>).</summary>
	private const int ColossusTopFrame = 43, ColossusTopX = 0x56, ColossusTopY = 0x19;

	/// <summary>The chassis that carries the shoulder piece, the Colossus.</summary>
	private const short Colossus = 7;

	/// <summary><c>Herc_BodyPositions</c> (<c>000a0398</c>), which <c>Herc_StaticInit</c> (<c>00025090</c>) fills: each chassis' body point.</summary>
	private static readonly (int X, int Y)[] BodyPositions = [(77, 99), (77, 89), (83, 64), (86, 53), (75, 57), (73, 37), (72, 37), (58, 26)];

	/// <summary><c>Herc_BuildPositions</c> (<c>000a03d8</c>), two points per chassis, which <c>Herc_StaticInit</c> fills: the <c>BILD</c> frames' points, chassis 2 to 7.</summary>
	private static readonly (int X, int Y)[][] BuildPositions = [
		[(0, 0), (0, 0)], [(0, 0), (0, 0)], [(84, 97), (0, 0)], [(86, 94), (0, 0)], [(84, 92), (0, 0)], [(78, 50), (78, 85)],
		[(78, 51), (78, 91)], [(74, 41), (74, 82)],
	];

	/// <summary>The lines of <c>eng\watext.bin</c> the internal words' and the external areas' lists name, the word lists <c>0007ff6c</c> and <c>0007ff80</c>.</summary>
	private static readonly short[] InternalLineNumbers = [0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88],
		ExternalLineNumbers = [0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e];

	/// <summary>The line an empty mount's status line copies, <c>0xa0</c>, and the first weapon's, <c>0x8f</c>.</summary>
	private const short EmptyMountLine = 0xa0, FirstWeaponLine = 0x8f;

	private readonly ConditionalWeakTable<Es1HercRecord, Es1HercDiagram> _diagrams = new();

	public Es1HercDiagrams(Es1HercArt art) {
		Art = art;
	}

	/// <summary>The tables and banks the diagrams are built from.</summary>
	public Es1HercArt Art { get; }

	/// <summary><c>Herc_MountHighlightShown</c> (<c>0007ff98</c>): the current mount's outline is drawn.</summary>
	public bool MountHighlightShown { get; set; }

	/// <summary><c>Herc_ColossusTopShown</c> (<c>0007ff8e</c>): the Colossus' shoulder piece is drawn; no build clears it.</summary>
	public bool ColossusTopShown { get; set; }

	/// <summary>
	/// The diagram of <paramref name="herc"/>, the HERC in <paramref name="hangar"/>'s <paramref name="slot"/>. Retail's
	/// <c>Hangar_Read</c> (<c>00028c50</c>) builds each HERC's diagram as <c>Herc_Read</c> (<c>00023501</c>) reads it,
	/// with the marked slot's word <see cref="Es1Hangar.MarkedSlotWord"/> for the HERC in the marked slot when the word
	/// is not 0 and 0 otherwise; the hangar reads here are <c>HercWorks.Core</c>'s, which keep no diagrams, so the first
	/// call for a HERC makes that build from the HERC and the hangar as they then stand, an engine reading that matches
	/// retail's while nothing changes them between the read and the first draw.
	/// </summary>
	public Es1HercDiagram Of(Es1Hangar hangar, short slot, Es1HercRecord herc) {
		var diagram = _diagrams.GetValue(herc, _ => new Es1HercDiagram());
		if (!diagram.Built) {
			Build(herc, slot == hangar.MarkedSlot ? hangar.MarkedSlotWord : (short)0);
		}

		return diagram;
	}

	/// <summary>The diagram kept for <paramref name="herc"/>, made when there is none (<c>Herc_Ctor</c>'s fields), with no build.</summary>
	private Es1HercDiagram State(Es1HercRecord herc) => _diagrams.GetValue(herc, _ => new Es1HercDiagram());

	/// <summary>
	/// <c>Herc_BuildDiagram</c> (<c>00023883</c>)(<paramref name="herc"/>, <paramref name="remaining"/>)
	/// (docs/es1/formats/herc-diagrams.md#the-hercs-diagram): the current mount −1 and <see cref="MountHighlightShown"/>
	/// cleared; the chassis' <c>WEP1</c> and <c>OUTL</c> banks taken, each loaded the first time; the body
	/// (<see cref="SetBodyPicture"/>); on chassis 7 a new shoulder piece, on the others none; with a mount count other
	/// than 0 new mount pictures and outlines; then per mount the record's changes
	/// (<see cref="Es1HercRecord.BuildDiagramMounts"/>, the empty mount and the ammunition word, which each mount takes
	/// before its pictures and which touch no other mount), the outline (<see cref="Es1HercArt.FindFrame"/> of the
	/// <c>OUTL</c> bank at <see cref="Es1HercArt.FindSecondPoint"/>), the picture (<see cref="SetMountPicture"/> with the
	/// mount's status word) and, on chassis 7 with mount 2 or 3 not empty, the shoulder piece at its point and
	/// <see cref="ColossusTopShown"/> set; last, with a shoulder piece and the byte clear, the piece set to no bitmap at
	/// (0, 0). Every sprite keeps its background in a buffer of its own.
	/// </summary>
	public void Build(Es1HercRecord herc, short remaining) {
		var diagram = State(herc);
		diagram.Built = true;
		short chassis = herc.Chassis;
		diagram.CurrentMount = -1;
		MountHighlightShown = false;
		diagram.WeaponBank = Art.Need(Es1HercArt.Bank.Weapons, chassis);
		diagram.OutlineBank = Art.Need(Es1HercArt.Bank.Outlines, chassis);
		SetBodyPicture(herc, diagram, remaining);
		diagram.ColossusTop = chassis == Colossus ? new Es1Sprite() : null;
		int count = herc.Weapons.Length;
		if (count != 0) {
			diagram.Mounts = Enumerable.Range(0, count).Select(_ => new Es1MountPicture()).ToArray();
			diagram.Outlines = Enumerable.Range(0, count).Select(_ => new Es1Sprite()).ToArray();
		}

		herc.BuildDiagramMounts();
		for (short mount = 0; mount < count; mount++) {
			short weapon = herc.Weapons[mount];
			var point = Art.FindSecondPoint(weapon, mount, chassis);
			var outline = Es1HercArt.Frame(diagram.OutlineBank, Art.FindFrame(weapon, mount, chassis));
			Element(diagram.Outlines, mount)?.Set(0, point.X, point.Y, outline);
			if (Element(diagram.Mounts, mount) is { } picture) {
				SetMountPicture(picture, herc.MountStatus(mount), weapon, mount, chassis);
			}

			if (chassis == Colossus && weapon != Es1HercArt.EmptyMount && mount is 2 or 3) {
				diagram.ColossusTop?.Set(0, ColossusTopX, ColossusTopY, Es1HercArt.Frame(diagram.WeaponBank, ColossusTopFrame));
				ColossusTopShown = true;
			}
		}

		if (diagram.ColossusTop is { } top && !ColossusTopShown) {
			top.Set(0, 0, 0, null);
		}
	}

	/// <summary>
	/// <c>Herc_FreeDiagram</c> (<c>0002330e</c>)(<paramref name="herc"/>): the mount pictures' sprites and the outlines
	/// destroyed (<see cref="Es1Sprite.Dtor"/>) and their arrays nulled, the shoulder piece destroyed and nulled. With
	/// <c>Shell_LowMemory</c> (<c>0007fd09</c>) set it also frees the chassis' banks and nulls <c>+0x9c</c> and
	/// <c>+0xa0</c>; this engine never sets that byte.
	/// </summary>
	public void Free(Es1HercRecord herc) {
		if (!_diagrams.TryGetValue(herc, out var diagram)) {
			return;
		}

		if (diagram.Mounts is { } mounts) {
			foreach (var picture in mounts) {
				picture.Sprite.Dtor();
			}

			diagram.Mounts = null;
		}

		if (diagram.Outlines is { } outlines) {
			foreach (var outline in outlines) {
				outline.Dtor();
			}

			diagram.Outlines = null;
		}

		diagram.ColossusTop?.Dtor();
		diagram.ColossusTop = null;
	}

	/// <summary>
	/// <c>Herc_Dtor</c> (<c>000232a4</c>)(<paramref name="herc"/>, flags): <see cref="Free"/>, and the body's sprite
	/// destroyed (<see cref="Es1Sprite.Dtor"/>); the HERC's own free is the collector's here.
	/// </summary>
	public void Destroy(Es1HercRecord herc) {
		Free(herc);
		if (_diagrams.TryGetValue(herc, out var diagram)) {
			diagram.Body.Dtor();
			_diagrams.Remove(herc);
		}
	}

	/// <summary>
	/// <c>Herc_SetBodyPicture</c> (<c>00023605</c>)(<paramref name="herc"/>, <paramref name="remaining"/>)
	/// (docs/es1/formats/herc-diagrams.md#the-body): the body set by the chassis and <paramref name="remaining"/>: 0, the
	/// <c>WEP1</c> frame 0 at the chassis' body point; on chassis 2 to 4 for 1, and on 5 to 7 for 1 or 2, the <c>BILD</c>
	/// frame <paramref name="remaining"/> − 1 at the chassis' building point of that frame, the bank loaded the first time;
	/// on chassis 0 and 1 for any other, on 2 to 4 for 2 and on 5 to 7 for 3, no bitmap at the body point; any other value,
	/// or a chassis past 7, leaves the body as it was.
	/// </summary>
	private void SetBodyPicture(Es1HercRecord herc, Es1HercDiagram diagram, short remaining) {
		short chassis = herc.Chassis;
		if (chassis is < 0 or >= Es1HercArt.ChassisCount) {
			return;
		}

		var body = BodyPositions[chassis];
		short none = chassis switch { <= 1 => -1, <= 4 => 2, _ => 3 };
		short building = chassis switch { <= 1 => 0, <= 4 => 1, _ => 2 };
		if (remaining == 0) {
			diagram.Body.Set(0, body.X, body.Y, Es1HercArt.Frame(diagram.WeaponBank, 0));
		} else if (remaining >= 1 && remaining <= building) {
			var bank = Art.Need(Es1HercArt.Bank.Building, chassis);
			var point = BuildPositions[chassis][remaining - 1];
			diagram.Body.Set(0, point.X, point.Y, Es1HercArt.Frame(bank, remaining - 1));
		} else if (none == -1 || remaining == none) {
			diagram.Body.Set(0, body.X, body.Y, null);
		}
	}

	/// <summary>
	/// <c>MountPicture_Set</c> (<c>00022d78</c>)(<paramref name="picture"/>, <paramref name="value"/>,
	/// <paramref name="weapon"/>, <paramref name="mount"/>, <paramref name="chassis"/>): the record <see cref="Es1HercArt.Find"/>
	/// gives stored, and the sprite set at the record's first point: for weapon 17 to no bitmap at (0, 0); for a value
	/// below <c>0x33</c> to frame (the record's frame − 1) of the chassis' <c>WEPD</c> bank, loaded the first time; at
	/// <c>0x33</c> or more to the record's frame of the <c>WEP1</c> bank, only when that bank is loaded. Retail reads
	/// through a null record for a weapon the mount's records do not hold; the sprite is left as it was here then, an
	/// engine guard.
	/// </summary>
	private void SetMountPicture(Es1MountPicture picture, short value, short weapon, short mount, short chassis) {
		picture.Record = Art.Find(weapon, mount, chassis);
		if (weapon == Es1HercArt.EmptyMount) {
			picture.Sprite.Set(0, 0, 0, null);
			return;
		}

		if (picture.Record is not { } record) {
			return;
		}

		if (value < DamagedBelow) {
			var damaged = Art.Need(Es1HercArt.Bank.DamagedWeapons, chassis);
			picture.Sprite.Set(0, record.FirstX, record.FirstY, Es1HercArt.Frame(damaged, record.Frame - 1));
		} else if (Art.Loaded(Es1HercArt.Bank.Weapons, chassis) is { } weapons) {
			picture.Sprite.Set(0, record.FirstX, record.FirstY, Es1HercArt.Frame(weapons, record.Frame));
		}
	}

	/// <summary>Element <paramref name="index"/> of <paramref name="array"/>, which retail reads with no bounds test; null for no array or an index outside it, an engine guard.</summary>
	private static T? Element<T>(T[]? array, int index) where T : class =>
		array != null && index >= 0 && index < array.Length ? array[index] : null;

	/// <summary>
	/// The mount <c>Herc_MountDrawOrder</c> names at place <paramref name="place"/> of the chassis' nine words; a place past
	/// the chassis' mounts reads the row's 0, and a chassis outside the eight −1 here, an engine guard.
	/// </summary>
	private static short OrderedMount(short chassis, int place) {
		if (chassis is < 0 or >= Es1HercArt.ChassisCount) {
			return -1;
		}

		short[] order = Es1HercArt.MountDrawOrder[chassis];
		return place < order.Length ? order[place] : (short)0;
	}

	/// <summary>
	/// <c>Herc_DrawMounts</c> (<c>00024670</c>)(<paramref name="herc"/>) (docs/es1/formats/herc-diagrams.md#drawing-the-diagram):
	/// for each place below the mount count, <i>m</i> the mount <c>Herc_MountDrawOrder</c> puts there, the mount picture's
	/// sprite drawn; then, while <see cref="MountHighlightShown"/> is set and <i>m</i> is the current mount, its outline;
	/// then, on chassis 7 with <see cref="ColossusTopShown"/> set and <i>m</i> 3, the shoulder piece; each only when its
	/// array is set.
	/// </summary>
	public void DrawMounts(Es1HercRecord herc, Es1SpriteSurface surface) {
		var diagram = State(herc);
		for (int place = 0; place < herc.Weapons.Length; place++) {
			short mount = OrderedMount(herc.Chassis, place);
			Element(diagram.Mounts, mount)?.Sprite.Draw(surface);
			if (MountHighlightShown && diagram.CurrentMount != -1 && mount == diagram.CurrentMount) {
				Element(diagram.Outlines, diagram.CurrentMount)?.Draw(surface);
			}

			if (herc.Chassis == Colossus && ColossusTopShown && mount == 3) {
				diagram.ColossusTop?.Draw(surface);
			}
		}
	}

	/// <summary>
	/// <c>Herc_EraseMounts</c> (<c>00024561</c>)(<paramref name="herc"/>): the sprites <see cref="DrawMounts"/> draws
	/// erased in the reverse order, each place's shoulder piece and outline before its picture.
	/// </summary>
	public void EraseMounts(Es1HercRecord herc, Es1SpriteSurface surface) {
		var diagram = State(herc);
		for (int place = herc.Weapons.Length - 1; place >= 0; place--) {
			short mount = OrderedMount(herc.Chassis, place);
			if (herc.Chassis == Colossus && ColossusTopShown && mount == 3) {
				diagram.ColossusTop?.Erase(surface);
			}

			if (MountHighlightShown && diagram.CurrentMount != -1 && mount == diagram.CurrentMount) {
				Element(diagram.Outlines, diagram.CurrentMount)?.Erase(surface);
			}

			Element(diagram.Mounts, mount)?.Sprite.Erase(surface);
		}
	}

	/// <summary>
	/// <c>Herc_DrawDamageMarks</c> (<c>00024366</c>)(<paramref name="herc"/>) (docs/es1/formats/herc-diagrams.md#the-damage-marks):
	/// the chassis' <c>DMG1</c> bank loaded the first time; for each group <i>g</i>, 0 to 5, the level
	/// <see cref="Es1RepairCosts.LevelForCondition"/> gives for external area <i>g</i>'s mean
	/// (<see cref="Es1HercRecord.ExternalAverage"/>), and for a level of 1 to 3 each mark of the group's list for that
	/// level blitted at its point twice: with the display's draw page <c>+0x38</c> set to page 2, then with it put back, so
	/// the marks land on the background and on the draw page. Retail reads before the lists for a negative level, and the
	/// bank's frames with no bounds test; such a level draws nothing, and a frame outside the bank is not drawn, engine
	/// guards.
	/// </summary>
	public void DrawDamageMarks(Es1HercRecord herc, Es1SpriteSurface surface) {
		short chassis = herc.Chassis;
		var bank = Art.Need(Es1HercArt.Bank.DamageMarks, chassis);
		var marks = chassis is >= 0 and < Es1HercArt.ChassisCount ? Art.DamageMarks[chassis] : null;
		var display = surface.Display;
		var canvas = new Es1PanelCanvas(display, Es1ShellScreens.FullScreen);
		for (int group = 0; group < Es1HercDamageMarks.GroupCount; group++) {
			short level = Es1RepairCosts.LevelForCondition(herc.ExternalAverage(group));
			if (level is < 1 or > Es1HercDamageMarks.LevelCount || marks == null || group >= marks.Groups.Length) {
				continue;
			}

			foreach (var mark in marks.Groups[group][level - 1]) {
				var bitmap = Es1HercArt.Frame(bank, mark.Frame);
				int kept = display.DrawPage;
				display.DrawPage = Es1ShellBackdrop.BackgroundPage;
				canvas.Blit(bitmap, mark.X, mark.Y);
				display.DrawPage = kept;
				canvas.Blit(bitmap, mark.X, mark.Y);
			}
		}
	}

	/// <summary>
	/// <c>Herc_HighlightMount</c> (<c>00024326</c>)(<paramref name="herc"/>, <paramref name="mount"/>)
	/// (docs/es1/formats/herc-diagrams.md#the-mount-highlight): the mounts erased, <paramref name="mount"/> made current,
	/// <see cref="MountHighlightShown"/> set for a mount other than −1 and cleared for −1, the mounts drawn.
	/// </summary>
	public void HighlightMount(Es1HercRecord herc, short mount, Es1SpriteSurface surface) {
		EraseMounts(herc, surface);
		State(herc).CurrentMount = mount;
		MountHighlightShown = mount != -1;
		DrawMounts(herc, surface);
	}

	/// <summary>
	/// <c>Herc_HighlightNextMount</c> (<c>000242a6</c>)(<paramref name="herc"/>): the mounts erased, the current mount plus
	/// 1 modulo the mount count, <see cref="MountHighlightShown"/> set, the mounts drawn. Retail divides by a mount count
	/// of 0; such a HERC keeps its current mount here, an engine guard.
	/// </summary>
	public void HighlightNextMount(Es1HercRecord herc, Es1SpriteSurface surface) {
		EraseMounts(herc, surface);
		var diagram = State(herc);
		if (herc.Weapons.Length != 0) {
			diagram.CurrentMount = (short)((diagram.CurrentMount + 1) % herc.Weapons.Length);
		}

		MountHighlightShown = true;
		DrawMounts(herc, surface);
	}

	/// <summary>
	/// <c>Herc_HighlightPreviousMount</c> (<c>000242e8</c>)(<paramref name="herc"/>): the mounts erased, the current mount
	/// less 1, below 0 the last, <see cref="MountHighlightShown"/> set, the mounts drawn.
	/// </summary>
	public void HighlightPreviousMount(Es1HercRecord herc, Es1SpriteSurface surface) {
		EraseMounts(herc, surface);
		var diagram = State(herc);
		diagram.CurrentMount--;
		if (diagram.CurrentMount < 0) {
			diagram.CurrentMount = (short)(herc.Weapons.Length - 1);
		}

		MountHighlightShown = true;
		DrawMounts(herc, surface);
	}

	/// <summary>
	/// <c>Herc_HideMountHighlight</c> (<c>00024287</c>)(<paramref name="herc"/>): the mounts erased,
	/// <see cref="MountHighlightShown"/> cleared and the mounts drawn; the current mount stays.
	/// </summary>
	public void HideMountHighlight(Es1HercRecord herc, Es1SpriteSurface surface) {
		EraseMounts(herc, surface);
		MountHighlightShown = false;
		DrawMounts(herc, surface);
	}

	/// <summary><c>+0x70</c> of <paramref name="herc"/>, the current mount, −1 for none.</summary>
	public short CurrentMount(Es1HercRecord herc) => State(herc).CurrentMount;

	/// <summary>
	/// <c>Herc_CurrentMountWeapon</c> (<c>00024a6f</c>)(<paramref name="herc"/>): the current mount's weapon, or 17 when
	/// there is no current mount or no mount. Retail reads a current mount past the mounts unchecked; 17 here, an engine
	/// guard.
	/// </summary>
	public short CurrentMountWeapon(Es1HercRecord herc) {
		short mount = CurrentMount(herc);
		return mount >= 0 && mount < herc.Weapons.Length ? herc.Weapons[mount] : Es1HercArt.EmptyMount;
	}

	/// <summary><c>Herc_CurrentMountStatus</c> (<c>00024b51</c>)(<paramref name="herc"/>): <c>Herc_GetMountStatus</c> (<c>00024b23</c>) of the current mount, 0 for none (<see cref="Es1HercRecord.MountStatus"/>).</summary>
	public short CurrentMountStatus(Es1HercRecord herc) => herc.MountStatus(CurrentMount(herc));

	/// <summary>
	/// <c>Herc_SetCurrentMountStatus</c> (<c>00024aea</c>)(<paramref name="herc"/>, <paramref name="value"/>): for a current
	/// mount, its status word in the block. A mount past the block's ten words writes nothing here, an engine guard.
	/// </summary>
	public void SetCurrentMountStatus(Es1HercRecord herc, short value) {
		short mount = CurrentMount(herc);
		if (mount != -1 && mount >= 0 && Es1HercRecord.MountStatusWords + mount < herc.Status.Length) {
			herc.Status[Es1HercRecord.MountStatusWords + mount] = value;
		}
	}

	/// <summary>
	/// <c>Herc_SetCurrentMountAmmo</c> (<c>00024a1e</c>)(<paramref name="herc"/>, <paramref name="value"/>): for a current
	/// mount, its word <c>+0x18</c>. A mount past the words writes nothing here, an engine guard.
	/// </summary>
	public void SetCurrentMountAmmo(Es1HercRecord herc, short value) {
		short mount = CurrentMount(herc);
		if (mount >= 0 && mount < herc.MountWords.Length) {
			herc.MountWords[mount] = value;
		}
	}

	/// <summary>
	/// <c>Herc_SetCurrentMountWeapon</c> (<c>00024788</c>)(<paramref name="herc"/>, <paramref name="weapon"/>)
	/// (docs/es1/formats/herc-diagrams.md#the-mount-highlight): for a current mount, its weapon becomes 17 when its status
	/// word is 0 and <paramref name="weapon"/> otherwise; its picture (<see cref="SetMountPicture"/> with the current
	/// mount's status) and its outline set as <see cref="Build"/> sets them, both looked up by <paramref name="weapon"/>.
	/// When the frame found is not 0: on chassis 7 with mount 2 or 3 current and <see cref="ColossusTopShown"/> clear, the
	/// shoulder piece set and the byte set; otherwise, with the byte set, chassis 7 and the other of mounts 2 and 3
	/// holding weapon 0, the piece set to no bitmap at (0, 0) and the byte cleared. Last the mount's word <c>+0x18</c>
	/// becomes 5 for a weapon outside 9 to 11, or 0 for one of them whose word is 5. Retail reads and writes past the
	/// mounts for a current mount outside them; nothing is done here then, an engine guard.
	/// </summary>
	public void SetCurrentMountWeapon(Es1HercRecord herc, short weapon) {
		var diagram = State(herc);
		short mount = diagram.CurrentMount;
		if (mount == -1 || mount < 0 || mount >= herc.Weapons.Length) {
			return;
		}

		short chassis = herc.Chassis;
		herc.Weapons[mount] = herc.MountStatus(mount) == 0 ? Es1HercArt.EmptyMount : weapon;
		short frame = Art.FindFrame(weapon, mount, chassis);
		if (Element(diagram.Mounts, mount) is { } picture) {
			SetMountPicture(picture, CurrentMountStatus(herc), weapon, mount, chassis);
		}

		var point = Art.FindSecondPoint(weapon, mount, chassis);
		Element(diagram.Outlines, mount)?.Set(0, point.X, point.Y, Es1HercArt.Frame(diagram.OutlineBank, frame));
		if (frame != 0) {
			if (chassis == Colossus && !ColossusTopShown && mount is 2 or 3) {
				diagram.ColossusTop?.Set(0, ColossusTopX, ColossusTopY, Es1HercArt.Frame(diagram.WeaponBank, ColossusTopFrame));
				ColossusTopShown = true;
			} else if (ColossusTopShown && chassis == Colossus && herc.Weapons.Length > 3
					&& ((mount == 2 && herc.Weapons[3] == 0) || (mount == 3 && herc.Weapons[2] == 0))) {
				diagram.ColossusTop?.Set(0, 0, 0, null);
				ColossusTopShown = false;
			}
		}

		if (mount < herc.MountWords.Length) {
			short fitted = herc.Weapons[mount];
			if (fitted is < 9 or > 11) {
				herc.MountWords[mount] = 5;
			} else if (herc.MountWords[mount] == 5) {
				herc.MountWords[mount] = 0;
			}
		}
	}

	/// <summary>
	/// <c>Herc_GetStatus</c> (<c>00024b86</c>)(<paramref name="herc"/>, <paramref name="index"/>, <paramref name="kind"/>)
	/// as the status lines read it: for <see cref="Internal"/> internal word <paramref name="index"/>, for
	/// <see cref="External"/> external area <paramref name="index"/>'s mean, for <see cref="MountKind"/> the mount's word,
	/// and the kind itself for any other. Retail reads the block with no bounds test; an index outside it reads 0 here, an
	/// engine guard.
	/// </summary>
	public static short GetStatus(Es1HercRecord herc, int index, short kind) => kind switch {
		Internal => index >= 0 && Es1HercRecord.FirstInternalWord + index < herc.Status.Length
			? herc.Status[Es1HercRecord.FirstInternalWord + index]
			: (short)0,
		External => herc.ExternalAverage(index),
		MountKind => herc.MountStatus(index),
		_ => kind,
	};

	/// <summary>
	/// <c>Herc_SetStatus</c> (<c>00024c14</c>)(<paramref name="herc"/>, <paramref name="value"/>, <paramref name="index"/>,
	/// <paramref name="kind"/>), <c>HercStatus_Set</c> (<c>0001a492</c>) on the HERC's block
	/// (docs/es1/shell/repair-and-build.md#and): for <see cref="External"/> every external word of area
	/// <paramref name="index"/>'s list (<see cref="Es1HercRecord.ExternalAreas"/>) set to the value (<c>0001a42c</c>), for
	/// <see cref="Internal"/> internal word <paramref name="index"/>, for <see cref="MountKind"/> the mount's word; any other
	/// kind writes nothing. Retail writes with no bounds test; an index outside the block writes nothing here, an engine
	/// guard.
	/// </summary>
	public static void SetStatus(Es1HercRecord herc, short value, int index, short kind) {
		short[] block = herc.Status;
		switch (kind) {
			case External:
				if (index >= 0 && index < Es1HercRecord.ExternalAreas.Length) {
					foreach (short word in Es1HercRecord.ExternalAreas[index]) {
						block[word] = value;
					}
				}

				break;
			case Internal:
				if (index >= 0 && Es1HercRecord.FirstInternalWord + index < block.Length) {
					block[Es1HercRecord.FirstInternalWord + index] = value;
				}

				break;
			case MountKind:
				if (index >= 0 && Es1HercRecord.MountStatusWords + index < block.Length) {
					block[Es1HercRecord.MountStatusWords + index] = value;
				}

				break;
		}
	}

	/// <summary>
	/// <c>Herc_BuildStatusLines</c> (<c>00024d99</c>)(<paramref name="herc"/>, <paramref name="kind"/>)
	/// (docs/es1/formats/herc-diagrams.md#the-status-lines): for <see cref="InternalLines"/> or <see cref="Internal"/>
	/// <c>eng\watext.bin</c>'s lines <c>0x80</c> to <c>0x88</c>, for <see cref="External"/> lines <c>0x89</c> to
	/// <c>0x8e</c>, through <c>WaText_LinesFromList</c>, whose lines are the text table's own buffers, so the condition
	/// <see cref="FormatStatusLines"/> writes over their first three characters stays in <paramref name="text"/>; for
	/// <see cref="MountKind"/> a new line per mount, a copy of line <c>0xa0</c>, <c>---- NONE</c>, for weapon 17 and of
	/// line <c>0x8f</c> + the weapon otherwise, in the buffer <c>000a0308</c>; then the conditions written. The lines end
	/// with null. Retail hands the format whatever its local held for any other kind; such a kind gives no lines here,
	/// and a line past the table an empty one, engine guards.
	/// </summary>
	public static string?[] BuildStatusLines(Es1HercRecord herc, short kind, string[]? text) {
		string?[] lines;
		switch (kind) {
			case InternalLines:
			case Internal:
			case External:
				short[] numbers = kind == External ? ExternalLineNumbers : InternalLineNumbers;
				for (int i = 0; i < numbers.Length; i++) {
					if (text != null && numbers[i] < text.Length && !text[numbers[i]].StartsWith('-')) {
						text[numbers[i]] = FormatStatusLine(herc, i, text[numbers[i]], kind);
					}
				}

				return Es1MenuBuild.LinesFromList(text, numbers);
			case MountKind:
				lines = new string?[herc.Weapons.Length + 1];
				for (int i = 0; i < herc.Weapons.Length; i++) {
					int number = herc.Weapons[i] == Es1HercArt.EmptyMount ? EmptyMountLine : FirstWeaponLine + herc.Weapons[i];
					string line = text != null && number >= 0 && number < text.Length ? text[number] : "";
					lines[i] = FormatStatusLine(herc, i, line, kind);
				}

				return lines;
			default:
				return [null];
		}
	}

	/// <summary>
	/// <c>Herc_FormatStatusLine</c> (<c>00024ef4</c>)(<paramref name="herc"/>, <paramref name="index"/>,
	/// <paramref name="line"/>, <paramref name="kind"/>), whose loop over a list is <c>Herc_FormatStatusLines</c>
	/// (<c>00024f9e</c>): for a line not starting with <c>-</c>, the first three characters of <c>%3d</c> of the status
	/// (<see cref="GetStatus"/> with <see cref="Internal"/> for <see cref="InternalLines"/> and <see cref="Internal"/>, with
	/// <see cref="External"/>, or the mount's word for <see cref="MountKind"/>) written over the line's first three. Any
	/// other kind writes what the buffer held; the line is left here, an engine guard, and a line shorter than three is
	/// padded with spaces where retail writes past its end.
	/// </summary>
	public static string FormatStatusLine(Es1HercRecord herc, int index, string line, short kind) {
		if (line.StartsWith('-') || kind is not (InternalLines or Internal or External or MountKind)) {
			return line;
		}

		short status = GetStatus(herc, index, kind == InternalLines ? Internal : kind);
		string condition = status.ToString(CultureInfo.InvariantCulture).PadLeft(3);
		return string.Concat(condition.AsSpan(0, 3), line.Length > 3 ? line.AsSpan(3) : ReadOnlySpan<char>.Empty);
	}
}
