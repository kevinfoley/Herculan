using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes the mission's theater and its time of day, header words 0 and 9 (docs/retail/formats/script-dat.md#header-format),
/// as one step of <see cref="PointMover"/>'s undo history each, and dresses the scene in the world they pick,
/// <c>wld\world&lt;theater * 2 + variant&gt;</c> (<see cref="MissionScene.ChangeTheater"/>): the terrain bank, the palette every
/// model is drawn through, the sky and the fog. <see cref="WorldChanged"/> then tells whatever has the scene uploaded to
/// upload it again.
///
/// <para>Each change writes row 2 through <see cref="MissionDocument.SetHeaderWord"/>, which picks the record that supplies the
/// word, and generates again. A campaign mission's launch writes its stage's theater over the header, so its theater is
/// shown and not changed; its time of day is its own (<see cref="MissionOpener"/>).</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since reloading the world
/// replaces what the frame draws with.</para>
/// </summary>
internal sealed class TheaterChanger {
	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly MissionScene _scene;
	private readonly MissionIndex _index;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private (int Word, short Value)? _requested;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s, which the scene was loaded from.</param>
	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	public TheaterChanger(ScriptDat document, MissionDocument source, MissionScene scene, MissionIndex index, PointMover mover,
			MissionRebuild rebuild) {
		_document = document;
		_source = source;
		_scene = scene;
		_index = index;
		_mover = mover;
		_rebuild = rebuild;
	}

	/// <summary>Raised once the scene is dressed in another world, for whatever has it uploaded.</summary>
	public event Action? WorldChanged;

	/// <summary>The theater the mission is shown on, as generated.</summary>
	public int Theater => _document.TheaterIndex;

	/// <summary>Whether the mission is played by night: the generated header's variant 1.</summary>
	public bool Night => _document.TheaterVariant != 0;

	/// <summary>The generated header's theater variant, which <see cref="Night"/> reads.</summary>
	public int Variant => _document.TheaterVariant;

	/// <summary>Why the theater cannot be changed, or null when it can.</summary>
	public string? WhyTheaterFixed =>
		_source.Generation.Header[MissionOpener.TheaterWord] != null ? "The campaign stage sets the theater." : null;

	/// <summary>Why the time of day cannot be changed, or null when it can.</summary>
	public string? WhyTimeFixed =>
		_source.Generation.Header[MissionOpener.VariantWord] != null ? "The launch sets the time of day." : null;

	/// <summary>Why the last change asked for could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Asks for the mission to be played on theater <paramref name="theater"/>.</summary>
	public void RequestTheater(int theater) => _requested = (MissionOpener.TheaterWord, (short)theater);

	/// <summary>Asks for the mission to be played by night, or by day.</summary>
	public void RequestNight(bool night) => _requested = (MissionOpener.VariantWord, (short)(night ? 1 : 0));

	/// <summary>Makes the change asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is var (word, value)) {
			_requested = null;
			Failure = Set(word, value);
		}
	}

	private string? Set(int word, short value) {
		if ((word == MissionOpener.TheaterWord ? WhyTheaterFixed : WhyTimeFixed) is { } reason) {
			return reason;
		}

		if ((word == MissionOpener.TheaterWord ? Theater : _document.TheaterVariant) == value) {
			return null;
		}

		var source = _source;
		MsnEdit change;
		try {
			change = source.SetHeaderWord(word, value);
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		try {
			Reload();
		} catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or ArgumentOutOfRangeException) {
			source.Undo(change);
			Reload();
			return ex.Message;
		}

		_mover.Record(new WorldEdit(this, change));
		return null;
	}

	// Dresses the scene in the world the generated header picks: every object in it, and every object an edit has built
	// that an undo or redo can put back.
	private void Reload() {
		var objects = _index.Objects.Concat(_rebuild.Built).Distinct(ReferenceEqualityComparer.Instance).Cast<SceneObject>().ToList();
		_scene.ChangeTheater(_document.TheaterIndex, _document.TheaterVariant, objects);
		WorldChanged?.Invoke();
	}

	// Puts row 2 back as the step found or left it, and dresses the scene in the world the header then picks.
	private sealed record WorldEdit(TheaterChanger Changer, MsnEdit Change) : IEditorEdit {
		public void Undo() {
			Changer._source.Undo(Change);
			Changer.Reload();
		}

		public void Redo() {
			Changer._source.Redo(Change);
			Changer.Reload();
		}
	}
}
