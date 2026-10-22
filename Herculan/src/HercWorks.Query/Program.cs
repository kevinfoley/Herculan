using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using HercWorks.Core.Io.Transform;

namespace HercWorks.Query;

/// <summary>
/// Searches the retail missions. Every file is parsed by HercWorks.Core; see <see cref="RetailData"/>
/// for what is read from where.
/// </summary>
internal static class Program {
	private const string Usage = """
		HercWorks.Query — search the retail .MSN missions and chassis records of an Earthsiege 2 or a
		Metaltech: Earthsiege (ES1) install. The install is ES1's when its root holds GO.EXE, else ES2's.
		Rows are numbered in each game's own order: ES2's #1-#17, ES1's 1-11.

		Both games:
		  structures --type <index|name>   base records of a BASES.DAT type: ES2's row #14, ES1's row 10
		  mechs      --type <index|name>   mech records of a MECHS.NAM type: ES2's row #12, ES1's row 8
		  flyers     --type <index|name>   flyer records of a FLYERS.NAM type: ES2's row #13, ES1's row 9
		  types structures|mechs|flyers    list a roster's type table
		  missions                         every mission's record count per row: ES2's rows #1, #12, #13,
		                                   #14, #16 and its .ENG; all eleven of ES1's, and which files are
		                                   in the other layout
		  patches [<mission>...]           every row #2 header patch, of every mission or of those named:
		                                   its condition and ten header words; ES1's with its theater
		                                   (word 0) and zone (word 1), and an other-layout file's own words
		  orders [<mission>...]            ES2: every row #16 group's row #15 orders, of every mission or
		                                   of those named: verb, formation, point, route, subject and the
		                                   action that ends each. ES1: every row 11 group's order (0x32),
		                                   formation (0x30), member kind (0x2e), side word, target and
		                                   action, after a census of the order values
		  conditions [<mission>...]        every row #1 condition, of every mission or of those named:
		                                   the test, draw or variant table, its chain, and what it gates
		  repeats [<mission>...]           every GUID two or more unconditioned records carry in ES2's
		                                   rows #3, #6, #7, #8 or #9 or ES1's rows 3 to 6, where the last
		                                   replaces the rest
		  census                           every entry of every archive in the install's VOL folder
		                                   through the HercWorks reader bound to it, written back and
		                                   compared: per archive and type, how many round-trip, parse,
		                                   fail or have no reader, then each failure's cursor offset

		ES2 only (over an ES1 install each prints one line saying why, and exits 0):
		  flag <n>                         every record testing or writing campaign flag n
		  actions [<mission>...]           every row #10 action of every mission, or of those named:
		                                   its trigger subject, areas, verb, the records firing it and
		                                   the groups waiting on it
		  routes [<mission>...]            every row #8 route, of every mission or of those named: its
		                                   waypoints' coordinates and leg lengths, whether it closes on
		                                   itself, and the group orders and objectives naming it
		  objectives [<mission>...]        every row #17 objective, of every mission or of those named:
		                                   mandatory or failure, condition, subject, route, failure
		                                   text and counter writes
		  lint [<mission>...]              the mission check over the script.dat each mission's load
		                                   writes, every campaign flag zero and every roll 0
		  text <mission>... [--ids LO-HI]  the named missions' .ENG text records in file order, each with
		                                   its decoded condition; a later record of an id replaces the
		                                   line when its condition holds
		  chassis [--field <name|offset>]  every chassis' dat\<name>.DAT type record, in MECHS.NAM order:
		                                   with --field, each chassis' value of that field in decimal
		                                   and hex; without, the record's fields with their offsets and
		                                   types. --type picks the chassis (default all)

		Options:
		  --install <dir>   the install to read (default: the nearest ES2\ folder above the working
		                    directory; name an ES1 install to read ES1's). ZONES.VOL and SIMVOL0.VOL are
		                    read from <dir>\VOL, or from the VOL folder of the directory <dir>\DATA\drive.cfg
		                    names.
		  --against <dir>   census: count the entries whose folder, name and bytes match an entry of
		                    this install's archives
		  --variants        decode the condition on each conditioned record ("flag 625 > 0")
		  --route-switch    orders, ES2: list only groups with a later order naming a route other than
		                    slot 0's
		  --with-point      orders, ES2: list only groups with an order that names a row #6 point
		  --ids LO-HI       text: list only the records whose id is in this range (--ids 200-204);
		                    routes: only the routes whose GUID is in it
		  --field <f>       chassis: a record field, by name (whole, then prefix) or by record offset
		                    (0x52, 82). An offset that starts no field prints the word stored there
		                    and names the field it falls in. Record offset N is the exe's type-record
		                    offset N+2.
		  --json            print the raw result as JSON

		A type is a decimal or 0x-hex index, a name matched whole and then as a prefix, or all
		(--type TRANSPORT, --type 0x22, --type 34, --type all).
		""";

