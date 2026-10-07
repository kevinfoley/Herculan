namespace HercWorks.Help;

/// <summary>
/// The hash a jump names its target by (docs/retail/formats/winhelp.md#context-hashes). Only letters, digits
/// and <c>_</c> have known values; a context string with any other character has no hash here.
/// </summary>
public static class ContextHash {
	public static bool TryCompute(string context, out uint hash) {
		ArgumentNullException.ThrowIfNull(context);
		hash = 0;
		foreach (char c in context) {
			uint value;
			if (c is >= 'A' and <= 'Z') {
				value = (uint)(c - 'A' + 17);
			} else if (c is >= 'a' and <= 'z') {
				value = (uint)(c - 'a' + 17);
			} else if (c is >= '1' and <= '9') {
				value = (uint)(c - '0');
			} else if (c == '0') {
				value = 10;
			} else if (c == '_') {
				value = 13;
			} else {
				hash = 0;
				return false;
			}

			hash = unchecked(hash * 43 + value);
		}

		return true;
	}
}
