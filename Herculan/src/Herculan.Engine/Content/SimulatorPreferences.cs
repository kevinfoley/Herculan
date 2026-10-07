using HercWorks.Core.Data.File.Cfg;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Content;

/// <summary>
/// The simulator's options at run time — <c>data\prefs.cfg</c> (<see cref="Prefs"/>, which owns the
/// layout and the option indices) plus what the original hangs off it: a handler per option, the
/// load-time shadow the panels commit and revert against, and the read-modify-write save each panel
/// performs. What the [F12] preferences panel shows and edits.
///
/// <para><c>Prefs_SetOption</c> (<c>0045993c</c>) writes one byte by its index and calls the option's
/// own handler from the parallel table at <c>Prefs_OptionHandlers</c> (<c>004d2060</c>) — see <see cref="RegisterHandler"/>.
/// The panel's own reader is <c>PreferencesPanel_RefreshRow</c> (<c>004571f4</c>), which maps a byte to
/// one of the <c>PRF_ALRT.STR</c> captions — see <see cref="PreferencesPanel"/>.</para>
/// </summary>
public sealed class SimulatorPreferences {
	private readonly byte[] _options;

	private SimulatorPreferences(byte[] options, string? sourceDirectory = null) {
		_options = options;
		_baseline = (byte[])options.Clone();
		SourceDirectory = sourceDirectory;
	}

	/// <summary>The load-time shadow, <c>DAT_004d1ff2</c> — what <see cref="Commit"/> compares against.</summary>
	private readonly byte[] _baseline;

	/// <summary>
	/// The <c>data</c> folder this was read from, or null when it was not read from one. A
	/// <see cref="Save"/> goes back where it came from, as the original's does — it keeps the one path
	/// buffer at <c>Prefs_Path</c> (<c>0049e844</c>) that <c>Prefs_LoadOptions</c> reads through.
	/// </summary>
	public string? SourceDirectory { get; }

	/// <summary>
	/// Whether <see cref="Save"/> may touch the file. On by default; the host clears it for
	/// <c>--no-write-prefs</c>. Nothing in the original corresponds — retail has no reason to doubt
	/// that it owns its own install.
	/// </summary>
	public bool SaveEnabled { get; set; } = true;

	/// <summary>
	/// The nine options the preferences panel saves — <c>DAT_0049e304</c>, the list
	/// <c>PreferencesPanel_Save</c> (<c>004574cc</c>) hands to
	/// <c>Prefs_SaveSelectedOptions</c>. Its own rows, and nothing else.
	/// </summary>
	public static readonly int[] PreferencesPanelOptions =
		{ 0, 1, 2, 3, 7, 8, 9, 10, 11 };

	/// <summary>
	/// The thirteen options the controls panel saves — <c>ControlsPanel_Save</c> (<c>00459140</c>),
	/// which builds the list at run time as <c>ControlsOptionBase - 1</c> through <c>+11</c>.
	///
	/// <para>That leading entry is option 12, the joystick-configured flag, only for a walker. Flying
	/// the RAZOR the base is 0x19, so it is option 24 — the walker block's last button binding, which
	/// is rewritten with its own unchanged value. Reproduced rather than corrected: it writes what it
	/// read, so it changes nothing, and the arithmetic is the original's.</para>
	/// </summary>
	public static int[] ControlsPanelOptions(bool razor) {
		int start = Prefs.ControlsBase(razor) - 1;
		var options = new int[Prefs.ControlsOptionCount + 1];
		for (int i = 0; i < options.Length; i++) {
			options[i] = start + i;
		}

		return options;
	}

	/// <summary>
	/// The options as the simulator would hold them: all zero where no file was read, since that is
	/// what the memset leaves behind.
	/// </summary>
	public static SimulatorPreferences Defaults() => new(new Prefs().Options);

	/// <summary>Option <paramref name="index"/>'s byte, or 0 when it is outside the array.</summary>
	public byte this[int index] => index >= 0 && index < _options.Length ? _options[index] : (byte)0;

	/// <summary>
	/// Whether any option has been written since the file was read. The original tracks the same thing
	/// per option rather than for the array as a whole, marking each changed row so its DONE handler
	/// knows what to act on.
	/// </summary>
	public bool Changed { get; private set; }

	private readonly Action<byte>?[] _handlers = new Action<byte>?[Prefs.Length];

