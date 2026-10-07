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
		  objectives [<mission>...]        every row #17 objective, of every mission or of those named:
		                                   mandatory or failure, condition, subject, route, failure
		                                   text and counter writes

		Options:
		  --install <dir>   the install to read (default: the nearest ES2\ folder above the working
		                    directory). ZONES.VOL and SIMVOL0.VOL are read from <dir>\VOL, or from the
		                    VOL folder of the directory <dir>\DATA\drive.cfg names.
		  --variants        decode the condition on each conditioned record ("flag 625 > 0")
		  --route-switch    orders: list only groups with a later order naming a route other than
		                    slot 0's
		  --with-point      orders: list only groups with an order that names a row #6 point
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
		string? install = null, type = null;
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

		var data = RetailData.Load(install);
		string command = positional[0].ToLowerInvariant();
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

		if (command is "actions" or "orders" or "objectives"
				&& positional.Skip(1).FirstOrDefault(n => !data.Missions.Any(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase))) is { } unknown) {
			return Fail($"No mission is named {unknown}; `missions` lists them.");
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
