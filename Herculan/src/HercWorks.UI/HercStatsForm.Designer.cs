namespace HercWorks.UI;

partial class HercStatsForm {
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
		_menuStrip = new MenuStrip();
		_fileMenuItem = new ToolStripMenuItem();
		_openMenuItem = new ToolStripMenuItem();
		_saveAsMenuItem = new ToolStripMenuItem();
		_saveWithPrefixMenuItem = new ToolStripMenuItem();
		_fileMenuSeparator = new ToolStripSeparator();
		_closeMenuItem = new ToolStripMenuItem();
		_grid = new DataGridView();
		_hercIdColumn = new DataGridViewTextBoxColumn();
		_hercNameColumn = new DataGridViewTextBoxColumn();
		_weightColumn = new DataGridViewTextBoxColumn();
		_speedColumn = new DataGridViewTextBoxColumn();
		_hardpointTotalColumn = new DataGridViewTextBoxColumn();
		_salvageReqColumn = new DataGridViewTextBoxColumn();
		_unknownFlagColumn = new DataGridViewTextBoxColumn();
		_buildMissionCountColumn = new DataGridViewTextBoxColumn();
		_flagCampaignStartColumn = new DataGridViewTextBoxColumn();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _menuStrip
		//
		_menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenuItem });
		_menuStrip.Location = new Point(0, 0);
		_menuStrip.Name = "_menuStrip";
		_menuStrip.Size = new Size(900, 24);
		_menuStrip.TabIndex = 0;
		//
		// _fileMenuItem
		//
		_fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
			_openMenuItem, _saveAsMenuItem, _saveWithPrefixMenuItem, _fileMenuSeparator, _closeMenuItem
		});
		_fileMenuItem.Name = "_fileMenuItem";
		_fileMenuItem.Text = "&File";
		//
		// _openMenuItem
		//
		_openMenuItem.Name = "_openMenuItem";
		_openMenuItem.Text = "&Open HERC_INF.DAT...";
		_openMenuItem.Click += OnOpen;
		//
		// _saveAsMenuItem
		//
		_saveAsMenuItem.Name = "_saveAsMenuItem";
		_saveAsMenuItem.Text = "&Save As...";
		_saveAsMenuItem.Click += OnSaveAs;
		//
		// _saveWithPrefixMenuItem
		//
		_saveWithPrefixMenuItem.Enabled = false;
		_saveWithPrefixMenuItem.Name = "_saveWithPrefixMenuItem";
		_saveWithPrefixMenuItem.Text = "Save As With &VOL Prefix...";
		_saveWithPrefixMenuItem.Click += OnSaveWithPrefix;
		//
		// _fileMenuSeparator
		//
		_fileMenuSeparator.Name = "_fileMenuSeparator";
		//
		// _closeMenuItem
		//
		_closeMenuItem.Name = "_closeMenuItem";
		_closeMenuItem.Text = "&Close";
		_closeMenuItem.Click += OnClose;
		//
		// _grid
		//
		_grid.AllowUserToAddRows = false;
		_grid.AllowUserToDeleteRows = false;
		_grid.AutoGenerateColumns = false;
		_grid.Columns.AddRange(new DataGridViewColumn[] {
			_hercIdColumn, _hercNameColumn, _weightColumn, _speedColumn, _hardpointTotalColumn,
			_salvageReqColumn, _unknownFlagColumn, _buildMissionCountColumn, _flagCampaignStartColumn
		});
		_grid.DataSource = _rows;
		_grid.Dock = DockStyle.Fill;
		_grid.Location = new Point(0, 24);
		_grid.Name = "_grid";
		_grid.RowHeadersVisible = false;
		_grid.Size = new Size(900, 454);
		_grid.TabIndex = 1;
		_grid.CellEndEdit += OnGridCellEndEdit;
		//
		// _hercIdColumn
		//
		_hercIdColumn.DataPropertyName = nameof(HercStatRow.HercId);
		_hercIdColumn.HeaderText = "Herc Id";
		_hercIdColumn.Name = "_hercIdColumn";
		_hercIdColumn.Width = 60;
		//
		// _hercNameColumn
		//
		_hercNameColumn.DataPropertyName = nameof(HercStatRow.HercName);
		_hercNameColumn.HeaderText = "Herc Name";
		_hercNameColumn.Name = "_hercNameColumn";
		_hercNameColumn.ReadOnly = true;
		_hercNameColumn.Width = 110;
		//
		// _weightColumn
		//
		_weightColumn.DataPropertyName = nameof(HercStatRow.Weight);
		_weightColumn.HeaderText = "Weight (tons)";
		_weightColumn.Name = "_weightColumn";
		_weightColumn.Width = 100;
		//
		// _speedColumn
		//
		_speedColumn.DataPropertyName = nameof(HercStatRow.Speed);
		_speedColumn.HeaderText = "Speed (KPH)";
		_speedColumn.Name = "_speedColumn";
		_speedColumn.Width = 100;
		//
		// _hardpointTotalColumn
		//
		_hardpointTotalColumn.DataPropertyName = nameof(HercStatRow.HardpointTotal);
		_hardpointTotalColumn.HeaderText = "Hardpoints (display only)";
		_hardpointTotalColumn.Name = "_hardpointTotalColumn";
		_hardpointTotalColumn.Width = 90;
		//
		// _salvageReqColumn
		//
		_salvageReqColumn.DataPropertyName = nameof(HercStatRow.SalvageReq);
		_salvageReqColumn.HeaderText = "Price (tons)";
		_salvageReqColumn.Name = "_salvageReqColumn";
		_salvageReqColumn.Width = 130;
		//
		// _unknownFlagColumn
		//
		_unknownFlagColumn.DataPropertyName = nameof(HercStatRow.UnknownFlag);
		_unknownFlagColumn.HeaderText = "+0x0A (no reader known)";
		_unknownFlagColumn.Name = "_unknownFlagColumn";
		_unknownFlagColumn.Width = 100;
		//
		// _buildMissionCountColumn
		//
		_buildMissionCountColumn.DataPropertyName = nameof(HercStatRow.BuildMissionCount);
		_buildMissionCountColumn.HeaderText = "Build Time (missions)";
		_buildMissionCountColumn.Name = "_buildMissionCountColumn";
		_buildMissionCountColumn.Width = 140;
		//
		// _flagCampaignStartColumn
		//
		_flagCampaignStartColumn.DataPropertyName = nameof(HercStatRow.FlagCampaignStart);
		_flagCampaignStartColumn.HeaderText = "Available";
		_flagCampaignStartColumn.Name = "_flagCampaignStartColumn";
		_flagCampaignStartColumn.Width = 160;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 478);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(900, 22);
		_statusStrip.TabIndex = 2;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No file loaded.";
		//
		// HercStatsForm
		//
		Size = new Size(900, 500);
		Controls.Add(_grid);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "HercStatsForm";
		Text = "Herc Stats Editor — HERC_INF.DAT";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		((System.ComponentModel.ISupportInitialize)_grid).EndInit();
		_statusStrip.ResumeLayout(false);
		_statusStrip.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private MenuStrip _menuStrip;
	private ToolStripMenuItem _fileMenuItem;
	private ToolStripMenuItem _openMenuItem;
	private ToolStripMenuItem _saveAsMenuItem;
	private ToolStripMenuItem _saveWithPrefixMenuItem;
	private ToolStripSeparator _fileMenuSeparator;
	private ToolStripMenuItem _closeMenuItem;
	private DataGridView _grid;
	private DataGridViewTextBoxColumn _hercIdColumn;
	private DataGridViewTextBoxColumn _hercNameColumn;
	private DataGridViewTextBoxColumn _weightColumn;
	private DataGridViewTextBoxColumn _speedColumn;
	private DataGridViewTextBoxColumn _hardpointTotalColumn;
	private DataGridViewTextBoxColumn _salvageReqColumn;
	private DataGridViewTextBoxColumn _unknownFlagColumn;
	private DataGridViewTextBoxColumn _buildMissionCountColumn;
	private DataGridViewTextBoxColumn _flagCampaignStartColumn;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