	/// <summary>
	/// Installs option <paramref name="index"/>'s handler — <c>Prefs_RegisterOptionHandler</c>
	/// (<c>00459c58</c>), into the table at <c>Prefs_OptionHandlers</c> (<c>004d2060</c>). <c>Prefs_Init</c> fills five slots
	/// and leaves the rest empty; an option with no handler is read where it is used instead. See
	/// docs/retail/simulation/preferences.md.
	/// </summary>
	public void RegisterHandler(int index, Action<byte> handler) {
		ArgumentNullException.ThrowIfNull(handler);
		if (index >= 0 && index < _handlers.Length) {
			_handlers[index] = handler;
		}
	}

	/// <summary>
	/// Whether <see cref="ApplyAll"/> is running — <c>PrefsInitInProgress</c> (<c>004d2138</c>). The
	/// MUSIC and SOUNDS handlers read it to store their flag without muting or unmuting anything.
	/// </summary>
	public bool Initialising { get; private set; }

	/// <summary>
	/// Runs every registered handler once with the option's current byte — the walk
	/// <c>Prefs_LoadOptions</c> (<c>00459754</c>) makes over the table straight after reading the
	/// file, under <c>Prefs_Init</c>'s <see cref="Initialising"/>.
	/// </summary>
	public void ApplyAll() {
		Initialising = true;
		try {
			for (int i = 0; i < _handlers.Length; i++) {
				_handlers[i]?.Invoke(this[i]);
			}
		} finally {
			Initialising = false;
		}
	}

	/// <summary>
	/// Writes one option — <c>Prefs_SetOption</c> (<c>0045993c</c>), which stores the byte and, when
	/// <paramref name="apply"/> is set, calls the option's handler with it. The handler runs whether
	/// or not the byte changed, as the original's does. Every caller in the original passes apply,
	/// both panels included, so a stepped row takes effect while its panel is still up.
	///
	/// <para>The shadow copy the original saves the outgoing byte to (<c>DAT_004d2028</c>) is not
	/// kept: it serves a revert, and neither panel reverts — see <see cref="PreferencesPanel.Close"/>.</para>
	/// </summary>
	public void Set(int index, byte value, bool apply = true) {
		if (index < 0 || index >= _options.Length) {
			return;
		}

		if (_options[index] != value) {
			_options[index] = value;
			Changed = true;
		}

		if (apply && index < _handlers.Length) {
			_handlers[index]?.Invoke(value);
		}
	}

	/// <summary>
	/// <c>Prefs_CommitOptions</c> (<c>00459878</c>) with its apply argument set, which is how its one
	/// caller, the controls panel's close, passes it: every option that differs from the load-time
	/// shadow has its handler run again, and the shadow takes the current value. So a setting the
	/// preferences panel changed is applied a second time when the controls panel closes after it.
	///
	/// <para>VSHELL's <c>ShellOptions_Commit</c> (<c>0040d7b6</c>) is the same walk, and its preferences
	/// screen's <c>Accept</c> passes <paramref name="apply"/> clear: the shadow is rebaselined and no
	/// handler runs.</para>
	/// </summary>
	public void Commit(bool apply = true) {
		for (int i = 0; i < Prefs.Length && i < _options.Length; i++) {
			if (_baseline[i] == _options[i]) {
				continue;
			}

			if (apply) {
				_handlers[i]?.Invoke(_options[i]);
			}

			_baseline[i] = _options[i];
		}
	}

	/// <summary>
	/// VSHELL's <c>ShellOptions_RevertAll</c> (<c>0040d7fe</c>), the reverse of <see cref="Commit"/>: every option that differs from
	/// the shadow takes the shadow's value back, and with <paramref name="apply"/> set its handler runs.
	/// The preferences screen's <c>Cancel</c> passes it clear.
	/// </summary>
	public void Revert(bool apply) {
		for (int i = 0; i < Prefs.Length && i < _options.Length; i++) {
			if (_baseline[i] == _options[i]) {
				continue;
			}

			_options[i] = _baseline[i];
			if (apply) {
				_handlers[i]?.Invoke(_options[i]);
			}
		}
	}

	/// <summary>
	/// Steps one option to its next value and returns it, wrapping at <paramref name="modulus"/> —
	/// <c>Prefs_StepOption</c> (<c>004599b8</c>) forward and <c>004599f4</c> back. The two are the
	/// whole of how a panel row cycles, and the caller supplies the modulus, which is why the same
	/// pair drives a three-value row and a five-value one.
	/// </summary>
	public byte Step(int index, int modulus, bool forward = true) {
		if (modulus <= 0) {
			return this[index];
		}

		int value = forward
			? this[index] + 1 >= modulus ? 0 : this[index] + 1
			: this[index] - 1 < 0 ? modulus - 1 : this[index] - 1;

		Set(index, (byte)value);
		return (byte)value;
	}

