using System;
using System.Drawing;
using System.Windows.Forms;
using swagSMB.Security;
using swagSMB.Storage;

namespace swagSMB.UI
{
    public sealed class ChangeMasterPasswordForm : Form
    {
        private readonly string _currentPassword;
        private readonly TextBox _currentPasswordTextBox;
        private readonly TextBox _newPasswordTextBox;
        private readonly TextBox _confirmNewPasswordTextBox;
        private readonly Label _statusLabel;

        public string NewMasterPassword { get; private set; } = string.Empty;

        public ChangeMasterPasswordForm(string currentPassword, AppConfigStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            _currentPassword = currentPassword ?? string.Empty;

            Text = "Change Master Password";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 268);
            MinimumSize = new Size(420, 260);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = 5
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            table.Controls.Add(new Label { Text = "Current Password", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            _currentPasswordTextBox = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            table.Controls.Add(WrapPasswordRow(_currentPasswordTextBox), 1, 0);

            table.Controls.Add(new Label { Text = "New Password", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            _newPasswordTextBox = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            table.Controls.Add(WrapPasswordRow(_newPasswordTextBox), 1, 1);

            table.Controls.Add(new Label { Text = "Confirm Password", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 2);
            _confirmNewPasswordTextBox = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            table.Controls.Add(WrapPasswordRow(_confirmNewPasswordTextBox), 1, 2);

            _statusLabel = new Label { Text = "Enter new master password.", Dock = DockStyle.Fill };
            table.Controls.Add(_statusLabel, 0, 3);
            table.SetColumnSpan(_statusLabel, 2);
            _newPasswordTextBox.TextChanged += (_, __) =>
            {
                var s = PasswordPolicy.Estimate(_newPasswordTextBox.Text);
                _statusLabel.Text = "Strength: " + PasswordPolicy.Describe(s);
            };

            var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var saveButton = new Button { Text = "Save", Width = 88, Height = 28 };
            saveButton.Click += SaveClick;
            var cancelButton = new Button { Text = "Cancel", Width = 88, Height = 28 };
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            buttonPanel.Controls.Add(saveButton);
            buttonPanel.Controls.Add(cancelButton);
            table.Controls.Add(buttonPanel, 0, 4);
            table.SetColumnSpan(buttonPanel, 2);

            Controls.Add(table);
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            UiTheme.Apply(this, store.LoadUiPreferences().Theme, null);
        }

        private void SaveClick(object sender, EventArgs e)
        {
            string current = _currentPasswordTextBox.Text;
            string fresh = _newPasswordTextBox.Text;
            string confirm = _confirmNewPasswordTextBox.Text;
            _currentPasswordTextBox.Text = string.Empty;
            _newPasswordTextBox.Text = string.Empty;
            _confirmNewPasswordTextBox.Text = string.Empty;

            if (!string.Equals(current, _currentPassword, StringComparison.Ordinal))
            {
                _statusLabel.Text = "Current password is incorrect.";
                return;
            }

            if (fresh.Length < PasswordPolicy.MasterMinimumLength)
            {
                _statusLabel.Text = "New password must be at least " + PasswordPolicy.MasterMinimumLength + " characters.";
                return;
            }

            if (!string.Equals(fresh, confirm, StringComparison.Ordinal))
            {
                _statusLabel.Text = "New passwords do not match.";
                return;
            }

            NewMasterPassword = fresh;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static TableLayoutPanel WrapPasswordRow(TextBox textBox)
        {
            var wrap = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            wrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            wrap.Controls.Add(textBox, 0, 0);
            wrap.Controls.Add(PasswordRevealButton.Create(textBox), 1, 0);
            return wrap;
        }
    }
}
