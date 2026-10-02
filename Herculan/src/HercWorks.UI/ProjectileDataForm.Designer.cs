namespace HercWorks.UI;

partial class ProjectileDataForm {
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
		_indexColumn = new DataGridViewTextBoxColumn();
		_typeColumn = new DataGridViewComboBoxColumn();
		_subtypeIdColumn = new DataGridViewTextBoxColumn();
		_damageShieldColumn = new DataGridViewTextBoxColumn();
		_damageArmorColumn = new DataGridViewTextBoxColumn();
		_splashFactorColumn = new DataGridViewTextBoxColumn();
		_speedColumn = new DataGridViewTextBoxColumn();
		_shieldFx0Column = new DataGridViewTextBoxColumn();
		_shieldFx1Column = new DataGridViewTextBoxColumn();
		_shieldFx2Column = new DataGridViewTextBoxColumn();
		_shieldFx3Column = new DataGridViewTextBoxColumn();
		_groundFx0Column = new DataGridViewTextBoxColumn();
		_groundFx1Column = new DataGridViewTextBoxColumn();
		_groundFx2Column = new DataGridViewTextBoxColumn();
		_groundFx3Column = new DataGridViewTextBoxColumn();
		_armorFx0Column = new DataGridViewTextBoxColumn();
		_armorFx1Column = new DataGridViewTextBoxColumn();
		_armorFx2Column = new DataGridViewTextBoxColumn();
		_armorFx3Column = new DataGridViewTextBoxColumn();
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
		_menuStrip.Size = new Size(1384, 24);
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
		_openMenuItem.Text = "&Open PROJ.DAT...";
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
			_indexColumn, _typeColumn, _subtypeIdColumn, _damageShieldColumn, _damageArmorColumn,
			_splashFactorColumn, _speedColumn,
			_shieldFx0Column, _shieldFx1Column, _shieldFx2Column, _shieldFx3Column,
			_groundFx0Column, _groundFx1Column, _groundFx2Column, _groundFx3Column,
			_armorFx0Column, _armorFx1Column, _armorFx2Column, _armorFx3Column
		});
		_grid.DataSource = _rows;
		_grid.Dock = DockStyle.Fill;
		_grid.Location = new Point(0, 24);
		_grid.Name = "_grid";
		_grid.RowHeadersVisible = false;
		_grid.Size = new Size(1384, 515);
		_grid.TabIndex = 1;
		_grid.CellEndEdit += OnGridCellEndEdit;
		_grid.DataError += OnGridDataError;
		//
		// _indexColumn
		//
		_indexColumn.DataPropertyName = nameof(ProjectileRow.Index);
		_indexColumn.Frozen = true;
		_indexColumn.HeaderText = "#";
		_indexColumn.Name = "_indexColumn";
		_indexColumn.ReadOnly = true;
		_indexColumn.ToolTipText = "The record's index — what a weapon template's PROJ.DAT index names.";
		_indexColumn.Width = 40;
		//
		// _typeColumn
		//
		_typeColumn.DataPropertyName = nameof(ProjectileRow.Type);
		_typeColumn.DataSource = HercWorks.Core.Data.Struct.ProjectileType.All;
		_typeColumn.DisplayMember = "Type";
		_typeColumn.HeaderText = "Type";
		_typeColumn.Name = "_typeColumn";
		_typeColumn.ToolTipText = "Firing-mechanism selector: which projectile class the record builds.";
		_typeColumn.ValueMember = "Val";
		_typeColumn.Width = 90;
		//
		// _subtypeIdColumn
		//
		_subtypeIdColumn.DataPropertyName = nameof(ProjectileRow.SubtypeId);
		_subtypeIdColumn.HeaderText = "Subtype";
		_subtypeIdColumn.Name = "_subtypeIdColumn";
		_subtypeIdColumn.ToolTipText = "The ROCKETS.DAT, BULLETS.DAT or BEAM.DAT record, by Type.";
		_subtypeIdColumn.Width = 65;
		//
		// _damageShieldColumn
		//
		_damageShieldColumn.DataPropertyName = nameof(ProjectileRow.DamageShield);
		_damageShieldColumn.HeaderText = "Shield Dmg";
		_damageShieldColumn.Name = "_damageShieldColumn";
		_damageShieldColumn.ToolTipText = "Damage against shields, at full power (1024).";
		_damageShieldColumn.Width = 80;
		//
		// _damageArmorColumn
		//
		_damageArmorColumn.DataPropertyName = nameof(ProjectileRow.DamageArmor);
		_damageArmorColumn.HeaderText = "Armor Dmg";
		_damageArmorColumn.Name = "_damageArmorColumn";
		_damageArmorColumn.ToolTipText = "Damage against armor, at full power (1024).";
		_damageArmorColumn.Width = 80;
		//
		// _splashFactorColumn
		//
		_splashFactorColumn.DataPropertyName = nameof(ProjectileRow.SplashFactor);
		_splashFactorColumn.HeaderText = "Splash";
		_splashFactorColumn.Name = "_splashFactorColumn";
		_splashFactorColumn.ToolTipText = "Q10 fraction (1024 = all) of the shield-reduced armor damage diverted into a secondary explosion.";
		_splashFactorColumn.Width = 65;
		//
		// _speedColumn
		//
		_speedColumn.DataPropertyName = nameof(ProjectileRow.Speed);
		_speedColumn.HeaderText = "Speed";
		_speedColumn.Name = "_speedColumn";
		_speedColumn.ToolTipText = "A travelling round's speed, a rocket's ceiling. 0 on every Beam.";
		_speedColumn.Width = 65;
		//
		// _shieldFx0Column
		//
		_shieldFx0Column.DataPropertyName = nameof(ProjectileRow.ShieldFx0);
		_shieldFx0Column.HeaderText = "Shield FX 1";
		_shieldFx0Column.Name = "_shieldFx0Column";
		_shieldFx0Column.ToolTipText = "EXPLOS.DAT effect type for a hit the shields fully absorbed; one of the four is drawn at random.";
		_shieldFx0Column.Width = 80;
		//
		// _shieldFx1Column
		//
		_shieldFx1Column.DataPropertyName = nameof(ProjectileRow.ShieldFx1);
		_shieldFx1Column.HeaderText = "Shield FX 2";
		_shieldFx1Column.Name = "_shieldFx1Column";
		_shieldFx1Column.ToolTipText = "EXPLOS.DAT effect type for a hit the shields fully absorbed; one of the four is drawn at random.";
		_shieldFx1Column.Width = 80;
		//
		// _shieldFx2Column
		//
		_shieldFx2Column.DataPropertyName = nameof(ProjectileRow.ShieldFx2);
		_shieldFx2Column.HeaderText = "Shield FX 3";
		_shieldFx2Column.Name = "_shieldFx2Column";
		_shieldFx2Column.ToolTipText = "EXPLOS.DAT effect type for a hit the shields fully absorbed; one of the four is drawn at random.";
		_shieldFx2Column.Width = 80;
		//
		// _shieldFx3Column
		//
		_shieldFx3Column.DataPropertyName = nameof(ProjectileRow.ShieldFx3);
		_shieldFx3Column.HeaderText = "Shield FX 4";
		_shieldFx3Column.Name = "_shieldFx3Column";
		_shieldFx3Column.ToolTipText = "EXPLOS.DAT effect type for a hit the shields fully absorbed; one of the four is drawn at random.";
		_shieldFx3Column.Width = 80;
		//
		// _groundFx0Column
		//
		_groundFx0Column.DataPropertyName = nameof(ProjectileRow.GroundFx0);
		_groundFx0Column.HeaderText = "Ground FX 1";
		_groundFx0Column.Name = "_groundFx0Column";
		_groundFx0Column.ToolTipText = "EXPLOS.DAT effect type for a shot ending on terrain, or an armor hit that left the struck component's health band unchanged.";
		_groundFx0Column.Width = 85;
		//
		// _groundFx1Column
		//
		_groundFx1Column.DataPropertyName = nameof(ProjectileRow.GroundFx1);
		_groundFx1Column.HeaderText = "Ground FX 2";
		_groundFx1Column.Name = "_groundFx1Column";
		_groundFx1Column.ToolTipText = "EXPLOS.DAT effect type for a shot ending on terrain, or an armor hit that left the struck component's health band unchanged.";
		_groundFx1Column.Width = 85;
		//
		// _groundFx2Column
		//
		_groundFx2Column.DataPropertyName = nameof(ProjectileRow.GroundFx2);
		_groundFx2Column.HeaderText = "Ground FX 3";
		_groundFx2Column.Name = "_groundFx2Column";
		_groundFx2Column.ToolTipText = "EXPLOS.DAT effect type for a shot ending on terrain, or an armor hit that left the struck component's health band unchanged.";
		_groundFx2Column.Width = 85;
		//
		// _groundFx3Column
		//
		_groundFx3Column.DataPropertyName = nameof(ProjectileRow.GroundFx3);
		_groundFx3Column.HeaderText = "Ground FX 4";
		_groundFx3Column.Name = "_groundFx3Column";
		_groundFx3Column.ToolTipText = "EXPLOS.DAT effect type for a shot ending on terrain, or an armor hit that left the struck component's health band unchanged.";
		_groundFx3Column.Width = 85;
		//
		// _armorFx0Column
		//
		_armorFx0Column.DataPropertyName = nameof(ProjectileRow.ArmorFx0);
		_armorFx0Column.HeaderText = "Armor FX 1";
		_armorFx0Column.Name = "_armorFx0Column";
		_armorFx0Column.ToolTipText = "EXPLOS.DAT effect type for an armor hit that dropped the struck component's health band.";
		_armorFx0Column.Width = 80;
		//
		// _armorFx1Column
		//
		_armorFx1Column.DataPropertyName = nameof(ProjectileRow.ArmorFx1);
		_armorFx1Column.HeaderText = "Armor FX 2";
		_armorFx1Column.Name = "_armorFx1Column";
		_armorFx1Column.ToolTipText = "EXPLOS.DAT effect type for an armor hit that dropped the struck component's health band.";
		_armorFx1Column.Width = 80;
		//
		// _armorFx2Column
		//
		_armorFx2Column.DataPropertyName = nameof(ProjectileRow.ArmorFx2);
		_armorFx2Column.HeaderText = "Armor FX 3";
		_armorFx2Column.Name = "_armorFx2Column";
		_armorFx2Column.ToolTipText = "EXPLOS.DAT effect type for an armor hit that dropped the struck component's health band.";
		_armorFx2Column.Width = 80;
		//
		// _armorFx3Column
		//
		_armorFx3Column.DataPropertyName = nameof(ProjectileRow.ArmorFx3);
		_armorFx3Column.HeaderText = "Armor FX 4";
		_armorFx3Column.Name = "_armorFx3Column";
		_armorFx3Column.ToolTipText = "EXPLOS.DAT effect type for an armor hit that dropped the struck component's health band.";
		_armorFx3Column.Width = 80;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 539);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(1384, 22);
		_statusStrip.TabIndex = 2;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No file loaded.";
		//
		// ProjectileDataForm
		//
		ClientSize = new Size(1384, 561);
		Controls.Add(_grid);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "ProjectileDataForm";
		Text = "Projectile Editor — PROJ.DAT";
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
	private DataGridViewTextBoxColumn _indexColumn;
	private DataGridViewComboBoxColumn _typeColumn;
	private DataGridViewTextBoxColumn _subtypeIdColumn;
	private DataGridViewTextBoxColumn _damageShieldColumn;
	private DataGridViewTextBoxColumn _damageArmorColumn;
	private DataGridViewTextBoxColumn _splashFactorColumn;
	private DataGridViewTextBoxColumn _speedColumn;
	private DataGridViewTextBoxColumn _shieldFx0Column;
	private DataGridViewTextBoxColumn _shieldFx1Column;
	private DataGridViewTextBoxColumn _shieldFx2Column;
	private DataGridViewTextBoxColumn _shieldFx3Column;
	private DataGridViewTextBoxColumn _groundFx0Column;
	private DataGridViewTextBoxColumn _groundFx1Column;
	private DataGridViewTextBoxColumn _groundFx2Column;
	private DataGridViewTextBoxColumn _groundFx3Column;
	private DataGridViewTextBoxColumn _armorFx0Column;
	private DataGridViewTextBoxColumn _armorFx1Column;
	private DataGridViewTextBoxColumn _armorFx2Column;
	private DataGridViewTextBoxColumn _armorFx3Column;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
