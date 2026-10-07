namespace HercWorks.Query;

/// <summary>The plain-text form of each query's result.</summary>
internal static class TextReport {
	public static void Types(TextWriter o, RetailData data, TypeQueryResult result, bool variants) {
		string noun = Roster.Noun(result.Roster);
		string types = string.Join(", ", result.Types.Select((t, i) => Label(t, result.TypeNames[i])));
		o.WriteLine($"Row #{result.Row} ({noun}) type {types}: {result.Missions.Count} of {result.MissionsSearched} missions, " +
			$"{result.Records} records, {result.Placed} placed by a group, {result.AnyDeploymentGated} by a group that waits on a deployment action " +
			$"({result.DeploymentGated} by such groups alone).");
		if (result.Missions.Count == 0) {
			return;
		}

		o.WriteLine();
		o.WriteLine("Mission   Records  Placed  Deploy-gated  Any-deploy  Cond-gated  Sides");
		foreach (var m in result.Missions) {
			o.WriteLine($"{m.Mission,-9} {m.Records,7}  {m.Placed,6}  {m.DeploymentGated,12}  {m.AnyDeploymentGated,10}  {m.ConditionGated,10}  {string.Join(", ", m.Sides.Select(FlagQuery.Side))}");
		}

		foreach (var m in result.Missions) {
			o.WriteLine();
			o.WriteLine($"{m.Mission}");
			foreach (var hit in m.Hits) {
				var line = new List<string> { hit.Guid == -1 ? $"#{hit.Index} (no GUID)" : $"GUID {hit.Guid}" };
				if (result.Types.Count > 1 || !result.Types.Contains(hit.TypeIndex)) {
					line.Add(Label(hit.TypeIndex, hit.TypeName));
				}

				if (hit.StartingCondition is { } start) {
					line.Add($"start {start}%");
				}

				if (hit.ConditionRef != -1) {
					line.Add(Cond(hit.ConditionRef, hit.Condition, variants));
				}

				if (hit.VariantKey != -1) {
					line.Add($"variant key {hit.VariantKey}: type and payload drawn");
				}

				if (hit.VariantSourceKey is { } key) {
					line.Add($"variant of key {key}, drawn by {(hit.DrawnBy.Count == 0 ? "nothing" : "GUID " + string.Join(", ", hit.DrawnBy))}");
				}

				o.WriteLine("  " + string.Join(", ", line));

				if (hit.PlacedBy.Count == 0) {
					o.WriteLine("    not placed by any group");
				}

				foreach (var g in hit.PlacedBy) {
					var parts = new List<string> { $"placed by group {g.Guid} slot {g.Slot}", FlagQuery.Side(g.Side) };
					if (g.DeploymentGated) {
						parts.Add(g.DeploymentVerb is { } verb
							? $"deploys on action {g.DeploymentActionRef} (verb {verb})"
							: $"deploys on action {g.DeploymentActionRef}");
					}

					if (g.ConditionRef != -1) {
						parts.Add(Cond(g.ConditionRef, g.Condition, variants));
					}

					if (g.FirstRecord) {
						parts.Add("row #16's first record, whose members the simulator does not read");
					}

					o.WriteLine("    " + string.Join(", ", parts));
				}

				foreach (var s in hit.SameGuid) {
					var parts = new List<string> { $"same GUID, #{s.Index}" };
					parts.Add(s.ConditionRef == -1 ? "unconditioned" : Cond(s.ConditionRef, s.Condition, variants));
					parts.Add(s.TypeIndex == -1 ? "type unchanged" : Label(s.TypeIndex, data.TypeName(result.Roster, s.TypeIndex)));
					if (s.StartingCondition is { } sc) {
						parts.Add($"start {sc}%");
					}

					o.WriteLine("    " + string.Join(", ", parts));
				}
			}
		}
	}

	public static void Flag(TextWriter o, RetailData data, FlagQueryResult result) {
		o.WriteLine($"Campaign flag {result.Flag} across {result.MissionsSearched} missions: " +
			$"{result.Tests.Count} tests, {result.Writes.Count} writes.");

		o.WriteLine();
		o.WriteLine("Tested by");
		if (result.Tests.Count == 0) {
			o.WriteLine("  nothing");
		}

		foreach (var t in result.Tests) {
			o.WriteLine($"  {t.Mission,-9} cond {t.ConditionGuid} (row #1 #{t.Index}): {t.Chain}");
			o.WriteLine(t.Gates.Count == 0
				? "            gates nothing"
				: "            gates " + string.Join(", ", t.Gates.Select(g => g.Label)));
		}

		o.WriteLine();
		o.WriteLine("Written by");
		if (result.Writes.Count == 0) {
			o.WriteLine("  nothing");
		}

		foreach (var w in result.Writes) {
			string condition = w.Condition == null ? "" : $" [{w.Condition}]";
			o.WriteLine($"  {w.Mission,-9} row #{w.Row} {w.Subject}: {w.Effect}{condition}");
		}
	}

