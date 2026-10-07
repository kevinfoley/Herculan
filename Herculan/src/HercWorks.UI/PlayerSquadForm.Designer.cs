namespace HercWorks.UI;

partial class PlayerSquadForm {
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
		_topPanel = new Panel();
		_squadGroupBox = new GroupBox();
		_playerSlotLabel = new Label();
		_playerSlotInput = new NumericUpDown();
		_noteLabel = new Label();
		_squadSplit = new SplitContainer();
		_squadGrid = new DataGridView();
		_indexColumn = new DataGridViewTextBoxColumn();
		_hercTypeColumn = new DataGridViewComboBoxColumn();
		_slotsColumn = new DataGridViewTextBoxColumn();
		_fitColumn = new DataGridViewTextBoxColumn();
		_loadoutGroupBox = new GroupBox();
		_loadoutGrid = new DataGridView();
		_slotIndexColumn = new DataGridViewTextBoxColumn();
		_slotWeaponColumn = new DataGridViewComboBoxColumn();
		_slotAmmoColumn = new DataGridViewComboBoxColumn();
		_slotButtonPanel = new Panel();
		_addSlotButton = new Button();
		_removeSlotButton = new Button();
		_loadoutHintLabel = new Label();
		_unk00Column = new DataGridViewTextBoxColumn();
		_unk02Column = new DataGridViewTextBoxColumn();
		_unk3AColumn = new DataGridViewTextBoxColumn();
		_buttonPanel = new Panel();
		_addEntryButton = new Button();
		_removeEntryButton = new Button();
		_conditionsButton = new Button();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		_topPanel.SuspendLayout();
		_squadGroupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_playerSlotInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_squadSplit).BeginInit();
		_squadSplit.Panel1.SuspendLayout();
		_squadSplit.Panel2.SuspendLayout();
		_squadSplit.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_squadGrid).BeginInit();
		_loadoutGroupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_loadoutGrid).BeginInit();
		_slotButtonPanel.SuspendLayout();
		_buttonPanel.SuspendLayout();
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
		_openMenuItem.Text = "&Open player.mec...";
		_openMenuItem.Click += OnOpen;
		//
		// _saveAsMenuItem
		//
		_saveAsMenuItem.Name = "_saveAsMenuItem";
		_saveAsMenuItem.Text = "Save &As...";
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
		// _topPanel
		//
		_topPanel.Controls.Add(_squadGroupBox);
		_topPanel.Dock = DockStyle.Top;
		_topPanel.Location = new Point(0, 24);
		_topPanel.Name = "_topPanel";
		_topPanel.Padding = new Padding(8);
		_topPanel.Size = new Size(900, 130);
		_topPanel.TabIndex = 1;
		//
		// _squadGroupBox
		//
		_squadGroupBox.Controls.Add(_playerSlotLabel);
		_squadGroupBox.Controls.Add(_playerSlotInput);
		_squadGroupBox.Controls.Add(_noteLabel);
		_squadGroupBox.Dock = DockStyle.Fill;
		_squadGroupBox.Location = new Point(8, 8);
		_squadGroupBox.Name = "_squadGroupBox";
		_squadGroupBox.Size = new Size(884, 114);
		_squadGroupBox.TabIndex = 0;
		_squadGroupBox.TabStop = false;
		_squadGroupBox.Text = "Squad";
		//
		// _playerSlotLabel
		//
		_playerSlotLabel.AutoSize = true;
		_playerSlotLabel.Location = new Point(18, 32);
		_playerSlotLabel.Name = "_playerSlotLabel";
		_playerSlotLabel.Text = "Player pilots entry #:";
		//
		// _playerSlotInput
		//
		_playerSlotInput.Location = new Point(160, 30);
		_playerSlotInput.Name = "_playerSlotInput";
		_playerSlotInput.Size = new Size(80, 23);
		_playerSlotInput.TabIndex = 0;
		//
		// _noteLabel
		//
		_noteLabel.AutoSize = true;
		_noteLabel.Location = new Point(18, 64);
		_noteLabel.Name = "_noteLabel";
		_noteLabel.Text =
			"Each entry's weapon slots are edited below the roster — a weapon per slot, and for the four missile launchers the ammunition it is loaded with.\r\n" +
			"The squad spawns at the point script.dat block 11's record 0 carries; nothing else about it comes from script.dat.";
		//
		// _squadSplit
		//
		_squadSplit.Dock = DockStyle.Fill;
		_squadSplit.Location = new Point(0, 154);
		_squadSplit.Name = "_squadSplit";
		_squadSplit.Orientation = Orientation.Horizontal;
		_squadSplit.Panel1.Controls.Add(_squadGrid);
		_squadSplit.Panel1MinSize = 100;
		_squadSplit.Panel2.Controls.Add(_loadoutGroupBox);
		_squadSplit.Panel2MinSize = 180;
		_squadSplit.Size = new Size(900, 422);
		_squadSplit.SplitterDistance = 180;
		_squadSplit.TabIndex = 2;
		//
		// _squadGrid
		//
		_squadGrid.AllowUserToAddRows = false;
		_squadGrid.AllowUserToDeleteRows = false;
		_squadGrid.AutoGenerateColumns = false;
		_squadGrid.Columns.AddRange(new DataGridViewColumn[] {
			_indexColumn, _hercTypeColumn, _slotsColumn, _fitColumn,
			_unk00Column, _unk02Column, _unk3AColumn
		});
		_squadGrid.Dock = DockStyle.Fill;
		_squadGrid.Location = new Point(0, 0);
		_squadGrid.MultiSelect = false;
		_squadGrid.Name = "_squadGrid";
		_squadGrid.RowHeadersVisible = false;
		_squadGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
		_squadGrid.Size = new Size(900, 180);
		_squadGrid.TabIndex = 0;
		_squadGrid.DataError += OnGridDataError;
		_squadGrid.CellValueChanged += OnSquadCellChanged;
		_squadGrid.CurrentCellDirtyStateChanged += OnSquadCellDirtyStateChanged;
		_squadGrid.SelectionChanged += OnSquadSelectionChanged;
		//
		// _indexColumn
		//
		_indexColumn.DataPropertyName = nameof(PlayerSquadRow.Index);
		_indexColumn.HeaderText = "#";
		_indexColumn.Name = "_indexColumn";
		_indexColumn.ReadOnly = true;
		_indexColumn.Width = 50;
		//
		// _hercTypeColumn
		//
		// Items are filled per load (see PlayerSquadForm.BindHercTypes) so that a type the file
		// carries but MECHS.NAM has no name for still has an entry to select.
		_hercTypeColumn.DataPropertyName = nameof(PlayerSquadRow.HercType);
		_hercTypeColumn.DisplayMember = nameof(HercTypeOption.Label);
		_hercTypeColumn.HeaderText = "Herc Type";
		_hercTypeColumn.Name = "_hercTypeColumn";
		_hercTypeColumn.ValueMember = nameof(HercTypeOption.Id);
		_hercTypeColumn.Width = 170;
		//
		// _slotsColumn
		//
		_slotsColumn.DataPropertyName = nameof(PlayerSquadRow.Slots);
		_slotsColumn.HeaderText = "Slots";
		_slotsColumn.Name = "_slotsColumn";
		_slotsColumn.ReadOnly = true;
		_slotsColumn.Width = 60;
		//
		// _fitColumn
		//
		// Read-only: the fit is edited slot by slot below, where each slot gets its weapon and (for
		// launchers) its ammunition type by name.
		_fitColumn.DataPropertyName = nameof(PlayerSquadRow.WeaponFit);
		_fitColumn.HeaderText = "Weapon fit";
		_fitColumn.Name = "_fitColumn";
		_fitColumn.ReadOnly = true;
		_fitColumn.Width = 400;
		//
		// _loadoutGroupBox
		//
		_loadoutGroupBox.Controls.Add(_loadoutGrid);
		_loadoutGroupBox.Controls.Add(_loadoutHintLabel);
		_loadoutGroupBox.Controls.Add(_slotButtonPanel);
		_loadoutGroupBox.Dock = DockStyle.Fill;
		_loadoutGroupBox.Location = new Point(0, 0);
		_loadoutGroupBox.Name = "_loadoutGroupBox";
		_loadoutGroupBox.Padding = new Padding(6, 3, 6, 6);
		_loadoutGroupBox.Size = new Size(900, 238);
		_loadoutGroupBox.TabIndex = 1;
		_loadoutGroupBox.TabStop = false;
		_loadoutGroupBox.Text = "Weapon fit";
		//
		// _loadoutGrid
		//
		_loadoutGrid.AllowUserToAddRows = false;
		_loadoutGrid.AllowUserToDeleteRows = false;
		_loadoutGrid.AutoGenerateColumns = false;
		_loadoutGrid.Columns.AddRange(new DataGridViewColumn[] {
			_slotIndexColumn, _slotWeaponColumn, _slotAmmoColumn
		});
		_loadoutGrid.Dock = DockStyle.Fill;
		_loadoutGrid.Location = new Point(6, 19);
		_loadoutGrid.MultiSelect = false;
		_loadoutGrid.Name = "_loadoutGrid";
		_loadoutGrid.RowHeadersVisible = false;
		_loadoutGrid.Size = new Size(888, 145);
		_loadoutGrid.TabIndex = 0;
		_loadoutGrid.CellBeginEdit += OnLoadoutCellBeginEdit;
		_loadoutGrid.CellFormatting += OnLoadoutCellFormatting;
		_loadoutGrid.CellValueChanged += OnLoadoutCellValueChanged;
		_loadoutGrid.CurrentCellDirtyStateChanged += OnLoadoutCellDirtyStateChanged;
		_loadoutGrid.DataError += OnGridDataError;
		//
		// _slotIndexColumn
		//
		_slotIndexColumn.DataPropertyName = nameof(PlayerWeaponSlotRow.Slot);
		_slotIndexColumn.HeaderText = "Slot";
		_slotIndexColumn.Name = "_slotIndexColumn";
		_slotIndexColumn.ReadOnly = true;
		_slotIndexColumn.Width = 50;
		//
		// _slotWeaponColumn
		//
		// Items are filled per load (see PlayerSquadForm.BindWeaponOptions) so that an id the file
		// carries but WeaponLUT has no name for still has an entry to select.
		_slotWeaponColumn.DataPropertyName = nameof(PlayerWeaponSlotRow.WeaponId);
		_slotWeaponColumn.DisplayMember = nameof(WeaponFitOption.Label);
		_slotWeaponColumn.HeaderText = "Weapon";
		_slotWeaponColumn.Name = "_slotWeaponColumn";
		_slotWeaponColumn.ValueMember = nameof(WeaponFitOption.Id);
		_slotWeaponColumn.Width = 180;
		//
		// _slotAmmoColumn
		//
		_slotAmmoColumn.DataPropertyName = nameof(PlayerWeaponSlotRow.AmmoType);
		_slotAmmoColumn.DisplayMember = nameof(AmmoTypeOption.Label);
		_slotAmmoColumn.HeaderText = "Missile ammo";
		_slotAmmoColumn.Name = "_slotAmmoColumn";
		_slotAmmoColumn.ValueMember = nameof(AmmoTypeOption.Id);
		_slotAmmoColumn.Width = 220;
		//
		// _loadoutHintLabel
		//
		_loadoutHintLabel.AutoSize = false;
		_loadoutHintLabel.Dock = DockStyle.Bottom;
		_loadoutHintLabel.ForeColor = SystemColors.GrayText;
		_loadoutHintLabel.Location = new Point(6, 164);
		_loadoutHintLabel.Name = "_loadoutHintLabel";
		_loadoutHintLabel.Size = new Size(888, 27);
		_loadoutHintLabel.TabIndex = 1;
		_loadoutHintLabel.TextAlign = ContentAlignment.MiddleLeft;
		_loadoutHintLabel.Text =
			"Only the four missile launchers (MSL6, MSL8, MSL10, FLYMSL) carry an ammunition type; " +
			"every other mount ignores it. A launcher left on (none) fires SARH.";
		//
		// _slotButtonPanel
		//
		_slotButtonPanel.Controls.Add(_addSlotButton);
		_slotButtonPanel.Controls.Add(_removeSlotButton);
		_slotButtonPanel.Dock = DockStyle.Bottom;
		_slotButtonPanel.Location = new Point(6, 191);
		_slotButtonPanel.Name = "_slotButtonPanel";
		_slotButtonPanel.Size = new Size(888, 41);
		_slotButtonPanel.TabIndex = 2;
		//
		// _addSlotButton
		//
		_addSlotButton.Location = new Point(2, 6);
		_addSlotButton.Name = "_addSlotButton";
		_addSlotButton.Size = new Size(120, 28);
		_addSlotButton.TabIndex = 0;
		_addSlotButton.Text = "Add Slot";
		_addSlotButton.UseVisualStyleBackColor = true;
		_addSlotButton.Click += OnAddSlot;
		//
		// _removeSlotButton
		//
		_removeSlotButton.Location = new Point(128, 6);
		_removeSlotButton.Name = "_removeSlotButton";
		_removeSlotButton.Size = new Size(120, 28);
		_removeSlotButton.TabIndex = 1;
		_removeSlotButton.Text = "Remove Slot";
		_removeSlotButton.UseVisualStyleBackColor = true;
		_removeSlotButton.Click += OnRemoveSlot;
		//
		// _unk00Column
		//
		_unk00Column.DataPropertyName = nameof(PlayerSquadRow.PilotNameIndex);
		_unk00Column.HeaderText = "Pilot name index";
		_unk00Column.Name = "_unk00Column";
		_unk00Column.Width = 70;
		//
		// _unk02Column
		//
		_unk02Column.DataPropertyName = nameof(PlayerSquadRow.PilotSkill);
		_unk02Column.HeaderText = "Pilot skill (0-3)";
		_unk02Column.Name = "_unk02Column";
		_unk02Column.Width = 70;
		//
		// _unk3AColumn
		//
		_unk3AColumn.DataPropertyName = nameof(PlayerSquadRow.Unk3A);
		_unk3AColumn.HeaderText = "Unk 3A (shell writes 0)";
		_unk3AColumn.Name = "_unk3AColumn";
		_unk3AColumn.Width = 70;
		//
		// _buttonPanel
		//
		_buttonPanel.Controls.Add(_addEntryButton);
		_buttonPanel.Controls.Add(_removeEntryButton);
		_buttonPanel.Controls.Add(_conditionsButton);
		_buttonPanel.Dock = DockStyle.Bottom;
		_buttonPanel.Location = new Point(0, 576);
		_buttonPanel.Name = "_buttonPanel";
		_buttonPanel.Padding = new Padding(6);
		_buttonPanel.Size = new Size(900, 52);
		_buttonPanel.TabIndex = 3;
		//
		// _addEntryButton
		//
		_addEntryButton.Location = new Point(8, 10);
		_addEntryButton.Name = "_addEntryButton";
		_addEntryButton.Size = new Size(140, 28);
		_addEntryButton.TabIndex = 0;
		_addEntryButton.Text = "Add Wingman";
		_addEntryButton.UseVisualStyleBackColor = true;
		_addEntryButton.Click += OnAddEntry;
		//
		// _removeEntryButton
		//
		_removeEntryButton.Location = new Point(154, 10);
		_removeEntryButton.Name = "_removeEntryButton";
		_removeEntryButton.Size = new Size(140, 28);
		_removeEntryButton.TabIndex = 1;
		_removeEntryButton.Text = "Remove Entry";
		_removeEntryButton.UseVisualStyleBackColor = true;
		_removeEntryButton.Click += OnRemoveEntry;
		//
		// _conditionsButton
		//
		_conditionsButton.Location = new Point(300, 10);
		_conditionsButton.Name = "_conditionsButton";
		_conditionsButton.Size = new Size(160, 28);
		_conditionsButton.TabIndex = 2;
		_conditionsButton.Text = "Edit Conditions...";
		_conditionsButton.UseVisualStyleBackColor = true;
		_conditionsButton.Click += OnEditConditions;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 628);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(900, 22);
		_statusStrip.TabIndex = 4;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No player.mec loaded.";
		//
		// PlayerSquadForm
		//
		Size = new Size(900, 650);
		Controls.Add(_squadSplit);
		Controls.Add(_buttonPanel);
		Controls.Add(_topPanel);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "PlayerSquadForm";
		Text = "Player Squad Editor — data\\player.mec";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		_topPanel.ResumeLayout(false);
		_squadGroupBox.ResumeLayout(false);
		_squadGroupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)_playerSlotInput).EndInit();
		_squadSplit.Panel1.ResumeLayout(false);
		_squadSplit.Panel2.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_squadSplit).EndInit();
		_squadSplit.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_squadGrid).EndInit();
		_loadoutGroupBox.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_loadoutGrid).EndInit();
		_slotButtonPanel.ResumeLayout(false);
		_buttonPanel.ResumeLayout(false);
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
	private Panel _topPanel;
	private GroupBox _squadGroupBox;
	private Label _playerSlotLabel;
	private NumericUpDown _playerSlotInput;
	private Label _noteLabel;
	private SplitContainer _squadSplit;
	private DataGridView _squadGrid;
	private DataGridViewTextBoxColumn _indexColumn;
	private DataGridViewComboBoxColumn _hercTypeColumn;
	private DataGridViewTextBoxColumn _slotsColumn;
	private DataGridViewTextBoxColumn _fitColumn;
	private GroupBox _loadoutGroupBox;
	private DataGridView _loadoutGrid;
	private DataGridViewTextBoxColumn _slotIndexColumn;
	private DataGridViewComboBoxColumn _slotWeaponColumn;
	private DataGridViewComboBoxColumn _slotAmmoColumn;
	private Panel _slotButtonPanel;
	private Button _addSlotButton;
	private Button _removeSlotButton;
	private Label _loadoutHintLabel;
	private DataGridViewTextBoxColumn _unk00Column;
	private DataGridViewTextBoxColumn _unk02Column;
	private DataGridViewTextBoxColumn _unk3AColumn;
	private Panel _buttonPanel;
	private Button _addEntryButton;
	private Button _removeEntryButton;
	private Button _conditionsButton;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
