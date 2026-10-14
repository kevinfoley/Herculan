using Herculan.Engine.Settings;

namespace Herculan.Engine.Shell;

/// <summary>
/// The weapon prose lines <see cref="TweakSettingDefinitions.ShowCorrectStats"/> corrects, shared by the
/// weapons screen (<c>wpn_desc.bin</c>, <see cref="ShellWeaponsScreen"/>) and the armory
/// (<c>wpn_info.bin</c>, <see cref="ShellArmoryScreen"/>). Each file entry is a whole hand-laid line —
/// labels, figures and alignment together — so a correction is the retail line swapped for a corrected
/// copy of it, matched exactly; a line not in <see cref="Corrections"/>, modded text included, is
/// printed as it stands. What the prose misstates is
/// docs/retail/formats/weapons-dat.md#the-bin-string-tables.
/// </summary>
internal static class ShellWeaponProse {
	/// <summary>
	/// Every retail line the tweak changes, by its exact text as the screens hold it (Latin-1, trimmed;
	/// the French and German files are in the DOS code page, so their accents read as <c>\u0082</c> and
	/// the like). The corrections:
	///
	/// <list type="bullet">
	/// <item><b>Range.</b> <c>L100</c>'s template is 55000 units, 330 m, where both files print 320. A
	/// launcher's round flies about 315 m whatever it is loaded with
	/// (docs/retail/simulation/rockets.md#flight--rocket_tickupdate-0040a538), printed as 320 m, to the
	/// nearest 10 m as the prose prints every other range: the files give the four guidance kinds 510,
	/// 420, 420 and 360 m and the racks nothing or "Varies".</item>
	/// <item><b>Power consumption</b>, re-rated by this engine against each weapon's cost per shot —
	/// HERCULAN's ratings, not retail data:
	/// <c>ELFW</c> Very High (as <c>ELF2</c>), <c>L500</c> Medium (as <c>L300</c> and <c>L400</c>),
	/// <c>EMP2</c> and <c>PBW2</c> High (as <c>EMPC</c> and <c>PBW</c>).</item>
	/// <item><b>Recharge</b>, likewise against the capacitor's refill time: Fast for every laser, Medium
	/// for <c>EMPC</c>, <c>EMP2</c>, <c>PBW2</c>, <c>ELFW</c> and <c>ELF2</c> (as <c>PBW</c>), and
	/// <c>PLAS</c>'s Very Slow left alone.</item>
	/// </list>
	///
	/// <para>Each corrected line keeps its translator's own words for the new value, taken from another
	/// weapon's line, and keeps the next field in its column. No key is also the text of a line the
	/// tweak leaves alone, in either file of any language.</para>
	/// </summary>
	private static readonly Dictionary<string, string> Corrections = new(StringComparer.Ordinal) {
		// English, which v1.0's French and German folders also carry
		["Range: 120 m   Power Consumption: High"] =
			"Range: 120 m   Power Consumption: Very High",
		["Recharge: Slow   Nominal Power: 1000 Gigawatts/sec"] =
			"Recharge: Medium Nominal Power: 1000 Gigawatts/sec",
		["Recharge: Slow   Nominal Power: 900 Gigawatts"] =
			"Recharge: Medium Nominal Power: 900 Gigawatts",
		["Range: 320 m   Power Consumption: Low"] =
			"Range: 330 m   Power Consumption: Low",
		["Recharge: Medium Nominal Power: 300 Gigawatts"] =
			"Recharge: Fast   Nominal Power: 300 Gigawatts",
		["Recharge: Medium Nominal Power: 400 Gigawatts"] =
			"Recharge: Fast   Nominal Power: 400 Gigawatts",
		["Range: 220 m   Power Consumption: High"] =
			"Range: 220 m   Power Consumption: Medium",
		["Recharge: Slow   Nominal Power: 500 Gigawatts"] =
			"Recharge: Fast   Nominal Power: 500 Gigawatts",
		["Range: Varies  Power Consumption: None"] =
			"Range: 320 m   Power Consumption: None",
		["Recharge: Slow   Nominal Power: 1250 Gigawatts/sec"] =
			"Recharge: Medium Nominal Power: 1250 Gigawatts/sec",
		["Range: 300 m   Power Consumption: Very High"] =
			"Range: 300 m   Power Consumption: High",
		["Recharge: Slow   Nominal Power: 1800 (900X2) Gigawatts"] =
			"Recharge: Medium Nominal Power: 1800 (900X2) Gigawatts",
		["Range: 210 m   Power Consumption: Very High"] =
			"Range: 210 m   Power Consumption: High",
		["Recharge: Slow   Nominal Power: 1250 Gigawatts"] =
			"Recharge: Medium Nominal Power: 1250 Gigawatts",
		["Range: 510 m   Warhead Damage: 3000"] =
			"Range: 320 m   Warhead Damage: 3000",
		["Range: 420 m   Warhead Damage: 2250"] =
			"Range: 320 m   Warhead Damage: 2250",
		["Range: 420 m   Warhead Damage: 2500"] =
			"Range: 320 m   Warhead Damage: 2500",
		["Range: 360 m   Warhead Damage: 3500"] =
			"Range: 320 m   Warhead Damage: 3500",
		["Range: 320 m     Salvage Required: 8,000 kg"] =
			"Range: 330 m     Salvage Required: 8,000 kg",
		["Range:           Salvage Required: 6,000 kg"] =
			"Range: 320 m     Salvage Required: 6,000 kg",
		["Range:           Salvage Required: 12,000 kg"] =
			"Range: 320 m     Salvage Required: 12,000 kg",
		["Range:           Salvage Required: 18,000 kg"] =
			"Range: 320 m     Salvage Required: 18,000 kg",
		// French, v1.10
		["Port\u0082e: 120 m   Consommation d'\u0082nergie: Elev\u0082e"] =
			"Port\u0082e: 120 m   Consommation d'\u0082nergie: Tr\u008as \u0082lev\u0082e",
		["Rechargement: Lent  Puissance: 1 000 giga-watts/s"] =
			"Rechargement: Moyen Puissance: 1 000 giga-watts/s",
		["Rechargement: Lent   Puissance: 900 giga-watts"] =
			"Rechargement: Moyen  Puissance: 900 giga-watts",
		["Port\u0082e: 320 m   Consommation d'\u0082nergie: Faible"] =
			"Port\u0082e: 330 m   Consommation d'\u0082nergie: Faible",
		["Rechargement: Moyen   Puissance: 300 giga-watts"] =
			"Rechargement: Rapide  Puissance: 300 giga-watts",
		["Rechargement: Moyen   Puissance: 400 giga-watts"] =
			"Rechargement: Rapide  Puissance: 400 giga-watts",
		["Port\u0082e: 220 m   Consommation d'\u0082nergie: Elev\u0082e"] =
			"Port\u0082e: 220 m   Consommation d'\u0082nergie: Moyenne",
		["Rechargement: Lent    Puissance: 500 giga-watts"] =
			"Rechargement: Rapide  Puissance: 500 giga-watts",
		["Port\u0082e: variable  Consommation d'\u0082nergie: n\u0082ant"] =
			"Port\u0082e: 320 m     Consommation d'\u0082nergie: n\u0082ant",
		["Rechargement: Lent   Puissance: 1 250 giga-watts/s"] =
			"Rechargement: Moyen  Puissance: 1 250 giga-watts/s",
		["Port\u0082e: 300 m   Consommation d'\u0082nergie: Tr\u008as \u0082lev\u0082e"] =
			"Port\u0082e: 300 m   Consommation d'\u0082nergie: Elev\u0082e",
		["Rechargement: Lent   Puissance: 1 800 (900X2) giga-watts"] =
			"Rechargement: Moyen  Puissance: 1 800 (900X2) giga-watts",
		["Port\u0082e: 210 m   Consommation d'\u0082nergie: Tr\u008as \u0082lev\u0082e"] =
			"Port\u0082e: 210 m   Consommation d'\u0082nergie: Elev\u0082e",
		["Rechargement: Lent   Puissance: 1 250 giga-watts"] =
			"Rechargement: Moyen  Puissance: 1 250 giga-watts",
		["Port\u0082e: 510 m   D\u0082g\u0083ts caus\u0082s par les ogives: 3 000"] =
			"Port\u0082e: 320 m   D\u0082g\u0083ts caus\u0082s par les ogives: 3 000",
		["Port\u0082e: 420 m   D\u0082g\u0083ts caus\u0082s par les ogives: 2 250"] =
			"Port\u0082e: 320 m   D\u0082g\u0083ts caus\u0082s par les ogives: 2 250",
		["Port\u0082e: 420 m   D\u0082g\u0083ts caus\u0082s par les ogives: 2 500"] =
			"Port\u0082e: 320 m   D\u0082g\u0083ts caus\u0082s par les ogives: 2 500",
		["Port\u0082e: 360 m   D\u0082g\u0083ts caus\u0082s par les ogives: 3 500"] =
			"Port\u0082e: 320 m   D\u0082g\u0083ts caus\u0082s par les ogives: 3 500",
		["Port\u0082e: 320 m     R\u0082cup. n\u0082cessaire: 8 000 kg"] =
			"Port\u0082e: 330 m     R\u0082cup. n\u0082cessaire: 8 000 kg",
		["Port\u0082e:           R\u0082cup. n\u0082cessaire: 6 000 kg"] =
			"Port\u0082e: 320 m     R\u0082cup. n\u0082cessaire: 6 000 kg",
		["Port\u0082e:           R\u0082cup. n\u0082cessaire: 12 000 kg"] =
			"Port\u0082e: 320 m     R\u0082cup. n\u0082cessaire: 12 000 kg",
		["Port\u0082e:        R\u0082cup. n\u0082cessaire: 18 000 kg"] =
			"Port\u0082e: 320 m  R\u0082cup. n\u0082cessaire: 18 000 kg",
		// German, v1.10
		["Reichweite: 120 m   Energieverbrauch: Hoch"] =
			"Reichweite: 120 m   Energieverbrauch: Sehr hoch",
		["Ladezeit: Langsam   Nominalenergie: 1000 Gigawatt/sek"] =
			"Ladezeit: Mittel    Nominalenergie: 1000 Gigawatt/sek",
		["Ladezeit: Langsam   Nominalenergie: 900 Gigawatt/sek"] =
			"Ladezeit: Mittel    Nominalenergie: 900 Gigawatt/sek",
		["Reichweite: 320 m   Energieverbrauch: Gering"] =
			"Reichweite: 330 m   Energieverbrauch: Gering",
		["Ladezeit: Mittel    Nominalenergie: 300 Gigawatt"] =
			"Ladezeit: Schnell   Nominalenergie: 300 Gigawatt",
		["Ladezeit: Mittel    Nominalenergie: 400 Gigawatt"] =
			"Ladezeit: Schnell   Nominalenergie: 400 Gigawatt",
		["Reichweite: 220 m   Energieverbrauch: Hoch"] =
			"Reichweite: 220 m   Energieverbrauch: Mittel",
		["Ladezeit: Langsam   Nominalenergie: 500 Gigawatt"] =
			"Ladezeit: Schnell   Nominalenergie: 500 Gigawatt",
		["Reichweite: Untersch. Energieverbrauch: Null"] =
			"Reichweite: 320 m     Energieverbrauch: Null",
		["Ladezeit: Langsam Nominalenergie: 1250 Gigawatt/sek"] =
			"Ladezeit: Mittel  Nominalenergie: 1250 Gigawatt/sek",
		["Reichweite: 300 m  Energieverbrauch: Sehr hoch"] =
			"Reichweite: 300 m  Energieverbrauch: Hoch",
		["Ladezeit: Langsam  Nominalenergie: 1800 (900X2) Gigawatt"] =
			"Ladezeit: Mittel   Nominalenergie: 1800 (900X2) Gigawatt",
		["Reichweite: 210 m  Energieverbrauch: Sehr hoch"] =
			"Reichweite: 210 m  Energieverbrauch: Hoch",
		["Ladezeit: Langsam  Nominalenergie: 1250 Gigawatt"] =
			"Ladezeit: Mittel   Nominalenergie: 1250 Gigawatt",
		["Reichweite: 510 m  Sprengkopfst\u0084rke: 3000"] =
			"Reichweite: 320 m  Sprengkopfst\u0084rke: 3000",
		["Reichweite: 420 m  Sprengkopfst\u0084rke: 2250"] =
			"Reichweite: 320 m  Sprengkopfst\u0084rke: 2250",
		["Reichweite: 420 m  Sprengkopfst\u0084rke: 2500"] =
			"Reichweite: 320 m  Sprengkopfst\u0084rke: 2500",
		["Reichweite: 360 m  Sprengkopfst\u0084rke: 3500"] =
			"Reichweite: 320 m  Sprengkopfst\u0084rke: 3500",
		["Reichweite: 320 m  Erforderl. Material: 8.000 kg"] =
			"Reichweite: 330 m  Erforderl. Material: 8.000 kg",
		["Reichweite:        Erforderl. Material: 6.000 kg"] =
			"Reichweite: 320 m  Erforderl. Material: 6.000 kg",
		["Reichweite:        Erforderl. Material: 12.000 kg"] =
			"Reichweite: 320 m  Erforderl. Material: 12.000 kg",
		["Reichweite:        Erforderl. Material: 18.000 kg"] =
			"Reichweite: 320 m  Erforderl. Material: 18.000 kg",
	};

	/// <summary><paramref name="text"/>, or its corrected line when the tweak is on and it has one.</summary>
	public static string? Correct(string? text) =>
		text != null && Corrections.TryGetValue(text, out string? corrected)
			&& TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.ShowCorrectStats)
			? corrected
			: text;
}
