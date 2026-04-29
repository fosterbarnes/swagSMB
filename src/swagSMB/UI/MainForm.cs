using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using swagSMB.Core;
using swagSMB.Export;
using swagSMB.Models;
using swagSMB.Storage;

namespace swagSMB.UI
{
    public sealed class MainForm : Form
    {
        private const string ClearAllSettingsConfirmationPhrase = "poopFart";
        private const string WindowsStartupRunValueName = "swagSMB";
        private const int AboutTabIconPx = 192;
        private const int PersistDebounceMs = 350;
        private const int LogFlushIntervalMs = 150;
        private const int MaxServerLogLines = 2000;

        private readonly AppConfigStore _store;
        private readonly SmbServerHost _serverHost;
        private readonly SessionContext _session;
        private readonly DataGridView _sharesGrid;
        private readonly BindingList<ShareGridRow> _shareGridRows = new BindingList<ShareGridRow>();
        private readonly Dictionary<Guid, ShareConfig> _shareById = new Dictionary<Guid, ShareConfig>();
        private readonly Label _sharesStatusLabel;
        private readonly Label _globalStatusLabel;
        private readonly Panel _sharesListPanel;
        private readonly ShareEditorView _shareEditorView;
        private readonly Panel _sharesEditorPanel;
        private readonly CheckBox _autoStartCheckBox;
        private readonly CheckBox _startWithWindowsCheckBox;
        private bool _suppressStartWithWindowsEvent;
        private readonly CheckBox _startMinimizedToTrayCheckBox;
        private readonly NotifyIcon _trayIcon;
        private bool _handlingTrayMinimize;
        private bool _startupTrayApplied;
        private bool _exitRequested;
        private bool _launchedToTray;
        private bool _suppressInitialShow;
        private readonly CheckBox _closeToTrayCheckBox;
        private readonly CheckBox _requireMasterPasswordTrayCheckBox;
        private bool _trayStartMinimizedFirstGuiGateConsumed;
        private bool _trayStartMinimizedFirstGuiVerifyPending;
        private bool _insideMinimizeToTray;
        private DateTime _trayIgnoreLeftClicksUntilUtc;
        private readonly CheckBox _listenAllInterfacesCheckBox;
        private readonly ComboBox _bindIpComboBox;
        private readonly NumericUpDown _bindPortNumeric;
        private readonly CheckBox _requireSigningCheckBox;
        private readonly CheckBox _defaultEncryptionCheckBox;
        private readonly CheckBox _protocolLockCheckBox;
        private readonly NumericUpDown _autoLockMinutesNumeric;
        private readonly Timer _activityTimer;
        private readonly ToolTip _actionToolTip;
        private DateTime _lastActivityUtc;
        private Guid _editingShareId;
        private readonly TextBox _serverLogTextBox;
        private readonly TabControl _mainTabControl;
        private readonly Button _enableShareButton;
        private readonly Button _disableShareButton;
        private readonly ContextMenuStrip _trayContextMenu;
        private readonly RadioButton _themeSystemRadio;
        private readonly RadioButton _themeLightRadio;
        private readonly RadioButton _themeDarkRadio;
        private readonly RadioButton _themeDraculaRadio;
        private readonly Label _firewallLabel;
        private bool _suppressThemeEvent;
        private readonly Timer _saveDebounceTimer;
        private bool _saveQueued;
        private bool _saveWorkerActive;
        private Task _saveTask = Task.CompletedTask;
        private readonly Timer _logFlushTimer;
        private readonly Queue<string> _pendingLogLines = new Queue<string>();
        private readonly Queue<string> _serverLogHistory = new Queue<string>();
        private DateTime _lastActivitySampleUtc;

        public MainForm(AppConfigStore store, SessionContext session)
            : this(store, session, false)
        {
        }

        public MainForm(AppConfigStore store, SessionContext session, bool launchedToTray)
        {
            _launchedToTray = launchedToTray;
            _suppressInitialShow = launchedToTray;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _serverHost = new SmbServerHost();
            _actionToolTip = new ToolTip();
            _trayIcon = new NotifyIcon { Text = "swagSMB", Visible = false };
            try
            {
                using (Icon extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (extracted != null)
                    {
                        _trayIcon.Icon = (Icon)extracted.Clone();
                        Icon = (Icon)extracted.Clone();
                    }
                    else
                    {
                        _trayIcon.Icon = SystemIcons.Application;
                    }
                }
            }
            catch
            {
                _trayIcon.Icon = SystemIcons.Application;
            }

            _trayContextMenu = new ContextMenuStrip();
            _trayContextMenu.Items.Add("Open", null, delegate { ShowFromTray(); });
            _trayContextMenu.Items.Add("Exit", null, delegate
            {
                _exitRequested = true;
                Application.Exit();
            });
            UiTheme.ApplySystemContextMenuColors(_trayContextMenu);
            _trayIcon.ContextMenuStrip = _trayContextMenu;
            _trayIcon.MouseClick += TrayIcon_MouseClick;

            Text = "swagSMB";
            Font = new Font("Segoe UI", 9f);
            Width = 940;
            Height = 620;
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;

            _mainTabControl = new TabControl { Dock = DockStyle.Fill };
            var sharesTab = new TabPage("Shares");
            var settingsTab = new TabPage("Settings");
            var aboutTab = new TabPage("About");
            _mainTabControl.TabPages.Add(sharesTab);
            _mainTabControl.TabPages.Add(settingsTab);

            _sharesListPanel = new Panel { Dock = DockStyle.Fill };
            _sharesEditorPanel = new Panel { Dock = DockStyle.Fill, Visible = false };

            var sharesTopActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 58,
                Padding = new Padding(6),
                FlowDirection = FlowDirection.LeftToRight
            };
            sharesTopActions.Controls.Add(NewActionButton("", "Add", AddShareClick));
            sharesTopActions.Controls.Add(NewActionButton("", "Edit", EditShareClick));
            sharesTopActions.Controls.Add(NewActionButton("", "Remove", RemoveShareClick));
            _enableShareButton = NewActionButton("\uF5B0", "Enable share", EnableShareClick);
            _disableShareButton = NewActionButton("\uF8AE", "Disable share", DisableShareClick);
            sharesTopActions.Controls.Add(_enableShareButton);
            sharesTopActions.Controls.Add(_disableShareButton);
            sharesTopActions.Controls.Add(NewActionButton("\uEC50", "Open in File Explorer", RevealPathClick));
            sharesTopActions.Controls.Add(NewActionButton("\uE8E5", "Export client setup & removal scripts (.ps1))", ExportSetupScriptClick));

            _sharesGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _sharesGrid.RowTemplate.Height = 22;
            DataGridViewTextBoxColumn idColumn = NewTextColumn("Id", "Id");
            idColumn.Visible = false;
            _sharesGrid.Columns.Add(idColumn);
            _sharesGrid.Columns.Add(NewTextColumn("ShareName", "Share Name"));
            _sharesGrid.Columns.Add(NewTextColumn("LocalPath", "Local Path"));
            _sharesGrid.Columns.Add(NewTextColumn("Username", "Username"));
            _sharesGrid.Columns.Add(NewTextColumn("ProtocolMode", "Protocol"));
            _sharesGrid.Columns.Add(NewTextColumn("Enabled", "Status"));
            _sharesGrid.Columns.Add(NewTextColumn("RequireEncryption", "Encryption Required"));
            _sharesGrid.DataSource = _shareGridRows;
            _sharesGrid.CellDoubleClick += delegate { OpenEditorForSelection(); };
            _sharesGrid.SelectionChanged += SharesGridSelectionChanged;

            _sharesStatusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                Padding = new Padding(8, 5, 8, 4),
                Text = "Ready."
            };

            _sharesListPanel.Controls.Add(_sharesGrid);
            _sharesListPanel.Controls.Add(_sharesStatusLabel);
            _sharesListPanel.Controls.Add(sharesTopActions);

            _shareEditorView = new ShareEditorView();
            _shareEditorView.SaveRequested += ShareEditorSaveRequested;
            _shareEditorView.SaveAndApplyRequested += ShareEditorSaveAndApplyRequested;
            _shareEditorView.DeleteRequested += ShareEditorDeleteRequested;
            _shareEditorView.CancelRequested += ShareEditorCancelRequested;
            _shareEditorView.BackRequested += ShareEditorBackRequested;
            _sharesEditorPanel.Controls.Add(_shareEditorView);

            sharesTab.Controls.Add(_sharesEditorPanel);
            sharesTab.Controls.Add(_sharesListPanel);

