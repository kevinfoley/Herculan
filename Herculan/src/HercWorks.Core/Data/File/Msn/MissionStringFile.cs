using System.Text;

namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// FILE - (Mission).ENG, .GER, .FRE — a mission's text, records keyed by id. The mission load reads
/// it itself (<see cref="Io.Transform.Common.MissionGenerator"/>) and writes the survivors to
/// <c>data\mission.str</c>. See docs/retail/formats/msn-mission-file.md#the-eng-string-table.
/// </summary>
public class MissionStringFile {
	public int TotalSize { get; set; }
	public StringEntry[]? Strings { get; set; }

	public StringEntry CreateEntry(short guid, short conditionRef, short parentRef, short len, string val) {
		return new StringEntry {
			Guid = guid,
			ConditionRef = conditionRef,
			ParentRef = parentRef,
			Len = len,
			Val = val
		};
	}

	public class StringEntry {
		/// <summary>The id the <c>.MSN</c> rows' text refs name.</summary>
		public short Guid { get; set; }

		/// <summary>Condition ref, as in the <c>.MSN</c> rows, or <c>-1</c>.</summary>
		public short ConditionRef { get; set; }

		/// <summary><c>-99</c> beside a condition, <c>-1</c> otherwise (docs/retail/formats/msn-mission-file.md#the-eng-string-table).</summary>
		public short ParentRef { get; set; }

		/// <summary>The text's length, including its NUL.</summary>
		public short Len { get; set; }

		public string? Val { get; set; }

		public override string ToString() {
			var str = new StringBuilder();

			str.Append("{")
				.Append(" guid = ").Append(Guid)
				.Append(", condition = ").Append(ConditionRef)
				.Append(", parent = ").Append(ParentRef)
				.Append(", len = ").Append(Len)
				.Append(", val = ").Append(Val)
				.Append("}");

			return str.ToString();
		}
	}
}