	/// <summary>
	/// Flips one option between 0 and 1 — <c>Prefs_ToggleOption</c> (<c>00459980</c>), which is
	/// literally <c>value ^ 1</c>. The three on/off rows of the preferences panel take this instead of
	/// <see cref="Step"/>, so a row whose file byte is something other than 0 or 1 lands on 0 or 1 the
	/// first time it is clicked rather than cycling from where it was.
	/// </summary>
	public byte Toggle(int index) {
		byte value = (byte)(this[index] ^ 1);
		Set(index, value);
		return value;
	}

	/// <summary>
	/// Reads the file beside a mission's <c>script.dat</c>, or null when there is none there to read.
	///
	/// <para>A short file is rejected; see <see cref="PrefsTransformer.Parse"/>.</para>
	/// </summary>
	/// <param name="dataDirectory">The game's <c>data</c> folder — where its <c>script.dat</c> is.</param>
	public static SimulatorPreferences? Load(string? dataDirectory) {
		if (string.IsNullOrEmpty(dataDirectory)) {
			return null;
		}

		try {
			string path = Path.Combine(dataDirectory, Prefs.FileName);
			if (!File.Exists(path)) {
				return null;
			}

			return new PrefsTransformer().Parse(File.ReadAllBytes(path)) is { } prefs
				? new SimulatorPreferences(prefs.Options, dataDirectory)
				: null;
		} catch (IOException) {
			return null;
		} catch (UnauthorizedAccessException) {
			return null;
		}
	}

	/// <summary>
	/// Writes the named options back into the file — <c>Prefs_SaveSelectedOptions</c>
	/// (<c>00459b78</c>), and the only shape of save the original performs.
	///
	/// <para><b>It is a read-modify-write, not a dump.</b> The file on disk is re-read, only the
	/// options <paramref name="indices"/> names are copied out of this array over it, and the merged
	/// bytes go back. So an option this call does not name keeps whatever is on disk rather than
	/// whatever is in memory, which is what lets each panel own its own rows: the CONTROLS panel
	/// cannot push out a sound setting, and neither can push out the nineteen options nothing here
	/// interprets. <c>Prefs_SaveAllOptions</c> (<c>0045981c</c>) is the dump, and nothing in the
	/// original calls it.</para>
	///
	/// <para>The file's own length is kept: a longer-than-54-byte file keeps its tail, and a file
	/// that has gone missing or short since it was read is not written at all rather than being
	/// replaced with a fresh 54 bytes.</para>
	///
	/// <para>Clears <see cref="Changed"/> only when every option is back in step with the file,
	/// which after a partial save it need not be.</para>
	/// </summary>
	/// <param name="indices">
	/// Which options to write — <see cref="PreferencesPanelOptions"/> or
	/// <see cref="ControlsPanelOptions"/>. An index outside the array is skipped, as the original's
	/// own byte-indexed copy would leave it.
	/// </param>
	/// <returns>Whether the file was written. False also when it did not need to be.</returns>
	public bool Save(IReadOnlyList<int> indices) {
		ArgumentNullException.ThrowIfNull(indices);

		if (!SaveEnabled || SourceDirectory is not { Length: > 0 } directory) {
			return false;
		}

		try {
			string path = Path.Combine(directory, Prefs.FileName);
			if (!File.Exists(path)) {
				return false;
			}

			byte[] bytes = File.ReadAllBytes(path);
			if (bytes.Length < Prefs.Length) {
				return false;
			}

			bool dirty = false;
			foreach (int index in indices) {
				if (index < 0 || index >= Prefs.Length || bytes[index] == _options[index]) {
					continue;
				}

				bytes[index] = _options[index];
				dirty = true;
			}

			if (dirty) {
				File.WriteAllBytes(path, bytes);
			}

			// What is left over: the options this save did not name and that still differ from the
			// file. After the second panel has closed there are none, and this lands back on false.
			Changed = !bytes.AsSpan(0, Prefs.Length).SequenceEqual(_options.AsSpan(0, Prefs.Length));
			return dirty;
		} catch (IOException) {
			return false;
		} catch (UnauthorizedAccessException) {
			return false;
		}
	}
}
