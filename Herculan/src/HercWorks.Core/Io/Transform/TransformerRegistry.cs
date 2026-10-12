using HercWorks.Vol;

namespace HercWorks.Core.Io.Transform;

/// <summary>
/// Maps a VolEntry (by directory + extension + file name) to the ThreeSpaceByteTransformer that
/// knows how to parse it, so callers — chiefly the WinForms VOL browser — can show formatted
/// content for a selected file without needing to know every file-naming convention themselves.
///
/// Deliberately conservative: only covers file types this project actually has a real, working
/// transformer for (see HercWorks.Core.Io.Transform.{Bnd,Common,Dbsim,Shell}). Files that only
/// ever live loose in the install — data\script.dat, data\player.mec, sav\GAMEFILE.STR, the .TAP
/// tapes — have readers too, but never appear inside a VOL, so they are not registered here. Plenty of other file
/// types exist in the game data with a parsed C# data-model class but no ported
/// ThreeSpaceByteTransformer (e.g. MapInfo, MapLOCS, Theater) — those
/// intentionally have no registration here and will report "no parser available" rather than
/// risk a wrong/guessed match.
///
/// HercSimDataTransformer's target ("dat\[herc].dat") can't be distinguished from other .DAT
/// files by name/path, so it's matched instead by the 4-byte VolEntry.MagicPrefix observed on
/// real herc data files: 41 20 00 7A or 41 20 01 7A. Those bytes are the source files' MS-DOS
/// date and time (docs/retail/formats/vol-archive.md), so the match holds only for unmodified retail
/// archives.
///
/// <para>Each <see cref="RetailGame"/> has its own table. ES1's is ES2's with the readers of the
/// layouts that differ under the same name and header rebound, and the chassis files, whose timestamps
/// ES2's match cannot see, matched by name (<see cref="Es1Rebinds"/>), behind ES1's readers for files ES2 has no label for
/// (<see cref="Es1Additions"/>; docs/herculan/plan-es1-support.md#what-hercworks-reads).</para>
/// </summary>
public static class TransformerRegistry {
	private sealed record Registration(string Label, Func<VolEntry, bool> Matches, Func<ThreeSpaceByteTransformer> Create);

