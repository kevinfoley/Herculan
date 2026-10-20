using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HercWorks.Query;

/// <summary>
/// Searches the retail missions. Every file is parsed by HercWorks.Core; see <see cref="RetailData"/>
/// for what is read from where.
/// </summary>
internal static class Program {
	private const string Usage = """
		HercWorks.Query — search the retail .MSN missions.

		  structures --type <index|name>   row #14 records of a BASES.DAT type
		  mechs      --type <index|name>   row #12 records of a MECHS.NAM type
		  flyers     --type <index|name>   row #13 records of a FLYERS.NAM type
		  flag <n>                         every record testing or writing campaign flag n
		  types structures|mechs|flyers    list a roster's type table
		  missions                         every mission's record count per roster row
		  actions [<mission>...]           every row #10 action of every mission, or of those named:
		                                   its trigger subject, areas, verb, the records firing it and
		                                   the groups waiting on it
		  orders [<mission>...]            every row #16 group's row #15 orders, of every mission or of
		                                   those named: verb, formation, point, route, subject and the
		                                   action that ends each
		  routes [<mission>...]            every row #8 route, of every mission or of those named: its
		                                   waypoints' coordinates and leg lengths, whether it closes on
		                                   itself, and the group orders and objectives naming it
		  objectives [<mission>...]        every row #17 objective, of every mission or of those named:
		                                   mandatory or failure, condition, subject, route, failure
		                                   text and counter writes
		  conditions [<mission>...]        every row #1 condition, of every mission or of those named:
		                                   the test, draw or variant table, its chain, and what it gates
		  repeats [<mission>...]           every GUID two or more unconditioned records carry in rows
		                                   #3, #6, #7, #8 or #9, where the last replaces the rest; and
		                                   every GUID two or more records carry in rows #10 to #16,
		                                   where later ones overlay the fields they set, each with
		                                   their conditions and whether they differ
		  lint [<mission>...]              the mission check over the script.dat each mission's load
		                                   writes, every campaign flag zero and every roll 0
		  text <mission>... [--ids LO-HI]  the named missions' .ENG text records in file order, each with
		                                   its decoded condition; a later record of an id replaces the
		                                   line when its condition holds
		  records <mission> [--row N]      the mission's .MSN records as read, row by row in file order, each
		                                   with its index in the row and its named fields; raw: no condition
		                                   applied, no variant drawn, no repeated GUID merged
		  header <mission>                 the mission's row #2 records as read, beside the script.dat
		                                   header its load writes with every campaign flag zero and every
		                                   roll 0
		  values --row N --field X [<mission>...]
		                                   how many records of typed row #N (1, 3, 4, 6-17), of every mission
		                                   or of those named, carry each value of field X, most first; raw, as
		                                   records reads them

		  census                           every entry of every archive in the install's VOL folder
		                                   through the HercWorks reader bound to it, written back and
		                                   compared: per archive and type, how many round-trip, parse,
		                                   fail or have no reader, then each failure's cursor offset

		Options:
		  --install <dir>   the install to read (default: the nearest ES2\ folder above the working
		                    directory). ZONES.VOL and SIMVOL0.VOL are read from <dir>\VOL, or from the
		                    VOL folder of the directory <dir>\DATA\drive.cfg names.
		  --against <dir>   census: count the entries whose folder, name and bytes match an entry of
		                    this install's archives
		  --variants        decode the condition on each conditioned record ("flag 625 > 0")
		  --route-switch    orders: list only groups with a later order naming a route other than
		                    slot 0's
		  --with-point      orders: list only groups with an order that names a row #6 point
		  --ids LO-HI       text: list only the records whose id is in this range (--ids 200-204);
		                    routes: only the routes whose GUID is in it
		  --row N           records: list only row #N, 1-17; values: the row to count
		  --field X         values: a property of the row's record type, any case (--field TypeIndex); an
		                    array takes an element index (--field OrderRefs[0]), and without one each
		                    whole array is a value. An unknown field lists the row's fields
		  --non-squad       values: leave out the player's squad: row #16's group 0 and every later
		                    record with its GUID, the row #12 records those name and the alternatives
		                    their variant keys draw; row #15 keeps the orders a group outside it names
		  --kind K          values: mech, flyer or structure — row #16 groups naming a member of that
		                    member kind, row #15 orders such a group names
		  --where F=V       values: count only the records whose field F (as --field names it) prints
		  --where F!=V      as V, or does not; repeatable, all must hold (--where TypeIndex!=-1)
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
		string? install = null, type = null, against = null, ids = null, rowOption = null, field = null, kindOption = null;
		bool json = false, variants = false, routeSwitch = false, withPoint = false, nonSquad = false;
		var positional = new List<string>();
		var where = new List<string>();
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
				case "--ids" when i + 1 < args.Length:
					ids = args[++i];
					break;
				case "--row" when i + 1 < args.Length:
					rowOption = args[++i];
					break;
				case "--field" when i + 1 < args.Length:
					field = args[++i];
					break;
				case "--kind" when i + 1 < args.Length:
					kindOption = args[++i];
					break;
				case "--where" when i + 1 < args.Length:
					where.Add(args[++i]);
					break;
				case "--non-squad":
					nonSquad = true;
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

		if (command is "actions" or "orders" or "routes" or "objectives" or "conditions" or "text" or "lint" or "repeats" or "records" or "header" or "values"
				&& positional.Skip(1).FirstOrDefault(n => !data.Missions.Any(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase))) is { } unknown) {
			return Fail($"No mission is named {unknown}; `missions` lists them.");
		}

		short lowId = short.MinValue, highId = short.MaxValue;
		if (ids != null) {
			var bounds = ids.Split('-');
			if (bounds.Length != 2 || !short.TryParse(bounds[0], out lowId) || !short.TryParse(bounds[1], out highId)) {
				return Fail("--ids takes a range, LO-HI.");
			}
		}

		if (command is "records" or "header") {
			if (positional.Count != 2) {
				return Fail($"{command} takes one mission name.");
			}

			var mission = data.Missions.First(m => string.Equals(m.Name, positional[1], StringComparison.OrdinalIgnoreCase));
			if (command == "header") {
				var header = RecordQuery.Header(mission);
				if (json) {
					WriteJson(output, header);
				} else {
					RecordQuery.WriteHeader(output, header);
				}

				return 0;
			}

			int? rowNumber = null;
			if (rowOption != null) {
				if (!int.TryParse(rowOption, out int n) || n < 1 || n > RecordQuery.RowCount) {
					return Fail($"--row takes a row number, 1-{RecordQuery.RowCount}.");
				}

				rowNumber = n;
			}

			var records = RecordQuery.Records(mission, rowNumber);
			if (json) {
				WriteJson(output, records);
			} else {
				RecordQuery.WriteRecords(output, records);
			}

			return 0;
		}

		if (command == "values") {
			if (rowOption == null || !int.TryParse(rowOption, out int row) || field == null) {
				return Fail("values needs --row N and --field X.");
			}

			if (ValueQuery.Run(data, positional.Skip(1).ToList(), row, field, kindOption, nonSquad, where, out string? error) is not { } values) {
				return Fail(error!);
			}

			if (json) {
				WriteJson(output, values);
			} else {
				ValueQuery.Write(output, values);
			}

			return 0;
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
			var repeats = RepeatQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, repeats);
			} else {
				RepeatQuery.Write(output, repeats);
			}

			return 0;
		}

		if (command == "conditions") {
			var conditions = ConditionQuery.Run(data, positional.Skip(1).ToList());
			if (json) {
				WriteJson(output, conditions);
			} else {
				ConditionQuery.Write(output, conditions);
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

		var query = TypeQuery.Run(data, kind, types);
		if (json) {
			WriteJson(output, query);
		} else {
			TextReport.Types(output, data, query, variants);
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