            var settingsRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                RowCount = 6,
                ColumnCount = 1
            };
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            settingsRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            GroupBox serverBox = new GroupBox
            {
                Text = "Server Controls",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var serverActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Padding = new Padding(6, 6, 6, 12),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            var startButton = new Button { Text = "Start", Width = 88, Height = 30 };
            var stopButton = new Button { Text = "Stop", Width = 88, Height = 30 };
            var restartButton = new Button { Text = "Restart", Width = 88, Height = 30 };
            var applyButton = new Button { Text = "Apply", Width = 88, Height = 30, Margin = new Padding(3) };
            var restoreToDefaultsButton = new Button
            {
                Text = "Restore to Defaults",
                AutoSize = false,
                Width = 140,
                Height = 30,
                Margin = new Padding(3)
            };
            _autoStartCheckBox = new CheckBox { Text = "Auto-start server after unlock", Width = 220, Height = 24, Checked = _session.Config.Server.AutoStartAfterUnlock };
            _startWithWindowsCheckBox = new CheckBox
            {
                Text = "Start with Windows",
                Width = 140,
                Height = 24,
                Checked = _session.Config.Server.StartWithWindows
            };
            _startWithWindowsCheckBox.CheckedChanged += StartWithWindowsCheckBoxCheckedChanged;
            _startMinimizedToTrayCheckBox = new CheckBox
            {
                Text = "Start minimized to tray",
                Width = 170,
                Height = 24,
                Checked = _session.Config.Server.StartMinimizedToTray
            };
            _closeToTrayCheckBox = new CheckBox
            {
                Text = "Close to tray",
                Width = 120,
                Height = 24,
                Checked = _session.Config.Server.CloseToTray
            };
            _startMinimizedToTrayCheckBox.CheckedChanged += TrayRelatedOptionsChanged;
            _closeToTrayCheckBox.CheckedChanged += TrayRelatedOptionsChanged;
            _requireMasterPasswordTrayCheckBox = new CheckBox
            {
                Text = "Require master password when starting to tray",
                Width = 320,
                Height = 24,
                Checked = _session.Config.Server.RequireMasterPasswordWhenStartingToTray
            };
            startButton.Click += StartServerClick;
            stopButton.Click += StopServerClick;
            restartButton.Click += RestartServerClick;
            applyButton.Click += SaveAndApplySettingsClick;
            restoreToDefaultsButton.Click += RestoreToDefaultsClick;
            _actionToolTip.SetToolTip(restoreToDefaultsButton, "Remove encrypted config (all shares and settings on disk), then set up again.");

            var settingsFooterApplyRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0, 8, 0, 4)
            };
            settingsFooterApplyRow.Controls.Add(applyButton);
            settingsFooterApplyRow.Controls.Add(restoreToDefaultsButton);

            serverActions.Controls.Add(startButton);
            serverActions.Controls.Add(stopButton);
            serverActions.Controls.Add(restartButton);
            serverActions.Controls.Add(_autoStartCheckBox);
            serverActions.Controls.Add(_startWithWindowsCheckBox);
            serverActions.Controls.Add(_closeToTrayCheckBox);
            serverActions.Controls.Add(_startMinimizedToTrayCheckBox);
            serverActions.Controls.Add(_requireMasterPasswordTrayCheckBox);

            serverBox.Controls.Add(serverActions);

            void SyncServerActionsFlowHeight()
            {
                if (serverActions.IsDisposed)
                {
                    return;
                }

                int boxInnerW =
                    serverActions.Parent != null &&
                    serverActions.Parent.ClientSize.Width > 8
                        ? serverActions.Parent.ClientSize.Width
                        : Math.Max(0, serverBox.ClientSize.Width - 4);
                if (boxInnerW < 8)
                {
                    return;
                }

                int w = Math.Max(40, boxInnerW - serverActions.Padding.Horizontal);
                Size measured = serverActions.GetPreferredSize(new Size(w, 0));
                if (measured.Height <= 0)
                {
                    return;
                }

                if (serverActions.Height != measured.Height)
                {
                    serverActions.Height = measured.Height;
                }
            }

            Load += (_, __) => SyncServerActionsFlowHeight();
            serverBox.Resize += (_, __) => SyncServerActionsFlowHeight();

            GroupBox securityBox = new GroupBox
            {
                Text = "Security",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var securityPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 6, 6, 10),
                ColumnCount = 2,
                RowCount = 3
            };
            securityPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            securityPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            securityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            securityPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            securityPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _requireSigningCheckBox = new CheckBox { Text = "Require signing", Checked = _session.Config.Security.RequireSigning, AutoSize = true, Margin = new Padding(0, 4, 16, 4), AutoCheck = false, TabStop = false };
            _defaultEncryptionCheckBox = new CheckBox { Text = "Default encryption required", Checked = _session.Config.Security.DefaultRequireEncryption, AutoSize = true, Margin = new Padding(0, 4, 16, 4), AutoCheck = false, TabStop = false };
            _protocolLockCheckBox = new CheckBox { Text = "Lock protocol policy to SMB2.1 + SMB3.0", Checked = _session.Config.Security.LockProtocolToSmb21AndSmb30, AutoSize = true, Margin = new Padding(0, 4, 0, 4), AutoCheck = false, TabStop = false };
            const string upstreamSecurityToggleTip = "Not enforced (SMBLibrary 1.5.7 limitation). Server starts with SMB2/SMB3 only; SMB1 is hard-disabled in code.";
            _actionToolTip.SetToolTip(_requireSigningCheckBox, upstreamSecurityToggleTip);
            _actionToolTip.SetToolTip(_defaultEncryptionCheckBox, upstreamSecurityToggleTip);
            _actionToolTip.SetToolTip(_protocolLockCheckBox, upstreamSecurityToggleTip);
            var securityChecksFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            securityChecksFlow.Controls.Add(_requireSigningCheckBox);
            securityChecksFlow.Controls.Add(_defaultEncryptionCheckBox);
            securityChecksFlow.Controls.Add(_protocolLockCheckBox);
            _autoLockMinutesNumeric = new NumericUpDown { Minimum = 1, Maximum = 240, Value = _session.Config.Security.AutoLockMinutes, Width = 72, Margin = new Padding(0, 4, 0, 4) };
            var autoLockLabel = new Label { Text = "Auto-lock (Minutes)", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 8, 0) };
            var autoLockFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            autoLockFlow.Controls.Add(autoLockLabel);
            autoLockFlow.Controls.Add(_autoLockMinutesNumeric);
            var changeMasterPasswordButton = new Button { Text = "Change Master Password", Width = 170, Height = 28 };
            changeMasterPasswordButton.Click += ChangeMasterPasswordClick;
            var resetMasterPasswordButton = new Button { Text = "Reset Master Password", Width = 165, Height = 28 };
            resetMasterPasswordButton.Click += ResetMasterPasswordClick;
            var masterPwFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            masterPwFlow.Controls.Add(changeMasterPasswordButton);
            masterPwFlow.Controls.Add(resetMasterPasswordButton);
            securityPanel.Controls.Add(securityChecksFlow, 0, 0);
            securityPanel.SetColumnSpan(securityChecksFlow, 2);
            securityPanel.Controls.Add(autoLockFlow, 0, 1);
            securityPanel.SetColumnSpan(autoLockFlow, 2);
            securityPanel.Controls.Add(masterPwFlow, 0, 2);
            securityPanel.SetColumnSpan(masterPwFlow, 2);
            securityBox.Controls.Add(securityPanel);

            GroupBox networkBox = new GroupBox
            {
                Text = "Network",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var networkPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 6, 6, 10),
                ColumnCount = 3,
                RowCount = 4
            };
            networkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            networkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            networkPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            networkPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _listenAllInterfacesCheckBox = new CheckBox
            {
                Text = "Listen on all interfaces",
                Checked = _session.Config.Network.ListenOnAllInterfaces,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            _bindIpComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _bindPortNumeric = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 65535,
                Value = _session.Config.Network.Port <= 0 ? NetworkSettings.DefaultPort : _session.Config.Network.Port,
                Dock = DockStyle.Left,
                Width = 90
            };
            var testPortButton = new Button { Text = "Test Port", Width = 90, Height = 28 };
            testPortButton.Click += TestPortClick;
            _firewallLabel = new Label
            {
                Text = "Firewall reminder: allow inbound TCP on the selected SMB port.",
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            UiTheme.SetMuted(_firewallLabel, true);
            networkPanel.Controls.Add(new Label { Text = "Bind IP", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left }, 0, 0);
            networkPanel.Controls.Add(_bindIpComboBox, 1, 0);
            networkPanel.Controls.Add(new Label { Text = "Bind Port", TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left }, 0, 1);
            networkPanel.Controls.Add(_bindPortNumeric, 1, 1);
            networkPanel.Controls.Add(testPortButton, 2, 1);
            networkPanel.Controls.Add(_listenAllInterfacesCheckBox, 1, 2);
            networkPanel.Controls.Add(_firewallLabel, 1, 3);
            networkBox.Controls.Add(networkPanel);

            UiPreferences uiPreferences = _store.LoadUiPreferences();
            GroupBox themingBox = new GroupBox
            {
                Text = "Theming",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var themingPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6, 6, 6, 10),
                ColumnCount = 1,
                RowCount = 1
            };
            themingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            themingPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var themingFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 0),
                Margin = new Padding(0)
            };
            _themeSystemRadio = new RadioButton
            {
                Text = "System",
                AutoSize = true,
                Margin = new Padding(0, 4, 20, 4)
            };
            _themeLightRadio = new RadioButton
            {
                Text = "Light",
                AutoSize = true,
                Margin = new Padding(0, 4, 20, 4)
            };
            _themeDarkRadio = new RadioButton
            {
                Text = "Dark",
                AutoSize = true,
                Margin = new Padding(0, 4, 20, 4)
            };
            _themeDraculaRadio = new RadioButton
            {
                Text = "Dracula",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 4)
            };
            _themeSystemRadio.CheckedChanged += ThemeKindRadioCheckedChanged;
            _themeLightRadio.CheckedChanged += ThemeKindRadioCheckedChanged;
            _themeDarkRadio.CheckedChanged += ThemeKindRadioCheckedChanged;
            _themeDraculaRadio.CheckedChanged += ThemeKindRadioCheckedChanged;
            themingFlow.Controls.Add(_themeSystemRadio);
            themingFlow.Controls.Add(_themeLightRadio);
            themingFlow.Controls.Add(_themeDarkRadio);
            themingFlow.Controls.Add(_themeDraculaRadio);
            _suppressThemeEvent = true;
            switch (uiPreferences.Theme)
            {
                case UiThemeKind.System:
                    _themeSystemRadio.Checked = true;
                    break;
                case UiThemeKind.Light:
                    _themeLightRadio.Checked = true;
                    break;
                case UiThemeKind.Dark:
                    _themeDarkRadio.Checked = true;
                    break;
                case UiThemeKind.Dracula:
                    _themeDraculaRadio.Checked = true;
                    break;
                default:
                    _themeSystemRadio.Checked = true;
                    break;
            }
            _suppressThemeEvent = false;
            themingPanel.Controls.Add(themingFlow, 0, 0);
            themingBox.Controls.Add(themingPanel);

            void SyncThemingFlowHeight()
            {
                if (themingFlow.IsDisposed)
                {
                    return;
                }

                int boxInnerW =
                    themingFlow.Parent != null &&
                    themingFlow.Parent.ClientSize.Width > 8
                        ? themingFlow.Parent.ClientSize.Width
                        : Math.Max(0, themingBox.ClientSize.Width - 4);
                if (boxInnerW < 8)
                {
                    return;
                }

                int w = Math.Max(40, boxInnerW - themingFlow.Padding.Horizontal);
                Size measured = themingFlow.GetPreferredSize(new Size(w, 0));
                if (measured.Height <= 0)
                {
                    return;
                }

                if (themingFlow.Height != measured.Height)
                {
                    themingFlow.Height = measured.Height;
                }
            }

            Load += (_, __) => SyncThemingFlowHeight();
            themingBox.Resize += (_, __) => SyncThemingFlowHeight();

            settingsRoot.Controls.Add(serverBox, 0, 0);
            settingsRoot.Controls.Add(securityBox, 0, 1);
            settingsRoot.Controls.Add(networkBox, 0, 2);
            settingsRoot.Controls.Add(themingBox, 0, 4);
            settingsRoot.Controls.Add(settingsFooterApplyRow, 0, 5);
            settingsTab.Controls.Add(settingsRoot);

            var aboutPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var aboutBlurbRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 0, 0, 4),
                Margin = new Padding(0)
            };
            aboutBlurbRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AboutTabIconPx + 8));
            aboutBlurbRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            aboutBlurbRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var aboutIconPicture = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Normal,
                Margin = new Padding(0, 2, 8, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            aboutIconPicture.Image = TryLoadAboutIconBitmap();
            if (aboutIconPicture.Image == null)
            {
                aboutIconPicture.Image = ScaleAboutIconFallbackBitmap(Icon);
            }

            if (aboutIconPicture.Image == null)
            {
                aboutIconPicture.Image = ScaleAboutIconFallbackBitmap(SystemIcons.Application);
            }

            if (aboutIconPicture.Image != null)
            {
                Size sz = aboutIconPicture.Image.Size;
                aboutIconPicture.Size = sz;
                aboutBlurbRow.ColumnStyles[0].Width = sz.Width + 8f;
            }
            else
            {
                aboutIconPicture.Size = new Size(AboutTabIconPx, AboutTabIconPx);
            }

            string aboutVersion = TryReadBundledVersionFile();
            var aboutTightPair = new Padding(0, 0, 0, 2);
            var aboutBetweenGroups = new Padding(0, 0, 0, 12);
            var appVersionLabel = new Label
            {
                Text = $"swagSMB v{aboutVersion}  - Copyright © 2026 FosterBarnes",
                AutoSize = true,
                Margin = aboutTightPair,
                Padding = new Padding(0),
                TextAlign = ContentAlignment.TopLeft
            };
            const string repoUrl = "https://github.com/fosterbarnes/swagSMB";
            var repoLink = new LinkLabel
            {
                Text = repoUrl,
                AutoSize = true,
                Margin = aboutBetweenGroups,
                Padding = new Padding(0)
            };
            var aboutGuiLeadLabel = new Label
            {
                Text = "GUI frontend for SMBLibrary with some extras.",
                AutoSize = true,
                Margin = aboutTightPair,
                Padding = new Padding(0),
                TextAlign = ContentAlignment.TopLeft
            };
            var creditLink = new LinkLabel
            {
                Text = "Powered by SMBLibrary (TalAloni/SMBLibrary)",
                AutoSize = true,
                Margin = aboutBetweenGroups,
                Padding = new Padding(0)
            };
            creditLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/TalAloni/SMBLibrary",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _globalStatusLabel.Text = "Unable to open browser: " + ex.Message;
                }
            };
            var aboutGuiBlurbLabel = new Label
            {
                Text =
                    "Easily create SMB shares with custom paths, usernames, passwords, and enforce SMB3.0. Works on port 5446 by default, "
                    + "and intended to be separate from built-in Windows SMB functions. Locked by a master password with options for auto-run to tray. "
                    + "Offers other useful features like exporting setup scripts that let you easily deploy your SMB shares on other Windows clients using PowerShell.",
                AutoSize = true,
                MaximumSize = new Size(680, 0),
                Margin = new Padding(0, 0, 0, 0),
                Padding = new Padding(0),
                TextAlign = ContentAlignment.TopLeft
            };
            repoLink.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = repoUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    _globalStatusLabel.Text = "Unable to open browser: " + ex.Message;
                }
            };
            var aboutTextStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0),
                Margin = new Padding(0, 3, 0, 0)
            };
            aboutTextStack.Controls.Add(appVersionLabel);
            aboutTextStack.Controls.Add(repoLink);
            aboutTextStack.Controls.Add(aboutGuiLeadLabel);
            aboutTextStack.Controls.Add(creditLink);
            aboutTextStack.Controls.Add(aboutGuiBlurbLabel);
            aboutBlurbRow.Controls.Add(aboutIconPicture, 0, 0);
            aboutBlurbRow.Controls.Add(aboutTextStack, 1, 0);
            aboutPanel.Controls.Add(aboutBlurbRow);
            aboutTab.Controls.Add(aboutPanel);

            var logTab = new TabPage("Log");
            var logRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                RowCount = 2,
                ColumnCount = 1
            };
            logRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            logRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            var clearLogButton = new Button { Text = "Clear", Width = 72, Height = 28 };
            _serverLogTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                HideSelection = true,
                WordWrap = false
            };
            clearLogButton.Click += delegate { ClearServerLog(); };
            logToolbar.Controls.Add(clearLogButton);
            logRoot.Controls.Add(logToolbar, 0, 0);
            logRoot.Controls.Add(_serverLogTextBox, 0, 1);
            logTab.Controls.Add(logRoot);
            _mainTabControl.TabPages.Add(logTab);
            _mainTabControl.TabPages.Add(aboutTab);

            _serverHost.ServerActivity += AppendServerLogLine;
            _saveDebounceTimer = new Timer { Interval = PersistDebounceMs };
            _saveDebounceTimer.Tick += PersistDebounceTimerTick;
            _logFlushTimer = new Timer { Interval = LogFlushIntervalMs };
            _logFlushTimer.Tick += FlushPendingServerLogs;
            _logFlushTimer.Start();

            _globalStatusLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Padding = new Padding(8, 4, 8, 4),
                Text = "Ready."
            };

            Controls.Add(_mainTabControl);
            Controls.Add(_globalStatusLabel);

            ApplyUiTheme();
            PopulateAddressList();
            LoadStateToUi();
            BindSharesGrid();

            _activityTimer = new Timer { Interval = 30000 };
            _activityTimer.Tick += ActivityTimerTick;
            _activityTimer.Start();
            ResetActivityTimer();
            RegisterActivityEvents(this);

            FormClosing += MainFormClosing;
            Load += MainForm_Load;
            Shown += MainForm_Shown;
            Resize += MainForm_Resize;

            if (_launchedToTray)
            {
                _startupTrayApplied = true;
                _trayStartMinimizedFirstGuiVerifyPending = true;
                _trayStartMinimizedFirstGuiGateConsumed = true;
                ShowInTaskbar = false;
                _trayIcon.Visible = true;
            }

            if (_session.Config.Server.AutoStartAfterUnlock)
            {
                SafeStartServer();
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_suppressInitialShow && value)
            {
                if (!IsHandleCreated)
                {
                    CreateHandle();
                }

                _suppressInitialShow = false;
                base.SetVisibleCore(false);
                return;
            }

            base.SetVisibleCore(value);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            ResyncDwmFrame();
            if (_startupTrayApplied || !_session.Config.Server.StartMinimizedToTray)
            {
                return;
            }

            _startupTrayApplied = true;
            BeginInvoke(new Action(MinimizeToTray));
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (_handlingTrayMinimize || !_session.Config.Server.StartMinimizedToTray)
            {
                return;
            }

            if (WindowState != FormWindowState.Minimized)
            {
                return;
            }

            _handlingTrayMinimize = true;
            try
            {
                MinimizeToTray();
            }
            finally
            {
                _handlingTrayMinimize = false;
            }
        }

        private void MinimizeToTray()
        {
            _insideMinimizeToTray = true;
            try
            {
                SaveUiToState();
                if (_session.Config.Server.RequireMasterPasswordWhenStartingToTray)
                {
                    if (!_session.MasterSecret.IsEmpty)
                    {
                        PersistConfig();
                    }

                    _session.MasterSecret.Clear();
                }
                else if (_session.Config.Server.StartMinimizedToTray
                         && !_trayStartMinimizedFirstGuiGateConsumed)
                {
                    _trayStartMinimizedFirstGuiVerifyPending = true;
                    _trayStartMinimizedFirstGuiGateConsumed = true;
                }

                _trayIcon.Visible = true;
                Hide();
                ShowInTaskbar = false;
            }
            finally
            {
                _insideMinimizeToTray = false;
                _trayIgnoreLeftClicksUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
            }
        }

        private void ShowFromTray()
        {
            if (_session.Config.Server.RequireMasterPasswordWhenStartingToTray
                && string.IsNullOrEmpty(_session.MasterPassword))
            {
                using (var verifyForm = new VerifyMasterPasswordForm(_store))
                {
                    if (verifyForm.ShowDialog(Visible ? this : null) != DialogResult.OK)
                    {
                        return;
                    }

                    _session.MasterPassword = verifyForm.VerifiedPassword;
                }
            }
            else if (!_session.Config.Server.RequireMasterPasswordWhenStartingToTray
                     && _session.Config.Server.StartMinimizedToTray
                     && _trayStartMinimizedFirstGuiVerifyPending)
            {
                using (var verifyForm = new VerifyMasterPasswordForm(_store))
                {
                    if (verifyForm.ShowDialog(Visible ? this : null) != DialogResult.OK)
                    {
                        return;
                    }

                    _session.MasterPassword = verifyForm.VerifiedPassword;
                }

                _trayStartMinimizedFirstGuiVerifyPending = false;
            }

            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            ResyncDwmFrame();
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_insideMinimizeToTray || DateTime.UtcNow < _trayIgnoreLeftClicksUntilUtc)
            {
                return;
            }

            ShowFromTray();
        }

        private void TrayRelatedOptionsChanged(object sender, EventArgs e)
        {
            UpdateRequireMasterPasswordTrayAvailability();
        }

        private void UpdateRequireMasterPasswordTrayAvailability()
        {
            bool anyTray = _startMinimizedToTrayCheckBox.Checked || _closeToTrayCheckBox.Checked;
            _requireMasterPasswordTrayCheckBox.AutoCheck = anyTray;
            _requireMasterPasswordTrayCheckBox.TabStop = anyTray;
        }

        private void ThemeKindRadioCheckedChanged(object sender, EventArgs e)
        {
            if (_suppressThemeEvent)
            {
                return;
            }
            if (sender is not RadioButton { Checked: true })
            {
                return;
            }
            _store.SaveUiPreferences(new UiPreferences { Theme = GetSelectedTheme() });
            ApplyUiTheme();
        }

        private UiThemeKind GetSelectedTheme()
        {
            if (_themeSystemRadio.Checked)
            {
                return UiThemeKind.System;
            }
            if (_themeLightRadio.Checked)
            {
                return UiThemeKind.Light;
            }
            if (_themeDarkRadio.Checked)
            {
                return UiThemeKind.Dark;
            }
            if (_themeDraculaRadio.Checked)
            {
                return UiThemeKind.Dracula;
            }
            return UiThemeKind.System;
        }

        private void ApplyUiTheme()
        {
            UiThemeKind t = GetSelectedTheme();
            UiTheme.Apply(this, t, _serverLogTextBox);
            UiTheme.ApplyThemedChildChrome(this, t);
        }

        private void ResyncDwmFrame()
        {
            UiThemeKind t = GetSelectedTheme();
            UiTheme.ApplyWindowFrame(this, t);
            UiTheme.ApplyThemedChildChrome(this, t);
        }

        private static string TryReadBundledVersionFile()
        {
            try
            {
                string dir = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Application.StartupPath ?? ".";
                }

                string path = Path.Combine(dir, "version");
                if (!File.Exists(path))
                {
                    return "?";
                }

                string t = File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(t) ? "?" : t;
            }
            catch
            {
                return "?";
            }
        }

        private static Image TryLoadAboutIconBitmap()
        {
            try
            {
                string dir = AppContext.BaseDirectory;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Application.StartupPath ?? ".";
                }

                string pngPath = Path.Combine(dir, "swag192.png");
                if (File.Exists(pngPath))
                {
                    byte[] bytes = File.ReadAllBytes(pngPath);
                    using (var ms = new MemoryStream(bytes, writable: false))
                    using (var temp = new Bitmap(ms))
                    {
                        return new Bitmap(temp);
                    }
                }

                string icoPath = Path.Combine(dir, "swag.ico");
                if (!File.Exists(icoPath))
                {
                    return null;
                }

                using (Icon ico = new Icon(icoPath, AboutTabIconPx, AboutTabIconPx))
                {
                    return ico.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        private static Image ScaleAboutIconFallbackBitmap(Icon icon)
        {
            if (icon == null)
            {
                return null;
            }

            try
            {
                using (Icon scaled = new Icon(icon, AboutTabIconPx, AboutTabIconPx))
                {
                    return scaled.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        private static DataGridViewTextBoxColumn NewTextColumn(string fieldName, string header)
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = fieldName,
                HeaderText = header
            };
        }

        private Button NewActionButton(string glyph, string description, EventHandler clickHandler)
        {
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Padding(4),
                Text = glyph,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe MDL2 Assets", 10f),
                UseVisualStyleBackColor = false
            };
            button.Click += clickHandler;
            _actionToolTip.SetToolTip(button, description);
            return button;
        }

        private void BindSharesGrid()
        {
            var stopwatch = Stopwatch.StartNew();
            Guid? selectedId = TryGetCurrentSelectedShareId();

            _shareById.Clear();
            _shareGridRows.RaiseListChangedEvents = false;
            _shareGridRows.Clear();
            foreach (ShareConfig share in _session.Config.Shares)
            {
                _shareById[share.Id] = share;
                _shareGridRows.Add(new ShareGridRow
                {
                    Id = share.Id,
                    ShareName = share.ShareName,
                    LocalPath = share.LocalPath,
                    Username = share.Username,
                    ProtocolMode = share.ProtocolMode,
                    Enabled = share.Enabled ? "Enabled" : "Disabled",
                    RequireEncryption = share.RequireEncryption ? "Yes" : "No"
                });
            }
            _shareGridRows.RaiseListChangedEvents = true;
            _shareGridRows.ResetBindings();
            RestoreShareSelection(selectedId);
            _sharesStatusLabel.Text = "Shares loaded: " + _session.Config.Shares.Count;
            UpdateShareEnableButtons();
            stopwatch.Stop();
            Debug.WriteLine($"[Perf] BindSharesGrid took {stopwatch.ElapsedMilliseconds} ms (rows: {_shareGridRows.Count}).");
        }

        private void PopulateAddressList()
        {
            _ = PopulateAddressListAsync();
        }

        private Guid? TryGetCurrentSelectedShareId()
        {
            if (_sharesGrid.CurrentRow == null)
            {
                return null;
            }

            object idObject = _sharesGrid.CurrentRow.Cells[0].Value;
            return idObject != null && Guid.TryParse(idObject.ToString(), out Guid id) ? id : null;
        }

        private void RestoreShareSelection(Guid? selectedId)
        {
            if (selectedId == null)
            {
                return;
            }

            for (int index = 0; index < _sharesGrid.Rows.Count; index++)
            {
                DataGridViewRow row = _sharesGrid.Rows[index];
                object idObject = row.Cells[0].Value;
                if (idObject == null || !Guid.TryParse(idObject.ToString(), out Guid id))
                {
                    continue;
                }

                if (id != selectedId.Value)
                {
                    continue;
                }

                row.Selected = true;
                _sharesGrid.CurrentCell = row.Cells[1];
                return;
            }
        }

        private async Task PopulateAddressListAsync()
        {
            string[] addresses = await Task.Run(BuildAddressList);
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(() => ApplyAddressList(addresses)));
        }

        private static string[] BuildAddressList()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        seen.Add(address.ToString());
                    }
                }
            }
            catch
            {
            }

            string[] sorted = seen.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var addresses = new List<string>(sorted.Length + 1) { "0.0.0.0" };
            addresses.AddRange(sorted);
            return addresses.ToArray();
        }

        private void ApplyAddressList(IReadOnlyList<string> addresses)
        {
            if (addresses == null || addresses.Count == 0)
            {
                return;
            }

            bool changed = _bindIpComboBox.Items.Count != addresses.Count;
            if (!changed)
            {
                for (int index = 0; index < addresses.Count; index++)
                {
                    if (!string.Equals(_bindIpComboBox.Items[index]?.ToString(), addresses[index], StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed)
            {
                _bindIpComboBox.Items.Clear();
                foreach (string ip in addresses)
                {
                    _bindIpComboBox.Items.Add(ip);
                }
            }

            string selected = (_session.Config.Network.BindIPAddress ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(selected) || !_bindIpComboBox.Items.Contains(selected))
            {
                selected = _bindIpComboBox.Items.Count > 1
                    ? _bindIpComboBox.Items[1].ToString()
                    : "0.0.0.0";
            }

            if (!string.Equals(_bindIpComboBox.SelectedItem?.ToString(), selected, StringComparison.Ordinal))
            {
                _bindIpComboBox.SelectedItem = selected;
            }
            _session.Config.Network.BindIPAddress = selected;
        }

        private void LoadStateToUi()
        {
            _listenAllInterfacesCheckBox.Checked = _session.Config.Network.ListenOnAllInterfaces;
            _bindPortNumeric.Value = _session.Config.Network.Port <= 0 ? NetworkSettings.DefaultPort : _session.Config.Network.Port;
            _autoStartCheckBox.Checked = _session.Config.Server.AutoStartAfterUnlock;
            _suppressStartWithWindowsEvent = true;
            _startWithWindowsCheckBox.Checked = _session.Config.Server.StartWithWindows;
            _suppressStartWithWindowsEvent = false;
            _startMinimizedToTrayCheckBox.Checked = _session.Config.Server.StartMinimizedToTray;
            _closeToTrayCheckBox.Checked = _session.Config.Server.CloseToTray;
            _requireMasterPasswordTrayCheckBox.Checked = _session.Config.Server.RequireMasterPasswordWhenStartingToTray;
            UpdateRequireMasterPasswordTrayAvailability();
            _requireSigningCheckBox.Checked = _session.Config.Security.RequireSigning;
            _defaultEncryptionCheckBox.Checked = _session.Config.Security.DefaultRequireEncryption;
            _protocolLockCheckBox.Checked = _session.Config.Security.LockProtocolToSmb21AndSmb30;
            _autoLockMinutesNumeric.Value = _session.Config.Security.AutoLockMinutes;
            try
            {
                ApplyStartWithWindowsToRegistry(_session.Config.Server.StartWithWindows);
            }
            catch
            {
            }
        }

        private static void ApplyStartWithWindowsToRegistry(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Could not open the Windows startup registry key.");
                }

                if (enable)
                {
                    string path = Application.ExecutablePath;
                    if (path.IndexOf(' ') >= 0)
                    {
                        path = "\"" + path + "\"";
                    }

                    key.SetValue(WindowsStartupRunValueName, path);
                }
                else
                {
                    key.DeleteValue(WindowsStartupRunValueName, throwOnMissingValue: false);
                }
            }
        }

        private void StartWithWindowsCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            if (_suppressStartWithWindowsEvent)
            {
                return;
            }

            bool want = _startWithWindowsCheckBox.Checked;
            try
            {
                ApplyStartWithWindowsToRegistry(want);
                _session.Config.Server.StartWithWindows = want;
                PersistConfig();
            }
            catch (Exception ex)
            {
                _suppressStartWithWindowsEvent = true;
                try
                {
                    _startWithWindowsCheckBox.Checked = !want;
                    _session.Config.Server.StartWithWindows = !want;
                }
                finally
                {
                    _suppressStartWithWindowsEvent = false;
                }

                MessageBox.Show(
                    this,
                    "Could not change Windows startup: " + ex.Message,
                    "swagSMB",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void SaveUiToState()
        {
            _session.Config.Network.ListenOnAllInterfaces = _listenAllInterfacesCheckBox.Checked;
            _session.Config.Network.BindIPAddress = _bindIpComboBox.SelectedItem?.ToString() ?? string.Empty;
            _session.Config.Network.Port = (int)_bindPortNumeric.Value;
            _session.Config.Server.AutoStartAfterUnlock = _autoStartCheckBox.Checked;
            _session.Config.Server.StartWithWindows = _startWithWindowsCheckBox.Checked;
            _session.Config.Server.StartMinimizedToTray = _startMinimizedToTrayCheckBox.Checked;
            _session.Config.Server.CloseToTray = _closeToTrayCheckBox.Checked;
            _session.Config.Server.RequireMasterPasswordWhenStartingToTray = _requireMasterPasswordTrayCheckBox.Checked;
            if (!_session.Config.Server.StartMinimizedToTray
                || _session.Config.Server.RequireMasterPasswordWhenStartingToTray)
            {
                _session.Config.Server.AutoTrayConsented = false;
            }
            _session.Config.Security.RequireSigning = _requireSigningCheckBox.Checked;
            _session.Config.Security.DefaultRequireEncryption = _defaultEncryptionCheckBox.Checked;
            _session.Config.Security.LockProtocolToSmb21AndSmb30 = _protocolLockCheckBox.Checked;
            _session.Config.Security.AutoLockMinutes = (int)_autoLockMinutesNumeric.Value;
        }

        private void AddShareClick(object sender, EventArgs e)
        {
            _editingShareId = Guid.Empty;
            _shareEditorView.LoadShare(new ShareConfig
            {
                RequireEncryption = _session.Config.Security.DefaultRequireEncryption
            });
            ShowEditor(true);
        }

        private void EditShareClick(object sender, EventArgs e)
        {
            OpenEditorForSelection();
        }

        private void OpenEditorForSelection()
        {
            UseSelectedShare(selected =>
            {
                _editingShareId = selected.Id;
                _shareEditorView.LoadShare(selected);
                ShowEditor(true);
            });
        }

        private void RemoveShareClick(object sender, EventArgs e)
        {
            UseSelectedShare(selected =>
            {
                _session.Config.Shares.RemoveAll(item => item.Id == selected.Id);
                PersistConfig();
                BindSharesGrid();
                _sharesStatusLabel.Text = "Share removed.";
            });
        }

        private void EnableShareClick(object sender, EventArgs e)
        {
            UseSelectedShare(selected =>
            {
                if (selected.Enabled)
                {
                    return;
                }

                selected.Enabled = true;
                PersistConfig();
                BindSharesGrid();
                _sharesStatusLabel.Text = "Share enabled.";
            });
        }

        private void DisableShareClick(object sender, EventArgs e)
        {
            UseSelectedShare(selected =>
            {
                if (!selected.Enabled)
                {
                    return;
                }

                selected.Enabled = false;
                PersistConfig();
                BindSharesGrid();
                _sharesStatusLabel.Text = "Share disabled.";
            });
        }

        private void UpdateShareEnableButtons()
        {
            UpdateShareEnableButtons(GetSelectedShare());
        }

        private void UpdateShareEnableButtons(ShareConfig selected)
        {
            bool sel = selected != null;
            bool canEnable = sel && !selected.Enabled;
            bool canDisable = sel && selected.Enabled;

            _enableShareButton.Enabled = true;
            _disableShareButton.Enabled = true;
            _enableShareButton.Tag = canEnable ? null : UiTheme.ToolbarGlyphInactiveTag;
            _disableShareButton.Tag = canDisable ? null : UiTheme.ToolbarGlyphInactiveTag;
            _enableShareButton.TabStop = canEnable;
            _disableShareButton.TabStop = canDisable;
            _enableShareButton.Cursor = canEnable ? Cursors.Default : Cursors.No;
            _disableShareButton.Cursor = canDisable ? Cursors.Default : Cursors.No;

            ApplyUiTheme();
        }

        private void RevealPathClick(object sender, EventArgs e)
        {
            UseSelectedShare(selected =>
            {
                if (string.IsNullOrWhiteSpace(selected.LocalPath))
                {
                    _sharesStatusLabel.Text = "Selected share has no local path.";
                    return;
                }

                try
                {
                    Process.Start("explorer.exe", selected.LocalPath);
                }
                catch (Exception ex)
                {
                    _sharesStatusLabel.Text = "Could not open path: " + ex.Message;
                }
            });
        }

        private void ExportSetupScriptClick(object sender, EventArgs e)
        {
            List<ShareConfig> selected = GetSelectedShares();
            if (selected.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Select one or more shares, then export.",
                    "Export New-SmbMapping",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult modeChoice = MessageBox.Show(
                this,
                "How should credentials be handled in the exported script?\r\n\r\n" +
                "Yes - Embed share usernames and passwords in the script (PLAINTEXT-equivalent on disk; delete the script after first run).\r\n\r\n" +
                "No - Prompt for credentials at runtime via Get-Credential (more secure; recommended).\r\n\r\n" +
                "Cancel - Abort the export.",
                "Export New-SmbMapping - credential handling",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (modeChoice == DialogResult.Cancel)
            {
                return;
            }

            var credMode = modeChoice == DialogResult.Yes
                ? WindowsSetupScriptExporter.CredentialMode.EmbedPlaintext
                : WindowsSetupScriptExporter.CredentialMode.PromptAtRuntime;

            if (credMode == WindowsSetupScriptExporter.CredentialMode.EmbedPlaintext)
            {
                DialogResult ok = MessageBox.Show(
                    this,
                    "The exported .ps1 will contain share passwords (Base64 of UTF-8 bytes; this is NOT encryption). " +
                    "Anyone who reads the file can recover the plaintext password.\r\n\r\n" +
                    "Recommended: copy the script to the target machine, run it once, then delete it.\r\n\r\n" +
                    "Continue?",
                    "swagSMB - credential exposure warning",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (ok != DialogResult.OK)
                {
                    return;
                }
            }

            string script = WindowsSetupScriptExporter.Build(_session.Config, selected, credMode);
            if (string.IsNullOrEmpty(script))
            {
                MessageBox.Show(
                    this,
                    "Selected shares need a share name.",
                    "Export New-SmbMapping",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PowerShell (*.ps1)|*.ps1|All files (*.*)|*.*";
                dlg.FileName = "SmbMapping.ps1";
                dlg.DefaultExt = "ps1";
                dlg.OverwritePrompt = true;
                dlg.Title = "Export New-SmbMapping";
                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                    File.WriteAllText(dlg.FileName, script, utf8Bom);

                    string removal = WindowsSetupScriptExporter.BuildRemoval(_session.Config, selected);
                    if (!string.IsNullOrEmpty(removal))
                    {
                        string dir = Path.GetDirectoryName(dlg.FileName);
                        string baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                        string ext = Path.GetExtension(dlg.FileName);
                        string removePath = Path.Combine(dir ?? string.Empty, baseName + "-Remove" + ext);
                        File.WriteAllText(removePath, removal, utf8Bom);
                        _sharesStatusLabel.Text = "Saved: " + Path.GetFileName(dlg.FileName) + ", " + Path.GetFileName(removePath);
                    }
                    else
                    {
                        _sharesStatusLabel.Text = "Mapping script saved: " + Path.GetFileName(dlg.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ShareEditorSaveRequested(object sender, EventArgs e)
        {
            if (!TrySaveEditorChanges())
            {
                return;
            }

            ShowEditor(false);
            _sharesStatusLabel.Text = "Share saved.";
        }

        private void ShareEditorSaveAndApplyRequested(object sender, EventArgs e)
        {
            if (!TrySaveEditorChanges())
            {
                return;
            }

            SaveUiToState();
            PersistConfig();
            if (_serverHost.IsRunning)
            {
                SafeRestartServer(successMessage: "Saved and restarted SMB server.");
            }
            else
            {
                _globalStatusLabel.Text = "Saved.";
            }

            ShowEditor(false);
        }

        private void ShareEditorDeleteRequested(object sender, EventArgs e)
        {
            if (_editingShareId == Guid.Empty)
            {
                _shareEditorView.SetStatus("Cannot delete unsaved share.");
                return;
            }

            _session.Config.Shares.RemoveAll(item => item.Id == _editingShareId);
            PersistConfig();
            BindSharesGrid();
            ShowEditor(false);
            _sharesStatusLabel.Text = "Share deleted.";
        }

        private void ShareEditorCancelRequested(object sender, EventArgs e)
        {
            ShowEditor(false);
            _sharesStatusLabel.Text = "Edit canceled.";
        }

        private void ShareEditorBackRequested(object sender, EventArgs e)
        {
            ShowEditor(false);
            _sharesStatusLabel.Text = "Back to share list.";
        }

        private bool TrySaveEditorChanges()
        {
            if (!_shareEditorView.ValidateInput(out string message))
            {
                _shareEditorView.SetStatus(message);
                return false;
            }

            ShareConfig updated = _shareEditorView.BuildShareConfig(_editingShareId);
            if (_session.Config.Shares.Any(item =>
                    item.Id != updated.Id &&
                    string.Equals(item.ShareName, updated.ShareName, StringComparison.OrdinalIgnoreCase)))
            {
                _shareEditorView.SetStatus("Share name already exists.");
                return false;
            }

            ShareConfig conflictingUser = _session.Config.Shares.FirstOrDefault(item =>
                item.Id != updated.Id &&
                string.Equals(item.Username, updated.Username, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Password, updated.Password, StringComparison.Ordinal));
            if (conflictingUser != null)
            {
                _shareEditorView.SetStatus("That username already exists with a different password. Use a unique username or the same password.");
                return false;
            }

            int index = _session.Config.Shares.FindIndex(item => item.Id == updated.Id);
            if (index >= 0)
            {
                _session.Config.Shares[index] = updated;
            }
            else
            {
                _session.Config.Shares.Add(updated);
            }

            PersistConfig();
            BindSharesGrid();
            return true;
        }

        private ShareConfig GetSelectedShare()
        {
            if (_sharesGrid.CurrentRow == null)
            {
                return null;
            }

            object idObject = _sharesGrid.CurrentRow.Cells[0].Value;
            if (idObject == null || !Guid.TryParse(idObject.ToString(), out Guid id))
            {
                return null;
            }

            return _shareById.TryGetValue(id, out ShareConfig selected) ? selected : null;
        }

        private List<ShareConfig> GetSelectedShares()
        {
            var result = new List<ShareConfig>();
            if (_sharesGrid.SelectedRows == null || _sharesGrid.SelectedRows.Count == 0)
            {
                return result;
            }

            foreach (DataGridViewRow row in _sharesGrid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index))
            {
                object idObject = row.Cells[0].Value;
                if (idObject == null || !Guid.TryParse(idObject.ToString(), out Guid id))
                {
                    continue;
                }

                if (_shareById.TryGetValue(id, out ShareConfig share))
                {
                    result.Add(share);
                }
            }

            return result;
        }

        private void UseSelectedShare(Action<ShareConfig> action)
        {
            ShareConfig selected = GetSelectedShare();
            if (selected == null)
            {
                _sharesStatusLabel.Text = "Select a share first.";
                return;
            }

            action(selected);
        }

        private void ShowEditor(bool visible)
        {
            _sharesListPanel.Visible = !visible;
            _sharesEditorPanel.Visible = visible;
        }

        private void SaveAndApplySettingsClick(object sender, EventArgs e)
        {
            SaveUiToState();
            if (!EnsureAutoTrayConsent())
            {
                _requireMasterPasswordTrayCheckBox.Checked = true;
                _session.Config.Server.RequireMasterPasswordWhenStartingToTray = true;
                _globalStatusLabel.Text = "Auto-tray not enabled (consent required).";
                return;
            }

            PersistConfig();
            if (_serverHost.IsRunning)
            {
                SafeRestartServer(successMessage: "Settings saved and server restarted.");
            }
            else
            {
                _globalStatusLabel.Text = "Settings saved.";
            }
        }

        private bool EnsureAutoTrayConsent()
        {
            ServerSettings server = _session.Config.Server;
            bool autoTrayActive = server.StartMinimizedToTray && !server.RequireMasterPasswordWhenStartingToTray;
            if (!autoTrayActive || server.AutoTrayConsented)
            {
                return true;
            }

            DialogResult choice = MessageBox.Show(
                this,
                "Auto-tray will store your master password on disk under DPAPI (Current User scope).\r\n\r\n" +
                "Any program running as your Windows user can decrypt it and recover the master password, " +
                "which would expose every share credential in your vault.\r\n\r\n" +
                "Continue with auto-tray enabled?",
                "swagSMB - Auto-tray credential exposure",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (choice != DialogResult.Yes)
            {
                return false;
            }

            server.AutoTrayConsented = true;
            return true;
        }

        private void StartServerClick(object sender, EventArgs e)
        {
            SaveUiToState();
            PersistConfig();
            SafeStartServer();
        }

        private void StopServerClick(object sender, EventArgs e)
        {
            _serverHost.Stop();
            _globalStatusLabel.Text = "SMB server stopped.";
        }

        private void RestartServerClick(object sender, EventArgs e)
        {
            SaveUiToState();
            PersistConfig();
            bool wasRunning = _serverHost.IsRunning;
            SafeRestartServer(
                successMessage: wasRunning
                    ? "Settings saved and server restarted."
                    : "Settings saved; SMB server running.");
        }

        private void SafeStartServer()
        {
            try
            {
                _serverHost.Start(_session.Config);
                _globalStatusLabel.Text = "SMB server running.";
            }
            catch (Exception ex)
            {
                _globalStatusLabel.Text = "Failed to start server: " + ex.Message;
                AppendServerLogLine($"[{DateTime.Now:HH:mm:ss}] [Server] Failed to start: {ex.Message}");
                AppendBindFailureHints(ex);
            }
        }

        private void SafeRestartServer(string successMessage = null)
        {
            try
            {
                _serverHost.Restart(_session.Config);
                _globalStatusLabel.Text = successMessage ?? "SMB server restarted.";
            }
            catch (Exception ex)
            {
                _globalStatusLabel.Text = "Failed to restart server: " + ex.Message;
                AppendServerLogLine($"[{DateTime.Now:HH:mm:ss}] [Server] Failed to restart: {ex.Message}");
                AppendBindFailureHints(ex);
            }
        }

        private void AppendBindFailureHints(Exception ex)
        {
            SocketException sockEx = ex as SocketException ?? ex.InnerException as SocketException;
            if (sockEx == null)
            {
                return;
            }

            if (sockEx.SocketErrorCode != SocketError.AccessDenied)
            {
                return;
            }

            int configured = _session.Config.Network.Port;
            int effectivePort = configured <= 0 ? NetworkSettings.DefaultPort : configured;
            string detail = effectivePort == 445
                ? "TCP 445 is often held by Windows File Sharing (LanmanServer); stop that service or use another port (default is " + NetworkSettings.DefaultPort + "). "
                : string.Empty;

            AppendServerLogLine(
                $"[{DateTime.Now:HH:mm:ss}] [Server] Hint: Socket access denied — binding port {effectivePort} was blocked. " +
                detail +
                "Confirm the port is allowed in Windows excluded ranges (admin: netsh interface ipv4 show excludedportrange protocol=tcp) and in the firewall.");
        }

        private void AppendServerLogLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendServerLogLine), line);
                return;
            }

            _pendingLogLines.Enqueue(line);
        }

        private void FlushPendingServerLogs(object sender, EventArgs e)
        {
            if (_pendingLogLines.Count == 0)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var appendBuilder = new StringBuilder();
            bool didTrim = false;
            int flushed = 0;
            while (_pendingLogLines.Count > 0)
            {
                string line = _pendingLogLines.Dequeue();
                _serverLogHistory.Enqueue(line);
                if (_serverLogHistory.Count > MaxServerLogLines)
                {
                    _serverLogHistory.Dequeue();
                    didTrim = true;
                }
                if (!didTrim)
                {
                    appendBuilder.AppendLine(line);
                }

                flushed++;
            }

            if (didTrim)
            {
                _serverLogTextBox.Lines = _serverLogHistory.ToArray();
            }
            else if (appendBuilder.Length > 0)
            {
                _serverLogTextBox.AppendText(appendBuilder.ToString());
            }

            if (_serverLogTextBox.TextLength > 0)
            {
                _serverLogTextBox.SelectionStart = _serverLogTextBox.TextLength;
                _serverLogTextBox.ScrollToCaret();
            }

            stopwatch.Stop();
            Debug.WriteLine($"[Perf] Flushed {flushed} log lines in {stopwatch.ElapsedMilliseconds} ms.");
        }

        private void ClearServerLog()
        {
            _pendingLogLines.Clear();
            _serverLogHistory.Clear();
            _serverLogTextBox.Clear();
        }

        private void TestPortClick(object sender, EventArgs e)
        {
            int port = (int)_bindPortNumeric.Value;
            IPAddress testAddress = _listenAllInterfacesCheckBox.Checked
                ? IPAddress.Any
                : (IPAddress.TryParse(_bindIpComboBox.SelectedItem?.ToString(), out IPAddress ip) ? ip : IPAddress.Any);

            bool available = IsPortAvailable(testAddress, port);
            _globalStatusLabel.Text = available
                ? "Port test succeeded: " + testAddress + ":" + port + " is available."
                : "Port test failed: " + testAddress + ":" + port + " is already in use or blocked.";
        }

        private static bool IsPortAvailable(IPAddress ip, int port)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(ip, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }

        private void ChangeMasterPasswordClick(object sender, EventArgs e)
        {
            using (var form = new ChangeMasterPasswordForm(_session.MasterPassword, _store))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    FlushPendingConfigSave();
                    _session.MasterPassword = form.NewMasterPassword;
                    PersistConfig();
                    _globalStatusLabel.Text = "Master password updated.";
                }
            }
        }

        private void ResetMasterPasswordClick(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "This removes the encrypted config file (all shares and settings on disk).\n\n" +
                "You will choose a new master password next. If you cancel that step, the app will try to save your current session back to disk.\n\n" +
                "Continue?",
                "Reset master password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            WipeEncryptedConfigAndReunlock(
                "Encrypted data was cleared. Set a new master password. All shares and settings start from defaults.",
                "Reset canceled; previous settings restored to disk.",
                "Master password reset; server started with default settings.",
                "Master password reset. Configure shares and start the server when ready.");
        }

        private void RestoreToDefaultsClick(object sender, EventArgs e)
        {
            if (!TryConfirmRestoreToDefaultsPhrase())
            {
                return;
            }

            WipeEncryptedConfigAndReunlock(
                "All settings were cleared. Set a master password. Configuration starts from defaults.",
                "Restore to defaults canceled; previous settings restored to disk.",
                "All settings cleared; server started with default settings.",
                "All settings cleared. Configure shares and start the server when ready.",
                restartApplicationWhenDone: true);
        }

        private bool TryConfirmRestoreToDefaultsPhrase()
        {
            using (var form = new Form())
            {
                form.Text = "Restore to defaults";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.ClientSize = new Size(420, 148);
                form.Font = Font;

                var label = new Label
                {
                    AutoSize = false,
                    Text = "This removes the encrypted config (all shares and settings on disk).\n\nType " + ClearAllSettingsConfirmationPhrase + " to confirm:",
                    Location = new Point(12, 12),
                    Size = new Size(396, 56),
                };
                var textBox = new TextBox
                {
                    Location = new Point(12, 72),
                    Size = new Size(396, 23),
                };
                var ok = new Button
                {
                    Text = "OK",
                    Location = new Point(232, 108),
                    Size = new Size(88, 28),
                    DialogResult = DialogResult.OK,
                };
                var cancel = new Button
                {
                    Text = "Cancel",
                    Location = new Point(326, 108),
                    Size = new Size(88, 28),
                    DialogResult = DialogResult.Cancel,
                };
                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                UiTheme.Apply(form, GetSelectedTheme(), null);

                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                if (!string.Equals(textBox.Text, ClearAllSettingsConfirmationPhrase, StringComparison.Ordinal))
                {
                    MessageBox.Show(
                        this,
                        "The confirmation phrase did not match.",
                        "swagSMB",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                return true;
            }
        }

        private void WipeEncryptedConfigAndReunlock(
            string unlockIntro,
            string statusWhenUnlockCanceled,
            string statusAfterAutoStart,
            string statusAfterManual,
            bool restartApplicationWhenDone = false)
        {
            _serverHost.Stop();
            _store.DeleteConfig();

            using (var unlockForm = new UnlockForm(_store, unlockIntro))
            {
                if (unlockForm.ShowDialog(this) != DialogResult.OK || unlockForm.SessionContext == null)
                {
                    try
                    {
                        AppConfig snapshot = CloneConfig(_session.Config);
                        _store.Save(_session.MasterPassword, snapshot);
                        SyncTraySidecar(snapshot, _session.MasterPassword);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            this,
                            "Could not restore the config file: " + ex.Message,
                            "swagSMB",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }

                    _globalStatusLabel.Text = statusWhenUnlockCanceled;
                    return;
                }

                _session.MasterPassword = unlockForm.SessionContext.MasterPassword;
                _session.Config = unlockForm.SessionContext.Config;
            }

            if (restartApplicationWhenDone)
            {
                LaunchNewInstanceAndExit();
                return;
            }

            _editingShareId = Guid.Empty;
            ShowEditor(false);
            _shareEditorView.LoadShare(null);
            PopulateAddressList();
            LoadStateToUi();
            BindSharesGrid();
            _sharesStatusLabel.Text = "Configuration reset; add shares as needed.";

            if (_session.Config.Server.AutoStartAfterUnlock)
            {
                SafeStartServer();
                _globalStatusLabel.Text = statusAfterAutoStart;
            }
            else
            {
                _globalStatusLabel.Text = statusAfterManual;
            }
        }

        private void LaunchNewInstanceAndExit()
        {
            _serverHost.ServerActivity -= AppendServerLogLine;
            _serverHost.Stop();
            _activityTimer.Stop();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    WorkingDirectory = Application.StartupPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Could not start a new swagSMB instance: " + ex.Message + "\n\nThe app will stay open with your new settings.",
                    "swagSMB",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _activityTimer.Start();
                _serverHost.ServerActivity += AppendServerLogLine;
                _editingShareId = Guid.Empty;
                ShowEditor(false);
                _shareEditorView.LoadShare(null);
                PopulateAddressList();
                LoadStateToUi();
                BindSharesGrid();
                _sharesStatusLabel.Text = "Configuration reset; add shares as needed.";
                if (_session.Config.Server.AutoStartAfterUnlock)
                {
                    SafeStartServer();
                }

                return;
            }

            _exitRequested = true;
            Application.Exit();
        }

        private void PersistConfig()
        {
            if (string.IsNullOrEmpty(_session.MasterPassword))
            {
                return;
            }

            _saveQueued = true;
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void PersistDebounceTimerTick(object sender, EventArgs e)
        {
            _saveDebounceTimer.Stop();
            StartPersistWorkerIfNeeded();
        }

        private void StartPersistWorkerIfNeeded()
        {
            if (_saveWorkerActive || !_saveQueued || string.IsNullOrEmpty(_session.MasterPassword))
            {
                return;
            }

            _saveQueued = false;
            _saveWorkerActive = true;
            AppConfig snapshot = CloneConfig(_session.Config);
            string password = _session.MasterPassword;
            _saveTask = Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                Exception error = null;
                try
                {
                    _store.Save(password, snapshot);
                    SyncTraySidecar(snapshot, password);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    stopwatch.Stop();
                }

                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    _saveWorkerActive = false;
                    if (error != null)
                    {
                        _globalStatusLabel.Text = "Save failed: " + error.Message;
                    }

                    Debug.WriteLine($"[Perf] PersistConfig worker took {stopwatch.ElapsedMilliseconds} ms.");
                    StartPersistWorkerIfNeeded();
                }));
            });
        }

        private static AppConfig CloneConfig(AppConfig source)
        {
            return new AppConfig
            {
                Network = new NetworkSettings
                {
                    ListenOnAllInterfaces = source.Network.ListenOnAllInterfaces,
                    BindIPAddress = source.Network.BindIPAddress,
                    Port = source.Network.Port
                },
                Security = new SecuritySettings
                {
                    RequireSigning = source.Security.RequireSigning,
                    DefaultRequireEncryption = source.Security.DefaultRequireEncryption,
                    LockProtocolToSmb21AndSmb30 = source.Security.LockProtocolToSmb21AndSmb30,
                    AutoLockMinutes = source.Security.AutoLockMinutes
                },
                Server = new ServerSettings
                {
                    AutoStartAfterUnlock = source.Server.AutoStartAfterUnlock,
                    StartWithWindows = source.Server.StartWithWindows,
                    StartMinimizedToTray = source.Server.StartMinimizedToTray,
                    CloseToTray = source.Server.CloseToTray,
                    RequireMasterPasswordWhenStartingToTray = source.Server.RequireMasterPasswordWhenStartingToTray
                },
                Shares = source.Shares.Select(share => new ShareConfig
                {
                    Id = share.Id,
                    ShareName = share.ShareName,
                    LocalPath = share.LocalPath,
                    Username = share.Username,
                    Password = share.Password,
                    ProtocolMode = share.ProtocolMode,
                    RequireEncryption = share.RequireEncryption,
                    Enabled = share.Enabled,
                    MapDriveLetter = share.MapDriveLetter
                }).ToList()
            };
        }

        private void SyncTraySidecar(AppConfig snapshot, string password)
        {
            ServerSettings server = snapshot.Server;
            bool autoTrayEligible = server != null
                && server.StartMinimizedToTray
                && !server.RequireMasterPasswordWhenStartingToTray
                && server.AutoTrayConsented;

            if (autoTrayEligible && !string.IsNullOrEmpty(password))
            {
                _store.SaveProtectedMasterPassword(password);
            }
            else
            {
                _store.DeleteProtectedMasterPassword();
            }
        }

        private void RegisterActivityEvents(Control root)
        {
            root.MouseMove += AnyActivity;
            root.KeyDown += AnyActivity;
            root.KeyPress += AnyActivity;
            foreach (Control child in root.Controls)
            {
                RegisterActivityEvents(child);
            }
        }

        private void AnyActivity(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastActivitySampleUtc).TotalMilliseconds < 1000)
            {
                return;
            }

            _lastActivitySampleUtc = now;
            _lastActivityUtc = now;
        }

        private void ResetActivityTimer()
        {
            DateTime now = DateTime.UtcNow;
            _lastActivityUtc = now;
            _lastActivitySampleUtc = now;
        }

        private void ActivityTimerTick(object sender, EventArgs e)
        {
            int lockMinutes = Math.Max(1, (int)_autoLockMinutesNumeric.Value);
            TimeSpan idle = DateTime.UtcNow - _lastActivityUtc;
            if (idle.TotalMinutes >= lockMinutes)
            {
                _serverHost.Stop();
                _activityTimer.Stop();
                _session.MasterSecret.Clear();
                _globalStatusLabel.Text = "Auto-lock timeout reached. App will close.";
                _exitRequested = true;
                Close();
            }
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_session.Config.Server.CloseToTray
                && !_exitRequested
                && e.CloseReason != CloseReason.WindowsShutDown
                && e.CloseReason != CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            FlushPendingConfigSave();
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Dispose();
            _logFlushTimer.Stop();
            _logFlushTimer.Dispose();
            _serverHost.ServerActivity -= AppendServerLogLine;
            _serverHost.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _actionToolTip.Dispose();
            _session.MasterSecret.Clear();
        }

        private void FlushPendingConfigSave()
        {
            _saveDebounceTimer.Stop();
            if (_saveQueued && !_saveWorkerActive && !string.IsNullOrEmpty(_session.MasterPassword))
            {
                try
                {
                    AppConfig snapshot = CloneConfig(_session.Config);
                    _store.Save(_session.MasterPassword, snapshot);
                    SyncTraySidecar(snapshot, _session.MasterPassword);
                    _saveQueued = false;
                }
                catch (Exception ex)
                {
                    _globalStatusLabel.Text = "Save failed: " + ex.Message;
                }
            }

            if (_saveWorkerActive)
            {
                try
                {
                    _saveTask.Wait(4000);
                }
                catch
                {
                }
            }
        }

        private void SharesGridSelectionChanged(object sender, EventArgs e)
        {
            ShareConfig selected = GetSelectedShare();
            UpdateShareEnableButtons(selected);
            if (selected == null)
            {
                return;
            }

            _sharesStatusLabel.Text = string.Format(
                "Selected: {0} | User: {1} | Protocol: {2} | Encryption: {3} | Path: {4}",
                selected.ShareName,
                selected.Username,
                selected.ProtocolMode,
                selected.RequireEncryption ? "Required" : "Optional",
                selected.LocalPath);
        }

        private sealed class ShareGridRow
        {
            public Guid Id { get; set; }
            public string ShareName { get; set; }
            public string LocalPath { get; set; }
            public string Username { get; set; }
            public string ProtocolMode { get; set; }
            public string Enabled { get; set; }
            public string RequireEncryption { get; set; }
        }
    }
}