	public static void Missions(TextWriter o, RetailData data, IReadOnlyList<MissionSummary> rows) {
		o.WriteLine($"{rows.Count} missions in {data.MissionArchivePath}");
		o.WriteLine();
		o.WriteLine("Mission   Row#1  Row#12  Row#13  Row#14  Row#16  ENG");
		foreach (var r in rows) {
			o.WriteLine($"{r.Mission,-9} {r.Conditions,5}  {r.Mechs,6}  {r.Flyers,6}  {r.Bases,6}  {r.Groups,6}  {(r.TextRecords?.ToString() ?? "none")}");
		}

		o.WriteLine($"{"Total",-9} {rows.Sum(r => r.Conditions),5}  {rows.Sum(r => r.Mechs),6}  {rows.Sum(r => r.Flyers),6}  " +
			$"{rows.Sum(r => r.Bases),6}  {rows.Sum(r => r.Groups),6}  {rows.Sum(r => r.TextRecords ?? 0)}");
	}

	public static void Actions(TextWriter o, IReadOnlyList<ActionQueryMission> missions) {
		foreach (var m in missions) {
			o.WriteLine($"{m.Mission}: {m.Actions.Count} actions");
			foreach (var a in m.Actions) {
				var line = new List<string> {
					a.Guid == -1 ? $"#{a.Index} (no GUID)" : $"action {a.Guid} (#{a.Index})",
					$"type {a.Type} ({ActionQuery.Subject(a.Type)}{(a.TargetRef is { } target ? " " + target : "")})",
					$"verb {a.Verb} ({ActionQuery.Arrival(a.Verb)})",
				};
				if (a.ConditionRef != -1) {
					line.Add(Cond(a.ConditionRef, a.Condition, true));
				}

				if (a.MessageId != 0) {
					line.Add($"message {a.MessageId - 1}");
				}

				o.WriteLine("  " + string.Join(", ", line));
				foreach (var area in a.Areas) {
					o.WriteLine("    " + Area(area));
				}

				if (a.FiredBy.Count > 0) {
					o.WriteLine("    fired by " + string.Join(", ", a.FiredBy.Select(s => s.Row == "timer" ? $"timer {s.Guid}" : $"{s.Row} {s.Guid} {s.What}")));
				}

				if (a.DeploysGroups.Count > 0) {
					o.WriteLine("    deploys group " + string.Join(", ", a.DeploysGroups.Select(g => g.X is { } x ? $"{g.Guid} at point {g.PointRef} ({x}, {g.Y})" : $"{g.Guid}")));
				}
			}

			o.WriteLine();
		}
	}

	public static void Objectives(TextWriter o, IReadOnlyList<ObjectiveQueryMission> missions) {
		foreach (var m in missions) {
			o.WriteLine($"{m.Mission}: {m.Objectives.Count} objectives");
			foreach (var r in m.Objectives) {
				var line = new List<string> {
					$"#{r.Index}",
					r.Required == 1 ? "mandatory" : $"failure condition ({r.Required})",
					$"code {r.ConditionCode} ({ObjectiveQuery.Code(r.ConditionCode)})",
					$"{OrderQuery.SubjectKind(r.SubjectKind)} {r.SubjectRef}" + (r.Subject is { } subject ? $" [{subject}]" : " [no such record]"),
				};
				if (r.RouteRef != -1) {
					line.Add($"route {r.RouteRef}" + (r.RouteOrders.Count == 0 ? " (no order of the subject runs it)" : $" (order slot {string.Join(", ", r.RouteOrders)})"));
				}

				if (r.PointRef != -1) {
					line.Add($"point {r.PointRef}");
				}

				if (r.ConditionRef != -1) {
					line.Add(Cond(r.ConditionRef, r.Condition, true));
				}

				o.WriteLine("  " + string.Join(", ", line));
				foreach (var text in r.Text) {
					o.WriteLine($"    text {text}");
				}

				if (r.Pairs.Count > 0) {
					o.WriteLine("    writes " + string.Join(", ", r.Pairs.Select(p => $"counter {p.CounterRef} {ObjectiveQuery.Op(p.Op)}")));
				}
			}

			o.WriteLine();
		}
	}