	private static readonly List<Registration> Es2Registrations = new() {
		// --- shell (Dir=Gam, Ext=Dat) ---
		new("Herc Armory Panel", e => NameStartsWith(e, "ARM_") && ExtIs(e, FileType.Dat) && !NameIs(e, "ARM_WEAP.DAT") && !NameIs(e, "ARM_HOTS.DAT"),
			() => new Shell.ArmHercTransformer()),
		new("Armory Weapon Icons", e => NameIs(e, "ARM_WEAP.DAT"), () => new Shell.ArmWeapTransformer()),
		new("Career Missions", e => NameIs(e, "CAREER.DAT"), () => new Shell.CareerDataTransformer()),
		new("Component Value Table", e => NameIs(e, "DAMAGE.DAT"), () => new Shell.DamageRepairCostTransformer()),
		// One format, two files: Hotspots_BuildOverlay (0043c1a0) reads both through the same code,
		// picking the name on which tab it is building for. Both parse to EOF.
		new("Hardpoint Overlay", e => NameIs(e, "RPR_HOTS.DAT") || NameIs(e, "ARM_HOTS.DAT"),
			() => new Shell.HardpointOverlayTransformer()),
		new("Herc Info", e => NameIs(e, "HERC_INF.DAT"), () => new Shell.HercInfoTransformer()),
		new("Starting Hercs", e => NameIs(e, "HERCS.DAT"), () => new Shell.HercsStartTransformer()),
		new("Herc Init Data", e => NameStartsWith(e, "INI_"), () => new Shell.InitHercTransformer()),
		new("Repair Herc Panel", e => NameStartsWith(e, "RPR_") && ExtIs(e, FileType.Dat) && !NameIs(e, "RPR_HOTS.DAT"), () => new Shell.RprHercTransform()),
		new("Training Hercs", e => NameIs(e, "TRN_HERCS.DAT"), () => new Shell.TrainingHercsTransform()),
		new("Weapons Catalog", e => NameIs(e, "WEAPONS.DAT") && DirIs(e, FileType.Gam), () => new Shell.WeaponsDatTransformer()),

		// --- bnd (per-subsystem tuning blobs, see docs/retail/formats/bnd-notes.md — every file has its
		// own unrelated record shape, so these are matched by exact name, not by extension) ---
		new("Camera Config", e => NameIs(e, "CAM.BND"), () => new Bnd.CamTransformer()),

		// --- dbsim (various dirs) ---
		new("Beam Data", e => NameIs(e, "BEAM.DAT"), () => new Dbsim.BeamDatFileTransformer()),
		new("Scanline Clip Mask", e => ExtIs(e, FileType.Edg), () => new Dbsim.EdgeClipFileTransformer()),
		new("HUD Layout", e => ExtIs(e, FileType.Gau), () => new Dbsim.GauFileTransformer()),
		new("Herc Collider", e => ExtIs(e, FileType.Col), () => new Dbsim.HercColliderTransformer()),
		new("3D Model (DTS)", e => ExtIs(e, FileType.Dts), () => new Dbsim.DTSModelTransformer()),
		new("Herc Debris Data", e => NameEndsWith(e, "_DEB.DAT"), () => new Dbsim.DebrisHercTransformer()),
		new("Flight Model", e => ExtIs(e, FileType.Fm), () => new Dbsim.FlightModelTransformer()),
		new("Gun Layout", e => ExtIs(e, FileType.Gl), () => new Dbsim.GunLayoutTransformer()),
		new("Herc Damage Data", e => ExtIs(e, FileType.Dmg), () => new Dbsim.HercDamageFileTransformer()),
		new("Bullet Data", e => NameIs(e, "BULLETS.DAT"), () => new Dbsim.BulletDataTransformer()),
		new("Rocket Data", e => NameIs(e, "ROCKETS.DAT"), () => new Dbsim.RocketDataTransformer()),
		new("Transport Weapon Slots", e => NameIs(e, "LC_WPNS.DAT"), () => new Dbsim.LcWeaponDataTransformer()),
		new("Weapons Paper Diagram", e => NameIs(e, "WEAPONS.PDG"), () => new Dbsim.WeaponPDGTransformer()),
		new("Paper Diagram Graphic", e => ExtIs(e, FileType.Pdg) && !NameIs(e, "WEAPONS.PDG"), () => new Dbsim.PaperDiagramGraphTransformer()),
		new("Projectile Data", e => NameIs(e, "PROJ.DAT"), () => new Dbsim.ProjectileDataTransformer()),
		new("Weapon Mount Templates", e => NameIs(e, "WEAPONS.DAT") && DirIs(e, FileType.Dat), () => new Dbsim.WeaponsSimTransformer()),
		new("Structure Types", e => NameIs(e, "BASES.DAT") && DirIs(e, FileType.Dat), () => new Dbsim.BasesDatTransformer()),
		new(HercSimDataLabel, e => MagicIs(e, "4120007A") || MagicIs(e, "4120017A"), () => new Dbsim.HercSimDataTransformer()),
		// Only known sample is SKIMMER.DAT, whose magic prefix doesn't match either Herc value above
		// (7C1F640E) — matched by exact name for now rather than guessing a magic-based rule from a
		// single data point. See FlyerSimData's doc comment for the format itself.
		new("Flyer Sim Data", e => NameIs(e, "SKIMMER.DAT") && DirIs(e, FileType.Dat), () => new Dbsim.FlyerSimDataTransformer()),
		new("Pilot Portrait Offsets", e => ExtIs(e, FileType.Ofs), () => new Dbsim.PilotOffsetFileTransformer()),
		new("Terrain Ramp Data", e => ExtIs(e, FileType.Rmp), () => new Dbsim.TerrainRampFileTransformer()),
		new("Viewport Data", e => ExtIs(e, FileType.Vue), () => new Dbsim.VueTransformer()),
		new("World/Environment Data", e => ExtIs(e, FileType.Wld), () => new Dbsim.WorldDataTransformer()),
		// One layout in both games (docs/es1/rendering/polygon-shading.md#the-shades-file).
		new("Distance Band Ramps", e => NameIs(e, "SHADES.DAT"), () => new Dbsim.WorldShadesTransformer()),
		new("Explosion Types", e => NameIs(e, "EXPLOS.DAT"), () => new Dbsim.ExplosionDataTransformer()),
		new("Zone Header", e => NameStartsWith(e, "ZONE") && ExtIs(e, FileType.Dat), () => new Common.ZoneDatTransformer()),
		// Structure shape libraries. Matched by name: the .DGS container is documented for these two
		// files only (docs/retail/formats/dgs-hd0-notes.md).
		new("Structure Shape Library", e => NameIs(e, "BASES.DGS") || NameIs(e, "BHULKS.DGS"),
			() => new Dbsim.BasesDgsTransformer()),

		// --- common ---
		new("Dynamix Bitmap Array", e => ExtIs(e, FileType.Dba), () => new Common.DynamixBitmapArrayTransformer()),
		new("Dynamix Bitmap", e => ExtIs(e, FileType.Dbm), () => new Common.DynamixBitmapTransformer()),
		new("Dynamix Palette", e => ExtIs(e, FileType.Dpl), () => new Common.DynamixPaletteTransformer()),
		new("Mission File", e => ExtIs(e, FileType.Msn), () => new Common.MissionFileTransformer()),
		new("Player Save", e => ExtIs(e, FileType.Sav), () => new Common.PlayerSaveTransform()),
		new("String Table", e => ExtIs(e, FileType.Str), () => new Common.StringFileTransformer()),
		// LANG0.VOL's six .BIN files — weapons, estext, esnames, missions, wpn_info, wpn_desc — share
		// one container (docs/retail/formats/weapons-dat.md, "The .BIN string tables").
		new("Indexed String Table", e => ExtIs(e, FileType.Bin), () => new Common.BinStringFileTransformer()),
		// A mission's text, beside its .MSN in ZONES.VOL (docs/retail/formats/msn-mission-file.md).
		new("Mission Text", e => ExtIs(e, FileType.Eng), () => new Common.MissionStringFileTransformer()),
		new("Sound", e => ExtIs(e, FileType.Wav), () => new Common.WavInfoTransformer()),

		// .HBA and .HB0/.HB1/.HB2 are byte-identical to the .DBA container format
		// (same 12-byte "01 00 28 00" + size + count header, same embedded DynamixBitmap-per-entry
		// layout, same zero-byte pad after each odd-length entry) — confirmed against every real HBA/HB0/HB1/HB2
		// file in simvol0 by walking the existing DynamixBitmapArrayTransformer's exact algorithm
		// by hand. HB0/HB1/HB2 always parse to a single 640x480 frame (a full-screen cockpit
		// background, one per team color); HBA holds several smaller gauge/UI sprites.
		new("Herc Cockpit Bitmap Array", e => ExtIs(e, FileType.Hba), () => new Common.DynamixBitmapArrayTransformer()),
		new("Herc Cockpit Texture (640x480)", e => ExtIs(e, FileType.Hb0) || ExtIs(e, FileType.Hb1) || ExtIs(e, FileType.Hb2),
			() => new Common.DynamixBitmapArrayTransformer()),

		// Same DBA-compatible container as HBA/HB0-2 above, confirmed against real data:
		// .DB0/.DB1/.DB2 always parse to a single 320x240 frame — a smaller/alternate
		// framing of the same cockpit interior (e.g. DB2 shows a rotated/cropped angle), also
		// correctly colored under COCKPIT.DPL. Same "COCKPIT TEXTURES.txt" hint file present
		// alongside HB0/HB1/HB2's real folders is also present in db0/db1/db2.
		new("Herc Cockpit Texture (320x240)", e => ExtIs(e, FileType.Db0) || ExtIs(e, FileType.Db1) || ExtIs(e, FileType.Db2),
			() => new Common.DynamixBitmapArrayTransformer()),
	};

