using System;
using System.Drawing;
using System.Windows.Forms;
using swagSMB.Models;
using swagSMB.Security;
using swagSMB.Storage;

namespace swagSMB.UI
{
    public sealed class UnlockForm : Form
    {
        private readonly AppConfigStore _store;
        private readonly TextBox _masterPasswordTextBox;
        private readonly TextBox _confirmPasswordTextBox;
        private readonly Label _descriptionLabel;
        private readonly Label _strengthLabel;
        private readonly Button _unlockButton;
        private readonly Button _cancelButton;

        public SessionContext SessionContext { get; private set; }

        public UnlockForm(AppConfigStore store, string setupIntroText = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));

            Text = "swagSMB Unlock";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 200);

            _descriptionLabel = new Label
            {
                Left = 20,
                Top = 20,
                Width = 420,
                Height = 40
            };

            var passwordLabel = new Label
            {
                Text = "Master Password",
                Left = 20,
                Top = 66,
                Width = 120
            };

            _masterPasswordTextBox = new TextBox
            {
                Left = 145,
                Top = 62,
                Width = 272,
                UseSystemPasswordChar = true
            };

            Control masterReveal = PasswordRevealButton.Create(_masterPasswordTextBox);
            masterReveal.Left = 421;
            masterReveal.Top = 65;

            var confirmLabel = new Label
            {
                Text = "Confirm Password",
                Left = 20,
                Top = 98,
                Width = 120
            };

            _confirmPasswordTextBox = new TextBox
            {
                Left = 145,
                Top = 94,
                Width = 272,
                UseSystemPasswordChar = true
            };

            Control confirmReveal = PasswordRevealButton.Create(_confirmPasswordTextBox);
            confirmReveal.Left = 421;
            confirmReveal.Top = 97;

            _unlockButton = new Button
            {
                Text = "Unlock",
                Width = 94,
                Height = 30,
                Left = 346,
                Top = 150
            };
            _unlockButton.Click += UnlockButtonClick;

            _cancelButton = new Button
            {
                Text = "Exit",
                Width = 94,
                Height = 30,
                Left = 246,
                Top = 150
            };
            _cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            _strengthLabel = new Label
            {
                Left = 145,
                Top = 124,
                Width = 272,
                Height = 18,
                Text = string.Empty,
                AutoSize = false
            };

            Controls.Add(_descriptionLabel);
            Controls.Add(passwordLabel);
            Controls.Add(_masterPasswordTextBox);
            Controls.Add(masterReveal);
            Controls.Add(confirmLabel);
            Controls.Add(_confirmPasswordTextBox);
            Controls.Add(confirmReveal);
            Controls.Add(_strengthLabel);
            Controls.Add(_unlockButton);
            Controls.Add(_cancelButton);

            bool firstRun = !_store.ConfigExists();
            _confirmPasswordTextBox.Visible = firstRun;
            confirmLabel.Visible = firstRun;
            confirmReveal.Visible = firstRun;
            _strengthLabel.Visible = firstRun;
            if (firstRun)
            {
                _masterPasswordTextBox.TextChanged += (_, __) =>
                {
                    var s = PasswordPolicy.Estimate(_masterPasswordTextBox.Text);
                    _strengthLabel.Text = "Strength: " + PasswordPolicy.Describe(s);
                };
            }
            string defaultIntro = "First run detected. Set a master password to encrypt your local SMB credentials.";
            _descriptionLabel.Text = firstRun
                ? (setupIntroText ?? defaultIntro)
                : "Enter the master password to unlock local encrypted SMB credentials.";

            AcceptButton = _unlockButton;
            CancelButton = _cancelButton;

            UiTheme.Apply(this, _store.LoadUiPreferences().Theme, null);
        }

        private void UnlockButtonClick(object sender, EventArgs e)
        {
            string masterPassword = _masterPasswordTextBox.Text;
            string confirmPassword = _confirmPasswordTextBox.Text;
            _masterPasswordTextBox.Text = string.Empty;
            _confirmPasswordTextBox.Text = string.Empty;
            if (masterPassword.Length == 0)
            {
                _descriptionLabel.Text = "Master password is required.";
                return;
            }

            if (UnlockRetryGuard.LimitReached)
            {
                _descriptionLabel.Text = "Too many failed attempts. The app will close.";
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            try
            {
                bool firstRun = !_store.ConfigExists();
                if (firstRun && masterPassword.Length < PasswordPolicy.MasterMinimumLength)
                {
                    _descriptionLabel.Text = "Master password must be at least " + PasswordPolicy.MasterMinimumLength + " characters.";
                    return;
                }

                if (firstRun)
                {
                    if (!string.Equals(masterPassword, confirmPassword, StringComparison.Ordinal))
                    {
                        _descriptionLabel.Text = "Passwords do not match.";
                        return;
                    }

                    var config = new AppConfig();
                    _store.Save(masterPassword, config);
                    SessionContext = new SessionContext
                    {
                        MasterPassword = masterPassword,
                        Config = config
                    };
                }
                else
                {
                    AppConfig config = _store.Load(masterPassword);
                    SessionContext = new SessionContext
                    {
                        MasterPassword = masterPassword,
                        Config = config
                    };
                }

                UnlockRetryGuard.Reset();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Unlock] " + ex);
                UnlockRetryGuard.RegisterFailure();
                if (UnlockRetryGuard.LimitReached)
                {
                    _descriptionLabel.Text = "Too many failed attempts. The app will close.";
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }

                _descriptionLabel.Text = "Unlock failed. " + UnlockRetryGuard.FailuresRemaining + " attempt(s) remaining.";
            }
        }
    }
}
