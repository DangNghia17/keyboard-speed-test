// Keyboard Speed Test - các control giao diện tự vẽ (nền sáng, tối giản, hỗ trợ DPI).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace KeyboardSpeedTest
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 245, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(225, 229, 236);
        public static readonly Color Divider = Color.FromArgb(236, 239, 243);
        public static readonly Color Text = Color.FromArgb(22, 30, 41);
        public static readonly Color Text2 = Color.FromArgb(78, 89, 104);
        public static readonly Color Muted = Color.FromArgb(128, 139, 154);
        public static readonly Color Subtle = Color.FromArgb(247, 248, 250);

        public static readonly Color Accent = Color.FromArgb(22, 163, 74);       // xanh lá: hành động chính, đang lắng nghe
        public static readonly Color AccentHover = Color.FromArgb(21, 128, 61);
        public static readonly Color AccentSoft = Color.FromArgb(220, 252, 231);

        public static readonly Color Warn = Color.FromArgb(180, 83, 9);
        public static readonly Color WarnSoft = Color.FromArgb(254, 243, 199);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerSoft = Color.FromArgb(254, 226, 226);
        public static readonly Color Info = Color.FromArgb(37, 99, 235);
        public static readonly Color InfoSoft = Color.FromArgb(232, 240, 254);

        // Màu nhận diện của từng bàn phím, dùng thống nhất ở thẻ thiết bị, khu vực test, biểu đồ và bảng.
        public static readonly Color[] DeviceColor = new Color[] { Color.FromArgb(37, 99, 235), Color.FromArgb(234, 88, 12) };
        public static readonly Color[] DeviceSoft = new Color[] { Color.FromArgb(239, 245, 255), Color.FromArgb(255, 245, 237) };

        public const string FontName = "Segoe UI";
        public const string FontSemibold = "Segoe UI Semibold";

        private static readonly Dictionary<string, Font> cache = new Dictionary<string, Font>();

        public static Font F(float size) { return Get(FontName, size, FontStyle.Regular); }
        public static Font Semi(float size) { return Get(FontSemibold, size, FontStyle.Regular); }
        public static Font Bold(float size) { return Get(FontName, size, FontStyle.Bold); }

        private static Font Get(string name, float size, FontStyle style)
        {
            string key = name + "|" + size + "|" + style;
            Font f;
            if (!cache.TryGetValue(key, out f))
            {
                try { f = new Font(name, size, style, GraphicsUnit.Point); }
                catch { f = new Font(FontName, size, style, GraphicsUnit.Point); }
                cache[key] = f;
            }
            return f;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    public static class Dpi
    {
        public static float Scale = 1f;

        public static void Init()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            }
            catch { Scale = 1f; }
            if (Scale < 1f) Scale = 1f;
        }

        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }
    }

    public static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color c, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s ?? "", f, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        }

        public static Size Measure(string s, Font f)
        {
            return TextRenderer.MeasureText(s ?? "", f, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static int MeasureHeight(string s, Font f, int width)
        {
            if (width <= 0) return 0;
            return TextRenderer.MeasureText(s ?? "", f, new Size(width, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height;
        }

        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        public const TextFormatFlags Wrap = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;

        public static void Dot(Graphics g, int cx, int cy, int radius, Color c)
        {
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        /// <summary>Viên "pill" chứa chữ, trả về chiều rộng đã vẽ.</summary>
        public static int Pill(Graphics g, int x, int y, int h, string text, Font f, Color fore, Color back, Color dot)
        {
            Size sz = Measure(text, f);
            int padX = Dpi.S(10);
            int dotW = dot.IsEmpty ? 0 : Dpi.S(12);
            Rectangle r = new Rectangle(x, y, sz.Width + padX * 2 + dotW, h);
            FillRound(g, r, h / 2, back);
            if (!dot.IsEmpty) Dot(g, r.X + padX + Dpi.S(3), r.Y + h / 2, Dpi.S(4), dot);
            Text(g, text, f, new Rectangle(r.X + padX + dotW, r.Y, sz.Width + Dpi.S(2), h), fore, Left);
            return r.Width;
        }
    }

    /// <summary>Thẻ nền trắng bo góc, viền mảnh.</summary>
    public class CardPanel : Panel
    {
        private Color highlight = Color.Empty;
        private float highlightLevel;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
            Padding = new Padding(Dpi.S(16));
        }

        public int Radius = 10;

        public void SetHighlight(Color c, float level)
        {
            if (highlight == c && Math.Abs(highlightLevel - level) < 0.01f) return;
            highlight = c; highlightLevel = level;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Bg;
            using (SolidBrush b = new SolidBrush(parentBack)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Draw.FillRound(g, r, Dpi.S(Radius), BackColor);
            Color border = Theme.Border;
            float w = 1f;
            if (!highlight.IsEmpty && highlightLevel > 0)
            {
                border = Theme.Blend(Theme.Border, highlight, highlightLevel);
                w = 1f + Dpi.Sf(1f) * highlightLevel;
            }
            Draw.StrokeRound(g, r, Dpi.S(Radius), border, w);
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    public enum ButtonKind { Primary, Secondary, Ghost, Danger, Toggle, Link }

    /// <summary>
    /// Nút tự vẽ có trạng thái hover / pressed / disabled / checked.
    /// Không nhận focus bàn phím, nên phím J / Space / Enter không bao giờ kích hoạt nút trong lúc test.
    /// </summary>
    public class ModernButton : Control
    {
        private bool hover, pressed, isChecked;
        private ButtonKind kind = ButtonKind.Secondary;
        public Color ToggleColor = Theme.Text;
        public int Radius = 8;

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            Font = Theme.Semi(9.5f);
            Size = new Size(Dpi.S(120), Dpi.S(36));
        }

        public ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public bool Checked { get { return isChecked; } set { if (isChecked != value) { isChecked = value; Invalidate(); } } }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); hover = pressed = false; Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Card;
            using (SolidBrush b = new SolidBrush(parentBack)) g.FillRectangle(b, ClientRectangle);

            Color back, fore, border = Color.Empty;
            switch (kind)
            {
                case ButtonKind.Primary:
                    back = pressed ? Theme.Blend(Theme.AccentHover, Color.Black, 0.15f) : hover ? Theme.AccentHover : Theme.Accent;
                    fore = Color.White;
                    if (!Enabled) { back = Color.FromArgb(214, 219, 226); fore = Color.FromArgb(150, 158, 170); }
                    break;
                case ButtonKind.Danger:
                    back = pressed ? Color.FromArgb(254, 202, 202) : hover ? Theme.DangerSoft : Theme.Card;
                    fore = Theme.Danger; border = Color.FromArgb(252, 165, 165);
                    if (!Enabled) { back = Theme.Card; fore = Color.FromArgb(190, 196, 205); border = Theme.Border; }
                    break;
                case ButtonKind.Ghost:
                case ButtonKind.Link:
                    back = pressed ? Color.FromArgb(229, 233, 239) : hover ? Theme.Subtle : parentBack;
                    fore = kind == ButtonKind.Link ? Theme.Info : Theme.Text2;
                    if (!Enabled) fore = Color.FromArgb(190, 196, 205);
                    break;
                case ButtonKind.Toggle:
                    if (isChecked)
                    {
                        back = Theme.Blend(Theme.Card, ToggleColor, 0.10f);
                        fore = ToggleColor; border = ToggleColor;
                    }
                    else
                    {
                        back = pressed ? Color.FromArgb(229, 233, 239) : hover ? Theme.Subtle : Theme.Card;
                        fore = Theme.Text2; border = Theme.Border;
                    }
                    if (!Enabled) { fore = Theme.Blend(fore, Theme.Card, 0.5f); border = Theme.Blend(border, Theme.Card, 0.5f); }
                    break;
                default:
                    back = pressed ? Color.FromArgb(229, 233, 239) : hover ? Color.FromArgb(241, 243, 246) : Theme.Card;
                    fore = Theme.Text; border = hover ? Color.FromArgb(203, 209, 218) : Theme.Border;
                    if (!Enabled) { back = Theme.Card; fore = Color.FromArgb(175, 182, 193); border = Theme.Divider; }
                    break;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Draw.FillRound(g, r, Dpi.S(Radius), back);
            if (!border.IsEmpty) Draw.StrokeRound(g, r, Dpi.S(Radius), border, 1f);
            g.SmoothingMode = SmoothingMode.Default;
            Draw.Text(g, Text, Font, new Rectangle(Dpi.S(6), 0, Width - Dpi.S(12), Height), fore, Draw.Center);
        }
    }

    /// <summary>Thanh tab dạng gạch chân.</summary>
    public class TabStrip : Control
    {
        public readonly List<string> Items = new List<string>();
        private int selected;
        private int hoverIndex = -1;
        private readonly List<Rectangle> rects = new List<Rectangle>();
        public event EventHandler SelectedChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            Font = Theme.Semi(10f);
            BackColor = Theme.Card;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= Items.Count) return;
                selected = value; Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public void SetItemText(int i, string text) { if (i >= 0 && i < Items.Count && Items[i] != text) { Items[i] = text; Invalidate(); } }

        private void Measure()
        {
            rects.Clear();
            int x = 0;
            foreach (string s in Items)
            {
                int w = Draw.Measure(s, Font).Width + Dpi.S(28);
                rects.Add(new Rectangle(x, 0, w, Height));
                x += w;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h != hoverIndex) { hoverIndex = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int h = rects.FindIndex(r => r.Contains(e.Location));
            if (h >= 0) SelectedIndex = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Measure();
            using (Pen p = new Pen(Theme.Divider, 1f)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = rects[i];
                bool sel = i == selected;
                Color fore = sel ? Theme.Text : (i == hoverIndex ? Theme.Text2 : Theme.Muted);
                Draw.Text(g, Items[i], Font, r, fore, Draw.Center);
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillRectangle(b, r.X + Dpi.S(10), Height - Dpi.S(3), r.Width - Dpi.S(20), Dpi.S(3));
                }
            }
        }
    }

    /// <summary>Header của ứng dụng.</summary>
    public class HeaderBar : Control
    {
        public string StatusText = L.S("Đang khởi động...", "Starting...");
        public Color StatusColor = Theme.Muted;
        public string KeyboardsText = "";

        public HeaderBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = Dpi.S(20);
            int icon = Dpi.S(38);
            Rectangle ir = new Rectangle(pad, (Height - icon) / 2, icon, icon);
            Draw.FillRound(g, ir, Dpi.S(9), Theme.Text);
            // biểu tượng bàn phím đơn giản
            int kw = Dpi.S(5), kh = Dpi.S(4), gap = Dpi.S(2);
            int gx = ir.X + (icon - (kw * 4 + gap * 3)) / 2, gy = ir.Y + Dpi.S(11);
            using (SolidBrush kb = new SolidBrush(Color.White))
            {
                for (int row = 0; row < 2; row++)
                    for (int c = 0; c < 4; c++)
                        g.FillRectangle(kb, gx + c * (kw + gap), gy + row * (kh + gap), kw, kh);
                g.FillRectangle(kb, gx + kw + gap, gy + 2 * (kh + gap), kw * 2 + gap, kh);
            }
            using (SolidBrush ab = new SolidBrush(Theme.Accent)) g.FillEllipse(ab, ir.Right - Dpi.S(12), ir.Y + Dpi.S(5), Dpi.S(6), Dpi.S(6));
            g.SmoothingMode = SmoothingMode.Default;

            int tx = ir.Right + Dpi.S(14);
            Font titleFont = Theme.Bold(14f);
            Size ts = Draw.Measure("KEYBOARD SPEED TEST", titleFont);
            int titleY = Height / 2 - ts.Height + Dpi.S(2);
            Draw.Text(g, "KEYBOARD SPEED TEST", titleFont, new Rectangle(tx, titleY, ts.Width + Dpi.S(4), ts.Height), Theme.Text, Draw.Left);
            Font vsFont = Theme.Semi(11f);
            string versus = AppInfo.VersusTitle;
            Size vs = Draw.Measure(versus, vsFont);
            Draw.Text(g, versus, vsFont, new Rectangle(tx + ts.Width + Dpi.S(12), titleY + (ts.Height - vs.Height) / 2 + Dpi.S(1), vs.Width + Dpi.S(4), vs.Height), Theme.Muted, Draw.Left);
            Font sub = Theme.F(9f);
            Draw.Text(g, L.S("So sánh nhịp nhấn phím liên tục giữa hai bàn phím bằng Windows Raw Input · dữ liệu chỉ lưu trên máy này",
                             "Compare continuous tapping on two keyboards using Windows Raw Input · data stays on this PC"),
                sub, new Rectangle(tx, Height / 2 + Dpi.S(3), Math.Max(0, Width - tx - Dpi.S(420)), Draw.Measure("Ag", sub).Height + Dpi.S(2)), Theme.Text2, Draw.Left);

            // bên phải: phiên bản + trạng thái
            Font pf = Theme.Semi(9f);
            int ph = Dpi.S(26);
            int py = (Height - ph) / 2;
            int right = Width - pad;
            string ver = "v" + AppInfo.Version;
            int vw = Draw.Measure(ver, pf).Width + Dpi.S(20);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.Pill(g, right - vw, py, ph, ver, pf, Theme.Text2, Theme.Subtle, Color.Empty);
            right -= vw + Dpi.S(8);
            int sw = Draw.Measure(StatusText, pf).Width + Dpi.S(32);
            Color soft = Theme.Blend(Theme.Card, StatusColor, 0.12f);
            Draw.Pill(g, right - sw, py, ph, StatusText, pf, StatusColor, soft, StatusColor);
            right -= sw + Dpi.S(8);
            if (!String.IsNullOrEmpty(KeyboardsText))
            {
                int kw2 = Draw.Measure(KeyboardsText, pf).Width + Dpi.S(20);
                Draw.Pill(g, right - kw2, py, ph, KeyboardsText, pf, Theme.Text2, Theme.Subtle, Color.Empty);
            }
            g.SmoothingMode = SmoothingMode.Default;
        }
    }

    /// <summary>Thẻ thiết bị: tên, trạng thái kết nối, trạng thái nhận diện, tên Windows rút gọn, nút nhận diện.</summary>
    public class DeviceCard : CardPanel
    {
        public readonly int Device;
        public readonly ModernButton IdentifyButton = new ModernButton();
        public readonly ModernButton DetailsButton = new ModernButton();
        public readonly ModernButton RenameButton = new ModernButton();
        public bool Identified, Connected, Waiting, Restored;
        public string ShortName = "";
        public string Hint = "";
        private readonly ToolTip tip = new ToolTip();
        private float activity;

        public DeviceCard(int device)
        {
            Device = device;
            IdentifyButton.Text = L.S("Nhận diện", "Identify");
            IdentifyButton.Kind = ButtonKind.Secondary;
            DetailsButton.Text = L.S("Chi tiết", "Details");
            DetailsButton.Kind = ButtonKind.Ghost;
            RenameButton.Text = L.S("Đổi tên", "Rename");
            RenameButton.Kind = ButtonKind.Ghost;
            Controls.Add(IdentifyButton);
            Controls.Add(RenameButton);
            Controls.Add(DetailsButton);
            tip.InitialDelay = 300;
        }

        public string FullPath = "";
        public void SetFullPath(string path) { FullPath = path ?? ""; tip.SetToolTip(this, FullPath); }

        public void Pulse() { activity = 1f; SetHighlight(Theme.DeviceColor[Device], activity); }

        /// <returns>true nếu còn đang hiệu ứng.</returns>
        public bool Tick()
        {
            if (activity <= 0) return false;
            activity -= 0.12f;
            if (activity < 0) activity = 0;
            SetHighlight(Theme.DeviceColor[Device], activity);
            return activity > 0;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int pad = Dpi.S(16);
            int bh = Dpi.S(32);
            int y = Height - pad - bh;
            int idw = Math.Max(Dpi.S(100), Draw.Measure(L.S("Nhận diện lại", "Re-identify"), IdentifyButton.Font).Width + Dpi.S(28));
            IdentifyButton.SetBounds(pad, y, idw, bh);
            int rw = Draw.Measure(RenameButton.Text, RenameButton.Font).Width + Dpi.S(22);
            RenameButton.SetBounds(pad + idw + Dpi.S(6), y, rw, bh);
            int dw = Draw.Measure(DetailsButton.Text, DetailsButton.Font).Width + Dpi.S(22);
            DetailsButton.SetBounds(pad + idw + Dpi.S(6) + rw + Dpi.S(2), y, dw, bh);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int pad = Dpi.S(16);
            Color dc = Theme.DeviceColor[Device];
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, new Rectangle(pad, pad + Dpi.S(3), Dpi.S(4), Dpi.S(22)), Dpi.S(2), dc);
            g.SmoothingMode = SmoothingMode.Default;

            Font nameFont = Theme.Bold(14f);
            string slot = Device == AppInfo.A ? "A" : "B";
            Size ns = Draw.Measure(AppInfo.Name(Device), nameFont);
            int maxNameW = Math.Max(Dpi.S(60), Width - pad * 2 - Dpi.S(12) - Dpi.S(150));
            if (ns.Width > maxNameW) { nameFont = Theme.Bold(11f); ns = Draw.Measure(AppInfo.Name(Device), nameFont); }
            ns.Width = Math.Min(ns.Width, maxNameW);
            Draw.Text(g, AppInfo.Name(Device), nameFont, new Rectangle(pad + Dpi.S(12), pad, ns.Width + Dpi.S(4), Dpi.S(28)), Theme.Text, Draw.Left);

            // trạng thái nhận diện (pill bên phải tên)
            string idText; Color idColor;
            if (Waiting) { idText = L.S("Đang chờ phím", "Waiting for key"); idColor = Theme.Info; }
            else if (Identified) { idText = Restored ? L.S("Đã nhận diện (đã lưu)", "Identified (saved)") : L.S("Đã nhận diện", "Identified"); idColor = Theme.Accent; }
            else { idText = L.S("Chưa nhận diện", "Not identified"); idColor = Theme.Muted; }
            Font pf = Theme.Semi(8.5f);
            int ph = Dpi.S(22);
            int pw = Draw.Measure(idText, pf).Width + Dpi.S(32);
            int px = Math.Max(pad + Dpi.S(12) + ns.Width + Dpi.S(10), Width - pad - pw);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.Pill(g, px, pad + Dpi.S(3), ph, idText, pf, idColor, Theme.Blend(Theme.Card, idColor, 0.12f), idColor);
            g.SmoothingMode = SmoothingMode.Default;

            Font f = Theme.F(9f);
            int lineH = Dpi.S(20);
            int y = pad + Dpi.S(36);
            string conn; Color connColor;
            if (!Identified) { conn = L.S("Kết nối: chưa xác định", "Connection: unknown"); connColor = Theme.Muted; }
            else if (Connected) { conn = L.S("Đã kết nối", "Connected"); connColor = Theme.Accent; }
            else { conn = L.S("Mất kết nối – cắm lại để tự nhận lại", "Disconnected – plug it back in to reconnect"); connColor = Theme.Danger; }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.Dot(g, pad + Dpi.S(4), y + lineH / 2, Dpi.S(4), connColor);
            g.SmoothingMode = SmoothingMode.Default;
            Draw.Text(g, conn, Theme.Semi(9f), new Rectangle(pad + Dpi.S(14), y, Width - pad * 2 - Dpi.S(14), lineH), Identified ? Theme.Text : Theme.Muted, Draw.Left);
            y += lineH;
            string name = Waiting ? (Hint.Length > 0 ? Hint : L.S("Nhấn phím J trên bàn phím muốn gán vào vị trí ", "Press J on the keyboard for slot ") + slot)
                        : Identified ? ShortName : L.S("Bấm \"Nhận diện\" rồi nhấn J trên bàn phím của bạn", "Click \"Identify\", then press J on your keyboard");
            Draw.Text(g, name, f, new Rectangle(pad, y, Width - pad * 2, lineH), Waiting ? Theme.Info : Theme.Text2, Draw.Left);
        }
    }

    public enum StageMode { NotReady, Identifying, Ready, Running, Completed, Stopped }

    /// <summary>Trung tâm kiểm tra: thiết bị đang test, bộ đếm lớn, progress, chỉ báo đang lắng nghe.</summary>
    public class StageView : Control
    {
        public StageMode Mode = StageMode.NotReady;
        public int Device = AppInfo.A;
        public int Count, Target = 10;
        public int Round = 1, Rounds = 1;
        public int[] RoundPhaseDevices = new int[] { AppInfo.A, AppInfo.B };
        public int[] RoundPhaseCounts = new int[2];
        public int RoundPhasePos;       // 0 hoặc 1: giai đoạn hiện tại trong vòng
        public string Title = "";
        public string Subtitle = "";
        public string LastPress = "";
        public string Notice = "";
        public Color NoticeColor = Theme.Warn;
        public bool Listening;
        private float pulse;
        private float flash;

        public StageView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        public void Flash() { flash = 1f; Invalidate(); }

        public void Tick()
        {
            bool dirty = false;
            if (Listening) { pulse += 0.06f; if (pulse > 1f) pulse -= 1f; dirty = true; }
            if (flash > 0) { flash = Math.Max(0, flash - 0.15f); dirty = true; }
            if (dirty) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int pad = Dpi.S(20);
            bool running = Mode == StageMode.Running;
            Color dc = Theme.DeviceColor[Device];
            Color title = Theme.Text;
            if (running || Mode == StageMode.Identifying) title = dc;
            else if (Mode == StageMode.Completed) title = Theme.Accent;

            // Dòng trên: các bước giai đoạn
            int y = pad;
            int stepH = Dpi.S(26);
            int x = pad;
            Font sf = Theme.Semi(9f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (running || Mode == StageMode.Ready || Mode == StageMode.Completed || Mode == StageMode.Stopped)
            {
                if (Rounds > 1)
                {
                    string rt = L.S("Vòng ", "Round ") + Round + "/" + Rounds;
                    x += Draw.Pill(g, x, y, stepH, rt, sf, Theme.Text2, Theme.Subtle, Color.Empty) + Dpi.S(10);
                }
                for (int i = 0; i < 2; i++)
                {
                    int d = RoundPhaseDevices[i];
                    bool current = running && i == RoundPhasePos;
                    bool done = RoundPhaseCounts[i] >= Target || Mode == StageMode.Completed || (running && i < RoundPhasePos);
                    string text = (i + 1) + ". " + AppInfo.Name(d) + "  " + Math.Min(RoundPhaseCounts[i], Target) + "/" + Target;
                    Color fore = current ? Color.White : done ? Theme.DeviceColor[d] : Theme.Muted;
                    Color back = current ? Theme.DeviceColor[d] : done ? Theme.DeviceSoft[d] : Theme.Subtle;
                    string label = done && !current ? text + "  ✓" : text;
                    x += Draw.Pill(g, x, y, stepH, label, sf, fore, back, Color.Empty);
                    if (i == 0)
                    {
                        Draw.Text(g, "→", Theme.Semi(11f), new Rectangle(x + Dpi.S(4), y, Dpi.S(20), stepH), Theme.Muted, Draw.Center);
                        x += Dpi.S(28);
                    }
                }
            }
            // chỉ báo lắng nghe (góc phải trên); rút gọn chữ nếu không đủ chỗ cạnh các bước
            if (Listening)
            {
                Font lf = Theme.Semi(9f);
                string lt = Mode == StageMode.Identifying ? L.S("ĐANG CHỜ PHÍM NHẬN DIỆN", "WAITING FOR IDENTIFY KEY") : L.S("ĐANG LẮNG NGHE BÀN PHÍM", "LISTENING TO KEYBOARDS");
                if (Width - pad - Draw.Measure(lt, lf).Width - Dpi.S(30) < x + Dpi.S(12)) lt = L.S("ĐANG LẮNG NGHE", "LISTENING");
                int lw = Draw.Measure(lt, lf).Width;
                int cx = Width - pad - lw - Dpi.S(16);
                int cy = y + stepH / 2;
                float ring = pulse;
                int rr = Dpi.S(5) + (int)(Dpi.S(7) * ring);
                using (SolidBrush rb = new SolidBrush(Color.FromArgb((int)(90 * (1 - ring)), Theme.Accent)))
                    g.FillEllipse(rb, cx - rr, cy - rr, rr * 2, rr * 2);
                Draw.Dot(g, cx, cy, Dpi.S(5), Theme.Accent);
                Draw.Text(g, lt, lf, new Rectangle(cx + Dpi.S(14), y, lw + Dpi.S(4), stepH), Theme.AccentHover, Draw.Left);
            }
            g.SmoothingMode = SmoothingMode.Default;

            // Khối giữa: tiêu đề lớn + bộ đếm
            int midTop = y + stepH + Dpi.S(12);
            int barH = Dpi.S(10);
            int bottomRow = Dpi.S(24);
            int barY = Height - pad - bottomRow - Dpi.S(10) - barH;
            int midH = Math.Max(Dpi.S(40), barY - Dpi.S(10) - midTop);
            bool compact = Width < Dpi.S(640);

            Font counterBig = Theme.Bold(compact ? 30f : 40f);
            Font counterSmall = Theme.Semi(compact ? 14f : 18f);
            int counterW = 0;
            if (running || Mode == StageMode.Completed || Mode == StageMode.Stopped)
            {
                string big = Count.ToString();
                string small = "/ " + Target;
                string caption = running ? L.S("lần nhấn đã ghi nhận", "presses recorded") : L.S("lần nhấn (giai đoạn cuối)", "presses (last phase)");
                Font capF = Theme.F(8.5f);
                Size cs = Draw.Measure(caption, capF);
                Size bs = Draw.Measure(big, counterBig);
                foreach (float size in new float[] { 40f, 34f, 28f, 24f, 20f })
                {
                    // chọn cỡ chữ để số đếm + chú thích luôn nằm gọn phía trên thanh tiến độ
                    if (compact && size > 30f) continue;
                    counterBig = Theme.Bold(size);
                    counterSmall = Theme.Semi(Math.Max(11f, size * 0.45f));
                    bs = Draw.Measure(big, counterBig);
                    if (bs.Height + cs.Height - Dpi.S(2) <= midH) break;
                }
                Size ss = Draw.Measure(small, counterSmall);
                int numW = bs.Width + Dpi.S(6) + ss.Width;
                counterW = Math.Max(numW, cs.Width) + Dpi.S(4);
                int cxr = Width - pad - counterW + (counterW - numW) / 2;
                int blockC = bs.Height + cs.Height;
                int cyb = midTop + Math.Max(0, (midH - blockC) / 2);
                Color bc = running ? Theme.Blend(dc, Color.Black, 0.15f * flash) : Theme.Text;
                if (flash > 0 && running)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Draw.FillRound(g, new Rectangle(Width - pad - counterW - Dpi.S(10), cyb - Dpi.S(2), counterW + Dpi.S(20), blockC + Dpi.S(6)), Dpi.S(10),
                        Theme.Blend(Theme.Card, dc, 0.12f * flash));
                    g.SmoothingMode = SmoothingMode.Default;
                }
                Draw.Text(g, big, counterBig, new Rectangle(cxr, cyb, bs.Width + Dpi.S(2), bs.Height), bc, Draw.Left);
                Draw.Text(g, small, counterSmall, new Rectangle(cxr + bs.Width + Dpi.S(6), cyb + bs.Height - ss.Height - Dpi.S(6), ss.Width + Dpi.S(2), ss.Height), Theme.Muted, Draw.Left);
                Draw.Text(g, caption, capF, new Rectangle(Width - pad - counterW, cyb + bs.Height - Dpi.S(2), counterW, cs.Height + Dpi.S(2)), Theme.Muted, Draw.Center);
            }

            int textW = Math.Max(Dpi.S(60), Width - pad * 2 - (counterW > 0 ? counterW + Dpi.S(24) : 0));
            Font tf = null;
            Size tsz = Size.Empty;
            foreach (float size in new float[] { 24f, 20f, 16f, 13f })
            {
                tf = Theme.Bold(size);
                tsz = Draw.Measure(Title, tf);
                if (tsz.Width <= textW) break;
            }
            Font subF = Theme.F(compact ? 9.5f : 10.5f);
            int subMax = Math.Max(Draw.Measure("Ag", subF).Height, midH - tsz.Height - Dpi.S(4));
            int subH = Math.Min(subMax, Draw.MeasureHeight(Subtitle, subF, textW));
            int blockH = tsz.Height + Dpi.S(4) + subH;
            int ty = midTop + Math.Max(0, (midH - blockH) / 2);
            Draw.Text(g, Title, tf, new Rectangle(pad, ty, textW, tsz.Height), title, Draw.Left);
            Draw.Text(g, Subtitle, subF, new Rectangle(pad, ty + tsz.Height + Dpi.S(4), textW, subH), Theme.Text2, Draw.Wrap | TextFormatFlags.EndEllipsis);

            // progress
            Rectangle bar = new Rectangle(pad, barY, Width - pad * 2, barH);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, bar, barH / 2, Theme.Divider);
            if (Target > 0 && (running || Mode == StageMode.Completed || Mode == StageMode.Stopped))
            {
                float frac = Math.Min(1f, Count / (float)Target);
                int w = (int)(bar.Width * frac);
                if (w > 0) Draw.FillRound(g, new Rectangle(bar.X, bar.Y, Math.Max(w, barH), barH), barH / 2,
                    Mode == StageMode.Completed ? Theme.Accent : Mode == StageMode.Stopped ? Theme.Muted : dc);
            }
            g.SmoothingMode = SmoothingMode.Default;

            // dòng dưới: lần nhấn gần nhất + thông báo
            int by = Height - pad - bottomRow;
            Font bf = Theme.F(9.5f);
            int lpW = 0;
            if (!String.IsNullOrEmpty(LastPress))
            {
                lpW = Draw.Measure(LastPress, Theme.Semi(9.5f)).Width + Dpi.S(4);
                Draw.Text(g, LastPress, Theme.Semi(9.5f), new Rectangle(Width - pad - lpW, by, lpW, bottomRow), Theme.Text2, Draw.Right);
            }
            if (!String.IsNullOrEmpty(Notice))
            {
                Draw.Text(g, Notice, bf, new Rectangle(pad, by, Width - pad * 2 - lpW - Dpi.S(12), bottomRow), NoticeColor, Draw.Left);
            }
        }
    }

    /// <summary>Side-by-side metric table for keyboard A and B with visual bars.</summary>
    public class ComparisonView : Control
    {
        public Analysis Data;
        public bool ShowAdvanced;

        private sealed class Row
        {
            public string Name, Hint;
            public string[] Values = new string[2];
            public string[] Extra = new string[2];
            public double[] Bar = new double[] { double.NaN, double.NaN };
        }

        public ComparisonView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        private List<Row> BuildRows()
        {
            List<Row> rows = new List<Row>();
            DeviceResult[] d = Data != null ? Data.Devices : null;
            Func<int, DeviceResult> R = i => d == null ? null : d[i];

            Row r = new Row(); r.Name = L.S("Số lần nhấn", "Presses"); r.Hint = L.S("hợp lệ / mục tiêu", "valid / target");
            for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null ? Fmt.Dash : R(i).Presses + "/" + R(i).Target;
            rows.Add(r);

            r = new Row(); r.Name = L.S("Thời gian một lượt", "Time per run"); r.Hint = L.S("từ lần nhấn đầu đến lần nhấn cuối", "first press to last press");
            for (int i = 0; i < 2; i++)
            {
                r.Values[i] = R(i) == null ? Fmt.Dash : Fmt.Duration(R(i).PhaseDuration);
                if (R(i) != null && R(i).PhaseDurationIsPartial) r.Extra[i] = L.S("chưa đủ lượt", "incomplete");
                else if (R(i) != null && R(i).CompletedPhaseDurations.Count > 1) r.Extra[i] = L.S("TB ", "avg of ") + R(i).CompletedPhaseDurations.Count + L.S(" vòng", " rounds");
            }
            rows.Add(r);

            AddMs(rows, L.S("Khoảng cách trung bình", "Average interval"), L.S("thấp hơn = nhịp nhanh hơn", "lower = faster tapping"), s => s.Mean);
            AddMs(rows, L.S("Trung vị", "Median"), L.S("nhịp điển hình (giá trị ở giữa)", "typical rhythm (middle value)"), s => s.Median);
            AddMs(rows, L.S("Khoảng ngắn nhất", "Shortest interval"), L.S("hai lần nhấn sát nhau nhất", "two closest presses"), s => s.Min);
            AddMs(rows, L.S("Khoảng dài nhất", "Longest interval"), L.S("hai lần nhấn cách xa nhau nhất", "two furthest-apart presses"), s => s.Max);

            r = new Row(); r.Name = L.S("Độ ổn định", "Consistency"); r.Hint = L.S("độ lệch chuẩn · thấp hơn = đều hơn", "std. deviation · lower = steadier");
            for (int i = 0; i < 2; i++)
            {
                if (R(i) == null || double.IsNaN(R(i).Stats.StdDev)) { r.Values[i] = Fmt.Dash; continue; }
                r.Values[i] = "±" + Fmt.Ms(R(i).Stats.StdDev);
                r.Extra[i] = R(i).StabilityLabel;
                r.Bar[i] = R(i).Stats.StdDev;
            }
            rows.Add(r);

            if (ShowAdvanced)
            {
                r = new Row(); r.Name = L.S("Số khoảng thời gian", "Intervals"); r.Hint = L.S("N lần nhấn tạo ra N − 1 khoảng", "N presses give N − 1 intervals");
                for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null ? Fmt.Dash : R(i).Stats.Count.ToString();
                rows.Add(r);

                r = new Row(); r.Name = L.S("Tốc độ nhịp", "Tap rate"); r.Hint = L.S("lần nhấn mỗi giây · cao hơn = nhanh hơn", "presses per second · higher = faster");
                for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null ? Fmt.Dash : Fmt.Num(R(i).Stats.PressesPerSecond, 2) + L.S(" lần/s", " /s");
                rows.Add(r);

                r = new Row(); r.Name = L.S("Hệ số biến thiên", "Coefficient of variation"); r.Hint = L.S("độ lệch chuẩn ÷ trung bình", "std. deviation ÷ mean");
                for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null ? Fmt.Dash : Fmt.Percent(R(i).Stats.Cv);
                rows.Add(r);

                r = new Row(); r.Name = "P95"; r.Hint = L.S("95% khoảng cách nhỏ hơn giá trị này", "95% of intervals are below this");
                for (int i = 0; i < 2; i++)
                {
                    if (R(i) == null) { r.Values[i] = Fmt.Dash; continue; }
                    if (R(i).Stats.Count < Analyzer.MinIntervalsForP95) { r.Values[i] = Fmt.Dash; r.Extra[i] = L.S("cần ≥ ", "needs ≥ ") + Analyzer.MinIntervalsForP95 + L.S(" khoảng", " intervals"); }
                    else { r.Values[i] = Fmt.Ms(R(i).Stats.P95); r.Bar[i] = R(i).Stats.P95; }
                }
                rows.Add(r);

                r = new Row(); r.Name = L.S("Khoảng nghỉ dài", "Long pauses"); r.Hint = L.S("> 3 × trung vị và > trung vị + 250 ms", "> 3 × median and > median + 250 ms");
                for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null || R(i).Stats.Count < 3 ? Fmt.Dash : R(i).LongPauses.ToString();
                rows.Add(r);

                r = new Row(); r.Name = L.S("Nhấn khi chưa đến lượt", "Out-of-turn presses"); r.Hint = L.S("đã bỏ qua, không tính vào kết quả", "ignored, not counted");
                for (int i = 0; i < 2; i++) r.Values[i] = R(i) == null ? Fmt.Dash : R(i).WrongDevicePresses.ToString();
                rows.Add(r);
            }
            return rows;
        }

        private void AddMs(List<Row> rows, string name, string hint, Func<IntervalStats, double> pick)
        {
            Row r = new Row(); r.Name = name; r.Hint = hint;
            for (int i = 0; i < 2; i++)
            {
                DeviceResult dr = Data == null ? null : Data.Devices[i];
                double v = dr == null ? double.NaN : pick(dr.Stats);
                r.Values[i] = Fmt.Ms(v);
                r.Bar[i] = v;
            }
            rows.Add(r);
        }

        private int RowHeight { get { return Dpi.S(39); } }
        private int HeaderHeight { get { return Dpi.S(30); } }

        public int PreferredHeight() { return HeaderHeight + BuildRows().Count * RowHeight + Dpi.S(4); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            List<Row> rows = BuildRows();
            int nameW = Math.Max(Dpi.S(185), (int)(Width * 0.38));
            int colW = (Width - nameW) / 2;
            Font hf = Theme.Semi(8.5f);
            Draw.Text(g, L.S("CHỈ SỐ  ·  thấp hơn được tô màu", "METRIC  ·  lower value is coloured"), hf, new Rectangle(0, 0, nameW, HeaderHeight), Theme.Muted, Draw.Left);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 2; i++)
            {
                int cx = nameW + colW * i + Dpi.S(12);
                Draw.Dot(g, cx + Dpi.S(4), HeaderHeight / 2, Dpi.S(4), Theme.DeviceColor[i]);
                Draw.Text(g, AppInfo.Name(i).ToUpperInvariant(), Theme.Semi(9f), new Rectangle(cx + Dpi.S(14), 0, colW - Dpi.S(26), HeaderHeight), Theme.Text, Draw.Left);
            }
            g.SmoothingMode = SmoothingMode.Default;

            Font nf = Theme.Semi(9.5f), hintF = Theme.F(8f), vf = Theme.Semi(10.5f), ef = Theme.F(7.5f);
            int y = HeaderHeight;
            using (Pen div = new Pen(Theme.Divider))
            {
                g.DrawLine(div, 0, y - 1, Width, y - 1);
                foreach (Row r in rows)
                {
                    int rh = RowHeight;
                    Draw.Text(g, r.Name, nf, new Rectangle(0, y + Dpi.S(3), nameW - Dpi.S(8), Dpi.S(19)), Theme.Text, Draw.Left);
                    Draw.Text(g, r.Hint, hintF, new Rectangle(0, y + Dpi.S(21), nameW - Dpi.S(8), Dpi.S(15)), Theme.Muted, Draw.Left);

                    double max = 0;
                    for (int i = 0; i < 2; i++) if (!double.IsNaN(r.Bar[i])) max = Math.Max(max, r.Bar[i]);
                    bool bothNum = !double.IsNaN(r.Bar[0]) && !double.IsNaN(r.Bar[1]);
                    for (int i = 0; i < 2; i++)
                    {
                        int cx = nameW + colW * i + Dpi.S(12);
                        int cw = colW - Dpi.S(24);
                        bool lower = bothNum && r.Bar[i] < r.Bar[1 - i];
                        Size vs = Draw.Measure(r.Values[i], vf);
                        Draw.Text(g, r.Values[i], vf, new Rectangle(cx, y + Dpi.S(2), Math.Min(cw, vs.Width + Dpi.S(4)), Dpi.S(22)), lower ? Theme.DeviceColor[i] : Theme.Text, Draw.Left);
                        if (!String.IsNullOrEmpty(r.Extra[i]))
                            Draw.Text(g, r.Extra[i], ef, new Rectangle(cx + vs.Width + Dpi.S(8), y + Dpi.S(2), Math.Max(0, cw - vs.Width - Dpi.S(8)), Dpi.S(22)), Theme.Muted, Draw.Left);
                        if (!double.IsNaN(r.Bar[i]) && max > 0)
                        {
                            Rectangle track = new Rectangle(cx, y + Dpi.S(26), cw, Dpi.S(5));
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            Draw.FillRound(g, track, Dpi.S(3), Theme.Divider);
                            int w = Math.Max(Dpi.S(6), (int)(track.Width * (r.Bar[i] / max)));
                            Draw.FillRound(g, new Rectangle(track.X, track.Y, w, track.Height), Dpi.S(3), Theme.DeviceColor[i]);
                            g.SmoothingMode = SmoothingMode.Default;
                        }
                    }
                    y += rh;
                    g.DrawLine(div, 0, y - 1, Width, y - 1);
                }
            }
        }
    }

    /// <summary>Khung kết luận: câu kết luận trung thực + chi tiết + chú thích phương pháp.</summary>
    public class ConclusionView : Control
    {
        public Analysis Data;
        public string StatusLine = "";

        public ConclusionView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        private Font HeadFont { get { return Theme.Semi(11f); } }
        private Font BodyFont { get { return Theme.F(9.5f); } }
        private Font NoteFont { get { return Theme.F(8.5f); } }

        // Khi đã có kết luận cuối cùng (dải phía dưới), khung này chỉ giữ phần ghi chú để không lặp lại và đủ chỗ hiển thị.
        private bool VerdictShown { get { return Data != null && Data.Verdict != null && Data.Verdict.Ready; } }

        private List<string> VisibleDetails()
        {
            List<string> list = new List<string>();
            if (Data == null) return list;
            foreach (string d in Data.Details)
                if (!VerdictShown || !(d.StartsWith("Người dùng thực hiện nhịp nhấn", StringComparison.Ordinal) || d.StartsWith("You tapped faster", StringComparison.Ordinal))) list.Add(d);
            return list;
        }

        public int PreferredHeight(int width)
        {
            int pad = Dpi.S(16);
            int inner = width - pad * 2;
            int h = pad + Dpi.S(20);
            if (Data != null)
            {
                if (!VerdictShown) h += Draw.MeasureHeight(Data.Headline, HeadFont, inner) + Dpi.S(10);
                foreach (string d in VisibleDetails()) h += Draw.MeasureHeight(d, BodyFont, inner - Dpi.S(14)) + Dpi.S(6);
            }
            h += Dpi.S(10) + Draw.MeasureHeight(AppInfo.MethodNote, NoteFont, inner - Dpi.S(24)) + Dpi.S(20) + pad;
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Card;
            g.Clear(parentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), Dpi.S(10), Theme.Subtle);
            g.SmoothingMode = SmoothingMode.Default;
            int pad = Dpi.S(16);
            int inner = Width - pad * 2;
            int y = pad;
            Draw.Text(g, (VerdictShown ? L.S("GHI CHÚ", "NOTES") : L.S("KẾT LUẬN", "CONCLUSION")) + (String.IsNullOrEmpty(StatusLine) ? "" : "  ·  " + StatusLine), Theme.Semi(8.5f), new Rectangle(pad, y, inner, Dpi.S(18)), Theme.Muted, Draw.Left);
            y += Dpi.S(24);
            if (Data != null)
            {
                int hh = Draw.MeasureHeight(Data.Headline, HeadFont, inner);
                Color hc = Data.LowerMeanDevice >= 0 ? Theme.DeviceColor[Data.LowerMeanDevice] : Theme.Text;
                if (!Data.Comparable) hc = Theme.Text2;
                if (VerdictShown) hh = -Dpi.S(10);
                else Draw.Text(g, Data.Headline, HeadFont, new Rectangle(pad, y, inner, hh + Dpi.S(2)), hc, Draw.Wrap);
                y += hh + Dpi.S(10);
                foreach (string d in VisibleDetails())
                {
                    int dh = Draw.MeasureHeight(d, BodyFont, inner - Dpi.S(14));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    Draw.Dot(g, pad + Dpi.S(3), y + Dpi.S(9), Dpi.S(2), Theme.Muted);
                    g.SmoothingMode = SmoothingMode.Default;
                    Draw.Text(g, d, BodyFont, new Rectangle(pad + Dpi.S(14), y, inner - Dpi.S(14), dh + Dpi.S(2)), Theme.Text2, Draw.Wrap);
                    y += dh + Dpi.S(6);
                }
            }
            y += Dpi.S(6);
            int nh = Draw.MeasureHeight(AppInfo.MethodNote, NoteFont, inner - Dpi.S(24));
            Rectangle note = new Rectangle(pad, y, inner, nh + Dpi.S(16));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, note, Dpi.S(8), Theme.WarnSoft);
            g.SmoothingMode = SmoothingMode.Default;
            Draw.Text(g, AppInfo.MethodNote, NoteFont, new Rectangle(note.X + Dpi.S(12), note.Y + Dpi.S(8), note.Width - Dpi.S(24), nh + Dpi.S(2)), Theme.Warn, Draw.Wrap);
        }
    }


    /// <summary>"Final verdict" band: which keyboard you tapped faster on, by how much, how sure, criteria won.</summary>
    public class VerdictView : Control
    {
        public Verdict Data;

        public VerdictView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        private bool Wide { get { return Width >= Dpi.S(700); } }

        public int PreferredHeight(int width)
        {
            if (Data == null || !Data.Ready) return Dpi.S(64);
            return width >= Dpi.S(700) ? Dpi.S(130) : Dpi.S(290);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Card;
            g.Clear(parentBack);
            Verdict v = Data;
            bool ready = v != null && v.Ready;
            Color accent = ready && v.Winner >= 0 ? Theme.DeviceColor[v.Winner] : (ready ? Theme.Text2 : Theme.Muted);
            Color soft = ready && v.Winner >= 0 ? Theme.DeviceSoft[v.Winner] : Theme.Subtle;
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw.FillRound(g, box, Dpi.S(10), soft);
            Draw.StrokeRound(g, box, Dpi.S(10), ready ? Theme.Blend(soft, accent, 0.35f) : Theme.Border, 1f);
            if (ready) Draw.FillRound(g, new Rectangle(0, 0, Dpi.S(6), Height - 1), Dpi.S(3), accent);
            g.SmoothingMode = SmoothingMode.Default;

            int pad = Dpi.S(18);
            if (!ready)
            {
                string t = v == null ? new Verdict().Placeholder : v.Placeholder;
                Draw.Text(g, L.S("KẾT LUẬN CUỐI CÙNG", "FINAL VERDICT"), Theme.Semi(8.5f), new Rectangle(pad, Dpi.S(10), Width - pad * 2, Dpi.S(18)), Theme.Muted, Draw.Left);
                Draw.Text(g, t, Theme.F(9.5f), new Rectangle(pad, Dpi.S(30), Width - pad * 2, Height - Dpi.S(34)), Theme.Text2, Draw.Wrap | TextFormatFlags.EndEllipsis);
                return;
            }

            int leftW = Wide ? (int)((Width - pad * 3) * 0.52) : Width - pad * 2;
            int x = pad + Dpi.S(4), y = Dpi.S(12);
            Draw.Text(g, L.S("KẾT LUẬN CUỐI CÙNG", "FINAL VERDICT"), Theme.Semi(8.5f), new Rectangle(x, y, leftW, Dpi.S(18)), Theme.Text2, Draw.Left);
            y += Dpi.S(20);
            Font hf = Theme.Bold(17f);
            if (Draw.Measure(v.Headline, hf).Width > leftW) hf = Theme.Bold(14f);
            int hh = Draw.Measure(v.Headline, hf).Height;
            Draw.Text(g, v.Headline, hf, new Rectangle(x, y, leftW, hh), accent, Draw.Left);
            y += hh + Dpi.S(2);
            Font sf = Theme.F(9f);
            int sh = Math.Min(Draw.MeasureHeight(v.Sub, sf, leftW), Dpi.S(34));
            Draw.Text(g, v.Sub, sf, new Rectangle(x, y, leftW, sh), Theme.Text, Draw.Wrap | TextFormatFlags.EndEllipsis);
            y += sh + Dpi.S(6);

            Color cc = v.Clear && v.Winner >= 0 ? Theme.AccentHover : Theme.Warn;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pw = Draw.Pill(g, x, y, Dpi.S(22), v.ConfidenceTitle, Theme.Semi(8.5f), cc, Theme.Blend(Theme.Card, cc, 0.14f), cc);
            g.SmoothingMode = SmoothingMode.Default;
            int ctY = y;
            int ctX = x + pw + Dpi.S(8);
            int ctW = leftW - pw - Dpi.S(8);
            if (ctW < Dpi.S(200)) { ctX = x; ctW = leftW; ctY = y + Dpi.S(26); }
            int ch = Wide ? Height - ctY - Dpi.S(8) : Dpi.S(64);
            Draw.Text(g, v.ConfidenceText, Theme.F(8f), new Rectangle(ctX, ctY + Dpi.S(3), ctW, Math.Max(0, ch)), Theme.Text2, Draw.Wrap | TextFormatFlags.EndEllipsis);

            // Bên phải (hoặc phía dưới): các tiêu chí
            int rx = Wide ? pad + leftW + pad + Dpi.S(4) : x;
            int ry = Wide ? Dpi.S(12) : ctY + Dpi.S(26) + Dpi.S(64);
            int rw = Wide ? Width - rx - pad : leftW;
            if (Wide)
                using (Pen p = new Pen(Theme.Blend(soft, accent, 0.25f))) g.DrawLine(p, rx - pad / 2 - Dpi.S(2), Dpi.S(14), rx - pad / 2 - Dpi.S(2), Height - Dpi.S(14));
            string tally = v.Winner >= 0 ? L.S(AppInfo.Name(v.Winner) + " hơn ở " + v.Wins + "/" + v.Criteria.Count + " tiêu chí", AppInfo.Name(v.Winner) + " ahead in " + v.Wins + "/" + v.Criteria.Count + " criteria")
                                         : L.S("So sánh theo từng tiêu chí", "Criterion by criterion");
            Draw.Text(g, tally.ToUpperInvariant(), Theme.Semi(8.5f), new Rectangle(rx, ry, rw, Dpi.S(18)), Theme.Text2, Draw.Left);
            ry += Dpi.S(21);
            int rowH = Dpi.S(31);
            foreach (VerdictCriterion c in v.Criteria)
            {
                Color wc = c.Winner >= 0 ? Theme.DeviceColor[c.Winner] : Theme.Muted;
                string tag = c.Winner >= 0 ? AppInfo.Name(c.Winner) : L.S("Ngang", "Even");
                Font tf = Theme.Semi(8.5f);
                int tagW = Math.Max(Dpi.S(70), Draw.Measure(tag, tf).Width + Dpi.S(20));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Draw.FillRound(g, new Rectangle(rx, ry + Dpi.S(4), tagW, Dpi.S(22)), Dpi.S(11), c.Winner >= 0 ? Theme.Blend(Theme.Card, wc, 0.15f) : Theme.Card);
                g.SmoothingMode = SmoothingMode.Default;
                Draw.Text(g, tag, tf, new Rectangle(rx, ry + Dpi.S(4), tagW, Dpi.S(22)), wc, Draw.Center);
                int tx = rx + tagW + Dpi.S(10);
                Draw.Text(g, c.Name, Theme.Semi(9f), new Rectangle(tx, ry, rw - tagW - Dpi.S(10), Dpi.S(16)), Theme.Text, Draw.Left);
                Draw.Text(g, c.Detail, Theme.F(8f), new Rectangle(tx, ry + Dpi.S(16), rw - tagW - Dpi.S(10), Dpi.S(15)), Theme.Text2, Draw.Left);
                ry += rowH;
            }
        }
    }
    /// <summary>Biểu đồ đường: khoảng cách giữa các lần nhấn liên tiếp của từng bàn phím.</summary>
    public class IntervalChart : Control
    {
        public TestSession Session;

        public IntervalChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            List<double>[] series = new List<double>[] { new List<double>(), new List<double>() };
            if (Session != null)
                foreach (PressRecord p in Session.Presses)
                    if (!double.IsNaN(p.IntervalMs)) series[p.Device].Add(p.IntervalMs);

            int left = Dpi.S(64), right = Dpi.S(16), top = Dpi.S(36), bottom = Dpi.S(40);
            Rectangle plot = new Rectangle(left, top, Width - left - right, Height - top - bottom);
            Font small = Theme.F(8.5f);

            // chú giải
            int lx = left;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < 2; i++)
            {
                using (Pen p = new Pen(Theme.DeviceColor[i], Dpi.Sf(2.5f))) g.DrawLine(p, lx, Dpi.S(14), lx + Dpi.S(18), Dpi.S(14));
                string t = AppInfo.Name(i) + " (" + series[i].Count + L.S(" khoảng)", " intervals)");
                Size ts = Draw.Measure(t, Theme.Semi(9f));
                Draw.Text(g, t, Theme.Semi(9f), new Rectangle(lx + Dpi.S(24), Dpi.S(4), ts.Width + Dpi.S(4), Dpi.S(20)), Theme.Text, Draw.Left);
                lx += Dpi.S(24) + ts.Width + Dpi.S(24);
            }
            using (Pen dp = new Pen(Theme.Muted, 1f))
            {
                dp.DashStyle = DashStyle.Dash;
                g.DrawLine(dp, lx, Dpi.S(14), lx + Dpi.S(18), Dpi.S(14));
            }
            Draw.Text(g, L.S("đường nét đứt = trung bình", "dashed line = average"), small, new Rectangle(lx + Dpi.S(24), Dpi.S(4), Dpi.S(220), Dpi.S(20)), Theme.Muted, Draw.Left);
            g.SmoothingMode = SmoothingMode.Default;

            if (plot.Width < Dpi.S(60) || plot.Height < Dpi.S(40)) return;
            int n = Math.Max(series[0].Count, series[1].Count);
            if (n == 0)
            {
                Draw.Text(g, L.S("Chưa có dữ liệu. Biểu đồ hiển thị khoảng cách giữa các lần nhấn liên tiếp sau khi bắt đầu test.",
                                 "No data yet. The chart shows the interval between consecutive presses once a test starts."),
                    Theme.F(9.5f), plot, Theme.Muted, Draw.Center);
                return;
            }

            List<double> all = series[0].Concat(series[1]).OrderBy(v => v).ToList();
            double maxV = all[all.Count - 1];
            double med = IntervalStats.Percentile(all, 0.5);
            double yMax = maxV;
            if (all.Count >= 4 && maxV > med * 3) yMax = Math.Max(IntervalStats.Percentile(all, 0.9) * 1.3, med * 2.5); // giữ biểu đồ dễ đọc khi có khoảng nghỉ rất dài
            yMax = NiceCeil(yMax * 1.08);
            if (yMax <= 0) yMax = 1;

            using (Pen grid = new Pen(Theme.Divider))
            {
                for (int k = 0; k <= 4; k++)
                {
                    double v = yMax * k / 4.0;
                    int yy = plot.Bottom - (int)(plot.Height * k / 4.0);
                    g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                    Draw.Text(g, Fmt.Num(v, 0) + " ms", small, new Rectangle(0, yy - Dpi.S(9), left - Dpi.S(8), Dpi.S(18)), Theme.Muted, Draw.Right);
                }
            }
            Func<int, int> X = i => n == 1 ? plot.Left + plot.Width / 2 : plot.Left + (int)((double)plot.Width * i / (n - 1));
            Func<double, int> Y = v => plot.Bottom - (int)(plot.Height * Math.Min(v, yMax) / yMax);
            int step = Math.Max(1, (int)Math.Ceiling(n / 12.0));
            for (int i = 0; i < n; i += step)
                Draw.Text(g, (i + 1).ToString(), small, new Rectangle(X(i) - Dpi.S(15), plot.Bottom + Dpi.S(4), Dpi.S(30), Dpi.S(16)), Theme.Muted, Draw.Center);
            Draw.Text(g, L.S("Khoảng thứ (giữa lần nhấn k và k+1, nối tiếp các vòng)", "Interval # (between press k and k+1, rounds concatenated)"), small, new Rectangle(plot.Left, plot.Bottom + Dpi.S(20), plot.Width, Dpi.S(18)), Theme.Muted, Draw.Center);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int d = 0; d < 2; d++)
            {
                List<double> s = series[d];
                if (s.Count == 0) continue;
                Color c = Theme.DeviceColor[d];
                double mean = s.Average();
                using (Pen mp = new Pen(Color.FromArgb(150, c), 1.2f))
                {
                    mp.DashStyle = DashStyle.Dash;
                    g.DrawLine(mp, plot.Left, Y(mean), plot.Right, Y(mean));
                }
                if (s.Count > 1)
                {
                    Point[] pts = new Point[s.Count];
                    for (int i = 0; i < s.Count; i++) pts[i] = new Point(X(i), Y(s[i]));
                    using (Pen lp = new Pen(c, Dpi.Sf(2f))) { lp.LineJoin = LineJoin.Round; g.DrawLines(lp, pts); }
                }
                for (int i = 0; i < s.Count; i++)
                {
                    int px = X(i), py = Y(s[i]);
                    using (SolidBrush wb = new SolidBrush(Color.White)) g.FillEllipse(wb, px - Dpi.S(4), py - Dpi.S(4), Dpi.S(8), Dpi.S(8));
                    using (Pen pp = new Pen(c, Dpi.Sf(2f))) g.DrawEllipse(pp, px - Dpi.S(4), py - Dpi.S(4), Dpi.S(8), Dpi.S(8));
                    if (s[i] > yMax)
                    {
                        string t = "▲ " + Fmt.Num(s[i], 0) + " ms";
                        Size ts = Draw.Measure(t, small);
                        
                        Draw.Text(g, t, small, new Rectangle(Math.Min(px + Dpi.S(8), plot.Right - ts.Width - Dpi.S(4)), py + Dpi.S(2) + d * Dpi.S(14), ts.Width + Dpi.S(4), Dpi.S(16)), c, Draw.Left);
                    }
                }
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        private static double NiceCeil(double v)
        {
            if (v <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double[] steps = new double[] { 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10 };
            foreach (double s in steps) if (s * mag >= v) return s * mag;
            return 10 * mag;
        }
    }
}
