namespace HercWorks.UI;

partial class StockFitForm {
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
		_headerPanel = new FlowLayoutPanel();
		_chassisFileLabel = new Label();
		_chassisFileCombo = new ComboBox();
		_typeLabel = new Label();
		_typeCombo = new ComboBox();
		_grid = new DataGridView();
		_hardpointColumn = new DataGridViewTextBoxColumn();
		_weaponColumn = new DataGridViewComboBoxColumn();
		_conditionColumn = new DataGridViewTextBoxColumn();
		_ammoColumn = new DataGridViewComboBoxColumn();
		_buttonPanel = new FlowLayoutPanel();
		_addHardpointButton = new Button();
		_removeHardpointButton = new Button();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		_headerPanel.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_grid).BeginInit();
		_buttonPanel.SuspendLayout();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _menuStrip
		//
		_menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenuItem });
		_menuStrip.Location = new Point(0, 0);
		_menuStrip.Name = "_menuStrip";
		_menuStrip.Size = new Size(760, 24);
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
		_openMenuItem.Text = "&Open INI_*.DAT...";
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
		// _headerPanel
		//
		_headerPanel.AutoSize = true;
		_headerPanel.Controls.Add(_chassisFileLabel);
		_headerPanel.Controls.Add(_chassisFileCombo);
		_headerPanel.Controls.Add(_typeLabel);
		_headerPanel.Controls.Add(_typeCombo);
		_headerPanel.Dock = DockStyle.Top;
		_headerPanel.Location = new Point(0, 24);
		_headerPanel.Name = "_headerPanel";
		_headerPanel.Padding = new Padding(6);
		_headerPanel.Size = new Size(760, 41);
		_headerPanel.TabIndex = 1;
		//
		// _chassisFileLabel
		//
		_chassisFileLabel.Anchor = AnchorStyles.Left;
		_chassisFileLabel.AutoSize = true;
		_chassisFileLabel.Name = "_chassisFileLabel";
		_chassisFileLabel.Text = "Game file:";
		//
		// _chassisFileCombo
		//
		_chassisFileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		_chassisFileCombo.Name = "_chassisFileCombo";
		_chassisFileCombo.Size = new Size(190, 23);
		_chassisFileCombo.TabIndex = 0;
		_chassisFileCombo.SelectedIndexChanged += OnChassisFileSelected;
		//
		// _typeLabel
		//
		_typeLabel.Anchor = AnchorStyles.Left;
		_typeLabel.AutoSize = true;
		_typeLabel.Name = "_typeLabel";
		_typeLabel.Text = "Chassis type:";
		//
		// _typeCombo
		//
		_typeCombo.DisplayMember = nameof(HercTypeOption.Label);
		_typeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
		_typeCombo.Name = "_typeCombo";
		_typeCombo.Size = new Size(120, 23);
		_typeCombo.TabIndex = 1;
		_typeCombo.ValueMember = nameof(HercTypeOption.Id);
		//
		// _grid
		//
		_grid.AllowUserToAddRows = false;
		_grid.AllowUserToDeleteRows = false;
		_grid.AutoGenerateColumns = false;
		_grid.Columns.AddRange(new DataGridViewColumn[] { _hardpointColumn, _weaponColumn, _conditionColumn, _ammoColumn });
		_grid.Dock = DockStyle.Fill;
		_grid.Location = new Point(0, 65);
		_grid.Name = "_grid";
		_grid.RowHeadersVisible = false;
		_grid.Size = new Size(760, 297);
		_grid.TabIndex = 2;
		_grid.CellBeginEdit += OnGridCellBeginEdit;
		_grid.CellFormatting += OnGridCellFormatting;
		_grid.CellValueChanged += OnGridCellValueChanged;
		_grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
		_grid.DataError += OnGridDataError;
		//
		// _hardpointColumn
		//
		_hardpointColumn.DataPropertyName = nameof(StockFitRow.Hardpoint);
		_hardpointColumn.HeaderText = "Hardpoint";
		_hardpointColumn.Name = "_hardpointColumn";
		_hardpointColumn.Width = 80;
		//
		// _weaponColumn
		//
		_weaponColumn.DataPropertyName = nameof(StockFitRow.WeaponId);
		_weaponColumn.DisplayMember = nameof(WeaponFitOption.Label);
		_weaponColumn.HeaderText = "Weapon";
		_weaponColumn.Name = "_weaponColumn";
		_weaponColumn.ValueMember = nameof(WeaponFitOption.Id);
		_weaponColumn.Width = 220;
		//
		// _conditionColumn
		//
		_conditionColumn.DataPropertyName = nameof(StockFitRow.Condition);
		_conditionColumn.HeaderText = "Condition";
		_conditionColumn.Name = "_conditionColumn";
		_conditionColumn.Width = 80;
		//
		// _ammoColumn
		//
		_ammoColumn.DataPropertyName = nameof(StockFitRow.AmmoType);
		_ammoColumn.DisplayMember = nameof(AmmoTypeOption.Label);
		_ammoColumn.HeaderText = "Ammo type";
		_ammoColumn.Name = "_ammoColumn";
		_ammoColumn.ValueMember = nameof(AmmoTypeOption.Id);
		_ammoColumn.Width = 220;
		//
		// _buttonPanel
		//
		_buttonPanel.AutoSize = true;
		_buttonPanel.Controls.Add(_addHardpointButton);
		_buttonPanel.Controls.Add(_removeHardpointButton);
		_buttonPanel.Dock = DockStyle.Bottom;
		_buttonPanel.Location = new Point(0, 362);
		_buttonPanel.Name = "_buttonPanel";
		_buttonPanel.Padding = new Padding(6);
		_buttonPanel.Size = new Size(760, 41);
		_buttonPanel.TabIndex = 3;
		//
		// _addHardpointButton
		//
		_addHardpointButton.AutoSize = true;
		_addHardpointButton.Name = "_addHardpointButton";
		_addHardpointButton.TabIndex = 0;
		_addHardpointButton.Text = "Add Hardpoint";
		_addHardpointButton.Click += OnAddHardpoint;
		//
		// _removeHardpointButton
		//
		_removeHardpointButton.AutoSize = true;
		_removeHardpointButton.Name = "_removeHardpointButton";
		_removeHardpointButton.TabIndex = 1;
		_removeHardpointButton.Text = "Remove Hardpoint";
		_removeHardpointButton.Click += OnRemoveHardpoint;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 403);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(760, 22);
		_statusStrip.TabIndex = 4;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No file loaded.";
		//
		// StockFitForm
		//
		Size = new Size(760, 460);
		Controls.Add(_grid);
		Controls.Add(_buttonPanel);
		Controls.Add(_headerPanel);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "StockFitForm";
		Text = "Stock Fit Editor — INI_*.DAT";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		_headerPanel.ResumeLayout(false);
		_headerPanel.PerformLayout();
		((System.ComponentModel.ISupportInitialize)_grid).EndInit();
		_buttonPanel.ResumeLayout(false);
		_buttonPanel.PerformLayout();
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
	private FlowLayoutPanel _headerPanel;
	private Label _chassisFileLabel;
	private ComboBox _chassisFileCombo;
	private Label _typeLabel;
	private ComboBox _typeCombo;
	private DataGridView _grid;
	private DataGridViewTextBoxColumn _hardpointColumn;
	private DataGridViewComboBoxColumn _weaponColumn;
	private DataGridViewTextBoxColumn _conditionColumn;
	private DataGridViewComboBoxColumn _ammoColumn;
	private FlowLayoutPanel _buttonPanel;
	private Button _addHardpointButton;
	private Button _removeHardpointButton;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
