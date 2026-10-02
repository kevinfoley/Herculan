namespace HercWorks.UI;

partial class MainForm {
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
		_openVolMenuItem = new ToolStripMenuItem();
		_unpackVolMenuItem = new ToolStripMenuItem();
		_exportSelectedFileMenuItem = new ToolStripMenuItem();
		_gameFolderMenuSeparator = new ToolStripSeparator();
		_setGameFolderMenuItem = new ToolStripMenuItem();
		_fileMenuSeparator = new ToolStripSeparator();
		_exitMenuItem = new ToolStripMenuItem();
		_editMenuItem = new ToolStripMenuItem();
		_hercStatsMenuItem = new ToolStripMenuItem();
		_itemStatsMenuItem = new ToolStripMenuItem();
		_projectilesMenuItem = new ToolStripMenuItem();
		_campaignResourcesMenuItem = new ToolStripMenuItem();
		_missionScriptMenuItem = new ToolStripMenuItem();
		_playerSquadMenuItem = new ToolStripMenuItem();
		_missionFilesMenuItem = new ToolStripMenuItem();
		_toolsMenuItem = new ToolStripMenuItem();
		_imageExportMenuItem = new ToolStripMenuItem();
		_modelViewerMenuItem = new ToolStripMenuItem();
		_volTree = new TreeView();
		_fileDetails = new ListView();
		_fileDetailsPropertyColumn = new ColumnHeader();
		_fileDetailsValueColumn = new ColumnHeader();
		_contentTree = new TreeView();
		_viewAssetPanel = new Panel();
		_viewAssetButton = new Button();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		_viewAssetPanel.SuspendLayout();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		// 
		// _menuStrip
		// 
		_menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenuItem, _editMenuItem, _toolsMenuItem });
		_menuStrip.Location = new Point(0, 0);
		_menuStrip.Name = "_menuStrip";
		_menuStrip.Size = new Size(984, 24);
		_menuStrip.TabIndex = 0;
		_menuStrip.Text = "menuStrip";
		// 
		// _fileMenuItem
		// 
		_fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] { _openVolMenuItem, _unpackVolMenuItem, _exportSelectedFileMenuItem, _gameFolderMenuSeparator, _setGameFolderMenuItem, _fileMenuSeparator, _exitMenuItem });
		_fileMenuItem.Name = "_fileMenuItem";
		_fileMenuItem.Size = new Size(37, 20);
		_fileMenuItem.Text = "&File";
		// 
		// _openVolMenuItem
		// 
		_openVolMenuItem.Name = "_openVolMenuItem";
		_openVolMenuItem.Size = new Size(201, 22);
		_openVolMenuItem.Text = "&Open VOL...";
		_openVolMenuItem.Click += OnOpenVol;
		// 
		// _unpackVolMenuItem
		// 
		_unpackVolMenuItem.Name = "_unpackVolMenuItem";
		_unpackVolMenuItem.Size = new Size(201, 22);
		_unpackVolMenuItem.Text = "&Unpack VOL To Folder...";
		_unpackVolMenuItem.Click += OnUnpackVol;
		// 
		// _exportSelectedFileMenuItem
		// 
		_exportSelectedFileMenuItem.Enabled = false;
		_exportSelectedFileMenuItem.Name = "_exportSelectedFileMenuItem";
		_exportSelectedFileMenuItem.Size = new Size(201, 22);
		_exportSelectedFileMenuItem.Text = "Export &Selected File...";
		_exportSelectedFileMenuItem.Click += OnExportSelectedFile;
		// 
		// _gameFolderMenuSeparator
		// 
		_gameFolderMenuSeparator.Name = "_gameFolderMenuSeparator";
		_gameFolderMenuSeparator.Size = new Size(198, 6);
		// 
		// _setGameFolderMenuItem
		// 
		_setGameFolderMenuItem.Name = "_setGameFolderMenuItem";
		_setGameFolderMenuItem.Size = new Size(201, 22);
		_setGameFolderMenuItem.Text = "Set Earthsiege 2 &Folder...";
		_setGameFolderMenuItem.Click += OnSetGameFolder;
		// 
		// _fileMenuSeparator
		// 
		_fileMenuSeparator.Name = "_fileMenuSeparator";
		_fileMenuSeparator.Size = new Size(198, 6);
		// 
		// _exitMenuItem
		// 
		_exitMenuItem.Name = "_exitMenuItem";
		_exitMenuItem.Size = new Size(201, 22);
		_exitMenuItem.Text = "E&xit";
		_exitMenuItem.Click += OnExit;
		// 
		// _editMenuItem
		// 
		_editMenuItem.DropDownItems.AddRange(new ToolStripItem[] { _hercStatsMenuItem, _itemStatsMenuItem, _projectilesMenuItem, _campaignResourcesMenuItem, _missionScriptMenuItem, _playerSquadMenuItem, _missionFilesMenuItem });
		_editMenuItem.Name = "_editMenuItem";
		_editMenuItem.Size = new Size(39, 20);
		_editMenuItem.Text = "&Edit";
		// 
		// _hercStatsMenuItem
		// 
		_hercStatsMenuItem.Name = "_hercStatsMenuItem";
		_hercStatsMenuItem.Size = new Size(220, 22);
		_hercStatsMenuItem.Text = "Herc Stats...";
		_hercStatsMenuItem.Click += OnOpenHercStats;
		// 
		// _itemStatsMenuItem
		// 
		_itemStatsMenuItem.Name = "_itemStatsMenuItem";
		_itemStatsMenuItem.Size = new Size(220, 22);
		_itemStatsMenuItem.Text = "Item Stats...";
		_itemStatsMenuItem.Click += OnOpenItemStats;
		// 
		// _projectilesMenuItem
		// 
		_projectilesMenuItem.Name = "_projectilesMenuItem";
		_projectilesMenuItem.Size = new Size(220, 22);
		_projectilesMenuItem.Text = "Projectiles (PROJ.DAT)...";
		_projectilesMenuItem.Click += OnOpenProjectiles;
		// 
		// _campaignResourcesMenuItem
		// 
		_campaignResourcesMenuItem.Name = "_campaignResourcesMenuItem";
		_campaignResourcesMenuItem.Size = new Size(220, 22);
		_campaignResourcesMenuItem.Text = "Campaign Resources...";
		_campaignResourcesMenuItem.Click += OnOpenCampaignResources;
		// 
		// _missionScriptMenuItem
		// 
		_missionScriptMenuItem.Name = "_missionScriptMenuItem";
		_missionScriptMenuItem.Size = new Size(220, 22);
		_missionScriptMenuItem.Text = "Mission Script (script.dat)...";
		_missionScriptMenuItem.Click += OnOpenMissionScript;
		// 
		// _playerSquadMenuItem
		// 
		_playerSquadMenuItem.Name = "_playerSquadMenuItem";
		_playerSquadMenuItem.Size = new Size(220, 22);
		_playerSquadMenuItem.Text = "Player Squad (player.mec)...";
		_playerSquadMenuItem.Click += OnOpenPlayerSquad;
		// 
		// _missionFilesMenuItem
		// 
		_missionFilesMenuItem.Enabled = false;
		_missionFilesMenuItem.Name = "_missionFilesMenuItem";
		_missionFilesMenuItem.Size = new Size(220, 22);
		_missionFilesMenuItem.Text = "Mission Files...";
		// 
		// _toolsMenuItem
		// 
		_toolsMenuItem.DropDownItems.AddRange(new ToolStripItem[] { _imageExportMenuItem, _modelViewerMenuItem });
		_toolsMenuItem.Name = "_toolsMenuItem";
		_toolsMenuItem.Size = new Size(47, 20);
		_toolsMenuItem.Text = "&Tools";
		// 
		// _imageExportMenuItem
		// 
		_imageExportMenuItem.Name = "_imageExportMenuItem";
		_imageExportMenuItem.Size = new Size(243, 22);
		_imageExportMenuItem.Text = "Image Export (DBA/DBM/DPL)...";
		_imageExportMenuItem.Click += OnOpenImageExport;
		// 
		// _modelViewerMenuItem
		// 
		_modelViewerMenuItem.Name = "_modelViewerMenuItem";
		_modelViewerMenuItem.Size = new Size(243, 22);
		_modelViewerMenuItem.Text = "3D Model Viewer (DTS)...";
		_modelViewerMenuItem.Click += OnOpenModelViewer;
		// 
		// _volTree
		// 
		_volTree.Dock = DockStyle.Left;
		_volTree.HideSelection = false;
		_volTree.Location = new Point(0, 24);
		_volTree.Name = "_volTree";
		_volTree.Size = new Size(320, 565);
		_volTree.TabIndex = 1;
		_volTree.BeforeExpand += OnExpandTreeNode;
		_volTree.AfterSelect += OnTreeSelect;
		// 
		// _fileDetails
		// 
		_fileDetails.Columns.AddRange(new ColumnHeader[] { _fileDetailsPropertyColumn, _fileDetailsValueColumn });
		_fileDetails.Dock = DockStyle.Top;
		_fileDetails.FullRowSelect = true;
		_fileDetails.Location = new Point(320, 24);
		_fileDetails.Name = "_fileDetails";
		_fileDetails.Size = new Size(664, 202);
		_fileDetails.TabIndex = 2;
		_fileDetails.UseCompatibleStateImageBehavior = false;
		_fileDetails.View = View.Details;
		// 
		// _fileDetailsPropertyColumn
		// 
		_fileDetailsPropertyColumn.Text = "Property";
		_fileDetailsPropertyColumn.Width = 160;
		// 
		// _fileDetailsValueColumn
		// 
		_fileDetailsValueColumn.Text = "Value";
		_fileDetailsValueColumn.Width = 480;
		// 
		// _contentTree
		// 
		_contentTree.Dock = DockStyle.Fill;
		_contentTree.Location = new Point(320, 226);
		_contentTree.Name = "_contentTree";
		_contentTree.Size = new Size(664, 311);
		_contentTree.TabIndex = 3;
		// 
		// _viewAssetPanel
		// 
		_viewAssetPanel.Controls.Add(_viewAssetButton);
		_viewAssetPanel.Dock = DockStyle.Bottom;
		_viewAssetPanel.Location = new Point(320, 537);
		_viewAssetPanel.Name = "_viewAssetPanel";
		_viewAssetPanel.Padding = new Padding(8);
		_viewAssetPanel.Size = new Size(664, 52);
		_viewAssetPanel.TabIndex = 4;
		// 
		// _viewAssetButton
		// 
		_viewAssetButton.Dock = DockStyle.Fill;
		_viewAssetButton.Enabled = false;
		_viewAssetButton.Location = new Point(8, 8);
		_viewAssetButton.Name = "_viewAssetButton";
		_viewAssetButton.Size = new Size(648, 36);
		_viewAssetButton.TabIndex = 0;
		_viewAssetButton.Text = "View Asset";
		_viewAssetButton.Click += OnViewAsset;
		// 
		// _statusStrip
		// 
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 589);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(984, 22);
		_statusStrip.TabIndex = 5;
		// 
		// _statusLabel
		// 
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Size = new Size(109, 17);
		_statusLabel.Text = "No VOL file loaded.";
		// 
		// MainForm
		// 
		ClientSize = new Size(984, 611);
		Controls.Add(_contentTree);
		Controls.Add(_fileDetails);
		Controls.Add(_viewAssetPanel);
		Controls.Add(_volTree);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "MainForm";
		Text = "HercWorks MDK";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		_viewAssetPanel.ResumeLayout(false);
		_statusStrip.ResumeLayout(false);
		_statusStrip.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private MenuStrip _menuStrip;
	private ToolStripMenuItem _fileMenuItem;
	private ToolStripMenuItem _openVolMenuItem;
	private ToolStripMenuItem _unpackVolMenuItem;
	private ToolStripMenuItem _exportSelectedFileMenuItem;
	private ToolStripSeparator _gameFolderMenuSeparator;
	private ToolStripMenuItem _setGameFolderMenuItem;
	private ToolStripSeparator _fileMenuSeparator;
	private ToolStripMenuItem _exitMenuItem;
	private ToolStripMenuItem _editMenuItem;
	private ToolStripMenuItem _hercStatsMenuItem;
	private ToolStripMenuItem _itemStatsMenuItem;
	private ToolStripMenuItem _projectilesMenuItem;
	private ToolStripMenuItem _campaignResourcesMenuItem;
	private ToolStripMenuItem _missionScriptMenuItem;
	private ToolStripMenuItem _playerSquadMenuItem;
	private ToolStripMenuItem _missionFilesMenuItem;
	private ToolStripMenuItem _toolsMenuItem;
	private ToolStripMenuItem _imageExportMenuItem;
	private ToolStripMenuItem _modelViewerMenuItem;
	private TreeView _volTree;
	private ListView _fileDetails;
	private ColumnHeader _fileDetailsPropertyColumn;
	private ColumnHeader _fileDetailsValueColumn;
	private TreeView _contentTree;
	private Panel _viewAssetPanel;
	private Button _viewAssetButton;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