	/// <summary>The label of the chassis stats reader, <c>dat\[chassis].dat</c>, in each game's table.</summary>
	public const string HercSimDataLabel = "Herc Sim Data";

	/// <summary>An ES1 reader for an entry ES2's table binds by the same label, and the match ES1's table uses when ES2's does not fit ES1's files.</summary>
	private sealed record Rebinding(Func<ThreeSpaceByteTransformer> Create, Func<VolEntry, bool>? Matches = null);

	/// <summary>
	/// ES1's readers for entries ES2's table binds by the same label: each replaces that registration's reader in place,
	/// keeping its label, and its match unless the rebinding gives one.
	/// </summary>
	private static readonly Dictionary<string, Rebinding> Es1Rebinds = new() {
		// docs/es1/formats/msn-mission-file.md
		["Mission File"] = new(() => new Common.Es1MissionFileTransformer()),
		// docs/es1/formats/eng-mission-text.md: tagged briefing, objective and debriefing blocks, not ES2's id-keyed lines.
		["Mission Text"] = new(() => new Common.Es1MissionTextTransformer()),
		// docs/es1/formats/zone-terrain.md: ES1's zone file is the zone's shapes, not a heightmap header.
		["Zone Header"] = new(() => new Common.Es1ZoneDatTransformer()),
		// docs/es1/formats/wld-world.md: ES2's layout less two leading int16.
		["World/Environment Data"] = new(() => new Dbsim.Es1WorldDataTransformer()),
		// docs/es1/formats/chassis-dat.md: ES2's first 192 bytes. ES2's match is its files' source timestamps, which ES1's
		// do not share, so ES1's matches the dat\ files of the names ES1's nam\MECHS.NAM lists.
		[HercSimDataLabel] = new(() => new Dbsim.Es1HercSimDataTransformer(),
			e => DirIs(e, FileType.Dat) && IsEs1ChassisFile(e)),
		// docs/es1/formats/weapons-dat-sim.md: 17 templates and no count, then ES1's 19 PROJ.DAT records.
		["Weapon Mount Templates"] = new(() => new Dbsim.Es1WeaponsSimTransformer()),
		// docs/es1/formats/bullets-skimmer-dat.md: 10-byte records where ES2's are 14.
		["Bullet Data"] = new(() => new Dbsim.Es1BulletDataTransformer()),
		["Flyer Sim Data"] = new(() => new Dbsim.Es1FlyerSimDataTransformer()),
		// docs/es1/formats/bases-dat.md: fixed 54-byte records, no components.
		["Structure Types"] = new(() => new Dbsim.Es1BasesDatTransformer()),
		// docs/es1/formats/explos-dat.md: one table of 44-byte rows, ES2's shape table folded into them.
		["Explosion Types"] = new(() => new Dbsim.Es1ExplosionDataTransformer()),
		// docs/es1/formats/herc-catalogs.md#cfgdamagedat: ten internal percentages and 18 weapon values with no count.
		["Component Value Table"] = new(() => new Shell.Es1DamageRepairCostTransformer()),
		// docs/es1/formats/bnd-notes.md#bndcambnd: 20 bytes, the camera's constants, where ES2's file is 24.
		["Camera Config"] = new(() => new Bnd.Es1CamTransformer()),
	};

