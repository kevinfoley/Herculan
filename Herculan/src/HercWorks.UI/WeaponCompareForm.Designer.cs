namespace HercWorks.UI;

partial class WeaponCompareForm {
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
		_optionsPanel = new FlowLayoutPanel();
		_correctedBox = new CheckBox();
		_weaponPanel = new FlowLayoutPanel();
		_grid = new DataGridView();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
		_optionsPanel.SuspendLayout();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _optionsPanel
		//
		_optionsPanel.AutoSize = true;
		_optionsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		_optionsPanel.Controls.Add(_correctedBox);
		_optionsPanel.Dock = DockStyle.Top;
		_optionsPanel.Location = new Point(0, 0);
		_optionsPanel.Name = "_optionsPanel";
		_optionsPanel.Padding = new Padding(6, 6, 6, 0);
		_optionsPanel.Size = new Size(1100, 31);
		_optionsPanel.TabIndex = 0;
		//
		// _correctedBox
		//
		_correctedBox.AutoSize = true;
		_correctedBox.Name = "_correctedBox";
		_correctedBox.TabIndex = 0;
		_correctedBox.Text = "Show corrected values";
		_correctedBox.CheckedChanged += OnOptionChanged;
		//
		// _weaponPanel
		//
		_weaponPanel.AutoSize = true;
		_weaponPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
		_weaponPanel.Dock = DockStyle.Top;
		_weaponPanel.Location = new Point(0, 0);
		_weaponPanel.Name = "_weaponPanel";
		_weaponPanel.Padding = new Padding(6);
		_weaponPanel.Size = new Size(1100, 12);
		_weaponPanel.TabIndex = 1;
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
		_grid.Size = new Size(1100, 466);
		_grid.TabIndex = 2;
		_grid.CellPainting += OnGridCellPainting;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 478);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(1100, 22);
		_statusStrip.TabIndex = 3;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No weapons loaded.";
		//
		// WeaponCompareForm
		//
		Size = new Size(1100, 500);
		Controls.Add(_grid);
		Controls.Add(_weaponPanel);
		Controls.Add(_optionsPanel);
		Controls.Add(_statusStrip);
		Name = "WeaponCompareForm";
		Text = "Compare Weapons — SHELL0 / SIMVOL0";
		((System.ComponentModel.ISupportInitialize)_grid).EndInit();
		_optionsPanel.ResumeLayout(false);
		_optionsPanel.PerformLayout();
		_statusStrip.ResumeLayout(false);
		_statusStrip.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private FlowLayoutPanel _optionsPanel;
	private CheckBox _correctedBox;
	private FlowLayoutPanel _weaponPanel;
	private DataGridView _grid;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
