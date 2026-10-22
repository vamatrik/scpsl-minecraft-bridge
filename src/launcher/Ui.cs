using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SlBridge.Launcher
{
    /// <summary>Тёмная тема и мелкие элементы интерфейса.</summary>
    internal static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(17, 19, 23);
        public static readonly Color Side = Color.FromArgb(23, 26, 31);
        public static readonly Color Card = Color.FromArgb(30, 33, 40);
        public static readonly Color Input = Color.FromArgb(38, 42, 50);
        public static readonly Color Text = Color.FromArgb(228, 230, 235);
        public static readonly Color Muted = Color.FromArgb(145, 151, 162);
        public static readonly Color Ok = Color.FromArgb(76, 175, 80);
        public static readonly Color Bad = Color.FromArgb(214, 72, 72);
        public static readonly Color Warn = Color.FromArgb(230, 170, 60);
        public static readonly Color Off = Color.FromArgb(80, 84, 92);
        public static readonly Color Accent = Color.FromArgb(76, 175, 80);

        public static Font F(float size, FontStyle st = FontStyle.Regular) => new Font("Segoe UI", size, st);

        public static Label Label(string text, float size = 10f, Color? color = null, FontStyle st = FontStyle.Regular)
            => new Label { Text = text, AutoSize = true, Font = F(size, st), ForeColor = color ?? Text, BackColor = Color.Transparent };

        public static Label Wrap(string text, int width, float size = 9.5f, Color? color = null)
            => new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Font = F(size), ForeColor = color ?? Muted, BackColor = Color.Transparent };

        public static Button Button(string text, Color? back = null, int width = 0)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = back ?? Input, ForeColor = Color.White,
                Font = F(9.5f, FontStyle.Bold), Cursor = Cursors.Hand, AutoSize = width == 0, Height = 32,
                Padding = new Padding(8, 0, 8, 0), Margin = new Padding(0, 0, 8, 0),
            };
            if (width > 0) b.Width = width;
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        public static ComboBox Combo(int width)
            => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Input, ForeColor = Text, Width = width, Font = F(10f) };

        public static TextBox Box(int width)
            => new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Input, ForeColor = Text, Width = width, Font = F(10f) };
    }

    /// <summary>Строка состояния: цветная точка + название + текст.</summary>
    internal sealed class StatusRow : Control
    {
        private readonly string _title;
        private string _state = "—";
        private Color _dot = Ui.Off;
        public StatusRow(string title) { _title = title; DoubleBuffered = true; Height = 28; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Ui.Card);
            using (var b = new SolidBrush(_dot)) g.FillEllipse(b, 2, Height / 2 - 6, 12, 12);
            TextRenderer.DrawText(g, _title, Ui.F(10f, FontStyle.Bold), new Point(22, 4), Ui.Text);
            TextRenderer.DrawText(g, _state, Ui.F(9.5f), new Rectangle(120, 5, Width - 122, Height - 5), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        public void Set(Color dot, string state) { if (_dot == dot && _state == state) return; _dot = dot; _state = state; Invalidate(); }
    }

    /// <summary>Карточка шага «Подготовки»: номер, заголовок, пояснение, статус, кнопки.</summary>
    internal sealed class StepCard : Panel
    {
        public readonly Label Status;
        public readonly FlowLayoutPanel Buttons;
        public readonly FlowLayoutPanel Extra;

        public StepCard(int number, string title, string text, int width)
        {
            BackColor = Ui.Card;
            Width = width;
            Padding = new Padding(16, 12, 16, 12);
            Margin = new Padding(0, 0, 0, 10);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Dock = DockStyle.Fill };
            var head = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0) };
            head.Controls.Add(Ui.Label(number + ".", 12f, Ui.Muted, FontStyle.Bold));
            head.Controls.Add(Ui.Label(title, 12f, Ui.Text, FontStyle.Bold));
            Status = Ui.Label("…", 10f, Ui.Muted, FontStyle.Bold);
            Status.Margin = new Padding(12, 3, 0, 0);
            head.Controls.Add(Status);
            flow.Controls.Add(head);
            var t = Ui.Wrap(text, width - 40);
            t.Margin = new Padding(0, 6, 0, 8);
            flow.Controls.Add(t);
            Extra = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            flow.Controls.Add(Extra);
            Buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, WrapContents = true, MaximumSize = new Size(width - 40, 0), Margin = new Padding(0) };
            flow.Controls.Add(Buttons);
            Controls.Add(flow);
        }

        public void SetStatus(bool? ok, string text)
        {
            Status.Text = (ok == true ? "✓ " : ok == false ? "✗ " : "• ") + text;
            Status.ForeColor = ok == true ? Ui.Ok : ok == false ? Ui.Bad : Ui.Warn;
        }
    }
}
