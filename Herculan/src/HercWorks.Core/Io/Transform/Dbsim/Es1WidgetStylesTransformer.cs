using HercWorks.Core.Data.File.Dat.Sim;

namespace HercWorks.Core.Io.Transform.Dbsim;

/// <summary>
/// Reads and writes ES1's <c>STYLES.DAT</c> (<see cref="Es1WidgetStyles"/>; docs/es1/formats/styles-dat.md): the count and
/// the records, each record's bytes kept whole.
/// </summary>
public class Es1WidgetStylesTransformer : Es1CountedTableTransformer<Es1WidgetStyles, Es1WidgetStyle> {
	protected override int RecordLength => Es1WidgetStyle.Length;

	protected override Es1WidgetStyle ReadRecord() => new() { Bytes = IndexSegment(Es1WidgetStyle.Length) };

	protected override void WriteRecord(BinaryWriter w, Es1WidgetStyle record) => w.Write(record.Bytes);

	protected override Es1WidgetStyles Create(Es1WidgetStyle[] records) => new() { Styles = records };

	protected override IReadOnlyList<Es1WidgetStyle> RecordsOf(Es1WidgetStyles file) => file.Styles;
}
