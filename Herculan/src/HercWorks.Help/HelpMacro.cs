namespace HercWorks.Help;

/// <summary>One macro call: its name and its arguments, quoted strings unquoted.</summary>
public sealed record HelpMacroCall(string Name, IReadOnlyList<string> Arguments);

/// <summary>
/// Splits a macro string into calls (docs/retail/formats/winhelp.md#macros). The syntax is
/// <c>Name(arg, arg);Name(...)</c>, where a string argument is quoted <c>`like this'</c> and quotes
/// nest, so <c>`JI(`',`ctx')'</c> is one argument holding a macro of its own.
///
/// <para>This only splits. Nothing here runs a macro: a reader acts on the few names it recognises
/// and ignores the rest.</para>
/// </summary>
public static class HelpMacro {
	private const int MaxCalls = 16;
	private const int MaxArguments = 16;

	/// <summary>The calls in <paramref name="macro"/>, or null when it does not parse.</summary>
	public static IReadOnlyList<HelpMacroCall>? Parse(string macro) {
		ArgumentNullException.ThrowIfNull(macro);
		var calls = new List<HelpMacroCall>();
		int i = 0;
		while (true) {
			SkipSpaces(macro, ref i);
			int nameStart = i;
			while (i < macro.Length && char.IsAsciiLetter(macro[i])) {
				i++;
			}

			if (i == nameStart || i >= macro.Length || macro[i] != '(' || calls.Count >= MaxCalls) {
				return null;
			}

			string name = macro[nameStart..i];
			i++;
			var arguments = new List<string>();
			SkipSpaces(macro, ref i);
			if (i < macro.Length && macro[i] == ')') {
				i++;
			} else {
				while (true) {
					if (ReadArgument(macro, ref i) is not { } argument || arguments.Count >= MaxArguments) {
						return null;
					}

					arguments.Add(argument);
					SkipSpaces(macro, ref i);
					if (i >= macro.Length) {
						return null;
					}

					char separator = macro[i++];
					if (separator == ')') {
						break;
					}

					if (separator != ',') {
						return null;
					}
				}
			}

			calls.Add(new HelpMacroCall(name, arguments));
			SkipSpaces(macro, ref i);
			if (i >= macro.Length) {
				return calls;
			}

			if (macro[i++] != ';') {
				return null;
			}
		}
	}

	private static string? ReadArgument(string macro, ref int i) {
		SkipSpaces(macro, ref i);
		if (i < macro.Length && macro[i] == '`') {
			int start = ++i;
			int depth = 1;
			for (; i < macro.Length; i++) {
				if (macro[i] == '`') {
					depth++;
				} else if (macro[i] == '\'' && --depth == 0) {
					return macro[start..i++];
				}
			}

			return null;
		}

		int plain = i;
		while (i < macro.Length && macro[i] is not (',' or ')')) {
			i++;
		}

		return macro[plain..i].Trim();
	}

	private static void SkipSpaces(string macro, ref int i) {
		while (i < macro.Length && macro[i] == ' ') {
			i++;
		}
	}
}
