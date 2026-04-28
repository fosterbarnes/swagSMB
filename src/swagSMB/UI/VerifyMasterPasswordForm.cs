using System;
using System.Drawing;
using System.Windows.Forms;
using swagSMB.Security;
using swagSMB.Storage;

namespace swagSMB.UI
{
    public sealed class VerifyMasterPasswordForm : Form
    {
        private readonly AppConfigStore _store;
        private readonly TextBox _passwordTextBox;
        private readonly Label _statusLabel;

        public string VerifiedPassword { get; private set; } = string.Empty;

        public VerifyMasterPasswordForm(AppConfigStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            Text = "swagSMB";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 150);

            _statusLabel = new Label
            {
                Left = 16,
                Top = 16,
                Width = 388,
                Height = 36,
                Text = "Enter the master password to open settings."
            };

            var passwordLabel = new Label
            {
                Text = "Master Password",
                Left = 16,
                Top = 58,
                Width = 110
            };

            _passwordTextBox = new TextBox
            {
                Left = 128,
                Top = 54,
                Width = 240,
                UseSystemPasswordChar = true
            };

            Control reveal = PasswordRevealButton.Create(_passwordTextBox);
            reveal.Left = 372;
            reveal.Top = 54;

            var okButton = new Button
            {
                Text = "OK",
                Width = 88,
                Height = 28,
                Left = 316,
                Top = 108
            };
            okButton.Click += OkClick;

            var cancelButton = new Button
            {
                Text = "Cancel",
                Width = 88,
                Height = 28,
                Left = 216,
                Top = 108
            };
            cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(_statusLabel);
            Controls.Add(passwordLabel);
            Controls.Add(_passwordTextBox);
            Controls.Add(reveal);
            Controls.Add(okButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;

            Shown += (_, __) =>
            {
                BeginInvoke(new Action(FocusPassword));
            };

            UiTheme.Apply(this, _store.LoadUiPreferences().Theme, null);
        }

        private void FocusPassword()
        {
            Activate();
            if (_passwordTextBox.CanFocus)
            {
                _passwordTextBox.Focus();
            }
        }

        private void OkClick(object sender, EventArgs e)
        {
            string pw = _passwordTextBox.Text;
            _passwordTextBox.Text = string.Empty;
            if (pw.Length == 0)
            {
                _statusLabel.Text = "Master password is required.";
                return;
            }

            if (UnlockRetryGuard.LimitReached)
            {
                _statusLabel.Text = "Too many failed attempts.";
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (!_store.TryVerifyMasterPassword(pw))
            {
                UnlockRetryGuard.RegisterFailure();
                if (UnlockRetryGuard.LimitReached)
                {
                    _statusLabel.Text = "Too many failed attempts.";
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }
                _statusLabel.Text = "Incorrect master password. " + UnlockRetryGuard.FailuresRemaining + " attempt(s) remaining.";
                return;
            }

            UnlockRetryGuard.Reset();
            VerifiedPassword = pw;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
