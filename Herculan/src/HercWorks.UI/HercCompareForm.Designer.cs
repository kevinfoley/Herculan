namespace HercWorks.UI;

partial class HercCompareForm {
	/// <summary>Required designer variable.</summary>
	private System.ComponentModel.IContainer components = null;

	/// <summary>Clean up any resources being used.</summary>
	protected override void Dispose(bool disposing) {
		if (disposing && (components != null)) {
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	#region Windows Form Designer generated code

	/// <summary>
	/// Required method for Designer support - do not modify the contents of this
	/// method with the code editor.
	/// </summary>
	private void InitializeComponent() {
		DataGridViewCellStyle cellStyle = new DataGridViewCellStyle();
		_hercPanel = new FlowLayoutPanel();
		_grid = new DataGridView();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _hercPanel
		//
		_hercPanel.AutoSize = true;
		_hercPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		_hercPanel.Dock = DockStyle.Top;
		_hercPanel.Location = new Point(0, 0);
		_hercPanel.Name = "_hercPanel";
		_hercPanel.Padding = new Padding(6);
		_hercPanel.Size = new Size(900, 12);
		_hercPanel.TabIndex = 0;
		//
		// _grid
		//
		_grid.AllowUserToAddRows = false;
		_grid.AllowUserToDeleteRows = false;
		_grid.AllowUserToResizeRows = false;
		_grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
		cellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
		_grid.DefaultCellStyle = cellStyle;
		_grid.Dock = DockStyle.Fill;
		_grid.Location = new Point(0, 12);
		_grid.Name = "_grid";
		_grid.ReadOnly = true;
		_grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.AutoSizeToAllHeaders;
		_grid.Size = new Size(900, 666);
		_grid.TabIndex = 1;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 678);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(900, 22);
		_statusStrip.TabIndex = 2;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No hercs loaded.";
		//
		// HercCompareForm
		//
		Size = new Size(900, 700);
		Controls.Add(_grid);
		Controls.Add(_hercPanel);
		Controls.Add(_statusStrip);
		Name = "HercCompareForm";
		Text = "Compare Hercs — SIMVOL0";
		((System.ComponentModel.ISupportInitialize)_grid).EndInit();
		_statusStrip.ResumeLayout(false);
		_statusStrip.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private FlowLayoutPanel _hercPanel;
	private DataGridView _grid;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
