using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Ookii.Dialogs.WinForms;
using swagSMB.Models;
using swagSMB.Security;

namespace swagSMB.UI
{
    public sealed class ShareEditorView : UserControl
    {
        private readonly TextBox _shareNameTextBox;
        private readonly TextBox _localPathTextBox;
        private readonly TextBox _usernameTextBox;
        private readonly TextBox _passwordTextBox;
        private readonly ComboBox _mapDriveComboBox;
        private readonly ComboBox _protocolComboBox;
        private readonly CheckBox _encryptionCheckBox;
        private readonly CheckBox _enabledCheckBox;
        private readonly Label _statusLabel;

        public event EventHandler SaveRequested;
        public event EventHandler SaveAndApplyRequested;
        public event EventHandler DeleteRequested;
        public event EventHandler CancelRequested;
        public event EventHandler BackRequested;

        public ShareEditorView()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9f);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 9,
                Padding = new Padding(10),
                AutoSize = true
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

            _shareNameTextBox = AddTextField(table, 0, "Share Name");
            _localPathTextBox = AddTextField(table, 1, "Local Path");
            var browseButton = new Button
            {
                Text = "Browse",
                Dock = DockStyle.Fill
            };
            browseButton.Click += BrowseClick;
            table.Controls.Add(browseButton, 2, 1);

            _usernameTextBox = AddTextField(table, 2, "Username");
            _passwordTextBox = AddTextField(table, 3, "Password");
            _passwordTextBox.UseSystemPasswordChar = true;
            table.Controls.Add(PasswordRevealButton.Create(_passwordTextBox), 2, 3);
            _passwordTextBox.TextChanged += (_, __) =>
            {
                if (_statusLabel == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(_passwordTextBox.Text))
                {
                    _statusLabel.Text = "Edit share settings.";
                    return;
                }
                var s = PasswordPolicy.Estimate(_passwordTextBox.Text);
                _statusLabel.Text = "Password strength: " + PasswordPolicy.Describe(s);
            };

            table.Controls.Add(new Label { Text = "Map drive letter", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 4);
            _mapDriveComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _mapDriveComboBox.Items.Add("Automatic — first available");
            for (char c = 'D'; c <= 'Z'; c++)
            {
                _mapDriveComboBox.Items.Add($"{c}:");
            }

            _mapDriveComboBox.SelectedIndex = 0;
            table.Controls.Add(_mapDriveComboBox, 1, 4);

            table.Controls.Add(new Label { Text = "Protocol", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 5);
            _protocolComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _protocolComboBox.Items.AddRange(new object[] { "SMB2.1", "SMB3.0" });
            _protocolComboBox.SelectedIndex = 1;
            table.Controls.Add(_protocolComboBox, 1, 5);

            _encryptionCheckBox = new CheckBox
            {
                Text = "Require Encryption",
                Dock = DockStyle.Fill,
                Checked = true
            };
            table.Controls.Add(_encryptionCheckBox, 1, 6);

            _enabledCheckBox = new CheckBox
            {
                Text = "Enabled",
                Dock = DockStyle.Fill,
                Checked = true
            };
            table.Controls.Add(_enabledCheckBox, 1, 7);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                AutoSize = true
            };

            Button saveAndApplyButton = NewButton("Save + Apply");
            saveAndApplyButton.Click += delegate { SaveAndApplyRequested?.Invoke(this, EventArgs.Empty); };
            buttonPanel.Controls.Add(saveAndApplyButton);

            Button saveButton = NewButton("Save");
            saveButton.Click += delegate { SaveRequested?.Invoke(this, EventArgs.Empty); };
            buttonPanel.Controls.Add(saveButton);

            Button deleteButton = NewButton("Delete");
            deleteButton.Click += delegate { DeleteRequested?.Invoke(this, EventArgs.Empty); };
            buttonPanel.Controls.Add(deleteButton);

            Button cancelButton = NewButton("Cancel");
            cancelButton.Click += delegate { CancelRequested?.Invoke(this, EventArgs.Empty); };
            buttonPanel.Controls.Add(cancelButton);

            Button backButton = NewButton("Back To List");
            backButton.Click += delegate { BackRequested?.Invoke(this, EventArgs.Empty); };
            buttonPanel.Controls.Add(backButton);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                Text = "Share editor ready."
            };