	public static int Main(string[] args) {
		try {
			return Run(args, Console.Out);
		} catch (Exception ex) when (ex is IOException or InvalidDataException) {
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
	}

	internal static int Run(string[] args, TextWriter output) {
		string? install = null, type = null, against = null, ids = null, field = null;
		bool json = false, variants = false, routeSwitch = false, withPoint = false;
		var positional = new List<string>();
		for (int i = 0; i < args.Length; i++) {
			switch (args[i].ToLowerInvariant()) {
				case "--install" when i + 1 < args.Length:
					install = args[++i];
					break;
				case "--type" when i + 1 < args.Length:
					type = args[++i];
					break;
				case "--against" when i + 1 < args.Length:
					against = args[++i];
					break;
				case "--field" when i + 1 < args.Length:
					field = args[++i];
					break;
				case "--ids" when i + 1 < args.Length:
					ids = args[++i];
					break;
				case "--json":
					json = true;
					break;
				case "--variants":
					variants = true;
					break;
				case "--route-switch":
					routeSwitch = true;
					break;
				case "--with-point":
					withPoint = true;
					break;
				case "-h" or "--help" or "/?":
					output.WriteLine(Usage);
					return 0;
				default:
					if (args[i].StartsWith("--")) {
						return Fail($"Unknown option {args[i]}, or it is missing its value.");
					}
					positional.Add(args[i]);
					break;
			}
		}

		if (positional.Count == 0) {
			output.WriteLine(Usage);
			return 2;
		}

		install ??= RetailData.FindDefaultInstall();
		if (install == null) {
			return Fail("No ES2\\VOL\\ZONES.VOL above the working directory; pass --install <dir>.");
		}

		string command = positional[0].ToLowerInvariant();
		if (command == "census") {
			var entries = Census.Run(install, against);
			if (json) {
				WriteJson(output, entries);
			} else {
				TextReport.Census(output, install, entries);
			}

			return 0;
		}

		var data = RetailData.Load(install);
		bool es1 = data.Game == RetailGame.Earthsiege1;
		if (es1 && Es2Only(command) is { } why) {
			output.WriteLine(why);
			return 0;
		}

		if (command == "flag") {
			if (positional.Count < 2 || !short.TryParse(positional[1], out short flag) || flag < 0) {
				return Fail("flag takes a flag index, 0-999.");
			}

			var result = FlagQuery.Run(data, flag);
			if (json) {
				WriteJson(output, result);
			} else {
				TextReport.Flag(output, data, result);
			}

			return 0;
		}

		if (command == "chassis") {
			return Chassis(output, data, type, field, json);
		}

		if (command == "missions" && es1) {
			var counts = data.Es1Missions.Select(m => new Es1MissionSummary(m.Name, m.OtherLayout, [
				m.File.Conditions.Length, m.File.HeaderPatches.Length, m.File.Points.Length, m.File.Headings.Length,
				m.File.WaypointGroups.Length, m.File.TriggerAreas.Length, m.File.Actions.Length, m.File.Mechs.Length,
				m.File.Flyers.Length, m.File.Bases.Length, m.File.Groups.Length])).ToList();
			if (json) {
				WriteJson(output, counts);
			} else {
				TextReport.Es1Missions(output, data, counts);
			}

			return 0;
		}

		if (command == "missions") {
			var rows = data.Missions.Select(m => new MissionSummary(m.Name, m.File.Conditions?.Length ?? 0,
				m.File.Mechs?.Length ?? 0, m.File.Flyers?.Length ?? 0, m.File.Bases?.Length ?? 0,
				m.File.Groups?.Length ?? 0, m.Text?.Strings?.Length)).ToList();
			if (json) {
				WriteJson(output, rows);
			} else {
				TextReport.Missions(output, data, rows);
			}

			return 0;
		}

		if (command is "actions" or "orders" or "routes" or "objectives" or "conditions" or "text" or "lint" or "repeats" or "patches"
				&& positional.Skip(1).FirstOrDefault(n => !data.MissionNames.Contains(n, StringComparer.OrdinalIgnoreCase)) is { } unknown) {
			return Fail($"No mission is named {unknown}; `missions` lists them.");
		}

		if (command == "patches") {
			var patches = PatchQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, patches);
			} else {
				PatchQuery.Write(output, patches);
			}

			return 0;
		}