	/// <summary>
	/// ES1's readers for files ES2's table has no label for, placed ahead of the rebound table so they take their files before
	/// ES2's broader matches do.
	/// </summary>
	private static readonly List<Registration> Es1Additions = new() {
		// docs/es1/formats/bin-string-tables.md#the-dialog-text-files: a version word ahead of the .BIN table, in the ten
		// talking-head text files only; the shell's other .BIN stay on ES2's reader.
		new("Dialog Text", e => ExtIs(e, FileType.Bin) && NameEndsWith(e, "_TEXT.BIN"), () => new Common.Es1DialogTextTransformer()),
		// docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture: the fade files the world files' fifth string
		// names, and the textures their fourth names.
		new("Ground Fade Distances", e => NameIs(e, "TEX.DAT") || NameIs(e, "TEX_LO.DAT") || NameIs(e, "TEX_LO2.DAT"),
			() => new Dbsim.Es1GroundFadeTransformer()),
		new("Ground Texture", e => NameEndsWith(e, ".64K"), () => new Dbsim.Es1GroundTextureTransformer()),
		// docs/es1/formats/dfn-hfn-dci.md#the-old-bitmap-record: RES32\ICONS.DBA, matched by its first item's tag ahead of
		// ES2's bitmap array reader, which cannot read old-format items.
		new("Old Bitmap Array", e => ExtIs(e, FileType.Dba) && Common.Es1OldBitmapArrayTransformer.IsOldBitmapArray(e.RawBytes),
			() => new Common.Es1OldBitmapArrayTransformer()),
		// docs/es1/formats/dmg-damage-file.md#hovtankdmg: bytes not understood, kept whole (OPAQUE).
		new("Unread Damage File (opaque)", e => NameIs(e, "HOVTANK.DMG"), () => new Common.Es1OpaqueFileTransformer()),
	};