            Controls.Add(_statusLabel);
            Controls.Add(buttonPanel);
            Controls.Add(table);
        }

        public ShareConfig BuildShareConfig(Guid existingId)
        {
            return new ShareConfig
            {
                Id = existingId == Guid.Empty ? Guid.NewGuid() : existingId,
                ShareName = _shareNameTextBox.Text.Trim(),
                LocalPath = _localPathTextBox.Text.Trim(),
                Username = _usernameTextBox.Text.Trim(),
                Password = _passwordTextBox.Text,
                ProtocolMode = _protocolComboBox.SelectedItem?.ToString() ?? "SMB3.0",
                RequireEncryption = _encryptionCheckBox.Checked,
                Enabled = _enabledCheckBox.Checked,
                MapDriveLetter = GetMapDriveLetterFromCombo()
            };
        }

        public void LoadShare(ShareConfig share)
        {
            _shareNameTextBox.Text = share?.ShareName ?? string.Empty;
            _localPathTextBox.Text = share?.LocalPath ?? string.Empty;
            _usernameTextBox.Text = share?.Username ?? string.Empty;
            _passwordTextBox.Text = share?.Password ?? string.Empty;
            _protocolComboBox.SelectedItem = share?.ProtocolMode == "SMB2.1" ? "SMB2.1" : "SMB3.0";
            _encryptionCheckBox.Checked = share?.RequireEncryption ?? true;
            _enabledCheckBox.Checked = share?.Enabled ?? true;
            ApplyMapDriveCombo(share?.MapDriveLetter);
            _statusLabel.Text = "Edit share settings.";
        }

        public bool ValidateInput(out string message)
        {
            if (string.IsNullOrWhiteSpace(_shareNameTextBox.Text))
            {
                message = "Share name is required.";
                return false;
            }

            if (!Models.ShareValidator.IsShareNameValid(_shareNameTextBox.Text.Trim(), out string nameReason))
            {
                message = nameReason ?? "Share name is invalid.";
                return false;
            }

            if (!Models.ShareValidator.IsLocalPathSafe(_localPathTextBox.Text, out string pathReason))
            {
                message = pathReason ?? "Local path is not allowed.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_usernameTextBox.Text))
            {
                message = "Username is required.";
                return false;
            }

            if (_passwordTextBox.Text.Length < PasswordPolicy.ShareMinimumLength)
            {
                message = "Password must be at least " + PasswordPolicy.ShareMinimumLength + " characters.";
                return false;
            }

            message = "Input is valid.";
            return true;
        }

        public void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private static string NormalizeMapDriveLetter(string value)
        {
            string t = value?.Trim() ?? string.Empty;
            if (t.Length >= 2 && t.EndsWith(":", StringComparison.Ordinal))
            {
                t = t.Substring(0, 1);
            }

            if (t.Length != 1)
            {
                return string.Empty;
            }

            char c = char.ToUpperInvariant(t[0]);
            return c >= 'D' && c <= 'Z' ? c + ":" : string.Empty;
        }

        private string GetMapDriveLetterFromCombo()
        {
            return _mapDriveComboBox.SelectedIndex <= 0
                ? string.Empty
                : NormalizeMapDriveLetter(_mapDriveComboBox.SelectedItem?.ToString());
        }

        private void ApplyMapDriveCombo(string configured)
        {
            string normalized = NormalizeMapDriveLetter(configured ?? string.Empty);
            if (string.IsNullOrEmpty(normalized))
            {
                _mapDriveComboBox.SelectedIndex = 0;
                return;
            }

            for (int i = 1; i < _mapDriveComboBox.Items.Count; i++)
            {
                if (string.Equals(
                        _mapDriveComboBox.Items[i]?.ToString(),
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _mapDriveComboBox.SelectedIndex = i;
                    return;
                }
            }

            _mapDriveComboBox.SelectedIndex = 0;
        }

        private static TextBox AddTextField(TableLayoutPanel table, int row, string labelText)
        {
            table.Controls.Add(new Label
            {
                Text = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill
            };
            table.Controls.Add(textBox, 1, row);
            return textBox;
        }

        private static Button NewButton(string text)
        {
            return new Button
            {
                Text = text,
                Width = 104,
                Height = 30,
                Margin = new Padding(4)
            };
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (var dialog = new VistaFolderBrowserDialog())
            {
                dialog.Description = "Select a folder to share.";
                dialog.UseDescriptionForTitle = true;
                string initial = _localPathTextBox.Text.Trim();
                if (Directory.Exists(initial))
                {
                    dialog.SelectedPath = initial;
                }

                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    _localPathTextBox.Text = dialog.SelectedPath;
                }
            }
        }
    }
}
