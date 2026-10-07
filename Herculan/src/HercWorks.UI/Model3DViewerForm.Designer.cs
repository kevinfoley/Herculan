namespace HercWorks.UI;

partial class Model3DViewerForm {
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
		_openDtsMenuItem = new ToolStripMenuItem();
		_fileMenuSeparator = new ToolStripSeparator();
		_closeMenuItem = new ToolStripMenuItem();
		_viewMenuItem = new ToolStripMenuItem();
		_shadedModeMenuItem = new ToolStripMenuItem();
		_wireframeModeMenuItem = new ToolStripMenuItem();
		_shadedWireframeModeMenuItem = new ToolStripMenuItem();
		_viewMenuSeparator = new ToolStripSeparator();
		_resetViewMenuItem = new ToolStripMenuItem();
		_helpMenuItem = new ToolStripMenuItem();
		_keyboardShortcutsMenuItem = new ToolStripMenuItem();
		_rootPanel = new Panel();
		_texturePanel = new Panel();
		_texturePaletteSelector = new ComboBox();
		_paletteLabel = new Label();
		_textureBankSelector = new ComboBox();
		_textureBankLabel = new Label();
		_textureLabel = new Label();
		_lodSelector = new ComboBox();
		_lodLabel = new Label();
		_partNavPanel = new Panel();
		_nextPartButton = new Button();
		_prevPartButton = new Button();
		_partSelector = new ComboBox();
		_rootListLabel = new Label();
		_viewerControl = new Model3DViewerControl();
		_statusStrip = new StatusStrip();
		_statusLabel = new ToolStripStatusLabel();
		_menuStrip.SuspendLayout();
		_rootPanel.SuspendLayout();
		_texturePanel.SuspendLayout();
		_partNavPanel.SuspendLayout();
		_statusStrip.SuspendLayout();
		SuspendLayout();
		//
		// _menuStrip
		//
		_menuStrip.Items.AddRange(new ToolStripItem[] { _fileMenuItem, _viewMenuItem, _helpMenuItem });
		_menuStrip.Location = new Point(0, 0);
		_menuStrip.Name = "_menuStrip";
		_menuStrip.Size = new Size(900, 24);
		_menuStrip.TabIndex = 0;
		//
		// _fileMenuItem
		//
		_fileMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
			_openDtsMenuItem, _fileMenuSeparator, _closeMenuItem
		});
		_fileMenuItem.Name = "_fileMenuItem";
		_fileMenuItem.Text = "&File";
		//
		// _openDtsMenuItem
		//
		_openDtsMenuItem.Name = "_openDtsMenuItem";
		_openDtsMenuItem.Text = "&Open Model...";
		_openDtsMenuItem.Click += OnOpenDts;
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
		// _viewMenuItem
		//
		_viewMenuItem.DropDownItems.AddRange(new ToolStripItem[] {
			_shadedModeMenuItem, _wireframeModeMenuItem, _shadedWireframeModeMenuItem,
			_viewMenuSeparator, _resetViewMenuItem
		});
		_viewMenuItem.Name = "_viewMenuItem";
		_viewMenuItem.Text = "&View";
		//
		// _shadedModeMenuItem
		//
		_shadedModeMenuItem.Name = "_shadedModeMenuItem";
		_shadedModeMenuItem.Text = "Shaded";
		_shadedModeMenuItem.Click += OnShadedMode;
		//
		// _wireframeModeMenuItem
		//
		_wireframeModeMenuItem.Name = "_wireframeModeMenuItem";
		_wireframeModeMenuItem.Text = "Wireframe";
		_wireframeModeMenuItem.Click += OnWireframeMode;
		//
		// _shadedWireframeModeMenuItem
		//
		_shadedWireframeModeMenuItem.Name = "_shadedWireframeModeMenuItem";
		_shadedWireframeModeMenuItem.Text = "Shaded + Wireframe";
		_shadedWireframeModeMenuItem.Click += OnShadedWireframeMode;
		//
		// _viewMenuSeparator
		//
		_viewMenuSeparator.Name = "_viewMenuSeparator";
		//
		// _resetViewMenuItem
		//
		_resetViewMenuItem.Name = "_resetViewMenuItem";
		_resetViewMenuItem.Text = "&Reset View";
		_resetViewMenuItem.Click += OnResetView;
		//
		// _helpMenuItem
		//
		_helpMenuItem.DropDownItems.AddRange(new ToolStripItem[] { _keyboardShortcutsMenuItem });
		_helpMenuItem.Name = "_helpMenuItem";
		_helpMenuItem.Text = "&Help";
		//
		// _keyboardShortcutsMenuItem
		//
		_keyboardShortcutsMenuItem.Name = "_keyboardShortcutsMenuItem";
		_keyboardShortcutsMenuItem.Text = "&Keyboard Shortcuts...";
		_keyboardShortcutsMenuItem.Click += OnShowKeyboardShortcuts;
		//
		// _rootPanel
		//
		_rootPanel.Controls.Add(_texturePanel);
		_rootPanel.Controls.Add(_lodSelector);
		_rootPanel.Controls.Add(_lodLabel);
		_rootPanel.Controls.Add(_partNavPanel);
		_rootPanel.Controls.Add(_partSelector);
		_rootPanel.Controls.Add(_rootListLabel);
		_rootPanel.Dock = DockStyle.Right;
		_rootPanel.Location = new Point(680, 24);
		_rootPanel.Name = "_rootPanel";
		_rootPanel.Size = new Size(220, 604);
		_rootPanel.TabIndex = 1;
		//
		// _rootListLabel
		//
		_rootListLabel.Dock = DockStyle.Top;
		_rootListLabel.Location = new Point(0, 0);
		_rootListLabel.Name = "_rootListLabel";
		_rootListLabel.Padding = new Padding(6, 6, 6, 4);
		_rootListLabel.Size = new Size(220, 27);
		_rootListLabel.TabIndex = 0;
		_rootListLabel.Text = "Part:";
		//
		// _partSelector
		//
		// A DTS file's top-level entries are each fully independent objects with no in-file
		// signal for how they relate to each other (see DtsGeometryBuilder's doc comment) — some
		// files bundle alternate LOD levels of one thing, others bundle several unrelated objects,
		// and both look like garbled overlapping geometry if shown together. So this always shows
		// exactly one at a time, defaulting to the first, regardless of which case it is.
		_partSelector.Dock = DockStyle.Top;
		_partSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		_partSelector.Location = new Point(0, 27);
		_partSelector.Name = "_partSelector";
		_partSelector.Size = new Size(220, 23);
		_partSelector.TabIndex = 1;
		_partSelector.SelectedIndexChanged += OnPartSelectionChanged;
		//
		// _partNavPanel
		//
		_partNavPanel.Controls.Add(_nextPartButton);
		_partNavPanel.Controls.Add(_prevPartButton);
		_partNavPanel.Dock = DockStyle.Top;
		_partNavPanel.Location = new Point(0, 50);
		_partNavPanel.Name = "_partNavPanel";
		_partNavPanel.Size = new Size(220, 30);
		_partNavPanel.TabIndex = 2;
		//
		// _prevPartButton
		//
		// Wraps at both ends (see Model3DViewerForm.NavigatePart) — Left/Right arrow keys do the
		// same thing regardless of which control has focus (see Model3DViewerForm.ProcessCmdKey).
		_prevPartButton.Dock = DockStyle.Left;
		_prevPartButton.Location = new Point(0, 0);
		_prevPartButton.Name = "_prevPartButton";
		_prevPartButton.Size = new Size(108, 30);
		_prevPartButton.TabIndex = 0;
		_prevPartButton.Text = "◀ Previous";
		_prevPartButton.Click += OnPreviousPart;
		//
		// _nextPartButton
		//
		_nextPartButton.Dock = DockStyle.Right;
		_nextPartButton.Location = new Point(112, 0);
		_nextPartButton.Name = "_nextPartButton";
		_nextPartButton.Size = new Size(108, 30);
		_nextPartButton.TabIndex = 1;
		_nextPartButton.Text = "Next ▶";
		_nextPartButton.Click += OnNextPart;
		//
		// _lodLabel
		//
		_lodLabel.Dock = DockStyle.Top;
		_lodLabel.Location = new Point(0, 80);
		_lodLabel.Name = "_lodLabel";
		_lodLabel.Padding = new Padding(6, 6, 6, 4);
		_lodLabel.Size = new Size(220, 27);
		_lodLabel.TabIndex = 3;
		_lodLabel.Text = "Detail Level:";
		_lodLabel.Visible = false;
		//
		// _lodSelector
		//
		// Only shown/populated when the currently-selected Part actually has real TSDetailPart
		// data with more than one level (see Model3DViewerForm.RefreshDetailLevelSelector) — most
		// parts don't, and forcing this dropdown to always be visible would be misleading (implying
		// a choice exists when there isn't one).
		_lodSelector.Dock = DockStyle.Top;
		_lodSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		_lodSelector.Location = new Point(0, 107);
		_lodSelector.Name = "_lodSelector";
		_lodSelector.Size = new Size(220, 23);
		_lodSelector.TabIndex = 4;
		_lodSelector.Visible = false;
		_lodSelector.SelectedIndexChanged += OnDetailLevelSelectionChanged;
		//
		// _texturePanel
		//
		_texturePanel.Controls.Add(_texturePaletteSelector);
		_texturePanel.Controls.Add(_paletteLabel);
		_texturePanel.Controls.Add(_textureBankSelector);
		_texturePanel.Controls.Add(_textureBankLabel);
		_texturePanel.Controls.Add(_textureLabel);
		_texturePanel.Dock = DockStyle.Top;
		_texturePanel.Location = new Point(0, 130);
		_texturePanel.Name = "_texturePanel";
		_texturePanel.Size = new Size(220, 113);
		_texturePanel.TabIndex = 5;
		//
		// _textureLabel
		//
		_textureLabel.Dock = DockStyle.Top;
		_textureLabel.Location = new Point(0, 0);
		_textureLabel.Name = "_textureLabel";
		_textureLabel.Padding = new Padding(6, 6, 6, 4);
		_textureLabel.Size = new Size(220, 27);
		_textureLabel.TabIndex = 0;
		_textureLabel.Text = "Texture:";
		//
		// _textureBankLabel
		//
		_textureBankLabel.Dock = DockStyle.Top;
		_textureBankLabel.Location = new Point(0, 27);
		_textureBankLabel.Name = "_textureBankLabel";
		_textureBankLabel.Padding = new Padding(6, 2, 6, 2);
		_textureBankLabel.Size = new Size(220, 20);
		_textureBankLabel.TabIndex = 1;
		_textureBankLabel.Text = "Texture bank (.DBA):";
		//
		// _textureBankSelector
		//
		// Lists every .dba entry in the loaded VOL directly (no intermediate picker dialog) plus a
		// trailing "Browse..." item that falls back to a filesystem OpenFileDialog — see
		// Model3DViewerForm.PopulateTextureBankSelector.
		_textureBankSelector.Dock = DockStyle.Top;
		_textureBankSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		_textureBankSelector.Location = new Point(0, 47);
		_textureBankSelector.Name = "_textureBankSelector";
		_textureBankSelector.Size = new Size(220, 23);
		_textureBankSelector.TabIndex = 2;
		_textureBankSelector.SelectedIndexChanged += OnTextureBankSelectionChanged;
		//
		// _paletteLabel
		//
		_paletteLabel.Dock = DockStyle.Top;
		_paletteLabel.Location = new Point(0, 70);
		_paletteLabel.Name = "_paletteLabel";
		_paletteLabel.Padding = new Padding(6, 2, 6, 2);
		_paletteLabel.Size = new Size(220, 20);
		_paletteLabel.TabIndex = 3;
		_paletteLabel.Text = "Palette (.DPL):";
		//
		// _texturePaletteSelector
		//
		// Same VOL-list-plus-Browse pattern as _textureBankSelector, for .dpl palettes — see
		// Model3DViewerForm.PopulateTexturePaletteSelector.
		_texturePaletteSelector.Dock = DockStyle.Top;
		_texturePaletteSelector.DropDownStyle = ComboBoxStyle.DropDownList;
		_texturePaletteSelector.Location = new Point(0, 90);
		_texturePaletteSelector.Name = "_texturePaletteSelector";
		_texturePaletteSelector.Size = new Size(220, 23);
		_texturePaletteSelector.TabIndex = 4;
		_texturePaletteSelector.SelectedIndexChanged += OnTexturePaletteSelectionChanged;
		//
		// _viewerControl
		//
		_viewerControl.Dock = DockStyle.Fill;
		_viewerControl.Location = new Point(0, 24);
		_viewerControl.Name = "_viewerControl";
		_viewerControl.Size = new Size(680, 604);
		_viewerControl.TabIndex = 4;
		//
		// _statusStrip
		//
		_statusStrip.Items.AddRange(new ToolStripItem[] { _statusLabel });
		_statusStrip.Location = new Point(0, 628);
		_statusStrip.Name = "_statusStrip";
		_statusStrip.Size = new Size(900, 22);
		_statusStrip.TabIndex = 3;
		//
		// _statusLabel
		//
		_statusLabel.Name = "_statusLabel";
		_statusLabel.Text = "No model loaded.";
		//
		// Model3DViewerForm
		//
		Size = new Size(900, 650);
		Controls.Add(_viewerControl);
		Controls.Add(_rootPanel);
		Controls.Add(_statusStrip);
		Controls.Add(_menuStrip);
		MainMenuStrip = _menuStrip;
		Name = "Model3DViewerForm";
		Text = "3D Model Viewer — DTS";
		_menuStrip.ResumeLayout(false);
		_menuStrip.PerformLayout();
		_rootPanel.ResumeLayout(false);
		_texturePanel.ResumeLayout(false);
		_partNavPanel.ResumeLayout(false);
		_statusStrip.ResumeLayout(false);
		_statusStrip.PerformLayout();
		ResumeLayout(false);
		PerformLayout();
	}

	#endregion

	private MenuStrip _menuStrip;
	private ToolStripMenuItem _fileMenuItem;
	private ToolStripMenuItem _openDtsMenuItem;
	private ToolStripSeparator _fileMenuSeparator;
	private ToolStripMenuItem _closeMenuItem;
	private ToolStripMenuItem _viewMenuItem;
	private ToolStripMenuItem _shadedModeMenuItem;
	private ToolStripMenuItem _wireframeModeMenuItem;
	private ToolStripMenuItem _shadedWireframeModeMenuItem;
	private ToolStripSeparator _viewMenuSeparator;
	private ToolStripMenuItem _resetViewMenuItem;
	private ToolStripMenuItem _helpMenuItem;
	private ToolStripMenuItem _keyboardShortcutsMenuItem;
	private Panel _rootPanel;
	private ComboBox _partSelector;
	private Label _rootListLabel;
	private Panel _partNavPanel;
	private Button _prevPartButton;
	private Button _nextPartButton;
	private Label _lodLabel;
	private ComboBox _lodSelector;
	private Panel _texturePanel;
	private Label _textureLabel;
	private Label _textureBankLabel;
	private ComboBox _textureBankSelector;
	private Label _paletteLabel;
	private ComboBox _texturePaletteSelector;
	private Model3DViewerControl _viewerControl;
	private StatusStrip _statusStrip;
	private ToolStripStatusLabel _statusLabel;
}
