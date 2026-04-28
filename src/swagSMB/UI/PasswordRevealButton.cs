using System.Drawing;
using System.Windows.Forms;

namespace swagSMB.UI
{
    internal static class PasswordRevealButton
    {
        private const int IconSize = 20;

        public static Control Create(TextBox textBox)
        {
            var icon = new Label
            {
                Text = "\uE7B3",
                Font = new Font("Segoe MDL2 Assets", 8.25f),
                Size = new Size(IconSize, IconSize),
                MinimumSize = new Size(IconSize, IconSize),
                MaximumSize = new Size(IconSize, IconSize),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Margin = new Padding(3, 5, 0, 0),
                AutoSize = false,
                TabStop = false,
                AccessibleName = "Show or hide password"
            };
            icon.Click += (_, __) => { textBox.UseSystemPasswordChar = !textBox.UseSystemPasswordChar; };
            return icon;
        }
    }
}
