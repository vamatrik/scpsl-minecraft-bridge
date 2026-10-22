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
                "В темноте Minecraft по-настоящему темно — скрафти лампу и держи её включённой в руке (фонарик тоже светит, но слабее).",
                "Craft on a Minecraft crafting table: E on the table opens its window, drag items with the mouse. " +
                "The result immediately becomes a real SCP:SL item in your inventory; ammo is added 80 at a time. " +
                "Shapes can be mirrored left to right, small shapes can go anywhere in the grid. Minecraft iron and diamond chestplates are replaced by SL light and combat armor. " +
                "Darkness in Minecraft is real — craft a lantern and hold it lit in your hand (a flashlight works too, but is dimmer)."), full)));
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
                                            "Золотое яблоко → адреналин, зачарованное золотое яблоко → SCP-500.",
                                            "10 hunger points = 1 medkit. Put as many identical food items into the grid as listed (any order). " +
                                            "Golden apple → adrenaline, enchanted golden apple → SCP-500."), full)));
            var grid = new DataGridView
            {
                Width = full, ReadOnly = true, AllowUserToAddRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
                BackgroundColor = Ui.Card, GridColor = Ui.Input, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle = { BackColor = Ui.Input, ForeColor = Ui.Text, Font = Ui.F(9.5f, FontStyle.Bold) },
                DefaultCellStyle = { BackColor = Ui.Card, ForeColor = Ui.Text, SelectionBackColor = Ui.Input, SelectionForeColor = Ui.Text },
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ScrollBars = ScrollBars.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            grid.Columns.Add("f", L.T("Еда", "Food"));
            grid.Columns.Add("h", L.T("Голод", "Hunger"));
            grid.Columns.Add("n", L.T("Сколько положить", "How many"));
            grid.Columns.Add("o", L.T("Аптечек", "Medkits"));
            foreach (var m in RecipeData.Medkits) grid.Rows.Add(L.T(m.Ru, m.En), m.Hunger, m.Need, m.Out);
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

    internal sealed class RecipeCard : Panel
    {
        private static string Note(RecipeData.Rec r) => L.T(r.NoteRu, r.NoteEn);
        private const int CellSize = 34;
        private readonly RecipeData.Rec _r;
        private readonly ToolTip _tip = new ToolTip();

        public RecipeCard(RecipeData.Rec r)
        {
            _r = r;
            DoubleBuffered = true;
            BackColor = Ui.Card;
            Size = new Size(210, 44 + 3 * CellSize + 26);
            Margin = new Padding(0, 0, 10, 10);
            var tip = r.Shaped
                ? string.Join("\n", r.Grid.SelectMany(x => x).Distinct().Where(c => c != ' ').Select(c => RecipeData.Legend.TryGetValue(c, out var i) ? $"{CraftsPage.Cell(c, Note(r)).text} — {L.T(i.Ru, i.En)}" : ""))
                : L.T("В любом порядке: ", "In any order: ") + string.Join(" + ", r.Grid[0].Select(c => c == '?' ? Note(r) : L.T(RecipeData.Legend[c].Ru, RecipeData.Legend[c].En)));
            _tip.SetToolTip(this, tip);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var title = Ui.F(10f, FontStyle.Bold);
            using var small = Ui.F(7.5f, FontStyle.Bold);
            using var note = Ui.F(8.5f);
            TextRenderer.DrawText(g, L.T(_r.Ru, _r.En), title, new Rectangle(10, 8, Width - 20, 22), Ui.Text, TextFormatFlags.EndEllipsis);
            int x0 = 12, y0 = 38;
            if (_r.Shaped)
            {
                var rows = _r.Grid.Select(s => s.PadRight(3)).ToList();
                while (rows.Count < 3) rows.Add("   ");
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                        CraftsPage.DrawCell(g, new Rectangle(x0 + x * CellSize, y0 + y * CellSize, CellSize, CellSize), rows[y][x], Note(_r), small);
            }
            else
            {
                // бесформенный рецепт: ингредиенты в сетке по порядку
                var items = _r.Grid[0];
                for (int i = 0; i < 9; i++)
                    CraftsPage.DrawCell(g, new Rectangle(x0 + i % 3 * CellSize, y0 + i / 3 * CellSize, CellSize, CellSize), i < items.Length ? items[i] : ' ', Note(_r), small);
            }
            // стрелка и результат
            int ax = x0 + 3 * CellSize + 10, ay = y0 + 3 * CellSize / 2;
            using (var p = new Pen(Ui.Muted, 3) { EndCap = LineCap.ArrowAnchor }) g.DrawLine(p, ax, ay, ax + 22, ay);
            var res = new Rectangle(ax + 30, ay - 20, 40, 40);
            using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, res);
            TextRenderer.DrawText(g, "SL", title, res, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _r.Shaped ? L.T("форма", "shaped") : L.T("в любом порядке", "any order"), note, new Rectangle(10, y0 + 3 * CellSize + 4, Width - 20, 18), Ui.Muted, TextFormatFlags.Left);
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
                    CraftsPage.DrawCell(e.Graphics, new Rectangle(0, 0, 26, 26), c, "", f);
                    TextRenderer.DrawText(e.Graphics, L.T(kv.Value.Ru, kv.Value.En), f2, new Rectangle(30, 0, 130, 26), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                };
                Controls.Add(p);
            }
        }
    }
}
