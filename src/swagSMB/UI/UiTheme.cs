using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using swagSMB.Models;

namespace swagSMB.UI
{
    public static class UiTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);

        private struct ThemeColors
        {
            public Color Form, Surface, Field, Text, Muted, LogBack, LogFore, Link;
            public Color DgvHeader, DgvLine, DgvSelect, DgvSelectFore, MenuBack, ButtonBorder;
        }

        // Spec: app dark; VS-style (existing)
        private static readonly ThemeColors VSDark = new ThemeColors
        {
            Form = Color.FromArgb(30, 30, 30),
            Surface = Color.FromArgb(45, 45, 48),
            Field = Color.FromArgb(60, 60, 60),
            Text = Color.FromArgb(220, 220, 220),
            Muted = Color.FromArgb(150, 150, 150),
            LogBack = Color.FromArgb(20, 20, 20),
            LogFore = Color.White,
            Link = Color.FromArgb(100, 180, 255),
            DgvHeader = Color.FromArgb(50, 50, 50),
            DgvLine = Color.FromArgb(60, 60, 60),
            DgvSelect = Color.FromArgb(0, 99, 177),
            DgvSelectFore = Color.White,
            MenuBack = Color.FromArgb(40, 40, 40),
            ButtonBorder = Color.FromArgb(100, 100, 100)
        };

        // Dracula Classic + UI section (https://draculatheme.com/spec)
        private static readonly ThemeColors Dracula = new ThemeColors
        {
            Form = Color.FromArgb(40, 42, 54),
            Surface = Color.FromArgb(52, 55, 70),
            Field = Color.FromArgb(66, 68, 80),
            Text = Color.FromArgb(248, 248, 242),
            Muted = Color.FromArgb(98, 114, 164),
            LogBack = Color.FromArgb(33, 34, 44),
            LogFore = Color.FromArgb(241, 250, 140),
            Link = Color.FromArgb(139, 233, 253),
            DgvHeader = Color.FromArgb(68, 71, 90),
            DgvLine = Color.FromArgb(68, 71, 90),
            DgvSelect = Color.FromArgb(68, 71, 90),
            DgvSelectFore = Color.FromArgb(248, 248, 242),
            MenuBack = Color.FromArgb(25, 26, 33),
            ButtonBorder = Color.FromArgb(98, 114, 164)
        };

        public const string MutedLabelTag = "uiMutedText";

        public const string ToolbarGlyphInactiveTag = "uiToolbarGlyphInactive";

        public static UiThemeKind EffectiveTheme(UiThemeKind theme)
        {
            if (theme == UiThemeKind.System)
            {
                return IsWindowsAppDark() ? UiThemeKind.Dark : UiThemeKind.Light;
            }
            return theme;
        }

        private static bool IsWindowsAppDark()
        {
            try
            {
                using RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
                object o = k?.GetValue("AppsUseLightTheme");
                if (o is int i)
                {
                    return i == 0;
                }
            }
            catch
            {
            }
            return false;
        }

        public static void Apply(Control root, UiThemeKind theme)
        {
            ApplyInternal(root, theme, null);
        }

        public static void Apply(Control root, UiThemeKind theme, TextBox serverLogTextBox)
        {
            ApplyInternal(root, theme, serverLogTextBox);
        }

        public static void ApplyWindowFrame(Form form, UiThemeKind theme)
        {
            if (form == null)
            {
                return;
            }

            UiThemeKind t = EffectiveTheme(theme);
            if (form.IsHandleCreated)
            {
                SetImmersiveDarkMode(form, t);
            }
            else
            {
                void OnCreate(object s, EventArgs a)
                {
                    form.HandleCreated -= OnCreate;
                    SetImmersiveDarkMode(form, t);
                }
                form.HandleCreated += OnCreate;
            }
        }

        private static void ApplyInternal(Control root, UiThemeKind theme, TextBox serverLogTextBox)
        {
            if (root == null)
            {
                return;
            }

            UiThemeKind t = EffectiveTheme(theme);
            ApplyRecurse(root, t, serverLogTextBox, false);
            if (root is Form form)
            {
                ApplyWindowFrame(form, theme);
                ScheduleThemedChildChrome(form, t);
            }
        }

        public static void ApplyThemedChildChrome(Control root, UiThemeKind theme)
        {
            if (root == null)
            {
                return;
            }

            ThemedChildChromeRecurse(root, EffectiveTheme(theme));
        }

        private static void ScheduleThemedChildChrome(Form form, UiThemeKind theme)
        {
            if (form == null)
            {
                return;
            }
            void Run()
            {
                if (form.IsDisposed)
                {
                    return;
                }
                ThemedChildChromeRecurse(form, theme);
            }
            if (form.IsHandleCreated)
            {
                form.BeginInvoke((Action)Run);
            }
            else
            {
                void OnCreate(object s, EventArgs a)
                {
                    form.HandleCreated -= OnCreate;
                    if (!form.IsDisposed)
                    {
                        form.BeginInvoke((Action)Run);
                    }
                }
                form.HandleCreated += OnCreate;
            }
        }

        private static void ThemedChildChromeRecurse(Control c, UiThemeKind theme)
        {
            foreach (Control child in c.Controls)
            {
                if (child is TabControl tab && tab.IsHandleCreated)
                {
                    SetWindowThemeSystemTabs(tab);
                    tab.Invalidate(true);
                }
                if (child.HasChildren)
                {
                    ThemedChildChromeRecurse(child, theme);
                }
            }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        private static void SetWindowThemeSystemTabs(TabControl tab)
        {
            SetWindowTheme(tab.Handle, null, null);
        }

        public static void ApplySystemContextMenuColors(ContextMenuStrip menu)
        {
            ApplyContextMenu(menu, UiThemeKind.Light);
        }

        public static void ApplyContextMenu(ContextMenuStrip menu, UiThemeKind theme)
        {
            if (menu == null)
            {
                return;
            }

            if (theme == UiThemeKind.Light)
            {
                menu.BackColor = SystemColors.Menu;
                menu.ForeColor = SystemColors.MenuText;
            }
            else
            {
                ThemeColors s = GetThemeColors(theme);
                menu.BackColor = s.MenuBack;
                menu.ForeColor = s.Text;
            }

            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem tsmi)
                {
                    StyleToolStripItem(tsmi, theme);
                }
            }
        }

        private static void StyleToolStripItem(ToolStripMenuItem item, UiThemeKind theme)
        {
            if (theme == UiThemeKind.Light)
            {
                item.BackColor = SystemColors.Menu;
                item.ForeColor = SystemColors.MenuText;
            }
            else
            {
                ThemeColors s = GetThemeColors(theme);
                item.BackColor = s.MenuBack;
                item.ForeColor = s.Text;
            }

            foreach (ToolStripItem sub in item.DropDownItems)
            {
                if (sub is ToolStripMenuItem tsmi)
                {
                    StyleToolStripItem(tsmi, theme);
                }
            }
        }

        private static bool IsToolbarGlyphInactiveButton(Control c)
        {
            if (c is not Button)
            {
                return false;
            }

            object tag = c.Tag;
            return ReferenceEquals(tag, ToolbarGlyphInactiveTag)
                   || (tag is string s && s == ToolbarGlyphInactiveTag);
        }

        private static ThemeColors GetThemeColors(UiThemeKind kind)
        {
            return kind == UiThemeKind.Dracula ? Dracula : VSDark;
        }

        private static void ApplyRecurse(Control c, UiThemeKind theme, TextBox serverLogTextBox, bool isLog)
        {
            isLog = isLog || (serverLogTextBox != null && ReferenceEquals(c, serverLogTextBox));
            bool light = theme == UiThemeKind.Light;
            ThemeColors s = default;
            if (!light)
            {
                s = GetThemeColors(theme);
            }

            switch (c)
            {
                case TabControl tc:
                    if (tc.Tag is UiThemeKind)
                    {
                        tc.Tag = null;
                    }
                    tc.DrawMode = TabDrawMode.Normal;
                    tc.BackColor = SystemColors.Control;
                    break;
                case TabPage tp:
                    if (light)
                    {
                        tp.BackColor = SystemColors.Control;
                        tp.UseVisualStyleBackColor = true;
                        tp.ForeColor = SystemColors.ControlText;
                    }
                    else
                    {
                        tp.BackColor = s.Form;
                        tp.UseVisualStyleBackColor = false;
                        tp.ForeColor = s.Text;
                    }
                    break;
                case DataGridView dgv:
                    if (light)
                    {
                        dgv.BackgroundColor = SystemColors.AppWorkspace;
                        dgv.BorderStyle = BorderStyle.Fixed3D;
                        dgv.GridColor = SystemColors.Control;
                        dgv.EnableHeadersVisualStyles = true;
                        dgv.DefaultCellStyle = new DataGridViewCellStyle
                        {
                            BackColor = SystemColors.Window,
                            ForeColor = SystemColors.WindowText,
                            SelectionBackColor = SystemColors.Highlight,
                            SelectionForeColor = SystemColors.HighlightText
                        };
                        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                        {
                            BackColor = SystemColors.Control,
                            ForeColor = SystemColors.ControlText
                        };
                    }
                    else
                    {
                        dgv.BackgroundColor = s.Form;
                        dgv.BorderStyle = BorderStyle.None;
                        dgv.GridColor = s.DgvLine;
                        dgv.EnableHeadersVisualStyles = false;
                        dgv.DefaultCellStyle = new DataGridViewCellStyle
                        {
                            BackColor = s.Surface,
                            ForeColor = s.Text,
                            SelectionBackColor = s.DgvSelect,
                            SelectionForeColor = s.DgvSelectFore
                        };
                        dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                        {
                            BackColor = s.DgvHeader,
                            ForeColor = s.Text
                        };
                    }
                    break;
                case LinkLabel ll:
                    if (light)
                    {
                        ll.BackColor = SystemColors.Control;
                        ll.ForeColor = SystemColors.ControlText;
                        ll.LinkColor = Color.Blue;
                        ll.ActiveLinkColor = Color.Red;
                        ll.VisitedLinkColor = Color.Purple;
                    }
                    else
                    {
                        ll.BackColor = s.Form;
                        ll.ForeColor = s.Text;
                        ll.LinkColor = s.Link;
                        ll.ActiveLinkColor = s.Text;
                        ll.VisitedLinkColor = s.Link;
                    }
                    break;
                case GroupBox _:
                case Panel p:
                    if (light)
                    {
                        c.BackColor = SystemColors.Control;
                        c.ForeColor = SystemColors.ControlText;
                    }
                    else
                    {
                        c.BackColor = s.Form;
                        c.ForeColor = s.Text;
                    }
                    break;
                case Label lb:
                    if (lb.BackColor == Color.Transparent)
                    {
                        if (IsMutedLabel(lb))
                        {
                            lb.ForeColor = light ? SystemColors.GrayText : s.Muted;
                        }
                        else
                        {
                            lb.ForeColor = light ? SystemColors.ControlText : s.Text;
                        }
                    }
                    else
                    {
                        if (light)
                        {
                            if (IsMutedLabel(lb))
                            {
                                lb.ForeColor = SystemColors.GrayText;
                            }
                            else
                            {
                                lb.ForeColor = SystemColors.ControlText;
                            }
                            lb.BackColor = SystemColors.Control;
                        }
                        else
                        {
                            if (IsMutedLabel(lb))
                            {
                                lb.ForeColor = s.Muted;
                            }
                            else
                            {
                                lb.ForeColor = s.Text;
                            }
                            lb.BackColor = s.Form;
                        }
                    }
                    break;
                case TextBox tb:
                    if (isLog)
                    {
                        if (light)
                        {
                            tb.BackColor = SystemColors.Window;
                            tb.ForeColor = SystemColors.WindowText;
                        }
                        else
                        {
                            tb.BackColor = s.LogBack;
                            tb.ForeColor = s.LogFore;
                        }
                    }
                    else
                    {
                        if (light)
                        {
                            tb.BackColor = SystemColors.Window;
                            tb.ForeColor = SystemColors.WindowText;
                        }
                        else
                        {
                            tb.BackColor = s.Field;
                            tb.ForeColor = s.Text;
                        }
                    }
                    break;
                case ComboBox cb:
                    if (light)
                    {
                        cb.BackColor = SystemColors.Window;
                        cb.ForeColor = SystemColors.WindowText;
                    }
                    else
                    {
                        cb.BackColor = s.Field;
                        cb.ForeColor = s.Text;
                    }
                    break;
                case NumericUpDown nud:
                    if (light)
                    {
                        nud.BackColor = SystemColors.Window;
                        nud.ForeColor = SystemColors.WindowText;
                    }
                    else
                    {
                        nud.BackColor = s.Field;
                        nud.ForeColor = s.Text;
                    }
                    break;
                case CheckBox chk:
                    if (light)
                    {
                        chk.FlatStyle = FlatStyle.Standard;
                        chk.UseVisualStyleBackColor = true;
                        chk.BackColor = SystemColors.Control;
                        chk.ForeColor = SystemColors.ControlText;
                    }
                    else
                    {
                        chk.BackColor = s.Form;
                        if (!chk.Enabled)
                        {
                            chk.UseVisualStyleBackColor = false;
                            chk.FlatStyle = FlatStyle.Flat;
                            chk.ForeColor = s.Muted;
                            chk.FlatAppearance.BorderSize = 0;
                            chk.FlatAppearance.MouseOverBackColor = s.Form;
                            chk.FlatAppearance.MouseDownBackColor = s.Form;
                            chk.FlatAppearance.CheckedBackColor = s.Form;
                        }
                        else
                        {
                            chk.FlatStyle = FlatStyle.Standard;
                            chk.UseVisualStyleBackColor = true;
                            chk.ForeColor = s.Text;
                        }
                    }
                    break;
                case Button b:
                    if (light)
                    {
                        b.BackColor = SystemColors.Control;
                        b.ForeColor = IsToolbarGlyphInactiveButton(b) ? SystemColors.GrayText : SystemColors.ControlText;
                        b.FlatStyle = FlatStyle.System;
                    }
                    else
                    {
                        b.BackColor = s.Surface;
                        b.ForeColor = IsToolbarGlyphInactiveButton(b) ? s.Muted : s.Text;
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = s.ButtonBorder;
                    }
                    break;
                case UserControl u:
                    if (light)
                    {
                        u.BackColor = SystemColors.Control;
                        u.ForeColor = SystemColors.ControlText;
                    }
                    else
                    {
                        u.BackColor = s.Form;
                        u.ForeColor = s.Text;
                    }
                    break;
                case Form f:
                    if (light)
                    {
                        f.BackColor = SystemColors.Control;
                        f.ForeColor = SystemColors.ControlText;
                    }
                    else
                    {
                        f.BackColor = s.Form;
                        f.ForeColor = s.Text;
                    }
                    break;
            }

            foreach (Control child in c.Controls)
            {
                bool childIsLog = isLog || (serverLogTextBox != null && ReferenceEquals(child, serverLogTextBox));
                ApplyRecurse(child, theme, serverLogTextBox, childIsLog);
            }
        }

        public static void SetMuted(Label label, bool muted)
        {
            if (label == null)
            {
                return;
            }
            label.Tag = muted ? MutedLabelTag : null;
        }

        private static bool IsMutedLabel(Control c)
        {
            if (c is Label lb)
            {
                if (ReferenceEquals(lb.Tag, MutedLabelTag) || (lb.Tag is string t && t == MutedLabelTag))
                {
                    return true;
                }
            }
            return false;
        }

        #region DWM

        private static void SetImmersiveDarkMode(Form form, UiThemeKind theme)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }

            bool dark = theme != UiThemeKind.Light;
            IntPtr hwnd = form.Handle;
            try
            {
                SetDwmFrameForTheme(hwnd, theme);
                if (!form.IsDisposed)
                {
                    form.BeginInvoke(
                        (Action)(() =>
                        {
                            if (form.IsDisposed || !form.IsHandleCreated)
                            {
                                return;
                            }

                            SetDwmFrameForTheme(form.Handle, theme);
                        }));
                }
            }
            catch
            {
            }
        }

        private static void SetDwmFrameForTheme(IntPtr hwnd, UiThemeKind theme)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int d = DwmColorDefault;
            if (theme == UiThemeKind.Dark)
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref on, sizeof(int));
                ThemeColors s = GetThemeColors(theme);
                int caption = ColorToDwmColorRef(s.Form);
                int border = ColorToDwmColorRef(s.Form);
                int text = ColorToDwmColorRef(s.Text);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            else
            {
                int on = 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY, ref on, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref d, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref d, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref d, sizeof(int));
            }

            RefreshWindowFrame(hwnd);
        }

        private static int ColorToDwmColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        private static void RefreshWindowFrame(IntPtr hwnd)
        {
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOZORDER = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_FRAMECHANGED = 0x0020;
            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        #endregion
    }
}