	public static void Orders(TextWriter o, OrderQueryResult result) {
		int points = result.PointsByVerb.Values.Sum();
		o.WriteLine($"{result.OrderRecords} row #15 records across {result.MissionsSearched} missions. " +
			$"{points} name a row #6 point: " +
			string.Join(", ", result.PointsByVerb.Select(p => $"{p.Value} {OrderQuery.Verb(p.Key)}")) + ".");
		o.WriteLine($"{result.RouteSwitchGroups} groups, by GUID, have a later order naming a route other than slot 0's" +
			(result.RouteSwitchMissions.Count == 0 ? "." : $", in {string.Join(", ", result.RouteSwitchMissions)}."));

		foreach (var m in result.Missions) {
			o.WriteLine();
			o.WriteLine($"{m.Mission}: {m.Groups.Count} groups listed, {m.OrderRecords} row #15 records");
			foreach (var g in m.Groups) {
				var line = new List<string> {
					g.Guid == -1 ? $"group #{g.Index} (no GUID)" : $"group {g.Guid} (#{g.Index})",
					g.PlayerSquad ? "the player's squad" : $"{g.Members} {OrderQuery.MemberKind(g.MemberKind)}{(g.Members == 1 ? "" : "s")}",
					FlagQuery.Side(g.Side),
					$"formation {g.FormationId}",
				};
				if (g.DeploymentActionRef != -1) {
					line.Add($"deploys on action {g.DeploymentActionRef}");
				}

				if (g.ConditionRef != -1) {
					line.Add(Cond(g.ConditionRef, g.Condition, true));
				}

				o.WriteLine("  " + string.Join(", ", line));
				if (g.Slots.Count == 0) {
					o.WriteLine("    no orders");
				}

				foreach (var s in g.Slots) {
					if (s.Records.Count == 0) {
						o.WriteLine($"    slot {s.Slot}: order {s.Ref}, no such row #15 record");
					}

					foreach (var r in s.Records) {
						o.WriteLine($"    slot {s.Slot}: {Order(r, s.Ref)}");
					}
				}
			}
		}
	}

	private static string Order(OrderRecord r, short guid) {
		var parts = new List<string> { $"order {guid} (#{r.Index})", OrderQuery.Verb(r.Verb), $"formation {r.FormationId}" };
		if (r.PointRef != -1) {
			parts.Add(r.PointFound ? $"point {r.PointRef} ({r.X}, {r.Y})" : $"point {r.PointRef} (no such row #6 record)");
		}

		if (r.RouteRef != -1) {
			string route = r.RouteWaypoints is { } n ? $"route {r.RouteRef} ({n} waypoints)" : $"route {r.RouteRef} (no such row #8 record)";
			parts.Add(r.RouteDiffers ? route + ", not slot 0's" : route);
		}

		if (r.SubjectKind != -1) {
			parts.Add($"subject {OrderQuery.SubjectKind(r.SubjectKind)} {r.SubjectRef}");
		}

		if (r.ActionRef != -1) {
			parts.Add($"ends on action {r.ActionRef}");
		}

		if (r.ConditionRef != -1) {
			parts.Add(Cond(r.ConditionRef, r.Condition, true));
		}

		return string.Join(", ", parts);
	}

	private static string Area(ActionArea a) {
		if (!a.Found) {
			return $"area {a.Guid}: no such row #9 record";
		}

		string first = a.X is { } x ? $"({x}, {a.Y})" : "missing";
		if (a.Radius is { } radius) {
			return $"area {a.Guid}: circle r {radius} about point {a.PointRef} {first}";
		}

		string second = a.X2 is { } x2 ? $"({x2}, {a.Y2})" : "missing";
		return $"area {a.Guid}: box point {a.PointRef} {first} to point {a.SecondPointRef} {second}";
	}

	private static string Label(int type, string? name) => name == null ? $"0x{type:x2}" : $"0x{type:x2} {name}";

	private static string Cond(short guid, string? text, bool decode) =>
		decode && text != null ? $"cond {guid} ({text})" : $"cond {guid}";
}