	/// <summary>Whether the entry is one of the sixteen chassis files ES1's <c>nam\MECHS.NAM</c> names (docs/es1/formats/chassis-dat.md#which-files).</summary>
	private static bool IsEs1ChassisFile(VolEntry e) => e.FileName?.Trim().ToUpperInvariant() is
		"ROADRUNN.DAT" or "GUNSLING.DAT" or "RAPTOR.DAT" or "TOMAHAWK.DAT" or "PATRIOT.DAT" or "PANTHER.DAT" or "HERCULES.DAT"
		or "COLOSSUS.DAT" or "MONGOOSE.DAT" or "SCORPION.DAT" or "MIRIMAC.DAT" or "RAMSES.DAT" or "ACHILLES.DAT" or "APOLLO.DAT"
		or "BULLDOG.DAT" or "SPIDER.DAT";

	private static readonly Dictionary<RetailGame, List<Registration>> Tables = new() {
		[RetailGame.Earthsiege2] = Es2Registrations,
		[RetailGame.Earthsiege1] = [.. Es1Additions, .. Rebind(Es2Registrations, Es1Rebinds)],
	};

	private static List<Registration> Rebind(List<Registration> table, Dictionary<string, Rebinding> rebinds) {
		var missing = rebinds.Keys.Where(label => !table.Any(r => r.Label == label)).ToList();
		if (missing.Count > 0) {
			throw new InvalidOperationException($"No registration is labelled {string.Join(", ", missing)}.");
		}

		return table.Select(r => rebinds.TryGetValue(r.Label, out var rebind)
			? r with { Create = rebind.Create, Matches = rebind.Matches ?? r.Matches }
			: r).ToList();
	}

	/// <summary>
	/// A fresh transformer of the reader <paramref name="game"/>'s table binds under <paramref name="label"/>, whatever entry
	/// it is for, or null when the table has no such label.
	/// </summary>
	public static ThreeSpaceByteTransformer? CreateByLabel(string label, RetailGame game) =>
		Tables[game].FirstOrDefault(r => r.Label == label)?.Create();

	/// <summary>
	/// Returns a fresh transformer instance for the given entry from <paramref name="game"/>'s table, or null if no known
	/// parser matches.
	/// </summary>
	public static ThreeSpaceByteTransformer? FindTransformer(VolEntry entry, RetailGame game = RetailGame.Earthsiege2) =>
		Find(entry, game)?.Create();

	/// <summary>Human-readable label for the matched file type in <paramref name="game"/>'s table, or null if no known parser matches.</summary>
	public static string? FindLabel(VolEntry entry, RetailGame game = RetailGame.Earthsiege2) =>
		Find(entry, game)?.Label;

	private static Registration? Find(VolEntry entry, RetailGame game) =>
		Tables[game].FirstOrDefault(reg => reg.Matches(entry));

	private static bool NameIs(VolEntry e, string name) =>
		string.Equals(e.FileName?.Trim(), name, StringComparison.OrdinalIgnoreCase);

	private static bool NameStartsWith(VolEntry e, string prefix) =>
		e.FileName != null && e.FileName.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

	private static bool NameEndsWith(VolEntry e, string suffix) =>
		e.FileName != null && e.FileName.Trim().EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

	private static bool ExtIs(VolEntry e, FileType ext) => e.Ext == ext;

	private static bool DirIs(VolEntry e, FileType dir) => e.Dir == dir;

	private static bool MagicIs(VolEntry e, string hex) =>
		e.MagicPrefix != null && string.Equals(Convert.ToHexString(e.MagicPrefix), hex, StringComparison.OrdinalIgnoreCase);
}
