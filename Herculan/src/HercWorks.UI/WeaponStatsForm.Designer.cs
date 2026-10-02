namespace HercWorks.UI;

partial class WeaponStatsForm {
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
		_idColumn = new DataGridViewTextBoxColumn();
		_nameColumn = new DataGridViewTextBoxColumn();
		_salvageCostColumn = new DataGridViewTextBoxColumn();
		_startUnlockColumn = new DataGridViewTextBoxColumn();
		_autobuildPriorityColumn = new DataGridViewTextBoxColumn();
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
		_openMenuItem.Text = "&Open WEAPONS.DAT...";
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
			_idColumn, _nameColumn, _salvageCostColumn, _startUnlockColumn, _autobuildPriorityColumn
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
		// _idColumn
		//
		_idColumn.DataPropertyName = nameof(WeaponStatRow.Id);
		_idColumn.HeaderText = "Weapon Id";
		_idColumn.Name = "_idColumn";
		_idColumn.Width = 80;
		//
		// _nameColumn
		//
		_nameColumn.DataPropertyName = nameof(WeaponStatRow.Name);
		_nameColumn.HeaderText = "Catalog Code";
		_nameColumn.MaxInputLength = MaxCodeLength;
		_nameColumn.Name = "_nameColumn";
		_nameColumn.Width = 200;
		//
		// _salvageCostColumn
		//
		_salvageCostColumn.DataPropertyName = nameof(WeaponStatRow.SalvageCost);
		_salvageCostColumn.HeaderText = "Price (tons; x1000 = kg)";
		_salvageCostColumn.Name = "_salvageCostColumn";
		_salvageCostColumn.Width = 170;
		//
		// _startUnlockColumn
		//
		_startUnlockColumn.DataPropertyName = nameof(WeaponStatRow.StartUnlock);
		_startUnlockColumn.HeaderText = "Unlocked";
		_startUnlockColumn.Name = "_startUnlockColumn";
		_startUnlockColumn.Width = 110;
		//
		// _autobuildPriorityColumn
		//
		_autobuildPriorityColumn.DataPropertyName = nameof(WeaponStatRow.AutobuildPriority);
		_autobuildPriorityColumn.HeaderText = "Rank (autobuild order)";
		_autobuildPriorityColumn.Name = "_autobuildPriorityColumn";
		_autobuildPriorityColumn.Width = 130;
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
		// WeaponStatsForm
		//
		Size = new Size(900, 500);
		Controls.Add(_grid);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "WeaponStatsForm";
		Text = "Item Stats Editor — WEAPONS.DAT";
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
	private DataGridViewTextBoxColumn _idColumn;
	private DataGridViewTextBoxColumn _nameColumn;
	private DataGridViewTextBoxColumn _salvageCostColumn;
	private DataGridViewTextBoxColumn _startUnlockColumn;
	private DataGridViewTextBoxColumn _autobuildPriorityColumn;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
