using System;
using System.Drawing;
using System.Windows.Forms;
using swagSMB.Security;
using swagSMB.Storage;

namespace swagSMB.UI
{
    public sealed class VerifyMasterPasswordForm : Form
    {
        private const string RetryScope = UnlockRetryGuard.ScopeVerify;

        private readonly AppConfigStore _store;
        private readonly TextBox _passwordTextBox;
        private readonly Label _statusLabel;
        private readonly Button _okButton;
        private readonly Timer _cooldownTimer;
        private string _statusBeforeCooldown;

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

            _okButton = new Button
            {
                Text = "OK",
                Width = 88,
                Height = 28,
                Left = 316,
                Top = 108
            };
            _okButton.Click += OkClick;

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
            Controls.Add(_okButton);
            Controls.Add(cancelButton);

            AcceptButton = _okButton;
            CancelButton = cancelButton;

            _cooldownTimer = new Timer { Interval = 200 };
            _cooldownTimer.Tick += CooldownTimerTick;
            FormClosed += (_, __) =>
            {
                _cooldownTimer.Stop();
                _cooldownTimer.Dispose();
            };

            Shown += (_, __) =>
            {
                BeginInvoke(new Action(FocusPassword));
            };

            UiTheme.Apply(this, _store.LoadUiPreferences().Theme, null);
        }

        private void StartCooldown()
        {
            if (UnlockRetryGuard.RemainingCooldown(RetryScope) <= TimeSpan.Zero)
            {
                return;
            }

            _statusBeforeCooldown = _statusLabel.Text;
            _okButton.Enabled = false;
            _cooldownTimer.Start();
            UpdateCooldownLabel();
        }

        private void UpdateCooldownLabel()
        {
            TimeSpan remaining = UnlockRetryGuard.RemainingCooldown(RetryScope);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
            string baseText = string.IsNullOrEmpty(_statusBeforeCooldown)
                ? "Incorrect master password."
                : _statusBeforeCooldown;
            _statusLabel.Text = baseText + " Try again in " + seconds + "s.";
        }

        private void CooldownTimerTick(object sender, EventArgs e)
        {
            if (UnlockRetryGuard.RemainingCooldown(RetryScope) > TimeSpan.Zero)
            {
                UpdateCooldownLabel();
                return;
            }

            _cooldownTimer.Stop();
            _okButton.Enabled = true;
            if (!string.IsNullOrEmpty(_statusBeforeCooldown))
            {
                _statusLabel.Text = _statusBeforeCooldown;
            }
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

            if (UnlockRetryGuard.IsLimitReached(RetryScope))
            {
                _statusLabel.Text = "Too many failed attempts.";
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            if (!_store.TryVerifyMasterPassword(pw))
            {
                UnlockRetryGuard.RegisterFailure(RetryScope);
                if (UnlockRetryGuard.IsLimitReached(RetryScope))
                {
                    _statusLabel.Text = "Too many failed attempts.";
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }
                _statusLabel.Text = "Incorrect master password. " + UnlockRetryGuard.FailuresRemaining(RetryScope) + " attempt(s) remaining.";
                StartCooldown();
                return;
            }

            UnlockRetryGuard.Reset(RetryScope);
            VerifiedPassword = pw;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
