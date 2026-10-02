namespace HercWorks.UI;

partial class CampaignResourcesForm {
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
		_tabs = new TabControl();
		_resourcesTab = new TabPage();
		_careerGroupBox = new GroupBox();
		_stageLabel = new Label();
		_stageInput = new NumericUpDown();
		_missionLabel = new Label();
		_missionInput = new NumericUpDown();
		_squadPositionsLabel = new Label();
		_squadPositionsInput = new NumericUpDown();
		_onStrengthLabel = new Label();
		_onStrengthInput = new NumericUpDown();
		_gameStateLabel = new Label();
		_gameStateInput = new NumericUpDown();
		_careerTextBox = new TextBox();
		_flagsTab = new TabPage();
		_flagsGrid = new DataGridView();
		_flagIndexColumn = new DataGridViewTextBoxColumn();
		_flagHexColumn = new DataGridViewTextBoxColumn();
		_flagValueColumn = new DataGridViewTextBoxColumn();
		_resourcesGroupBox = new GroupBox();
		_salvageLabel = new Label();
		_salvageInput = new NumericUpDown();
		_workshopSlot1Label = new Label();
		_workshopSlot1Combo = new ComboBox();
		_workshopSlot2Label = new Label();
		_workshopSlot2Combo = new ComboBox();
		_workshopSlot3Label = new Label();
		_workshopSlot3Combo = new ComboBox();
		_workshopSlot4Label = new Label();
		_workshopSlot4Combo = new ComboBox();
		_workshopSlot5Label = new Label();
		_workshopSlot5Combo = new ComboBox();
		_hercUnlocksTab = new TabPage();
		_hercUnlocksGrid = new DataGridView();
		_hercUnlockIdColumn = new DataGridViewTextBoxColumn();
		_hercUnlockNameColumn = new DataGridViewTextBoxColumn();
		_hercUnlockUnlockedColumn = new DataGridViewCheckBoxColumn();
		_squadmatesTab = new TabPage();
		_squadmatesGrid = new DataGridView();
		_sqRoleColumn = new DataGridViewTextBoxColumn();
		_sqIdColumn = new DataGridViewTextBoxColumn();
		_sqNameColumn = new DataGridViewTextBoxColumn();
		_sqBayIdColumn = new DataGridViewTextBoxColumn();
		_sqActiveColumn = new DataGridViewTextBoxColumn();
		_sqSkillColumn = new DataGridViewComboBoxColumn();
		_sqCrewRowColumn = new DataGridViewTextBoxColumn();
		_sqRankColumn = new DataGridViewComboBoxColumn();
		_sqHealthColumn = new DataGridViewTextBoxColumn();
		_sqKillsHercsColumn = new DataGridViewTextBoxColumn();
		_sqKillsFlyersColumn = new DataGridViewTextBoxColumn();
		_sqKillsBuildingColumn = new DataGridViewTextBoxColumn();
		_sqTotalKillHercColumn = new DataGridViewTextBoxColumn();
		_sqTotalKillFlyerColumn = new DataGridViewTextBoxColumn();
		_sqTotalKillBldngColumn = new DataGridViewTextBoxColumn();
		_sqMissionCountColumn = new DataGridViewTextBoxColumn();
		_sqNameIdxColumn = new DataGridViewTextBoxColumn();
		_inventoryTab = new TabPage();
		_inventoryGrid = new DataGridView();
		_inventoryNameColumn = new DataGridViewTextBoxColumn();
		_inventoryBuildableColumn = new DataGridViewCheckBoxColumn();
		_inventoryQuantityColumn = new DataGridViewTextBoxColumn();
		_hercBayTab = new TabPage();
		_hercBayGrid = new DataGridView();
		_hercBayIdColumn = new DataGridViewTextBoxColumn();
		_hercBayHercColumn = new DataGridViewComboBoxColumn();
		_hercBayBuildPercentColumn = new DataGridViewTextBoxColumn();
		_hercBayBuildStepColumn = new DataGridViewTextBoxColumn();
		_hercBayHardpointMaxColumn = new DataGridViewTextBoxColumn();
		_hercBayActiveSocketsColumn = new DataGridViewTextBoxColumn();
		_hercBayEditColumn = new DataGridViewButtonColumn();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		_tabs.SuspendLayout();
		_resourcesTab.SuspendLayout();
		_resourcesGroupBox.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_salvageInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_stageInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_missionInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_squadPositionsInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_onStrengthInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_gameStateInput).BeginInit();
		((System.ComponentModel.ISupportInitialize)_flagsGrid).BeginInit();
		_hercUnlocksTab.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_hercUnlocksGrid).BeginInit();
		_squadmatesTab.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_squadmatesGrid).BeginInit();
		_inventoryTab.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_inventoryGrid).BeginInit();
		_hercBayTab.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)_hercBayGrid).BeginInit();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _menuStrip
		//
		_menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenuItem });
		_menuStrip.Location = new Point(0, 0);
		_menuStrip.Name = "_menuStrip";
		_menuStrip.Size = new Size(980, 24);
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
		_openMenuItem.Text = "&Open Save File...";
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
		// _tabs
		//
		_tabs.Controls.Add(_resourcesTab);
		_tabs.Controls.Add(_hercUnlocksTab);
		_tabs.Controls.Add(_squadmatesTab);
		_tabs.Controls.Add(_inventoryTab);
		_tabs.Controls.Add(_hercBayTab);
		_tabs.Controls.Add(_flagsTab);
		_tabs.Dock = DockStyle.Fill;
		_tabs.Location = new Point(0, 24);
		_tabs.Name = "_tabs";
		_tabs.SelectedIndex = 0;
		_tabs.Size = new Size(980, 554);
		_tabs.TabIndex = 1;
		//
		// _resourcesTab
		//
		_resourcesTab.Controls.Add(_resourcesGroupBox);
		_resourcesTab.Controls.Add(_careerGroupBox);
		_resourcesTab.Location = new Point(4, 24);
		_resourcesTab.Name = "_resourcesTab";
		_resourcesTab.Padding = new Padding(3);
		_resourcesTab.Size = new Size(972, 526);
		_resourcesTab.TabIndex = 0;
		_resourcesTab.Text = "Resources";
		_resourcesTab.UseVisualStyleBackColor = true;
		//
		// _resourcesGroupBox
		//
		_resourcesGroupBox.Controls.Add(_salvageLabel);
		_resourcesGroupBox.Controls.Add(_salvageInput);
		_resourcesGroupBox.Controls.Add(_workshopSlot1Label);
		_resourcesGroupBox.Controls.Add(_workshopSlot1Combo);
		_resourcesGroupBox.Controls.Add(_workshopSlot2Label);
		_resourcesGroupBox.Controls.Add(_workshopSlot2Combo);
		_resourcesGroupBox.Controls.Add(_workshopSlot3Label);
		_resourcesGroupBox.Controls.Add(_workshopSlot3Combo);
		_resourcesGroupBox.Controls.Add(_workshopSlot4Label);
		_resourcesGroupBox.Controls.Add(_workshopSlot4Combo);
		_resourcesGroupBox.Controls.Add(_workshopSlot5Label);
		_resourcesGroupBox.Controls.Add(_workshopSlot5Combo);
		_resourcesGroupBox.Location = new Point(12, 12);
		_resourcesGroupBox.Name = "_resourcesGroupBox";
		_resourcesGroupBox.Size = new Size(460, 260);
		_resourcesGroupBox.TabIndex = 0;
		_resourcesGroupBox.TabStop = false;
		_resourcesGroupBox.Text = "Campaign Resources";
		//
		// _salvageLabel
		//
		_salvageLabel.AutoSize = true;
		_salvageLabel.Location = new Point(16, 32);
		_salvageLabel.Name = "_salvageLabel";
		_salvageLabel.Size = new Size(100, 15);
		_salvageLabel.TabIndex = 0;
		_salvageLabel.Text = "Salvage Total:";
		//
		// _salvageInput
		//
		_salvageInput.Location = new Point(180, 30);
		_salvageInput.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
		_salvageInput.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
		_salvageInput.Name = "_salvageInput";
		_salvageInput.Size = new Size(160, 23);
		_salvageInput.TabIndex = 1;
		//
		// _workshopSlot1Label
		//
		_workshopSlot1Label.AutoSize = true;
		_workshopSlot1Label.Location = new Point(16, 74);
		_workshopSlot1Label.Name = "_workshopSlot1Label";
		_workshopSlot1Label.Size = new Size(100, 15);
		_workshopSlot1Label.TabIndex = 2;
		_workshopSlot1Label.Text = "Workshop Slot 1:";
		//
		// _workshopSlot1Combo
		//
		_workshopSlot1Combo.DropDownStyle = ComboBoxStyle.DropDownList;
		_workshopSlot1Combo.Location = new Point(180, 71);
		_workshopSlot1Combo.Name = "_workshopSlot1Combo";
		_workshopSlot1Combo.Size = new Size(200, 23);
		_workshopSlot1Combo.TabIndex = 3;
		//
		// _workshopSlot2Label
		//
		_workshopSlot2Label.AutoSize = true;
		_workshopSlot2Label.Location = new Point(16, 111);
		_workshopSlot2Label.Name = "_workshopSlot2Label";
		_workshopSlot2Label.Size = new Size(100, 15);
		_workshopSlot2Label.TabIndex = 4;
		_workshopSlot2Label.Text = "Workshop Slot 2:";
		//
		// _workshopSlot2Combo
		//
		_workshopSlot2Combo.DropDownStyle = ComboBoxStyle.DropDownList;
		_workshopSlot2Combo.Location = new Point(180, 108);
		_workshopSlot2Combo.Name = "_workshopSlot2Combo";
		_workshopSlot2Combo.Size = new Size(200, 23);
		_workshopSlot2Combo.TabIndex = 5;
		//
		// _workshopSlot3Label
		//
		_workshopSlot3Label.AutoSize = true;
		_workshopSlot3Label.Location = new Point(16, 148);
		_workshopSlot3Label.Name = "_workshopSlot3Label";
		_workshopSlot3Label.Size = new Size(100, 15);
		_workshopSlot3Label.TabIndex = 6;
		_workshopSlot3Label.Text = "Workshop Slot 3:";
		//
		// _workshopSlot3Combo
		//
		_workshopSlot3Combo.DropDownStyle = ComboBoxStyle.DropDownList;
		_workshopSlot3Combo.Location = new Point(180, 145);
		_workshopSlot3Combo.Name = "_workshopSlot3Combo";
		_workshopSlot3Combo.Size = new Size(200, 23);
		_workshopSlot3Combo.TabIndex = 7;
		//
		// _workshopSlot4Label
		//
		_workshopSlot4Label.AutoSize = true;
		_workshopSlot4Label.Location = new Point(16, 185);
		_workshopSlot4Label.Name = "_workshopSlot4Label";
		_workshopSlot4Label.Size = new Size(100, 15);
		_workshopSlot4Label.TabIndex = 8;
		_workshopSlot4Label.Text = "Workshop Slot 4:";
		//
		// _workshopSlot4Combo
		//
		_workshopSlot4Combo.DropDownStyle = ComboBoxStyle.DropDownList;
		_workshopSlot4Combo.Location = new Point(180, 182);
		_workshopSlot4Combo.Name = "_workshopSlot4Combo";
		_workshopSlot4Combo.Size = new Size(200, 23);
		_workshopSlot4Combo.TabIndex = 9;
		//
		// _workshopSlot5Label
		//
		_workshopSlot5Label.AutoSize = true;
		_workshopSlot5Label.Location = new Point(16, 222);
		_workshopSlot5Label.Name = "_workshopSlot5Label";
		_workshopSlot5Label.Size = new Size(100, 15);
		_workshopSlot5Label.TabIndex = 10;
		_workshopSlot5Label.Text = "Workshop Slot 5:";
		//
		// _workshopSlot5Combo
		//
		_workshopSlot5Combo.DropDownStyle = ComboBoxStyle.DropDownList;
		_workshopSlot5Combo.Location = new Point(180, 219);
		_workshopSlot5Combo.Name = "_workshopSlot5Combo";
		_workshopSlot5Combo.Size = new Size(200, 23);
		_workshopSlot5Combo.TabIndex = 11;
		//
		// _hercUnlocksTab
		//
		_hercUnlocksTab.Controls.Add(_hercUnlocksGrid);
		_hercUnlocksTab.Location = new Point(4, 24);
		_hercUnlocksTab.Name = "_hercUnlocksTab";
		_hercUnlocksTab.Padding = new Padding(3);
		_hercUnlocksTab.Size = new Size(972, 526);
		_hercUnlocksTab.TabIndex = 1;
		_hercUnlocksTab.Text = "Herc Unlocks";
		_hercUnlocksTab.UseVisualStyleBackColor = true;
		//
		// _hercUnlocksGrid
		//
		_hercUnlocksGrid.AllowUserToAddRows = false;
		_hercUnlocksGrid.AllowUserToDeleteRows = false;
		_hercUnlocksGrid.AutoGenerateColumns = false;
		_hercUnlocksGrid.Columns.AddRange(new DataGridViewColumn[] {
			_hercUnlockIdColumn, _hercUnlockNameColumn, _hercUnlockUnlockedColumn
		});
		_hercUnlocksGrid.Dock = DockStyle.Fill;
		_hercUnlocksGrid.Location = new Point(3, 3);
		_hercUnlocksGrid.Name = "_hercUnlocksGrid";
		_hercUnlocksGrid.RowHeadersVisible = false;
		_hercUnlocksGrid.Size = new Size(966, 520);
		_hercUnlocksGrid.TabIndex = 0;
		//
		// _hercUnlockIdColumn
		//
		_hercUnlockIdColumn.DataPropertyName = nameof(HercUnlockRow.HercId);
		_hercUnlockIdColumn.HeaderText = "Herc Id";
		_hercUnlockIdColumn.Name = "_hercUnlockIdColumn";
		_hercUnlockIdColumn.ReadOnly = true;
		_hercUnlockIdColumn.Width = 60;
		//
		// _hercUnlockNameColumn
		//
		_hercUnlockNameColumn.DataPropertyName = nameof(HercUnlockRow.HercName);
		_hercUnlockNameColumn.HeaderText = "Herc Name";
		_hercUnlockNameColumn.Name = "_hercUnlockNameColumn";
		_hercUnlockNameColumn.ReadOnly = true;
		_hercUnlockNameColumn.Width = 150;
		//
		// _hercUnlockUnlockedColumn
		//
		_hercUnlockUnlockedColumn.DataPropertyName = nameof(HercUnlockRow.Unlocked);
		_hercUnlockUnlockedColumn.HeaderText = "Available";
		_hercUnlockUnlockedColumn.Name = "_hercUnlockUnlockedColumn";
		_hercUnlockUnlockedColumn.Width = 80;
		//
		// _squadmatesTab
		//
		_squadmatesTab.Controls.Add(_squadmatesGrid);
		_squadmatesTab.Location = new Point(4, 24);
		_squadmatesTab.Name = "_squadmatesTab";
		_squadmatesTab.Padding = new Padding(3);
		_squadmatesTab.Size = new Size(972, 526);
		_squadmatesTab.TabIndex = 2;
		_squadmatesTab.Text = "Squadmates";
		_squadmatesTab.UseVisualStyleBackColor = true;
		//
		// _squadmatesGrid
		//
		_squadmatesGrid.AllowUserToAddRows = false;
		_squadmatesGrid.AllowUserToDeleteRows = false;
		_squadmatesGrid.AutoGenerateColumns = false;
		_squadmatesGrid.Columns.AddRange(new DataGridViewColumn[] {
			_sqRoleColumn, _sqIdColumn, _sqNameIdxColumn, _sqNameColumn, _sqBayIdColumn,
			_sqActiveColumn, _sqSkillColumn, _sqRankColumn, _sqCrewRowColumn, _sqHealthColumn,
			_sqKillsHercsColumn, _sqKillsFlyersColumn, _sqKillsBuildingColumn,
			_sqTotalKillHercColumn, _sqTotalKillFlyerColumn, _sqTotalKillBldngColumn,
			_sqMissionCountColumn
		});
		_squadmatesGrid.Dock = DockStyle.Fill;
		_squadmatesGrid.Location = new Point(3, 3);
		_squadmatesGrid.Name = "_squadmatesGrid";
		_squadmatesGrid.RowHeadersVisible = false;
		_squadmatesGrid.Size = new Size(966, 520);
		_squadmatesGrid.TabIndex = 0;
		//
		// _sqRoleColumn
		//
		_sqRoleColumn.DataPropertyName = nameof(SquadmateRow.Role);
		_sqRoleColumn.HeaderText = "Role";
		_sqRoleColumn.Name = "_sqRoleColumn";
		_sqRoleColumn.ReadOnly = true;
		_sqRoleColumn.Width = 70;
		//
		// _sqIdColumn
		//
		_sqIdColumn.DataPropertyName = nameof(SquadmateRow.SquadmateId);
		_sqIdColumn.HeaderText = "Roster Id";
		_sqIdColumn.Name = "_sqIdColumn";
		_sqIdColumn.ReadOnly = true;
		_sqIdColumn.Width = 40;
		//
		// _sqNameColumn
		//
		_sqNameColumn.DataPropertyName = nameof(SquadmateRow.Name);
		_sqNameColumn.HeaderText = "Name";
		_sqNameColumn.Name = "_sqNameColumn";
		_sqNameColumn.Width = 120;
		//
		// _sqBayIdColumn
		//
		_sqBayIdColumn.DataPropertyName = nameof(SquadmateRow.BayId);
		_sqBayIdColumn.HeaderText = "Bay Id";
		_sqBayIdColumn.Name = "_sqBayIdColumn";
		_sqBayIdColumn.Width = 60;
		//
		// _sqActiveColumn
		//
		_sqActiveColumn.DataPropertyName = nameof(SquadmateRow.Active);
		_sqActiveColumn.HeaderText = "On Strength";
		_sqActiveColumn.Name = "_sqActiveColumn";
		_sqActiveColumn.Width = 70;
		//
		// _sqSkillColumn
		//
		_sqSkillColumn.DataPropertyName = nameof(SquadmateRow.SkillLabel);
		_sqSkillColumn.HeaderText = "Skill";
		_sqSkillColumn.Name = "_sqSkillColumn";
		_sqSkillColumn.Width = 100;
		//
		// _sqRankColumn
		//
		_sqRankColumn.DataPropertyName = nameof(SquadmateRow.RankLabel);
		_sqRankColumn.HeaderText = "Rank";
		_sqRankColumn.Name = "_sqRankColumn";
		_sqRankColumn.Width = 100;
		//
		// _sqCrewRowColumn
		//
		_sqCrewRowColumn.DataPropertyName = nameof(SquadmateRow.CrewRowNum);
		_sqCrewRowColumn.HeaderText = "Squad Position";
		_sqCrewRowColumn.Name = "_sqCrewRowColumn";
		_sqCrewRowColumn.Width = 70;
		//
		// _sqHealthColumn
		//
		_sqHealthColumn.DataPropertyName = nameof(SquadmateRow.ProbablyHealth);
		_sqHealthColumn.HeaderText = "Condition";
		_sqHealthColumn.Name = "_sqHealthColumn";
		_sqHealthColumn.Width = 70;
		//
		// _sqKillsHercsColumn
		//
		_sqKillsHercsColumn.DataPropertyName = nameof(SquadmateRow.KillsHercs);
		_sqKillsHercsColumn.HeaderText = "Last Mission Kills (Hercs)";
		_sqKillsHercsColumn.Name = "_sqKillsHercsColumn";
		_sqKillsHercsColumn.Width = 90;
		//
		// _sqKillsFlyersColumn
		//
		_sqKillsFlyersColumn.DataPropertyName = nameof(SquadmateRow.KillsFlyers);
		_sqKillsFlyersColumn.HeaderText = "Last Mission Kills (Flyers)";
		_sqKillsFlyersColumn.Name = "_sqKillsFlyersColumn";
		_sqKillsFlyersColumn.Width = 90;
		//
		// _sqKillsBuildingColumn
		//
		_sqKillsBuildingColumn.DataPropertyName = nameof(SquadmateRow.KillsBuilding);
		_sqKillsBuildingColumn.HeaderText = "Last Mission Kills (Bases)";
		_sqKillsBuildingColumn.Name = "_sqKillsBuildingColumn";
		_sqKillsBuildingColumn.Width = 100;
		//
		// _sqTotalKillHercColumn
		//
		_sqTotalKillHercColumn.DataPropertyName = nameof(SquadmateRow.TotalKillHerc);
		_sqTotalKillHercColumn.HeaderText = "Total Kills (Hercs)";
		_sqTotalKillHercColumn.Name = "_sqTotalKillHercColumn";
		_sqTotalKillHercColumn.Width = 110;
		//
		// _sqTotalKillFlyerColumn
		//
		_sqTotalKillFlyerColumn.DataPropertyName = nameof(SquadmateRow.TotalKillFlyer);
		_sqTotalKillFlyerColumn.HeaderText = "Total Kills (Flyers)";
		_sqTotalKillFlyerColumn.Name = "_sqTotalKillFlyerColumn";
		_sqTotalKillFlyerColumn.Width = 110;
		//
		// _sqTotalKillBldngColumn
		//
		_sqTotalKillBldngColumn.DataPropertyName = nameof(SquadmateRow.TotalKillBldng);
		_sqTotalKillBldngColumn.HeaderText = "Total Kills (Bases)";
		_sqTotalKillBldngColumn.Name = "_sqTotalKillBldngColumn";
		_sqTotalKillBldngColumn.Width = 130;
		//
		// _sqMissionCountColumn
		//
		_sqMissionCountColumn.DataPropertyName = nameof(SquadmateRow.MissionCount);
		_sqMissionCountColumn.HeaderText = "Missions";
		_sqMissionCountColumn.Name = "_sqMissionCountColumn";
		_sqMissionCountColumn.Width = 70;
		//
		// _sqNameIdxColumn
		//
		_sqNameIdxColumn.DataPropertyName = nameof(SquadmateRow.NameIndex);
		_sqNameIdxColumn.HeaderText = "Name Idx";
		_sqNameIdxColumn.Name = "_sqNameIdxColumn";
		_sqNameIdxColumn.ReadOnly = true;
		_sqNameIdxColumn.Width = 70;
		//
		// _inventoryTab
		//
		_inventoryTab.Controls.Add(_inventoryGrid);
		_inventoryTab.Location = new Point(4, 24);
		_inventoryTab.Name = "_inventoryTab";
		_inventoryTab.Padding = new Padding(3);
		_inventoryTab.Size = new Size(972, 526);
		_inventoryTab.TabIndex = 3;
		_inventoryTab.Text = "Weapon Inventory";
		_inventoryTab.UseVisualStyleBackColor = true;
		//
		// _inventoryGrid
		//
		_inventoryGrid.AllowUserToAddRows = false;
		_inventoryGrid.AllowUserToDeleteRows = false;
		_inventoryGrid.AutoGenerateColumns = false;
		_inventoryGrid.Columns.AddRange(new DataGridViewColumn[] {
			_inventoryNameColumn, _inventoryBuildableColumn, _inventoryQuantityColumn
		});
		_inventoryGrid.Dock = DockStyle.Fill;
		_inventoryGrid.Location = new Point(3, 3);
		_inventoryGrid.Name = "_inventoryGrid";
		_inventoryGrid.RowHeadersVisible = false;
		_inventoryGrid.Size = new Size(966, 520);
		_inventoryGrid.TabIndex = 0;
		//
		// _inventoryNameColumn
		//
		_inventoryNameColumn.DataPropertyName = nameof(InventoryRow.WeaponName);
		_inventoryNameColumn.HeaderText = "Weapon";
		_inventoryNameColumn.Name = "_inventoryNameColumn";
		_inventoryNameColumn.ReadOnly = true;
		_inventoryNameColumn.Width = 150;
		//
		// _inventoryBuildableColumn
		//
		_inventoryBuildableColumn.DataPropertyName = nameof(InventoryRow.Buildable);
		_inventoryBuildableColumn.HeaderText = "Unlocked";
		_inventoryBuildableColumn.Name = "_inventoryBuildableColumn";
		_inventoryBuildableColumn.Width = 80;
		//
		// _inventoryQuantityColumn
		//
		_inventoryQuantityColumn.DataPropertyName = nameof(InventoryRow.Quantity);
		_inventoryQuantityColumn.HeaderText = "Quantity";
		_inventoryQuantityColumn.Name = "_inventoryQuantityColumn";
		_inventoryQuantityColumn.Width = 80;
		//
		// _hercBayTab
		//
		_hercBayTab.Controls.Add(_hercBayGrid);
		_hercBayTab.Location = new Point(4, 24);
		_hercBayTab.Name = "_hercBayTab";
		_hercBayTab.Padding = new Padding(3);
		_hercBayTab.Size = new Size(972, 526);
		_hercBayTab.TabIndex = 4;
		_hercBayTab.Text = "Herc Bay";
		_hercBayTab.UseVisualStyleBackColor = true;
		//
		// _careerGroupBox
		//
		_careerGroupBox.Controls.Add(_stageLabel);
		_careerGroupBox.Controls.Add(_stageInput);
		_careerGroupBox.Controls.Add(_missionLabel);
		_careerGroupBox.Controls.Add(_missionInput);
		_careerGroupBox.Controls.Add(_squadPositionsLabel);
		_careerGroupBox.Controls.Add(_squadPositionsInput);
		_careerGroupBox.Controls.Add(_onStrengthLabel);
		_careerGroupBox.Controls.Add(_onStrengthInput);
		_careerGroupBox.Controls.Add(_gameStateLabel);
		_careerGroupBox.Controls.Add(_gameStateInput);
		_careerGroupBox.Controls.Add(_careerTextBox);
		_careerGroupBox.Location = new Point(490, 12);
		_careerGroupBox.Name = "_careerGroupBox";
		_careerGroupBox.Size = new Size(460, 480);
		_careerGroupBox.TabIndex = 1;
		_careerGroupBox.TabStop = false;
		_careerGroupBox.Text = "Career";
		//
		// _stageLabel
		//
		_stageLabel.AutoSize = true;
		_stageLabel.Location = new Point(16, 32);
		_stageLabel.Name = "_stageLabel";
		_stageLabel.Text = "Campaign stage (from 0):";
		//
		// _stageInput
		//
		_stageInput.Location = new Point(200, 30);
		_stageInput.Maximum = short.MaxValue;
		_stageInput.Minimum = short.MinValue;
		_stageInput.Name = "_stageInput";
		_stageInput.Size = new Size(100, 23);
		_stageInput.TabIndex = 0;
		//
		// _missionLabel
		//
		_missionLabel.AutoSize = true;
		_missionLabel.Location = new Point(16, 66);
		_missionLabel.Name = "_missionLabel";
		_missionLabel.Text = "Mission within stage:";
		//
		// _missionInput
		//
		_missionInput.Location = new Point(200, 64);
		_missionInput.Maximum = short.MaxValue;
		_missionInput.Minimum = short.MinValue;
		_missionInput.Name = "_missionInput";
		_missionInput.Size = new Size(100, 23);
		_missionInput.TabIndex = 1;
		//
		// _squadPositionsLabel
		//
		_squadPositionsLabel.AutoSize = true;
		_squadPositionsLabel.Location = new Point(16, 100);
		_squadPositionsLabel.Name = "_squadPositionsLabel";
		_squadPositionsLabel.Text = "Squad positions in play:";
		//
		// _squadPositionsInput
		//
		_squadPositionsInput.Location = new Point(200, 98);
		_squadPositionsInput.Maximum = short.MaxValue;
		_squadPositionsInput.Minimum = short.MinValue;
		_squadPositionsInput.Name = "_squadPositionsInput";
		_squadPositionsInput.Size = new Size(100, 23);
		_squadPositionsInput.TabIndex = 2;
		//
		// _onStrengthLabel
		//
		_onStrengthLabel.AutoSize = true;
		_onStrengthLabel.Location = new Point(16, 134);
		_onStrengthLabel.Name = "_onStrengthLabel";
		_onStrengthLabel.Text = "Machines on strength:";
		//
		// _onStrengthInput
		//
		_onStrengthInput.Location = new Point(200, 132);
		_onStrengthInput.Maximum = short.MaxValue;
		_onStrengthInput.Minimum = short.MinValue;
		_onStrengthInput.Name = "_onStrengthInput";
		_onStrengthInput.Size = new Size(100, 23);
		_onStrengthInput.TabIndex = 3;
		//
		// _gameStateLabel
		//
		_gameStateLabel.AutoSize = true;
		_gameStateLabel.Location = new Point(16, 168);
		_gameStateLabel.Name = "_gameStateLabel";
		_gameStateLabel.Text = "Game state (0048260e):";
		//
		// _gameStateInput
		//
		_gameStateInput.Location = new Point(200, 166);
		_gameStateInput.Maximum = short.MaxValue;
		_gameStateInput.Minimum = short.MinValue;
		_gameStateInput.Name = "_gameStateInput";
		_gameStateInput.Size = new Size(100, 23);
		_gameStateInput.TabIndex = 4;
		//
		// _careerTextBox
		//
		// Read-only: the briefing and debrief line indices only mean anything against the slot's own
		// sav\missn%d.str, which this editor does not load.
		_careerTextBox.Location = new Point(16, 204);
		_careerTextBox.Multiline = true;
		_careerTextBox.Name = "_careerTextBox";
		_careerTextBox.ReadOnly = true;
		_careerTextBox.ScrollBars = ScrollBars.Vertical;
		_careerTextBox.Size = new Size(428, 260);
		_careerTextBox.TabIndex = 5;
		//
		// _flagsTab
		//
		_flagsTab.Controls.Add(_flagsGrid);
		_flagsTab.Location = new Point(4, 24);
		_flagsTab.Name = "_flagsTab";
		_flagsTab.Padding = new Padding(3);
		_flagsTab.Size = new Size(972, 526);
		_flagsTab.TabIndex = 5;
		_flagsTab.Text = "Campaign Flags";
		_flagsTab.UseVisualStyleBackColor = true;
		//
		// _flagsGrid
		//
		_flagsGrid.AllowUserToAddRows = false;
		_flagsGrid.AllowUserToDeleteRows = false;
		_flagsGrid.AutoGenerateColumns = false;
		_flagsGrid.Columns.AddRange(new DataGridViewColumn[] { _flagIndexColumn, _flagHexColumn, _flagValueColumn });
		_flagsGrid.Dock = DockStyle.Fill;
		_flagsGrid.Name = "_flagsGrid";
		_flagsGrid.RowHeadersVisible = false;
		_flagsGrid.TabIndex = 0;
		//
		// _flagIndexColumn
		//
		_flagIndexColumn.DataPropertyName = nameof(CampaignFlagRow.Index);
		_flagIndexColumn.HeaderText = "Slot";
		_flagIndexColumn.Name = "_flagIndexColumn";
		_flagIndexColumn.ReadOnly = true;
		_flagIndexColumn.Width = 80;
		//
		// _flagHexColumn
		//
		_flagHexColumn.DataPropertyName = nameof(CampaignFlagRow.Hex);
		_flagHexColumn.HeaderText = "Slot (hex)";
		_flagHexColumn.Name = "_flagHexColumn";
		_flagHexColumn.ReadOnly = true;
		_flagHexColumn.Width = 80;
		//
		// _flagValueColumn
		//
		_flagValueColumn.DataPropertyName = nameof(CampaignFlagRow.Value);
		_flagValueColumn.HeaderText = "Value";
		_flagValueColumn.Name = "_flagValueColumn";
		_flagValueColumn.Width = 100;
		//
		// _hercBayGrid
		//
		_hercBayGrid.AllowUserToAddRows = false;
		_hercBayGrid.AllowUserToDeleteRows = false;
		_hercBayGrid.AutoGenerateColumns = false;
		_hercBayGrid.Columns.AddRange(new DataGridViewColumn[] {
			_hercBayIdColumn, _hercBayHercColumn, _hercBayBuildPercentColumn, _hercBayBuildStepColumn,
			_hercBayHardpointMaxColumn, _hercBayActiveSocketsColumn, _hercBayEditColumn
		});
		_hercBayGrid.Dock = DockStyle.Fill;
		_hercBayGrid.Location = new Point(3, 3);
		_hercBayGrid.Name = "_hercBayGrid";
		_hercBayGrid.RowHeadersVisible = false;
		_hercBayGrid.Size = new Size(966, 520);
		_hercBayGrid.TabIndex = 0;
		_hercBayGrid.CellClick += OnHercBayCellClick;
		_hercBayGrid.CellValueChanged += OnHercBayCellValueChanged;
		//
		// _hercBayIdColumn
		//
		_hercBayIdColumn.DataPropertyName = nameof(HercBayRow.BayId);
		_hercBayIdColumn.HeaderText = "Bay Id";
		_hercBayIdColumn.Name = "_hercBayIdColumn";
		_hercBayIdColumn.ReadOnly = true;
		_hercBayIdColumn.Width = 60;
		//
		// _hercBayHercColumn
		//
		_hercBayHercColumn.DataPropertyName = nameof(HercBayRow.Herc);
		_hercBayHercColumn.HeaderText = "Herc";
		_hercBayHercColumn.Name = "_hercBayHercColumn";
		_hercBayHercColumn.Width = 130;
		//
		// _hercBayBuildPercentColumn
		//
		_hercBayBuildPercentColumn.DataPropertyName = nameof(HercBayRow.BuildPercent);
		_hercBayBuildPercentColumn.HeaderText = "Build %";
		_hercBayBuildPercentColumn.Name = "_hercBayBuildPercentColumn";
		_hercBayBuildPercentColumn.Width = 70;
		//
		// _hercBayBuildStepColumn
		//
		_hercBayBuildStepColumn.DataPropertyName = nameof(HercBayRow.BuildStepNum);
		_hercBayBuildStepColumn.HeaderText = "Missions to Build";
		_hercBayBuildStepColumn.Name = "_hercBayBuildStepColumn";
		_hercBayBuildStepColumn.Width = 80;
		//
		// _hercBayHardpointMaxColumn
		//
		_hercBayHardpointMaxColumn.DataPropertyName = nameof(HercBayRow.HardpointMax);
		_hercBayHardpointMaxColumn.HeaderText = "Mount Capacity";
		_hercBayHardpointMaxColumn.Name = "_hercBayHardpointMaxColumn";
		_hercBayHardpointMaxColumn.Width = 100;
		//
		// _hercBayActiveSocketsColumn
		//
		_hercBayActiveSocketsColumn.DataPropertyName = nameof(HercBayRow.ActiveSocketCount);
		_hercBayActiveSocketsColumn.HeaderText = "Equipped Weapons";
		_hercBayActiveSocketsColumn.Name = "_hercBayActiveSocketsColumn";
		_hercBayActiveSocketsColumn.ReadOnly = true;
		_hercBayActiveSocketsColumn.Width = 120;
		//
		// _hercBayEditColumn
		//
		_hercBayEditColumn.HeaderText = "";
		_hercBayEditColumn.Name = "_hercBayEditColumn";
		_hercBayEditColumn.Text = "Edit Health / Weapons...";
		_hercBayEditColumn.UseColumnTextForButtonValue = true;
		_hercBayEditColumn.Width = 160;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 578);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(980, 22);
		_statusStrip.TabIndex = 2;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No file loaded.";
		//
		// CampaignResourcesForm
		//
		Size = new Size(980, 600);
		Controls.Add(_tabs);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "CampaignResourcesForm";
		Text = "Campaign Resources Editor — Player Save";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		_tabs.ResumeLayout(false);
		_resourcesTab.ResumeLayout(false);
		_resourcesGroupBox.ResumeLayout(false);
		_resourcesGroupBox.PerformLayout();
		((System.ComponentModel.ISupportInitialize)_salvageInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_stageInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_missionInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_squadPositionsInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_onStrengthInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_gameStateInput).EndInit();
		((System.ComponentModel.ISupportInitialize)_flagsGrid).EndInit();
		_hercUnlocksTab.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_hercUnlocksGrid).EndInit();
		_squadmatesTab.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_squadmatesGrid).EndInit();
		_inventoryTab.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_inventoryGrid).EndInit();
		_hercBayTab.ResumeLayout(false);
		((System.ComponentModel.ISupportInitialize)_hercBayGrid).EndInit();
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
	private TabControl _tabs;
	private TabPage _resourcesTab;
	private GroupBox _resourcesGroupBox;
	private GroupBox _careerGroupBox;
	private Label _stageLabel;
	private NumericUpDown _stageInput;
	private Label _missionLabel;
	private NumericUpDown _missionInput;
	private Label _squadPositionsLabel;
	private NumericUpDown _squadPositionsInput;
	private Label _onStrengthLabel;
	private NumericUpDown _onStrengthInput;
	private Label _gameStateLabel;
	private NumericUpDown _gameStateInput;
	private TextBox _careerTextBox;
	private TabPage _flagsTab;
	private DataGridView _flagsGrid;
	private DataGridViewTextBoxColumn _flagIndexColumn;
	private DataGridViewTextBoxColumn _flagHexColumn;
	private DataGridViewTextBoxColumn _flagValueColumn;
	private Label _salvageLabel;
	private NumericUpDown _salvageInput;
	private Label _workshopSlot1Label;
	private ComboBox _workshopSlot1Combo;
	private Label _workshopSlot2Label;
	private ComboBox _workshopSlot2Combo;
	private Label _workshopSlot3Label;
	private ComboBox _workshopSlot3Combo;
	private Label _workshopSlot4Label;
	private ComboBox _workshopSlot4Combo;
	private Label _workshopSlot5Label;
	private ComboBox _workshopSlot5Combo;
	private TabPage _hercUnlocksTab;
	private DataGridView _hercUnlocksGrid;
	private DataGridViewTextBoxColumn _hercUnlockIdColumn;
	private DataGridViewTextBoxColumn _hercUnlockNameColumn;
	private DataGridViewCheckBoxColumn _hercUnlockUnlockedColumn;
	private TabPage _squadmatesTab;
	private DataGridView _squadmatesGrid;
	private DataGridViewTextBoxColumn _sqRoleColumn;
	private DataGridViewTextBoxColumn _sqIdColumn;
	private DataGridViewTextBoxColumn _sqNameColumn;
	private DataGridViewTextBoxColumn _sqBayIdColumn;
	private DataGridViewTextBoxColumn _sqActiveColumn;
	private DataGridViewComboBoxColumn _sqSkillColumn;
	private DataGridViewComboBoxColumn _sqRankColumn;
	private DataGridViewTextBoxColumn _sqCrewRowColumn;
	private DataGridViewTextBoxColumn _sqHealthColumn;
	private DataGridViewTextBoxColumn _sqKillsHercsColumn;
	private DataGridViewTextBoxColumn _sqKillsFlyersColumn;
	private DataGridViewTextBoxColumn _sqKillsBuildingColumn;
	private DataGridViewTextBoxColumn _sqTotalKillHercColumn;
	private DataGridViewTextBoxColumn _sqTotalKillFlyerColumn;
	private DataGridViewTextBoxColumn _sqTotalKillBldngColumn;
	private DataGridViewTextBoxColumn _sqMissionCountColumn;
	private DataGridViewTextBoxColumn _sqNameIdxColumn;
	private TabPage _inventoryTab;
	private DataGridView _inventoryGrid;
	private DataGridViewTextBoxColumn _inventoryNameColumn;
	private DataGridViewCheckBoxColumn _inventoryBuildableColumn;
	private DataGridViewTextBoxColumn _inventoryQuantityColumn;
	private TabPage _hercBayTab;
	private DataGridView _hercBayGrid;
	private DataGridViewTextBoxColumn _hercBayIdColumn;
	private DataGridViewComboBoxColumn _hercBayHercColumn;
	private DataGridViewTextBoxColumn _hercBayBuildPercentColumn;
	private DataGridViewTextBoxColumn _hercBayBuildStepColumn;
	private DataGridViewTextBoxColumn _hercBayHardpointMaxColumn;
	private DataGridViewTextBoxColumn _hercBayActiveSocketsColumn;
	private DataGridViewButtonColumn _hercBayEditColumn;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