		short lowId = short.MinValue, highId = short.MaxValue;
		if (ids != null) {
			var bounds = ids.Split('-');
			if (bounds.Length != 2 || !short.TryParse(bounds[0], out lowId) || !short.TryParse(bounds[1], out highId)) {
				return Fail("--ids takes a range, LO-HI.");
			}
		}

		if (command == "routes") {
			var routes = RouteQuery.Run(data, positional.Skip(1).ToList(), lowId, highId);
			if (json) {
				WriteJson(output, routes);
			} else {
				TextReport.Routes(output, routes);
			}

			return 0;
		}

		if (command == "text") {
			if (positional.Count < 2) {
				return Fail("text takes one or more mission names.");
			}

			var text = TextQuery.Run(data, positional.Skip(1).ToList(), lowId, highId);
			if (json) {
				WriteJson(output, text);
			} else {
				TextQuery.Write(output, text);
			}

			return 0;
		}

		if (command == "lint") {
			var lint = LintQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, lint);
			} else {
				LintQuery.Write(output, lint);
			}

			return 0;
		}

		if (command == "repeats") {
			var repeats = es1 ? RepeatQuery.RunEs1(data, positional.Skip(1).ToList()) : RepeatQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, repeats);
			} else {
				RepeatQuery.Write(output, repeats);
			}

			return 0;
		}

		if (command == "conditions") {
			var conditions = es1 ? ConditionQuery.RunEs1(data, positional.Skip(1).ToList()) : ConditionQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, conditions);
			} else {
				ConditionQuery.Write(output, conditions);
			}

			return 0;
		}

		if (command == "orders" && es1) {
			if (routeSwitch || withPoint) {
				return Fail("--route-switch and --with-point filter ES2's row #15 order records; ES1 missions have no such row.");
			}

			var es1Orders = Es1OrderQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, es1Orders);
			} else {
				Es1OrderQuery.Write(output, es1Orders);
			}

			return 0;
		}

		if (command == "orders") {
			var orders = OrderQuery.Run(data, positional.Skip(1).ToList(), routeSwitch, withPoint);
			if (json) {
				WriteJson(output, orders);
			} else {
				TextReport.Orders(output, orders);
			}

			return 0;
		}

		if (command == "objectives") {
			var objectives = ObjectiveQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, objectives);
			} else {
				TextReport.Objectives(output, objectives);
			}

			return 0;
		}

		if (command == "actions") {
			var actions = ActionQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, actions);
			} else {
				TextReport.Actions(output, actions);
			}

			return 0;
		}

		if (command == "types") {
			if (positional.Count < 2 || RosterOf(positional[1]) is not { } listed) {
				return Fail("types takes structures, mechs or flyers.");
			}

			var table = Enumerable.Range(0, data.TypeCount(listed))
				.Select(t => new { Type = t, Hex = $"0x{t:x2}", Name = data.TypeName(listed, t) })
				.ToList();
			if (json) {
				WriteJson(output, table);
			} else {
				foreach (var row in table) {
					output.WriteLine($"{row.Type,3}  {row.Hex}  {row.Name}");
				}
			}

			return 0;
		}

		if (RosterOf(command) is not { } kind) {
			return Fail($"Unknown command {positional[0]}.");
		}

		if (type == null) {
			return Fail($"{command} needs --type <index|name>.");
		}

		if (TypeQuery.ResolveTypes(data, kind, type) is not { } types) {
			return Fail($"No {Roster.Noun(kind)} type is named {type}; `types {command}` lists them.");
		}

		if (es1) {
			var es1Query = Es1TypeQuery.Run(data, kind, types);
			if (json) {
				WriteJson(output, es1Query);
			} else {
				Es1TypeQuery.Write(output, data, es1Query, variants);
			}

			return 0;
		}

		var query = TypeQuery.Run(data, kind, types);
		if (json) {
			WriteJson(output, query);
		} else {
			TextReport.Types(output, data, query, variants);
		}

		return 0;
	}

	/// <summary>
	/// The line an ES2-only command prints over an ES1 install: why it has no ES1 answer (docs/es1/formats/msn-mission-file.md#the-layout).
	/// Null for a command that answers for both games.
	/// </summary>
	private static string? Es2Only(string command) => command switch {
		"text" => "ES1 missions have no text rows: their .ENG holds the shell's briefing, objectives and debriefing, which the mission load does not read.",
		"objectives" => "ES1 missions have no objectives row (ES2's row #17).",
		"flag" => "flag reads ES2's campaign flags and the rows that write them; it has no ES1 counterpart.",
		"actions" => "actions reads ES2's row #10 actions, their counters, messages and timers; it has no ES1 counterpart.",
		"routes" => "routes reads ES2's row #8 routes and the row #15 orders and row #17 objectives naming them; it has no ES1 counterpart.",
		"lint" => "lint checks the script.dat ES2's mission load writes; it has no ES1 counterpart.",
		"chassis" => "chassis reads ES2's 216-byte dat\\<name>.DAT records; ES1's 192-byte records have their own reader (docs/es1/formats/chassis-dat.md), which this command does not use.",
		_ => null,
	};

	private static int Chassis(TextWriter output, RetailData data, string? type, string? spec, bool json) {
		if (data.Chassis.FirstOrDefault(c => c.Bytes != null)?.Bytes is not { } sample) {
			return Fail($"No chassis record in {RetailData.SimulatorArchive}.");
		}

		var fields = ChassisQuery.Fields(sample.Length);
		var chassis = data.Chassis;
		if (type != null) {
			if (TypeQuery.ResolveTypes(data, RosterKind.Mech, type) is not { } types
					|| chassis.Where(c => types.Contains(c.Index)).ToList() is not { Count: > 0 } picked) {
				return Fail($"No chassis is named {type}; `types mechs` lists them.");
			}

			chassis = picked;
		}

		if (spec == null) {
			if (json) {
				WriteJson(output, fields);
			} else {
				ChassisQuery.WriteFields(output, fields, sample.Length);
			}

			return 0;
		}

		if (ChassisQuery.ResolveOffset(fields, spec, sample.Length, out var named, out string? error) is not { } offset) {
			return Fail(error!);
		}

		var result = ChassisQuery.Run(chassis, fields, offset, named);
		if (json) {
			WriteJson(output, result);
		} else {
			ChassisQuery.Write(output, result);
		}

		return 0;
	}

	private static RosterKind? RosterOf(string word) => word.ToLowerInvariant() switch {
		"structures" or "bases" => RosterKind.Base,
		"mechs" or "hercs" => RosterKind.Mech,
		"flyers" => RosterKind.Flyer,
		_ => null,
	};

	private static readonly JsonSerializerOptions JsonOptions = new() {
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Converters = { new JsonStringEnumConverter() },
	};

	private static void WriteJson<T>(TextWriter output, T value) => output.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

	private static int Fail(string message) {
		Console.Error.WriteLine(message);
		return 2;
	}
}
