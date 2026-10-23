using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SlBridge.Launcher
{
    /// <summary>Тёмная полоса заголовка: иконка, название, «свернуть», «развернуть» и «закрыть». За неё окно перетаскивается.</summary>
    internal sealed class TitleBar : Panel
    {
        public const int H = 34;
        private readonly Form _form;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public TitleBar(Form form, string title)
        {
            _form = form;
            Dock = DockStyle.Top;
            Height = H;
            BackColor = Color.FromArgb(12, 13, 16);

            var icon = new PictureBox { Size = new Size(18, 18), Location = new Point(10, 8), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            try { if (form.Icon != null) icon.Image = new Icon(form.Icon, 32, 32).ToBitmap(); } catch { }
            var text = new Label { Text = title, AutoSize = true, ForeColor = Ui.Muted, Font = Ui.F(9.5f), Location = new Point(36, 9), BackColor = Color.Transparent };

            var close = Btn("✕", Color.FromArgb(196, 43, 28), () => _form.Close());
            var max = Btn("☐", Color.FromArgb(40, 44, 52), () => _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized);
            var min = Btn("—", Color.FromArgb(40, 44, 52), () => _form.WindowState = FormWindowState.Minimized);
            Controls.Add(min); Controls.Add(max); Controls.Add(close);
            Controls.Add(icon); Controls.Add(text);

            foreach (Control c in new Control[] { this, icon, text })
            {
                c.MouseDown += Drag;
                c.DoubleClick += (_, _) => max.PerformClick();
            }
        }

        private Button Btn(string glyph, Color hover, Action click)
        {
            var b = new Button
            {
                Text = glyph, Dock = DockStyle.Right, Width = 46, FlatStyle = FlatStyle.Flat, TabStop = false,
                ForeColor = Ui.Text, BackColor = BackColor, Font = Ui.F(10f), Cursor = Cursors.Default,
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = hover;
            b.Click += (_, _) => click();
            return b;
        }

        private void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            ReleaseCapture();
            SendMessage(_form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // WM_NCLBUTTONDOWN, HTCAPTION
        }
    }
}
