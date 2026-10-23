using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SlBridge.Launcher
{
    /// <summary>Вкладка «Крафты»: рисунки рецептов предметов SCP:SL (данные — RecipeData.g.cs).</summary>
    internal static class CraftsPage
    {
        public static Panel Build()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Bg };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg, Padding = new Padding(24, 20, 24, 20),
                FlowDirection = FlowDirection.LeftToRight, WrapContents = true,
            };
            const int full = 660;

            flow.Controls.Add(Break(Ui.Label(L.T("Крафты предметов SCP:SL", "Crafting SCP:SL items"), 16f, Ui.Text, FontStyle.Bold)));
            flow.Controls.Add(Break(Ui.Wrap(
                L.T("Крафтится на верстаке Minecraft: E по верстаку открывает его окно, предметы таскаются мышкой. " +
                "Готовый предмет сразу становится настоящим предметом SCP:SL в инвентаре, патроны добавляются по 80 штук. " +
                "Форму можно отражать слева направо, маленькие формы — ставить в любое место сетки. Кирасы Minecraft из железа и алмазов заменены лёгкой и боевой бронёй SL. " +
                "В темноте Minecraft по-настоящему темно — скрафти лампу и держи её включённой в руке (или фонарик — он светит узким лучом туда, куда смотришь). " +
                "В окнах Minecraft видны и твои предметы SL: Micro H.I.D., дубинку и молекулярный разрушитель можно перезарядить (группа «Перезарядка»).",
                "Craft on a Minecraft crafting table: E on the table opens its window, drag items with the mouse. " +
                "The result immediately becomes a real SCP:SL item in your inventory; ammo is added 80 at a time. " +
                "Shapes can be mirrored left to right, small shapes can go anywhere in the grid. Minecraft iron and diamond chestplates are replaced by SL light and combat armor. " +
                "Darkness in Minecraft is real — craft a lantern and hold it lit in your hand (or a flashlight — it shines a narrow beam where you look). " +
                "Your SL items show up in Minecraft windows too: the Micro H.I.D., Jailbird and Particle Disruptor can be recharged (the “Recharge” group)."), full)));
            flow.Controls.Add(Break(new LegendPanel(full)));

            string group = null;
            foreach (var r in RecipeData.All)
            {
                if (G(r) != group)
                {
                    group = G(r);
                    var h = Ui.Label(group, 13f, Ui.Text, FontStyle.Bold);
                    h.Margin = new Padding(0, 14, 0, 6);
                    flow.Controls.Add(Break(h));
                }
                flow.Controls.Add(new RecipeCard(r));
            }

            var mh = Ui.Label(L.T("Аптечки из еды", "Medkits from food"), 13f, Ui.Text, FontStyle.Bold);
            mh.Margin = new Padding(0, 14, 0, 6);
            flow.Controls.Add(Break(mh));
            flow.Controls.Add(Break(Ui.Wrap(L.T("10 единиц голода = 1 аптечка. Положи в сетку столько одинаковых продуктов, сколько указано (в любом порядке). " +
                                            "Наведи мышь на ячейку рецепта — увидишь название предмета.",
                                            "10 hunger points = 1 medkit. Put as many identical food items into the grid as listed (any order). " +
                                            "Hover a recipe cell to see the item name."), full)));
            var grid = new DataGridView
            {
                Width = full, ReadOnly = true, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                BackgroundColor = Ui.Card, GridColor = Ui.Input, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle = { BackColor = Ui.Input, ForeColor = Ui.Text, Font = Ui.F(9.5f, FontStyle.Bold) },
                DefaultCellStyle = { BackColor = Ui.Card, ForeColor = Ui.Text, SelectionBackColor = Ui.Input, SelectionForeColor = Ui.Text },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ScrollBars = ScrollBars.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            grid.Columns.Add(new DataGridViewImageColumn { Name = "i", HeaderText = "", Width = 40, FillWeight = 12, ImageLayout = DataGridViewImageCellLayout.Normal });
            grid.Columns.Add("f", L.T("Еда", "Food"));
            grid.Columns.Add("h", L.T("Голод", "Hunger"));
            grid.Columns.Add("n", L.T("Сколько положить", "How many"));
            grid.Columns.Add("o", L.T("Аптечек", "Medkits"));
            grid.RowTemplate.Height = 34;
            foreach (var m in RecipeData.Medkits)
            {
                Bitmap icon = new Bitmap(32, 32);
                using (var ig = Graphics.FromImage(icon)) McIcons.Draw(ig, m.Id, new Rectangle(0, 0, 32, 32));
                grid.Rows.Add(icon, L.T(m.Ru, m.En), m.Hunger, m.Need, m.Out);
            }
            grid.Height = grid.ColumnHeadersHeight + grid.Rows.Cast<DataGridViewRow>().Sum(x => x.Height) + 2;
            flow.Controls.Add(Break(grid));

            page.Controls.Add(flow);
            return page;
        }

        private static Control Break(Control c) => Brk(c);
        private static string G(RecipeData.Rec r) => L.T(r.GroupRu, r.GroupEn);
        private static Control Brk(Control c)
        {
            // перенос строки во FlowLayoutPanel
            c.ParentChanged += (_, _) => { if (c.Parent is FlowLayoutPanel f) f.SetFlowBreak(c, true); };
            return c;
        }

        internal static (Color back, string text) Cell(char c, string note)
        {
            if (c == '?') return (Color.FromArgb(240, 200, 60), note.StartsWith("Зачар") || note.StartsWith("Enchanted") ? L.T("ЗЗЯ", "EGA") : L.T("ЗЯ", "GA"));
            if (c == '*') return (Color.FromArgb(63, 163, 77), "★"); // сам предмет на перезарядку
            if (c == 'r') return (Color.FromArgb(160, 20, 20), L.T("БР", "RB")); // блок редстоуна
            if (RecipeData.Legend.TryGetValue(c, out var ing)) return (ColorTranslator.FromHtml(ing.Color), Abbr(L.T(ing.Ru, ing.En)));
            return (Ui.Input, "");
        }

        private static string Abbr(string label)
        {
            var w = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return w.Length == 1 ? label.Substring(0, Math.Min(3, label.Length)) : string.Concat(w.Select(x => char.ToUpper(x[0])));
        }

        internal static Color Contrast(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 140 ? Color.FromArgb(20, 20, 20) : Color.White;

        internal static void DrawCell(Graphics g, Rectangle r, char c, string note, Font f)
        {
            using (var b = new SolidBrush(Ui.Input)) g.FillRectangle(b, r);
            if (c != ' ')
            {
                var (back, text) = Cell(c, note);
                var inner = Rectangle.Inflate(r, -3, -3);
                using (var b = new SolidBrush(back)) g.FillRectangle(b, inner);
                TextRenderer.DrawText(g, text, f, inner, Contrast(back), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using var p = new Pen(Ui.Bg);
            g.DrawRectangle(p, r);
        }
    }

    /// <summary>Рецепт в стиле окна верстака Minecraft: серая панель, ячейки-слоты с иконками предметов, стрелка и результат.</summary>
    internal sealed class RecipeCard : Panel
    {
        internal static readonly Color McPanel = Color.FromArgb(198, 198, 198), McSlot = Color.FromArgb(139, 139, 139),
                                       McDark = Color.FromArgb(55, 55, 55), McText = Color.FromArgb(64, 64, 64);
        private static string Note(RecipeData.Rec r) => L.T(r.NoteRu, r.NoteEn);
        private const int Slot = 38;
        private readonly RecipeData.Rec _r;
        private readonly ToolTip _tip = new ToolTip { InitialDelay = 150, ReshowDelay = 50 };
        private string _lastTip;

        public RecipeCard(RecipeData.Rec r)
        {
            _r = r;
            DoubleBuffered = true;
            BackColor = Ui.Bg;
            Size = new Size(12 + 3 * Slot + 34 + Slot + 22, 30 + 3 * Slot + 24);
            Margin = new Padding(0, 0, 10, 10);
        }

        internal static void DrawSlot(Graphics g, Rectangle r)
        {
            using (var b = new SolidBrush(McSlot)) g.FillRectangle(b, r);
            using (var d = new Pen(McDark)) { g.DrawLine(d, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(d, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var w = new Pen(Color.White)) { g.DrawLine(w, r.Left + 1, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(w, r.Right - 1, r.Top + 1, r.Right - 1, r.Bottom - 1); }
        }

        /// <summary>Иконка предмета в слоте; если Minecraft не найден — цветной квадрат с подписью.</summary>
        internal static void DrawItem(Graphics g, Rectangle slot, string ids, char c, string note, Font f, int count = 1)
        {
            DrawSlot(g, slot);
            string id = (ids ?? "").Split('|')[0];
            var inner = Rectangle.Inflate(slot, -3, -3);
            if (id.Length > 0 && McIcons.Draw(g, id, inner)) { }
            else if (c != ' ' && c != '\0')
            {
                var (back, text) = CraftsPage.Cell(c, note);
                using (var b = new SolidBrush(back)) g.FillRectangle(b, inner);
                TextRenderer.DrawText(g, text, f, inner, CraftsPage.Contrast(back), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else return;
            if (count > 1)
            {
                using var cf = Ui.F(9f, FontStyle.Bold);
                var tr = new Rectangle(slot.Left, slot.Bottom - 18, slot.Width - 2, 17);
                TextRenderer.DrawText(g, count.ToString(), cf, new Rectangle(tr.X + 1, tr.Y + 1, tr.Width, tr.Height), Color.FromArgb(63, 63, 63), TextFormatFlags.Right | TextFormatFlags.Bottom);
                TextRenderer.DrawText(g, count.ToString(), cf, tr, Color.White, TextFormatFlags.Right | TextFormatFlags.Bottom);
            }
        }

        private char CharAt(int i)
        {
            if (_r.Shaped)
            {
                var rows = _r.Grid.Select(s => s.PadRight(3)).ToList();
                while (rows.Count < 3) rows.Add("   ");
                return rows[i / 3][i % 3];
            }
            var items = _r.Grid[0];
            return i < items.Length ? items[i] : ' ';
        }

        private string NameAt(int i)
        {
            char c = CharAt(i);
            if (c == ' ') return null;
            if (c == '?') return Note(_r);
            if (c == '*') return L.T(_r.Ru, _r.En);
            return RecipeData.Legend.TryGetValue(c, out var ing) ? L.T(ing.Ru, ing.En) : null;
        }

        private Rectangle CellRect(int i) => new Rectangle(8 + i % 3 * Slot, 28 + i / 3 * Slot, Slot, Slot);
        private Rectangle ResultRect => new Rectangle(8 + 3 * Slot + 34, 28 + Slot - 4, Slot + 8, Slot + 8);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string t = null;
            for (int i = 0; i < 9; i++) if (CellRect(i).Contains(e.Location)) t = NameAt(i);
            if (ResultRect.Contains(e.Location)) t = L.T(_r.Ru, _r.En);
            if (t == _lastTip) return;
            _lastTip = t;
            if (t == null) _tip.Hide(this); else _tip.Show(t, this, e.X + 14, e.Y + 18, 4000);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _lastTip = null; _tip.Hide(this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rc = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(McPanel)) g.FillRectangle(b, rc);
            using (var w = new Pen(Color.White, 2)) { g.DrawLine(w, 1, 1, Width - 2, 1); g.DrawLine(w, 1, 1, 1, Height - 2); }
            using (var d = new Pen(Color.FromArgb(85, 85, 85), 2)) { g.DrawLine(d, 2, Height - 2, Width - 1, Height - 2); g.DrawLine(d, Width - 2, 2, Width - 2, Height - 1); }
            using (var o = new Pen(Color.Black)) g.DrawRectangle(o, rc);
            using var title = Ui.F(10f, FontStyle.Bold);
            using var small = Ui.F(7.5f, FontStyle.Bold);
            using var note = Ui.F(8.5f);
            TextRenderer.DrawText(g, L.T(_r.Ru, _r.En), title, new Rectangle(8, 6, Width - 16, 20), McText, TextFormatFlags.EndEllipsis);
            for (int i = 0; i < 9; i++)
                DrawItem(g, CellRect(i), i < (_r.Cells?.Length ?? 0) ? _r.Cells[i] : "", CharAt(i), Note(_r), small);
            // стрелка, как в верстаке
            int ax = 8 + 3 * Slot + 6, ay = 28 + 3 * Slot / 2;
            using (var b = new SolidBrush(McSlot))
            {
                g.FillRectangle(b, ax, ay - 4, 14, 8);
                g.FillPolygon(b, new[] { new Point(ax + 14, ay - 10), new Point(ax + 24, ay), new Point(ax + 14, ay + 10) });
            }
            var res = ResultRect;
            using (var b = new SolidBrush(McSlot)) g.FillRectangle(b, Rectangle.Inflate(res, 2, 2));
            string rid = _r.Result != null && _r.Result.Length > 0 ? _r.Result[0] : "";
            if (McIcons.Get(rid) != null) DrawItem(g, res, rid, ' ', "", small, _r.Count);
            else
            {
                DrawSlot(g, res);
                using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, Rectangle.Inflate(res, -4, -4));
                TextRenderer.DrawText(g, _r.Count > 1 ? "SL ×" + _r.Count : "SL", title, res, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, _r.Shaped ? L.T("форма", "shaped") : L.T("в любом порядке", "any order"), note,
                new Rectangle(8, 28 + 3 * Slot + 3, Width - 16, 18), Color.FromArgb(90, 90, 90), TextFormatFlags.Left);
        }
    }

    internal sealed class LegendPanel : FlowLayoutPanel
    {
        public LegendPanel(int width)
        {
            AutoSize = true;
            MaximumSize = new Size(width, 0);
            BackColor = Ui.Bg;
            WrapContents = true;
            Margin = new Padding(0, 6, 0, 4);
            var used = new HashSet<char>(RecipeData.All.SelectMany(r => r.Grid.SelectMany(s => s)));
            foreach (var kv in RecipeData.Legend.Where(k => used.Contains(k.Key)))
            {
                var p = new Panel { Size = new Size(160, 26), Margin = new Padding(0, 0, 6, 4), BackColor = Ui.Bg };
                var c = kv.Key;
                p.Paint += (_, e) =>
                {
                    using var f = Ui.F(7.5f, FontStyle.Bold);
                    using var f2 = Ui.F(8.5f);
                    RecipeCard.DrawItem(e.Graphics, new Rectangle(0, 0, 26, 26), kv.Value.Id, c, "", f);
                    TextRenderer.DrawText(e.Graphics, L.T(kv.Value.Ru, kv.Value.En), f2, new Rectangle(30, 0, 130, 26), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                };
                Controls.Add(p);
            }
        }
    }
}
