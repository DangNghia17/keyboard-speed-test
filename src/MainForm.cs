// Keyboard Speed Test - main window, language picker, rename dialog.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KeyboardSpeedTest
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Run(args);
        }

        public static void Run() { Run(new string[0]); }

        /// <summary>Arguments: --lang en | --lang vi skips the language picker.</summary>
        public static void Run(string[] args)
        {
            RunInitOnly();
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(L.S("Đã xảy ra lỗi không mong muốn:", "An unexpected error occurred:") + "\r\n\r\n" + e.Exception,
                    "Keyboard Speed Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Language? forced = null;
            for (int i = 0; args != null && i < args.Length - 1; i++)
            {
                if (args[i] == "--lang" || args[i] == "-lang")
                {
                    string v = args[i + 1].ToLowerInvariant();
                    if (v.StartsWith("vi")) forced = Language.Vietnamese;
                    else if (v.StartsWith("en")) forced = Language.English;
                }
            }
            if (forced.HasValue) L.Current = forced.Value;
            else
            {
                Language? chosen = LanguageDialog.Ask();
                if (!chosen.HasValue) return; // window closed without choosing
                L.Current = chosen.Value;
            }
            L.Save(L.Current);
            using (MainForm form = new MainForm()) Application.Run(form);
        }

        /// <summary>DPI awareness, number format, visual styles. Must run before any window is created.</summary>
        public static void RunInitOnly()
        {
            try { NativeMethods.SetProcessDPIAware(); } catch { }
            Dpi.Init();
            Fmt.Culture = CultureInfo.CurrentCulture;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
            }
            catch (InvalidOperationException) { }
        }
    }

    /// <summary>Small start-up window: choose Tiếng Việt or English.</summary>
    public sealed class LanguageDialog : Form
    {
        private Language? result;

        public static Language? Ask()
        {
            Language? saved = L.LoadSaved();
            Language def = saved.HasValue ? saved.Value
                : (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi" || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "vi"
                    ? Language.Vietnamese : Language.English);
            using (LanguageDialog d = new LanguageDialog(def))
            {
                d.ShowDialog();
                return d.result;
            }
        }

        private LanguageDialog(Language def)
        {
            Text = "Keyboard Speed Test";
            Font = Theme.F(10f);
            BackColor = Theme.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.S(420), Dpi.S(220));
            KeyPreview = true;

            Label title = new Label();
            title.Text = "KEYBOARD SPEED TEST";
            title.Font = Theme.Bold(14f);
            title.ForeColor = Theme.Text;
            title.SetBounds(Dpi.S(24), Dpi.S(20), Dpi.S(372), Dpi.S(30));
            Label sub = new Label();
            sub.Text = "Chọn ngôn ngữ  ·  Choose your language";
            sub.Font = Theme.F(10f);
            sub.ForeColor = Theme.Text2;
            sub.SetBounds(Dpi.S(24), Dpi.S(54), Dpi.S(372), Dpi.S(24));
            Controls.Add(title);
            Controls.Add(sub);

            ModernButton vi = new ModernButton();
            vi.Text = "Tiếng Việt";
            ModernButton en = new ModernButton();
            en.Text = "English";
            ModernButton[] buttons = new ModernButton[] { vi, en };
            for (int i = 0; i < 2; i++)
            {
                ModernButton b = buttons[i];
                b.Font = Theme.Bold(11f);
                b.Kind = (i == 0) == (def == Language.Vietnamese) ? ButtonKind.Primary : ButtonKind.Secondary;
                b.SetBounds(Dpi.S(24) + i * Dpi.S(192), Dpi.S(98), Dpi.S(180), Dpi.S(56));
                Controls.Add(b);
            }
            vi.Click += delegate { result = Language.Vietnamese; Close(); };
            en.Click += delegate { result = Language.English; Close(); };

            Label hint = new Label();
            hint.Text = "Enter = " + (def == Language.Vietnamese ? "Tiếng Việt" : "English");
            hint.Font = Theme.F(8.5f);
            hint.ForeColor = Theme.Muted;
            hint.SetBounds(Dpi.S(24), Dpi.S(170), Dpi.S(372), Dpi.S(22));
            Controls.Add(hint);
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { result = def; Close(); }
                else if (e.KeyCode == Keys.V) { result = Language.Vietnamese; Close(); }
                else if (e.KeyCode == Keys.E) { result = Language.English; Close(); }
            };
        }
    }

    public sealed class MainForm : Form
    {
        // ---- Raw Input / data ----
        private readonly RawInputListener listener = new RawInputListener();
        private readonly ConcurrentQueue<RawKeyEvent> keyQueue = new ConcurrentQueue<RawKeyEvent>();
        private readonly ConcurrentQueue<KeyValuePair<IntPtr, bool>> deviceQueue = new ConcurrentQueue<KeyValuePair<IntPtr, bool>>();
        private int drainPending;
        private readonly KeyFilter keyFilter = new KeyFilter();
        private readonly IntPtr[] handles = new IntPtr[2];
        private readonly string[] paths = new string[] { "", "" };
        private readonly DeviceDescriptor[] descriptors = new DeviceDescriptor[2];
        private readonly bool[] connected = new bool[2];
        private int identifying = -1;
        private TestSession session;
        private Analysis analysis = Analyzer.Analyze(null);
        private bool exported = true;
        private bool resultsDirty;
        private int keyboardCount;

        // ---- UI ----
        private readonly HeaderBar header = new HeaderBar();
        private readonly Panel host = new Panel();
        private readonly Panel content = new Panel();
        private readonly DeviceCard[] cards = new DeviceCard[2];
        private readonly CardPanel settingsCard = new CardPanel();
        private readonly CardPanel guideCard = new CardPanel();
        private readonly Label guideText = new Label();
        private readonly CardPanel stageCard = new CardPanel();
        private readonly StageView stage = new StageView();
        private readonly ModernButton startButton = new ModernButton();
        private readonly ModernButton stopButton = new ModernButton();
        private readonly ModernButton resetButton = new ModernButton();
        private readonly CardPanel resultsCard = new CardPanel();
        private readonly TabStrip tabs = new TabStrip();
        private readonly ModernButton exportButton = new ModernButton();
        private readonly ModernButton advancedToggle = new ModernButton();
        private readonly Panel comparePage = new Panel();
        private readonly VScrollBar compareScroll = new VScrollBar();
        private readonly ComparisonView comparison = new ComparisonView();
        private readonly ConclusionView conclusion = new ConclusionView();
        private readonly VerdictView verdict = new VerdictView();
        private readonly Panel chartPage = new Panel();
        private readonly IntervalChart chart = new IntervalChart();
        private readonly Panel historyPage = new Panel();
        private readonly ModernButton[] filterButtons = new ModernButton[4];
        private readonly Label historyCount = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly List<EventRecord> gridRows = new List<EventRecord>();
        private int historyFilter; // 0 all, 1 keyboard A, 2 keyboard B, 3 ignored

        // settings
        private readonly Label settingsTitle = new Label();
        private readonly Label pressesLabel = new Label();
        private readonly Label orderLabel = new Label();
        private readonly Label roundsLabel = new Label();
        private readonly Label settingsSummary = new Label();
        private readonly ModernButton[] pressChips = new ModernButton[4];
        private static readonly int[] PressPresets = new int[] { 10, 20, 30, 50 };
        private readonly NumericUpDown pressesInput = new NumericUpDown();
        private readonly ModernButton orderAFirst = new ModernButton();
        private readonly ModernButton orderBFirst = new ModernButton();
        private readonly NumericUpDown roundsInput = new NumericUpDown();
        private readonly CheckBox alternateCheck = new CheckBox();
        private int firstDevice = AppInfo.A;

        private readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer();

        public MainForm()
        {
            Font = Theme.F(9f);
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None; // scaled manually with Dpi.S()
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            DoubleBuffered = true;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Dpi.S(1360), wa.Width - Dpi.S(20));
            int h = Math.Min(Dpi.S(880), wa.Height - Dpi.S(20));
            Size = new Size(w, h);
            MinimumSize = new Size(Math.Min(Dpi.S(900), wa.Width), Math.Min(Dpi.S(560), wa.Height));
            if (wa.Width < Dpi.S(1300) || wa.Height < Dpi.S(780)) WindowState = FormWindowState.Maximized;

            BuildUi();
            UpdateNamesUi();

            animTimer.Interval = 40; // animation only, unrelated to event capture
            animTimer.Tick += delegate { OnAnimTick(); };
            listener.KeyEvent += OnRawKey;
            listener.DeviceChanged += OnRawDeviceChanged;
        }

        // ================= Build UI =================

        private void BuildUi()
        {
            SuspendLayout();
            header.Dock = DockStyle.Top;
            header.Height = Dpi.S(64);

            host.Dock = DockStyle.Fill;
            host.AutoScroll = true;
            host.BackColor = Theme.Bg;
            host.Resize += delegate { LayoutContent(); };
            content.BackColor = Theme.Bg;
            content.Location = new Point(0, 0);
            host.Controls.Add(content);

            for (int d = 0; d < 2; d++)
            {
                DeviceCard card = new DeviceCard(d);
                int dev = d;
                card.IdentifyButton.Click += delegate { ToggleIdentify(dev); };
                card.RenameButton.Click += delegate { RenameDevice(dev); };
                card.DetailsButton.Click += delegate { ShowDetails(dev); };
                cards[d] = card;
                content.Controls.Add(card);
            }

            BuildSettings();
            content.Controls.Add(settingsCard);

            guideText.Text = L.S("1. Bấm Nhận diện ở thẻ A, nhấn J trên bàn phím thứ nhất; làm tương tự với thẻ B.\r\n" +
                                 "2. Chọn số lần nhấn, thứ tự rồi bấm BẮT ĐẦU TEST.\r\n" +
                                 "3. Nhấn J liên tục trên bàn phím đang test, không cần chờ.\r\n" +
                                 "4. Đủ số lần sẽ tự chuyển bàn phím và hiện kết quả.",
                                 "1. Click Identify on card A and press J on your first keyboard; repeat for card B.\r\n" +
                                 "2. Pick the press count and order, then click START TEST.\r\n" +
                                 "3. Tap J continuously on the keyboard being tested – no waiting.\r\n" +
                                 "4. When the count is reached it switches keyboards, then shows results.");
            guideText.Font = Theme.F(9f);
            guideText.ForeColor = Theme.Text2;
            guideText.BackColor = Theme.Card;
            Label guideTitle = MakeLabel(L.S("Cách thực hiện", "How it works"), Theme.Semi(10f), Theme.Text);
            guideTitle.Location = new Point(Dpi.S(16), Dpi.S(14));
            guideTitle.Size = new Size(Dpi.S(260), Dpi.S(22));
            guideCard.Controls.Add(guideTitle);
            guideCard.Controls.Add(guideText);
            guideCard.Resize += delegate { guideText.SetBounds(Dpi.S(16), Dpi.S(42), Math.Max(0, guideCard.Width - Dpi.S(32)), Math.Max(0, guideCard.Height - Dpi.S(54))); };
            content.Controls.Add(guideCard);

            stageCard.Padding = new Padding(0);
            stage.Location = new Point(1, 1);
            stageCard.Controls.Add(stage);
            startButton.Text = L.S("BẮT ĐẦU TEST", "START TEST");
            startButton.Kind = ButtonKind.Primary;
            startButton.Font = Theme.Bold(11f);
            startButton.Click += delegate { StartTest(); };
            stopButton.Text = L.S("Dừng", "Stop");
            stopButton.Kind = ButtonKind.Danger;
            stopButton.Click += delegate { StopTest(); };
            resetButton.Text = L.S("Làm mới kết quả", "Clear results");
            resetButton.Kind = ButtonKind.Secondary;
            resetButton.Click += delegate { ResetResults(); };
            stageCard.Controls.Add(startButton);
            stageCard.Controls.Add(stopButton);
            stageCard.Controls.Add(resetButton);
            stageCard.Resize += delegate { LayoutStage(); };
            content.Controls.Add(stageCard);

            BuildResults();
            content.Controls.Add(resultsCard);

            Controls.Add(host);
            Controls.Add(header);
            ResumeLayout(false);
        }

        private Label MakeLabel(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Theme.Card;
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.UseMnemonic = false;
            return l;
        }

        private void BuildSettings()
        {
            settingsTitle.Text = L.S("Cài đặt bài test", "Test settings");
            settingsTitle.Font = Theme.Semi(10f);
            pressesLabel.Text = L.S("Số lần nhấn mỗi bàn phím", "Presses per keyboard");
            orderLabel.Text = L.S("Thứ tự thử", "Order");
            roundsLabel.Text = L.S("Số vòng", "Rounds");
            foreach (Label l in new Label[] { settingsTitle, pressesLabel, orderLabel, roundsLabel, settingsSummary })
            {
                l.BackColor = Theme.Card; l.AutoSize = false; l.UseMnemonic = false; l.TextAlign = ContentAlignment.MiddleLeft;
                if (l != settingsTitle) { l.Font = Theme.Semi(8.5f); l.ForeColor = Theme.Text2; } else l.ForeColor = Theme.Text;
                settingsCard.Controls.Add(l);
            }
            settingsSummary.Font = Theme.F(8.5f);
            settingsSummary.ForeColor = Theme.Muted;

            for (int i = 0; i < 4; i++)
            {
                ModernButton b = new ModernButton();
                b.Kind = ButtonKind.Toggle;
                b.ToggleColor = Theme.Accent;
                b.Text = PressPresets[i].ToString();
                int v = PressPresets[i];
                b.Click += delegate { pressesInput.Value = v; };
                pressChips[i] = b;
                settingsCard.Controls.Add(b);
            }
            pressesInput.Minimum = 3;
            pressesInput.Maximum = 500;
            pressesInput.Value = 10;
            pressesInput.Font = Theme.F(10f);
            pressesInput.TextAlign = HorizontalAlignment.Center;
            pressesInput.BorderStyle = BorderStyle.FixedSingle;
            pressesInput.ValueChanged += delegate { UpdateSettingsUi(); };
            settingsCard.Controls.Add(pressesInput);

            orderAFirst.Kind = ButtonKind.Toggle; orderAFirst.ToggleColor = Theme.Accent;
            orderAFirst.Click += delegate { firstDevice = AppInfo.A; UpdateSettingsUi(); };
            orderBFirst.Kind = ButtonKind.Toggle; orderBFirst.ToggleColor = Theme.Accent;
            orderBFirst.Click += delegate { firstDevice = AppInfo.B; UpdateSettingsUi(); };
            orderAFirst.Font = Theme.Semi(9f);
            orderBFirst.Font = Theme.Semi(9f);
            settingsCard.Controls.Add(orderAFirst);
            settingsCard.Controls.Add(orderBFirst);

            roundsInput.Minimum = 1;
            roundsInput.Maximum = 10;
            roundsInput.Value = 1;
            roundsInput.Font = Theme.F(10f);
            roundsInput.TextAlign = HorizontalAlignment.Center;
            roundsInput.BorderStyle = BorderStyle.FixedSingle;
            roundsInput.ValueChanged += delegate { UpdateSettingsUi(); };
            settingsCard.Controls.Add(roundsInput);

            alternateCheck.Text = L.S("Đảo thứ tự giữa các vòng", "Alternate order each round");
            alternateCheck.Checked = true;
            alternateCheck.Font = Theme.F(9f);
            alternateCheck.ForeColor = Theme.Text2;
            alternateCheck.BackColor = Theme.Card;
            alternateCheck.UseMnemonic = false;
            alternateCheck.CheckedChanged += delegate { UpdateSettingsUi(); };
            settingsCard.Controls.Add(alternateCheck);

            settingsCard.Resize += delegate { LayoutSettings(); };
            UpdateSettingsUi();
        }

        private void BuildResults()
        {
            resultsCard.Padding = new Padding(0);
            tabs.Items.Add(L.S("So sánh kết quả", "Comparison"));
            tabs.Items.Add(L.S("Biểu đồ nhịp nhấn", "Rhythm chart"));
            tabs.Items.Add(L.S("Lịch sử nhấn phím (0)", "Press history (0)"));
            tabs.SelectedChanged += delegate { ShowPage(); };
            resultsCard.Controls.Add(tabs);

            exportButton.Text = L.S("↓  Xuất CSV", "↓  Export CSV");
            exportButton.Kind = ButtonKind.Secondary;
            exportButton.Font = Theme.Bold(9.5f);
            exportButton.Click += delegate { ExportCsv(); };
            resultsCard.Controls.Add(exportButton);

            advancedToggle.Text = L.S("Thống kê mở rộng", "Advanced stats");
            advancedToggle.Kind = ButtonKind.Toggle;
            advancedToggle.ToggleColor = Theme.Info;
            advancedToggle.Click += delegate { SetAdvanced(!advancedToggle.Checked); };
            resultsCard.Controls.Add(advancedToggle);

            comparePage.BackColor = Theme.Card;
            comparePage.Controls.Add(compareScroll);
            compareScroll.Visible = false;
            compareScroll.Scroll += delegate { LayoutComparePage(); };
            MouseEventHandler wheel = delegate(object s, MouseEventArgs me) { ScrollCompare(-me.Delta / 120 * Dpi.S(40)); };
            comparePage.MouseWheel += wheel; comparison.MouseWheel += wheel; conclusion.MouseWheel += wheel; verdict.MouseWheel += wheel;
            comparePage.Controls.Add(verdict);
            comparePage.Controls.Add(comparison);
            comparePage.Controls.Add(conclusion);
            comparePage.Resize += delegate { LayoutComparePage(); };
            resultsCard.Controls.Add(comparePage);

            chartPage.BackColor = Theme.Card;
            chart.Dock = DockStyle.Fill;
            chartPage.Controls.Add(chart);
            resultsCard.Controls.Add(chartPage);

            historyPage.BackColor = Theme.Card;
            for (int i = 0; i < 4; i++)
            {
                ModernButton b = new ModernButton();
                b.Kind = ButtonKind.Toggle;
                b.ToggleColor = i == 1 ? Theme.DeviceColor[0] : i == 2 ? Theme.DeviceColor[1] : i == 3 ? Theme.Warn : Theme.Text;
                b.Font = Theme.Semi(9f);
                b.Checked = i == 0;
                int idx = i;
                b.Click += delegate { historyFilter = idx; for (int k = 0; k < 4; k++) filterButtons[k].Checked = k == idx; RebuildGrid(); };
                filterButtons[i] = b;
                historyPage.Controls.Add(b);
            }
            filterButtons[0].Text = L.S("Tất cả", "All");
            filterButtons[3].Text = L.S("Bị bỏ qua", "Ignored");
            historyCount.Font = Theme.F(8.5f);
            historyCount.ForeColor = Theme.Muted;
            historyCount.BackColor = Theme.Card;
            historyCount.TextAlign = ContentAlignment.MiddleRight;
            historyPage.Controls.Add(historyCount);
            BuildGrid();
            historyPage.Controls.Add(grid);
            historyPage.Resize += delegate { LayoutHistoryPage(); };
            resultsCard.Controls.Add(historyPage);

            resultsCard.Resize += delegate { LayoutResults(); };
            ShowPage();
        }

        private void BuildGrid()
        {
            grid.VirtualMode = true;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Theme.Card;
            grid.GridColor = Theme.Divider;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Dpi.S(34);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Subtle;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text2;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Subtle;
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.Semi(8.5f);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(Dpi.S(6), 0, 0, 0);
            grid.DefaultCellStyle.Font = Theme.F(9f);
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.SelectionBackColor = Theme.InfoSoft;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.Padding = new Padding(Dpi.S(6), 0, 0, 0);
            grid.RowTemplate.Height = Dpi.S(30);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Vertical;
            AddColumn("#", 45);
            AddColumn(L.S("Vòng", "Round"), 45);
            AddColumn(L.S("Bàn phím", "Keyboard"), 85);
            AddColumn(L.S("Lần nhấn", "Press"), 65);
            AddColumn(L.S("Phím", "Key"), 50);
            AddColumn(L.S("Thời điểm (ms)", "Time (ms)"), 90);
            AddColumn(L.S("Khoảng cách (ms)", "Interval (ms)"), 105);
            AddColumn(L.S("Trạng thái", "Status"), 230);
            grid.Columns[5].ToolTipText = L.S("Thời điểm ứng dụng nhận sự kiện từ Windows, tính từ lúc bấm Bắt đầu test",
                                              "When the app received the event from Windows, measured from START TEST");
            grid.Columns[6].ToolTipText = L.S("Khoảng cách từ lần nhấn hợp lệ trước đó trên CÙNG bàn phím, trong cùng giai đoạn",
                                              "Time since the previous valid press on the SAME keyboard, within the same phase");
            grid.CellValueNeeded += OnGridValue;
            grid.CellFormatting += OnGridFormat;
        }

        private void AddColumn(string title, int weight)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.HeaderText = title;
            c.FillWeight = weight;
            c.SortMode = DataGridViewColumnSortMode.NotSortable;
            c.MinimumWidth = Dpi.S(36);
            grid.Columns.Add(c);
        }

        /// <summary>Refreshes every place that shows the keyboard names.</summary>
        private void UpdateNamesUi()
        {
            Text = "Keyboard Speed Test – " + AppInfo.VersusTitle;
            orderAFirst.Text = AppInfo.Name(AppInfo.A) + " → " + AppInfo.Name(AppInfo.B);
            orderBFirst.Text = AppInfo.Name(AppInfo.B) + " → " + AppInfo.Name(AppInfo.A);
            filterButtons[1].Text = AppInfo.Name(AppInfo.A);
            filterButtons[2].Text = AppInfo.Name(AppInfo.B);
            if (historyPage.Width > 0) LayoutHistoryPage();
            header.Invalidate();
            for (int d = 0; d < 2; d++) cards[d].Invalidate();
        }

        // ================= Layout =================

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            LayoutContent();
        }

        private void LayoutContent()
        {
            if (host.Width <= 0) return;
            int M = Dpi.S(16), G = Dpi.S(12);
            int minW = Dpi.S(1000), minH = Dpi.S(600);
            // Based on the outer size of the scroll host (not on whether scrollbars are showing) to avoid oscillation.
            int availW = host.Width, availH = host.Height;
            bool needH = availW < minW, needV = availH < minH;
            if (needH) availH -= SystemInformation.HorizontalScrollBarHeight;
            if (needV) availW -= SystemInformation.VerticalScrollBarWidth;
            if (!needH && availW < minW) availH -= SystemInformation.HorizontalScrollBarHeight;
            if (!needV && availH < minH) availW -= SystemInformation.VerticalScrollBarWidth;
            int W = Math.Max(minW, availW);
            int H = Math.Max(minH, availH);
            bool scroll = W > host.Width || H > host.Height;
            if (host.AutoScroll != scroll) host.AutoScroll = scroll; // scroll only when the window is below the minimum size
            if (!scroll) content.Location = Point.Empty;
            content.Size = new Size(W, H);

            int leftW = W >= Dpi.S(1500) ? Dpi.S(380) : Dpi.S(340);
            int devH = Dpi.S(150);
            int setH = Dpi.S(258);
            cards[0].SetBounds(M, M, leftW, devH);
            cards[1].SetBounds(M, M + devH + G, leftW, devH);
            settingsCard.SetBounds(M, M + (devH + G) * 2, leftW, setH);
            int gy = M + (devH + G) * 2 + setH + G;
            int gh = H - M - gy;
            guideCard.Visible = gh >= Dpi.S(130);
            if (guideCard.Visible) guideCard.SetBounds(M, gy, leftW, gh);

            int rx = M + leftW + G;
            int rw = W - rx - M;
            int stageH = Dpi.S(200);
            stageCard.SetBounds(rx, M, rw, stageH);
            resultsCard.SetBounds(rx, M + stageH + G, rw, H - M - stageH - G - M);
        }

        private void LayoutSettings()
        {
            int pad = Dpi.S(16);
            int w = settingsCard.Width - pad * 2;
            int y = Dpi.S(12);
            settingsTitle.SetBounds(pad, y, w, Dpi.S(24));
            y += Dpi.S(32);
            int labelH = Dpi.S(18), ctrlH = Dpi.S(32), gap = Dpi.S(6);

            pressesLabel.SetBounds(pad, y, w, labelH);
            y += labelH + Dpi.S(4);
            int numW = Dpi.S(70);
            int chipW = Math.Max(Dpi.S(40), (w - numW - gap * 4) / 4);
            for (int i = 0; i < 4; i++) pressChips[i].SetBounds(pad + i * (chipW + gap), y, chipW, ctrlH);
            pressesInput.Width = numW;
            pressesInput.Location = new Point(pad + 4 * (chipW + gap), y + (ctrlH - pressesInput.Height) / 2);
            y += ctrlH + Dpi.S(12);

            orderLabel.SetBounds(pad, y, w, labelH);
            y += labelH + Dpi.S(4);
            int ow = (w - gap) / 2;
            orderAFirst.SetBounds(pad, y, ow, ctrlH);
            orderBFirst.SetBounds(pad + ow + gap, y, w - ow - gap, ctrlH);
            y += ctrlH + Dpi.S(12);

            roundsLabel.SetBounds(pad, y, w, labelH);
            y += labelH + Dpi.S(4);
            roundsInput.Width = Dpi.S(64);
            roundsInput.Location = new Point(pad, y + (ctrlH - roundsInput.Height) / 2);
            alternateCheck.SetBounds(pad + Dpi.S(76), y, w - Dpi.S(76), ctrlH);
            y += ctrlH + Dpi.S(6);
            settingsSummary.SetBounds(pad, y, w, Dpi.S(20));
        }

        private void LayoutStage()
        {
            int pad = Dpi.S(20);
            int colW = Dpi.S(200);
            int x = stageCard.Width - pad - colW;
            stage.SetBounds(Dpi.S(2), Dpi.S(2), Math.Max(0, x - Dpi.S(8)), stageCard.Height - Dpi.S(4));
            int y = pad;
            startButton.SetBounds(x, y, colW, Dpi.S(54));
            y += Dpi.S(54) + Dpi.S(10);
            stopButton.SetBounds(x, y, colW, Dpi.S(38));
            y += Dpi.S(38) + Dpi.S(8);
            resetButton.SetBounds(x, y, colW, Dpi.S(38));
        }

        private void LayoutResults()
        {
            int pad = Dpi.S(16);
            int tabH = Dpi.S(46);
            int bh = Dpi.S(32);
            int bw = Math.Max(Dpi.S(130), Draw.Measure(exportButton.Text, exportButton.Font).Width + Dpi.S(28));
            int bx = resultsCard.Width - pad - bw;
            exportButton.SetBounds(bx, (tabH - bh) / 2 + Dpi.S(4), bw, bh);
            int aw = Draw.Measure(advancedToggle.Text, advancedToggle.Font).Width + Dpi.S(28);
            advancedToggle.SetBounds(bx - Dpi.S(8) - aw, (tabH - bh) / 2 + Dpi.S(4), aw, bh);
            tabs.SetBounds(pad - Dpi.S(4), Dpi.S(4), Math.Max(0, advancedToggle.Left - pad - Dpi.S(8)), tabH);
            Rectangle page = new Rectangle(pad, tabH + Dpi.S(12), resultsCard.Width - pad * 2, resultsCard.Height - tabH - Dpi.S(12) - pad);
            comparePage.Bounds = page;
            chartPage.Bounds = page;
            historyPage.Bounds = page;
        }

        private void LayoutComparePage()
        {
            int sbw = SystemInformation.VerticalScrollBarWidth;
            int w = comparePage.Width - sbw - Dpi.S(4); // always reserve room for the vertical scrollbar; never scroll horizontally
            if (w <= 0) return;
            int gap = Dpi.S(20);
            bool wide = w >= Dpi.S(720);
            int cw = wide ? (int)(w * 0.36) : w;
            int tw = wide ? w - cw - gap : w;
            int ch = conclusion.PreferredHeight(cw);
            int th = comparison.PreferredHeight();
            int vh = verdict.PreferredHeight(w);
            int body = wide ? Math.Max(ch, th) : ch + gap + th;
            int vgap = Dpi.S(12);
            int total = body + vgap + vh;
            int view = comparePage.Height;
            bool needV = total > view;
            compareScroll.Visible = needV;
            int offset = 0;
            if (needV)
            {
                compareScroll.SetBounds(comparePage.Width - sbw, 0, sbw, view);
                compareScroll.Minimum = 0;
                compareScroll.LargeChange = Math.Max(1, view);
                compareScroll.SmallChange = Dpi.S(40);
                compareScroll.Maximum = total; // largest reachable value = Maximum - LargeChange + 1
                int maxValue = Math.Max(0, total - view + 1);
                if (compareScroll.Value > maxValue) compareScroll.Value = maxValue;
                offset = compareScroll.Value;
            }
            else compareScroll.Value = 0;
            if (wide)
            {
                comparison.SetBounds(0, -offset, tw, th);
                conclusion.SetBounds(tw + gap, -offset, cw, ch);
            }
            else
            {
                conclusion.SetBounds(0, -offset, w, ch);
                comparison.SetBounds(0, ch + gap - offset, w, th);
            }
            verdict.SetBounds(0, body + vgap - offset, w, vh); // final-verdict band across the full width
        }

        private void ScrollCompare(int delta)
        {
            if (!compareScroll.Visible) return;
            int maxValue = Math.Max(0, compareScroll.Maximum - compareScroll.LargeChange + 1);
            long v = (long)compareScroll.Value + delta;
            compareScroll.Value = (int)Math.Max(0, Math.Min(maxValue, v));
            LayoutComparePage();
        }

        private void LayoutHistoryPage()
        {
            int x = 0, h = Dpi.S(30), gap = Dpi.S(6);
            for (int i = 0; i < 4; i++)
            {
                int w = Math.Min(Dpi.S(170), Draw.Measure(filterButtons[i].Text, filterButtons[i].Font).Width + Dpi.S(28));
                filterButtons[i].SetBounds(x, 0, w, h);
                x += w + gap;
            }
            historyCount.SetBounds(x + Dpi.S(8), 0, Math.Max(0, historyPage.Width - x - Dpi.S(8)), h);
            grid.SetBounds(0, h + Dpi.S(10), historyPage.Width, Math.Max(0, historyPage.Height - h - Dpi.S(10)));
        }

        private void SetAdvanced(bool on)
        {
            advancedToggle.Checked = on;
            comparison.ShowAdvanced = on;
            compareScroll.Value = 0;
            LayoutComparePage();
            comparison.Invalidate();
            if (on) ScrollCompare(int.MaxValue / 2); // scroll to the rows that were just added
        }

        private void ShowPage()
        {
            comparePage.Visible = tabs.SelectedIndex == 0;
            chartPage.Visible = tabs.SelectedIndex == 1;
            historyPage.Visible = tabs.SelectedIndex == 2;
            advancedToggle.Visible = tabs.SelectedIndex == 0;
            if (tabs.SelectedIndex == 2) LayoutHistoryPage();
        }

        // ================= Start-up / shutdown =================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!listener.Start())
            {
                header.StatusText = L.S("Raw Input lỗi (mã ", "Raw Input error (code ") + listener.LastError + ")";
                header.StatusColor = Theme.Danger;
                header.Invalidate();
                MessageBox.Show(this, L.S("Không đăng ký được Windows Raw Input (mã lỗi ", "Could not register Windows Raw Input (error code ") + listener.LastError + ").\r\n" +
                    L.S("Hãy đóng rồi mở lại ứng dụng.", "Please close and reopen the app."),
                    "Keyboard Speed Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
            {
                header.StatusText = L.S("Raw Input đang lắng nghe", "Raw Input listening");
                header.StatusColor = Theme.Accent;
                RestoreSavedDevices();
            }
            animTimer.Start();
            UpdateNamesUi();
            RefreshAll();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (session != null && session.State == SessionState.Running)
            {
                if (MessageBox.Show(this, L.S("Bài test đang chạy. Đóng ứng dụng sẽ mất dữ liệu chưa xuất. Vẫn đóng?",
                                              "A test is running. Closing will lose unexported data. Close anyway?"), "Keyboard Speed Test",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            }
            else if (!exported && session != null && session.Presses.Count > 0)
            {
                DialogResult r = MessageBox.Show(this, L.S("Kết quả chưa được xuất CSV. Xuất trước khi đóng?", "Results have not been exported. Export to CSV before closing?"),
                    "Keyboard Speed Test", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) { ExportCsv(); if (!exported) { e.Cancel = true; return; } }
            }
            animTimer.Stop();
            listener.Dispose();
            base.OnFormClosing(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // While testing or identifying, swallow keys at the UI level (Raw Input still receives everything)
            // so J never types into an input box or triggers a control.
            if (identifying >= 0 || (session != null && session.State == SessionState.Running))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (identifying >= 0 || (session != null && session.State == SessionState.Running)) return true;
            return base.ProcessDialogKey(keyData);
        }

        // ================= Raw Input -> UI thread =================

        // Called on the Raw Input thread: only enqueue, nothing else.
        private void OnRawKey(RawKeyEvent e)
        {
            keyQueue.Enqueue(e);
            ScheduleDrain();
        }

        private void OnRawDeviceChanged(IntPtr device, bool arrived)
        {
            deviceQueue.Enqueue(new KeyValuePair<IntPtr, bool>(device, arrived));
            ScheduleDrain();
        }

        private void ScheduleDrain()
        {
            if (Interlocked.Exchange(ref drainPending, 1) == 1) return;
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new MethodInvoker(Drain)); }
            catch { Interlocked.Exchange(ref drainPending, 0); }
        }

        private void Drain()
        {
            Interlocked.Exchange(ref drainPending, 0);
            KeyValuePair<IntPtr, bool> dc;
            while (deviceQueue.TryDequeue(out dc)) HandleDeviceChange(dc.Key, dc.Value);
            RawKeyEvent e;
            bool gridChanged = false;
            while (keyQueue.TryDequeue(out e))
            {
                if (!keyFilter.IsNewPress(e)) continue; // KeyUp or auto-repeat
                int dev = DeviceOf(e.Device);
                if (dev >= 0) cards[dev].Pulse();
                if (identifying >= 0) { HandleIdentifyPress(e); continue; }
                if (session != null && session.State == SessionState.Running)
                {
                    HandleTestPress(e);
                    gridChanged = true;
                }
            }
            if (gridChanged) AppendGridRows();
        }

        private int DeviceOf(IntPtr h)
        {
            if (h == IntPtr.Zero) return -1;
            if (h == handles[0]) return 0;
            if (h == handles[1]) return 1;
            return -1;
        }

        // ================= Device identification =================

        private void ToggleIdentify(int device)
        {
            if (session != null && session.State == SessionState.Running) return;
            if (!listener.IsRunning) return;
            identifying = identifying == device ? -1 : device;
            ActiveControl = null;
            RefreshAll();
        }

        private void HandleIdentifyPress(RawKeyEvent e)
        {
            int d = identifying;
            string slot = d == AppInfo.A ? "A" : "B";
            if (e.Device == IntPtr.Zero)
            {
                SetNotice(L.S("Phím này không đến từ bàn phím vật lý (ví dụ: bàn phím ảo). Hãy nhấn trên bàn phím thật.",
                              "That key did not come from a physical keyboard (e.g. on-screen keyboard). Press it on a real keyboard."), Theme.Warn);
                return;
            }
            int other = AppInfo.Other(d);
            if (handles[other] != IntPtr.Zero && e.Device == handles[other])
            {
                SetNotice(L.S("Bàn phím này đã được gán cho " + AppInfo.Name(other) + ". Hãy nhấn trên bàn phím còn lại.",
                              "This keyboard is already assigned to " + AppInfo.Name(other) + ". Press a key on the other keyboard."), Theme.Warn);
                cards[d].Hint = L.S("Đó là " + AppInfo.Name(other) + " – hãy nhấn trên bàn phím khác cho vị trí " + slot,
                                    "That is " + AppInfo.Name(other) + " – press on another keyboard for slot " + slot);
                cards[d].Invalidate();
                return;
            }
            string path = DeviceInfo.GetDevicePath(e.Device);
            Assign(d, e.Device, path, false);
            identifying = -1;
            SetNotice(L.S("Đã nhận diện ", "Identified ") + AppInfo.Name(d) + ": " + DeviceInfo.ShortName(descriptors[d]), Theme.Accent);
            SaveDevices();
            UpdateNamesUi();
            RefreshAll();
        }

        /// <summary>Name suggested from the HID product string (falls back to manufacturer / VID:PID / default).</summary>
        private string SuggestName(int d, DeviceDescriptor desc)
        {
            string n = "";
            if (desc != null)
            {
                if (!String.IsNullOrEmpty(desc.Product)) n = desc.Product;
                else if (!String.IsNullOrEmpty(desc.Manufacturer)) n = desc.Manufacturer + L.S(" (bàn phím)", " keyboard");
                else if (desc.Vid != null && desc.Pid != null) n = "VID " + desc.Vid + " PID " + desc.Pid;
            }
            n = AppInfo.CleanName(n);
            if (n.Length == 0) return "";
            string otherName = AppInfo.DeviceNames[AppInfo.Other(d)];
            if (String.Equals(n, otherName, StringComparison.OrdinalIgnoreCase))
                n = AppInfo.CleanName(n.Length > AppInfo.MaxNameLength - 2 ? n.Substring(0, AppInfo.MaxNameLength - 2) : n) + " " + (d == AppInfo.A ? "A" : "B");
            return n;
        }

        private void Assign(int d, IntPtr handle, string path, bool restored)
        {
            bool sameDevice = String.Equals(paths[d] ?? "", path ?? "", StringComparison.OrdinalIgnoreCase);
            handles[d] = handle;
            paths[d] = path ?? "";
            descriptors[d] = DeviceInfo.Describe(AppInfo.Name(d), handle, path);
            // A newly identified keyboard takes its name from HID; a re-identified/restored one keeps the user's name.
            if (!restored && (!sameDevice || AppInfo.DeviceNames[d] == "")) AppInfo.DeviceNames[d] = SuggestName(d, descriptors[d]);
            descriptors[d].Label = AppInfo.Name(d);
            connected[d] = true;
            cards[d].Identified = true;
            cards[d].Restored = restored;
            cards[d].Connected = true;
            cards[d].Hint = "";
            cards[d].ShortName = DeviceInfo.ShortName(descriptors[d]);
            cards[d].SetFullPath(path);
            if (session != null) session.UpdateHandle(d, handle);
        }

        private void SaveDevices() { DeviceStore.Save(paths, AppInfo.DeviceNames); }

        private void RestoreSavedDevices()
        {
            DeviceStore.Entry saved = DeviceStore.Load();
            Dictionary<IntPtr, string> list = DeviceInfo.ListKeyboards();
            keyboardCount = list.Count;
            for (int d = 0; d < 2; d++)
            {
                if (String.IsNullOrEmpty(saved.Paths[d])) continue;
                AppInfo.DeviceNames[d] = saved.Names[d] ?? "";
                foreach (KeyValuePair<IntPtr, string> kv in list)
                {
                    if (String.Equals(kv.Value, saved.Paths[d], StringComparison.OrdinalIgnoreCase) && kv.Key != handles[AppInfo.Other(d)])
                    {
                        Assign(d, kv.Key, kv.Value, true);
                        break;
                    }
                }
                if (handles[d] == IntPtr.Zero) paths[d] = saved.Paths[d]; // keep the path to auto-identify when plugged in
            }
        }

        private void HandleDeviceChange(IntPtr device, bool arrived)
        {
            keyboardCount = DeviceInfo.ListKeyboards().Count;
            if (arrived)
            {
                string path = DeviceInfo.GetDevicePath(device);
                for (int d = 0; d < 2; d++)
                {
                    if (String.IsNullOrEmpty(paths[d]) || !String.Equals(paths[d], path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (handles[d] == device && connected[d]) continue;
                    bool wasIdentified = cards[d].Identified;
                    Assign(d, device, path, true);
                    cards[d].Restored = !wasIdentified;
                    if (wasIdentified) SetNotice(AppInfo.Name(d) + L.S(" đã kết nối lại và được tự nhận diện.", " reconnected and was re-identified automatically."), Theme.Accent);
                }
            }
            else
            {
                for (int d = 0; d < 2; d++)
                {
                    if (handles[d] != device || !connected[d]) continue;
                    connected[d] = false;
                    cards[d].Connected = false;
                    keyFilter.ForgetDevice(device);
                    bool running = session != null && session.State == SessionState.Running;
                    SetNotice(AppInfo.Name(d) + L.S(" đã mất kết nối. Cắm lại để tiếp tục", " was disconnected. Plug it back in to continue") +
                        (running ? L.S(" – bài test vẫn giữ nguyên dữ liệu.", " – the test keeps its data.") : "."), Theme.Danger);
                }
            }
            UpdateNamesUi();
            RefreshAll();
        }

        private void RenameDevice(int d)
        {
            if (session != null && session.State == SessionState.Running) return;
            string slot = d == AppInfo.A ? "A" : "B";
            using (Form f = new Form())
            {
                f.Text = L.S("Đổi tên bàn phím ", "Rename keyboard ") + slot;
                f.Font = Theme.F(10f);
                f.BackColor = Theme.Card;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.AutoScaleMode = AutoScaleMode.None;
                f.ClientSize = new Size(Dpi.S(420), Dpi.S(176));
                f.ShowInTaskbar = false;
                Label lbl = MakeLabel(L.S("Tên hiển thị (tối đa ", "Display name (max ") + AppInfo.MaxNameLength + L.S(" ký tự). Để trống để dùng tên từ HID.", " characters). Leave empty to use the HID name."),
                    Theme.F(9f), Theme.Text2);
                lbl.SetBounds(Dpi.S(20), Dpi.S(16), Dpi.S(380), Dpi.S(40));
                lbl.TextAlign = ContentAlignment.TopLeft;
                TextBox tb = new TextBox();
                tb.Font = Theme.F(11f);
                tb.MaxLength = AppInfo.MaxNameLength;
                tb.Text = AppInfo.DeviceNames[d];
                tb.SetBounds(Dpi.S(20), Dpi.S(62), Dpi.S(380), Dpi.S(30));
                Button ok = new Button();
                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(Dpi.S(212), Dpi.S(118), Dpi.S(90), Dpi.S(34));
                Button cancel = new Button();
                cancel.Text = L.S("Hủy", "Cancel");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(Dpi.S(310), Dpi.S(118), Dpi.S(90), Dpi.S(34));
                f.Controls.Add(lbl); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                tb.SelectAll();
                if (f.ShowDialog(this) != DialogResult.OK) return;
                string n = AppInfo.CleanName(tb.Text);
                if (n.Length == 0) n = descriptors[d] != null ? SuggestName(d, descriptors[d]) : "";
                if (n.Length > 0 && String.Equals(n, AppInfo.DeviceNames[AppInfo.Other(d)], StringComparison.OrdinalIgnoreCase))
                    n = AppInfo.CleanName(n.Length > AppInfo.MaxNameLength - 2 ? n.Substring(0, AppInfo.MaxNameLength - 2) : n) + " " + slot;
                AppInfo.DeviceNames[d] = n;
                if (descriptors[d] != null) descriptors[d].Label = AppInfo.Name(d);
                if (!String.IsNullOrEmpty(paths[d])) SaveDevices();
            }
            UpdateNamesUi();
            RefreshAll();
        }

        private void ShowDetails(int d)
        {
            DeviceDescriptor dd = descriptors[d];
            string unknown = L.S("không đọc được", "not available");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(L.S("Bàn phím: ", "Keyboard: ") + AppInfo.Name(d) + L.S("  (vị trí ", "  (slot ") + (d == AppInfo.A ? "A" : "B") + ")");
            if (dd == null) sb.AppendLine(L.S("Chưa nhận diện.", "Not identified."));
            else
            {
                sb.AppendLine(L.S("Trạng thái: ", "Status: ") + (connected[d] ? L.S("Đã kết nối", "Connected") : L.S("Mất kết nối", "Disconnected")));
                sb.AppendLine(L.S("Tên sản phẩm (HID): ", "Product name (HID): ") + (dd.Product ?? unknown));
                sb.AppendLine(L.S("Nhà sản xuất (HID): ", "Manufacturer (HID): ") + (dd.Manufacturer ?? unknown));
                sb.AppendLine("VID: " + (dd.Vid ?? "—") + "    PID: " + (dd.Pid ?? "—") + "    Interface: " + (dd.Interface ?? "—"));
                sb.AppendLine("Device Handle: 0x" + handles[d].ToInt64().ToString("X"));
                sb.AppendLine();
                sb.AppendLine("Device Path:");
                sb.AppendLine(dd.Path);
            }
            using (Form f = new Form())
            {
                f.Text = L.S("Thông tin thiết bị – ", "Device details – ") + AppInfo.Name(d);
                f.Font = Theme.F(9.5f);
                f.BackColor = Theme.Card;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.AutoScaleMode = AutoScaleMode.None;
                f.ClientSize = new Size(Dpi.S(640), Dpi.S(300));
                f.ShowInTaskbar = false;
                TextBox tb = new TextBox();
                tb.Multiline = true; tb.ReadOnly = true; tb.WordWrap = true; tb.ScrollBars = ScrollBars.Vertical;
                tb.BorderStyle = BorderStyle.None; tb.BackColor = Theme.Card;
                tb.Font = Theme.F(9.5f);
                tb.Text = sb.ToString();
                tb.SetBounds(Dpi.S(20), Dpi.S(18), Dpi.S(600), Dpi.S(222));
                f.Controls.Add(tb);
                ModernButton copy = new ModernButton();
                copy.Text = L.S("Sao chép", "Copy");
                copy.SetBounds(Dpi.S(380), Dpi.S(250), Dpi.S(120), Dpi.S(34));
                copy.Click += delegate { try { Clipboard.SetText(tb.Text); copy.Text = L.S("Đã sao chép", "Copied"); } catch { } };
                ModernButton close = new ModernButton();
                close.Text = L.S("Đóng", "Close");
                close.Kind = ButtonKind.Primary;
                close.SetBounds(Dpi.S(508), Dpi.S(250), Dpi.S(112), Dpi.S(34));
                close.Click += delegate { f.Close(); };
                f.Controls.Add(copy);
                f.Controls.Add(close);
                tb.SelectionStart = 0; tb.SelectionLength = 0;
                f.ShowDialog(this);
            }
        }

        // ================= Test =================

        private TestConfig CurrentConfig()
        {
            TestConfig c = new TestConfig();
            c.PressesPerDevice = (int)pressesInput.Value;
            c.Rounds = (int)roundsInput.Value;
            c.FirstDevice = firstDevice;
            c.AlternateOrder = alternateCheck.Checked;
            return c;
        }

        private void StartTest()
        {
            if (session != null && session.State == SessionState.Running) return;
            string missingTitle = L.S("Thiếu thiết bị", "Keyboard missing");
            if (!listener.IsRunning)
            {
                MessageBox.Show(this, L.S("Raw Input chưa sẵn sàng. Hãy đóng rồi mở lại ứng dụng.", "Raw Input is not ready. Please close and reopen the app."),
                    "Keyboard Speed Test", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            for (int d = 0; d < 2; d++)
            {
                string slot = d == AppInfo.A ? "A" : "B";
                if (handles[d] == IntPtr.Zero)
                {
                    MessageBox.Show(this, L.S("Hãy nhận diện bàn phím " + slot + " trước khi test: bấm Nhận diện rồi nhấn J trên bàn phím đó.",
                                              "Identify keyboard " + slot + " first: click Identify, then press J on that keyboard."),
                        missingTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!connected[d])
                {
                    MessageBox.Show(this, AppInfo.Name(d) + L.S(" đang mất kết nối. Hãy cắm lại bàn phím.", " is disconnected. Please plug it back in."),
                        missingTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if (handles[0] == handles[1])
            {
                MessageBox.Show(this, L.S("Hai thẻ đang trỏ đến cùng một bàn phím. Hãy nhận diện lại.", "Both cards point to the same keyboard. Please identify again."),
                    L.S("Lỗi thiết bị", "Device error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!exported && session != null && session.Presses.Count > 0)
            {
                if (MessageBox.Show(this, L.S("Kết quả bài test trước chưa được xuất CSV và sẽ bị thay thế. Tiếp tục?",
                                              "The previous results have not been exported and will be replaced. Continue?"),
                    L.S("Bắt đầu bài test mới", "Start a new test"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }

            identifying = -1;
            Fmt.Culture = CultureInfo.CurrentCulture;
            session = new TestSession(CurrentConfig(), handles[0], handles[1], Stopwatch.GetTimestamp(), Stopwatch.Frequency, DateTime.Now);
            exported = false;
            lastPressText = "";
            SetNotice("", Theme.Warn);
            gridRows.Clear();
            grid.RowCount = 0;
            analysis = Analyzer.Analyze(session);
            ActiveControl = null;
            Focus();
            RefreshAll();
        }

        private string lastPressText = "";

        private void HandleTestPress(RawKeyEvent e)
        {
            PhaseInfo phaseBefore = session.CurrentPhase;
            FeedResult r = session.FeedKeyDown(e.Device, e.VKey, e.Timestamp);
            switch (r)
            {
                case FeedResult.Counted:
                case FeedResult.PhaseCompleted:
                case FeedResult.TestCompleted:
                {
                    PressRecord p = session.Presses[session.Presses.Count - 1];
                    lastPressText = double.IsNaN(p.IntervalMs) ? L.S("Lần nhấn đầu tiên – bắt đầu tính giờ", "First press – timing started")
                                                               : L.S("Khoảng vừa rồi: ", "Last interval: ") + Fmt.Ms(p.IntervalMs);
                    stage.Flash();
                    if (r == FeedResult.PhaseCompleted)
                    {
                        PhaseInfo next = session.CurrentPhase;
                        SetNotice(L.S("✓ Xong ", "✓ Done with ") + AppInfo.Name(phaseBefore.Device) + " (" + phaseBefore.Count + "/" + session.Config.PressesPerDevice +
                            L.S(") – tiếp tục nhấn J trên ", ") – keep tapping J on ") + AppInfo.Name(next.Device), Theme.AccentHover);
                        lastPressText = "";
                    }
                    else if (r == FeedResult.TestCompleted)
                    {
                        FinishTest();
                        return;
                    }
                    else if (noticeColor == Theme.AccentHover) SetNotice("", Theme.Warn);
                    break;
                }
                case FeedResult.WrongDevice:
                {
                    int dev = session.ResolveDevice(e.Device);
                    SetNotice(L.S("Bỏ qua " + session.WrongDeviceCount[dev] + " lần nhấn trên " + AppInfo.Name(dev) + " (chưa đến lượt) – không tính vào kết quả",
                                  "Ignored " + session.WrongDeviceCount[dev] + " press(es) on " + AppInfo.Name(dev) + " (not its turn) – not counted"), Theme.Warn);
                    break;
                }
                case FeedResult.WrongKey:
                    SetNotice(L.S("Chỉ phím " + session.Config.TestKeyName + " được tính – đã bỏ qua " + session.WrongKeyCount + " phím khác",
                                  "Only " + session.Config.TestKeyName + " counts – ignored " + session.WrongKeyCount + " other key(s)"), Theme.Warn);
                    break;
                case FeedResult.UnknownDevice:
                {
                    int sibling = SiblingOf(e.Device);
                    if (sibling >= 0)
                        SetNotice(L.S("Phím đến từ một interface khác của " + AppInfo.Name(sibling) + " nên không được tính. Hãy Dừng và Nhận diện lại bằng phím J.",
                                      "That key came from another interface of " + AppInfo.Name(sibling) + " and was not counted. Stop and re-identify using J."), Theme.Danger);
                    else
                        SetNotice(L.S("Bỏ qua phím từ thiết bị khác (không phải hai bàn phím đang test)", "Ignored a key from another device (not one of the two tested keyboards)"), Theme.Muted);
                    break;
                }
            }
            resultsDirty = true;
            RefreshStage();
        }

        private readonly Dictionary<IntPtr, DeviceDescriptor> unknownCache = new Dictionary<IntPtr, DeviceDescriptor>();

        /// <summary>
        /// A USB keyboard usually exposes several HID interfaces (MI_00, MI_01...), each with its own handle.
        /// Only the identified handle counts; this is used solely to explain to the user when a key arrives
        /// from another interface with the same VID/PID. Devices are never merged automatically.
        /// </summary>
        private int SiblingOf(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return -1;
            DeviceDescriptor d;
            if (!unknownCache.TryGetValue(handle, out d))
            {
                d = DeviceInfo.Describe("", handle, DeviceInfo.GetDevicePath(handle));
                unknownCache[handle] = d;
            }
            if (d.Vid == null || d.Pid == null) return -1;
            int match = -1;
            for (int i = 0; i < 2; i++)
            {
                DeviceDescriptor a = descriptors[i];
                if (a != null && a.Vid == d.Vid && a.Pid == d.Pid) { if (match >= 0) return -1; match = i; }
            }
            return match;
        }

        private void FinishTest()
        {
            analysis = Analyzer.Analyze(session);
            int a = session.PressCount(AppInfo.A), b = session.PressCount(AppInfo.B);
            if (a != session.TargetPerDevice || b != session.TargetPerDevice)
            {
                // Safety net: never report completion when the two keyboards have unbalanced data.
                session.State = SessionState.Stopped;
                SetNotice(L.S("Dữ liệu không cân bằng (", "Unbalanced data (") + AppInfo.Name(AppInfo.A) + " " + a + ", " + AppInfo.Name(AppInfo.B) + " " + b +
                    L.S("). Hãy kiểm tra thiết bị và chạy lại.", "). Check the devices and run again."), Theme.Danger);
            }
            else SetNotice("", Theme.Warn);
            lastPressText = "";
            tabs.SelectedIndex = 0;
            RefreshAll();
            ScrollCompare(int.MaxValue / 2); // if space is short, scroll so the final verdict is visible
        }

        private void StopTest()
        {
            if (session == null || session.State != SessionState.Running) return;
            session.Stop(Stopwatch.GetTimestamp());
            analysis = Analyzer.Analyze(session);
            SetNotice(L.S("Đã dừng. Dữ liệu đã thu thập được giữ lại.", "Stopped. Collected data has been kept."), Theme.Text2);
            lastPressText = "";
            RefreshAll();
        }

        private void ResetResults()
        {
            if (session != null && session.State == SessionState.Running) return;
            if (!exported && session != null && session.Presses.Count > 0)
            {
                if (MessageBox.Show(this, L.S("Xóa kết quả hiện tại? Dữ liệu chưa được xuất CSV.", "Clear the current results? They have not been exported."),
                    L.S("Làm mới kết quả", "Clear results"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            session = null;
            exported = true;
            analysis = Analyzer.Analyze(null);
            gridRows.Clear();
            grid.RowCount = 0;
            lastPressText = "";
            SetNotice("", Theme.Warn);
            RefreshAll();
        }

        // ================= UI refresh =================

        private string noticeText = "";
        private Color noticeColor = Theme.Warn;

        private void SetNotice(string text, Color color)
        {
            noticeText = text ?? "";
            noticeColor = color;
        }

        private void OnAnimTick()
        {
            stage.Tick();
            for (int d = 0; d < 2; d++) cards[d].Tick();
            if (resultsDirty) { resultsDirty = false; RefreshResults(); }
        }

        private void RefreshAll()
        {
            bool running = session != null && session.State == SessionState.Running;
            bool ready = handles[0] != IntPtr.Zero && handles[1] != IntPtr.Zero && connected[0] && connected[1] && listener.IsRunning;

            for (int d = 0; d < 2; d++)
            {
                DeviceCard c = cards[d];
                c.Waiting = identifying == d;
                c.Connected = connected[d];
                c.IdentifyButton.Text = identifying == d ? L.S("Hủy", "Cancel") : c.Identified ? L.S("Nhận diện lại", "Re-identify") : L.S("Nhận diện", "Identify");
                c.IdentifyButton.Kind = identifying == d ? ButtonKind.Danger : c.Identified ? ButtonKind.Secondary : ButtonKind.Primary;
                c.IdentifyButton.Enabled = !running && listener.IsRunning && (identifying < 0 || identifying == d);
                c.RenameButton.Enabled = !running && identifying < 0;
                c.DetailsButton.Enabled = descriptors[d] != null;
                c.SetHighlight(identifying == d ? Theme.DeviceColor[d] : Color.Empty, identifying == d ? 1f : 0f);
                c.Invalidate();
            }
            header.KeyboardsText = keyboardCount > 0 ? keyboardCount + L.S(" bàn phím Raw Input", " Raw Input keyboards") : "";
            header.Invalidate();

            bool editable = !running && identifying < 0;
            foreach (ModernButton b in pressChips) b.Enabled = editable;
            pressesInput.Enabled = editable;
            orderAFirst.Enabled = editable;
            orderBFirst.Enabled = editable;
            roundsInput.Enabled = editable;
            alternateCheck.Enabled = editable && roundsInput.Value > 1;

            startButton.Enabled = !running && identifying < 0 && ready;
            startButton.Text = session != null && !running ? L.S("TEST LẠI", "TEST AGAIN") : L.S("BẮT ĐẦU TEST", "START TEST");
            stopButton.Enabled = running;
            resetButton.Enabled = !running && session != null;
            exportButton.Enabled = !running && session != null && session.Events.Count > 0;

            RefreshStage();
            RefreshResults();
        }

        private void RefreshStage()
        {
            bool ready = handles[0] != IntPtr.Zero && handles[1] != IntPtr.Zero;
            TestConfig cfg = session != null ? session.Config : CurrentConfig();
            stage.Target = cfg.PressesPerDevice;
            stage.Rounds = cfg.Rounds;
            stage.LastPress = lastPressText;
            stage.Notice = noticeText;
            stage.NoticeColor = noticeColor;

            if (identifying >= 0)
            {
                string slot = identifying == AppInfo.A ? "A" : "B";
                stage.Mode = StageMode.Identifying;
                stage.Device = identifying;
                stage.Listening = true;
                stage.Title = L.S("NHẤN J TRÊN BÀN PHÍM ", "PRESS J ON KEYBOARD ") + slot;
                stage.Subtitle = L.S("Phím đầu tiên nhận được sẽ gán bàn phím đó vào vị trí " + slot + ". Tên lấy từ HID, có thể đổi sau. Bấm Hủy để thôi.",
                                     "The first key received assigns that keyboard to slot " + slot + ". Its name comes from HID and can be changed later. Click Cancel to abort.");
                stage.LastPress = "";
            }
            else if (session == null)
            {
                stage.Listening = false;
                stage.Round = 1;
                stage.RoundPhaseDevices = new int[] { cfg.FirstDevice, AppInfo.Other(cfg.FirstDevice) };
                stage.RoundPhaseCounts = new int[2];
                stage.RoundPhasePos = 0;
                stage.Device = cfg.FirstDevice;
                if (!ready || !connected[0] || !connected[1])
                {
                    stage.Mode = StageMode.NotReady;
                    stage.Title = L.S("CHƯA SẴN SÀNG", "NOT READY");
                    List<string> miss = new List<string>();
                    for (int d = 0; d < 2; d++) if (handles[d] == IntPtr.Zero || !connected[d]) miss.Add(d == AppInfo.A ? "A" : "B");
                    stage.Subtitle = L.S("Cần nhận diện bàn phím " + String.Join(" và ", miss.ToArray()) + ": bấm Nhận diện trên thẻ rồi nhấn J trên đúng bàn phím đó.",
                                         "Identify keyboard " + String.Join(" and ", miss.ToArray()) + ": click Identify on the card, then press J on that keyboard.");
                }
                else
                {
                    stage.Mode = StageMode.Ready;
                    stage.Title = L.S("SẴN SÀNG", "READY");
                    stage.Subtitle = L.S("Bấm BẮT ĐẦU TEST rồi nhấn J liên tục: " + cfg.PressesPerDevice + " lần trên " + AppInfo.Name(cfg.FirstDevice) +
                                         ", sau đó " + cfg.PressesPerDevice + " lần trên " + AppInfo.Name(AppInfo.Other(cfg.FirstDevice)) + ". Không đếm ngược.",
                                         "Click START TEST, then tap J continuously: " + cfg.PressesPerDevice + " times on " + AppInfo.Name(cfg.FirstDevice) +
                                         ", then " + cfg.PressesPerDevice + " times on " + AppInfo.Name(AppInfo.Other(cfg.FirstDevice)) + ". No countdown.");
                }
            }
            else
            {
                PhaseInfo cur = session.CurrentPhase;
                PhaseInfo shown = cur ?? session.Phases[session.Phases.Count - 1];
                if (session.State != SessionState.Running && cur != null && cur.Count == 0 && cur.Index > 0)
                    shown = session.Phases[cur.Index - 1]; // stopped right after a switch: show the phase just finished
                int roundStart = (shown.Round - 1) * 2;
                stage.Round = shown.Round;
                stage.RoundPhaseDevices = new int[] { session.Phases[roundStart].Device, session.Phases[roundStart + 1].Device };
                stage.RoundPhaseCounts = new int[] { session.Phases[roundStart].Count, session.Phases[roundStart + 1].Count };
                stage.RoundPhasePos = shown.Index - roundStart;
                stage.Device = shown.Device;
                stage.Count = shown.Count;
                string na = AppInfo.Name(AppInfo.A), nb = AppInfo.Name(AppInfo.B);
                if (session.State == SessionState.Running)
                {
                    stage.Mode = StageMode.Running;
                    stage.Listening = true;
                    string name = AppInfo.Name(cur.Device);
                    stage.Title = L.S("ĐANG TEST ", "TESTING ") + name.ToUpper();
                    bool last = cur.Index == session.Phases.Count - 1;
                    if (cur.Count == 0 && cur.Index == 0)
                        stage.Subtitle = L.S("Nhấn phím J liên tục trên " + name + " bất cứ lúc nào. Thời gian chỉ tính từ lần nhấn đầu tiên.",
                                             "Tap J continuously on " + name + " whenever you are ready. Timing starts at the first press.");
                    else if (last)
                        stage.Subtitle = L.S("Nhấn phím J liên tục trên " + name + ". Đây là giai đoạn cuối – bài test kết thúc khi đủ " + cfg.PressesPerDevice + " lần.",
                                             "Tap J continuously on " + name + ". This is the last phase – the test ends after " + cfg.PressesPerDevice + " presses.");
                    else
                        stage.Subtitle = L.S("Nhấn phím J liên tục trên " + name + ". Đủ " + cfg.PressesPerDevice + " lần sẽ tự chuyển sang " + AppInfo.Name(session.Phases[cur.Index + 1].Device) + ".",
                                             "Tap J continuously on " + name + ". After " + cfg.PressesPerDevice + " presses it switches to " + AppInfo.Name(session.Phases[cur.Index + 1].Device) + ".");
                }
                else if (session.State == SessionState.Completed)
                {
                    stage.Mode = StageMode.Completed;
                    stage.Listening = false;
                    stage.Title = L.S("HOÀN THÀNH", "COMPLETED");
                    stage.Subtitle = na + " " + session.PressCount(AppInfo.A) + "/" + session.TargetPerDevice + "  ·  " + nb + " " +
                        session.PressCount(AppInfo.B) + "/" + session.TargetPerDevice + L.S(" – xem kết quả bên dưới.", " – see the results below.");
                }
                else
                {
                    stage.Mode = StageMode.Stopped;
                    stage.Listening = false;
                    stage.Title = L.S("ĐÃ DỪNG", "STOPPED");
                    stage.Subtitle = L.S("Đã ghi " + session.Presses.Count + " lần nhấn hợp lệ (", "Recorded " + session.Presses.Count + " valid presses (") +
                        na + " " + session.PressCount(AppInfo.A) + ", " + nb + " " + session.PressCount(AppInfo.B) +
                        L.S("). Dữ liệu được giữ lại để xem và xuất CSV.", "). Data is kept so you can review and export it.");
                }
            }
            stage.Invalidate();
        }

        private void RefreshResults()
        {
            analysis = Analyzer.Analyze(session);
            comparison.Data = analysis;
            conclusion.Data = analysis;
            verdict.Data = analysis.Verdict;
            verdict.Invalidate();
            conclusion.StatusLine = session == null ? "" : CsvExporter.StateText(session.State);
            chart.Session = session;
            LayoutComparePage();
            comparison.Invalidate();
            conclusion.Invalidate();
            chart.Invalidate();
            int valid = session == null ? 0 : session.Presses.Count;
            int ignored = session == null ? 0 : session.Events.Count - valid;
            tabs.SetItemText(2, L.S("Lịch sử nhấn phím (", "Press history (") + valid + ")");
            historyCount.Text = valid + L.S(" hợp lệ · ", " valid · ") + ignored + L.S(" bị bỏ qua", " ignored");
            grid.Invalidate();
        }

        private void UpdateSettingsUi()
        {
            int v = (int)pressesInput.Value;
            for (int i = 0; i < 4; i++) pressChips[i].Checked = PressPresets[i] == v;
            orderAFirst.Checked = firstDevice == AppInfo.A;
            orderBFirst.Checked = firstDevice == AppInfo.B;
            int rounds = (int)roundsInput.Value;
            alternateCheck.Enabled = rounds > 1 && pressesInput.Enabled;
            settingsSummary.Text = L.S("Mỗi bàn phím " + (v * rounds) + " lần · " + (rounds * 2) + " giai đoạn · phím J",
                                       (v * rounds) + " presses per keyboard · " + (rounds * 2) + " phases · key J");
            if (session == null) RefreshStage();
        }

        // ================= History grid =================

        private bool MatchesFilter(EventRecord e)
        {
            switch (historyFilter)
            {
                case 1: return e.Device == AppInfo.A && e.Status == EventStatus.Valid;
                case 2: return e.Device == AppInfo.B && e.Status == EventStatus.Valid;
                case 3: return e.Status != EventStatus.Valid;
                default: return true;
            }
        }

        private void RebuildGrid()
        {
            gridRows.Clear();
            if (session != null) foreach (EventRecord e in session.Events) if (MatchesFilter(e)) gridRows.Add(e);
            grid.RowCount = gridRows.Count;
            grid.Invalidate();
        }

        private void AppendGridRows()
        {
            if (session == null) return;
            int lastSeq = gridRows.Count == 0 ? 0 : gridRows[gridRows.Count - 1].Seq;
            bool atBottom = grid.RowCount == 0 || grid.FirstDisplayedScrollingRowIndex + grid.DisplayedRowCount(false) >= grid.RowCount - 1;
            for (int i = lastSeq; i < session.Events.Count; i++)
                if (session.Events[i].Seq > lastSeq && MatchesFilter(session.Events[i])) gridRows.Add(session.Events[i]);
            if (grid.RowCount != gridRows.Count)
            {
                grid.RowCount = gridRows.Count;
                if (atBottom && grid.RowCount > 0)
                {
                    try { grid.FirstDisplayedScrollingRowIndex = grid.RowCount - 1; } catch { }
                }
            }
        }

        private static string KeyName(ushort vk)
        {
            if ((vk >= 0x30 && vk <= 0x39) || (vk >= 0x41 && vk <= 0x5A)) return ((char)vk).ToString();
            switch (vk)
            {
                case 0x20: return "Space";
                case 0x0D: return "Enter";
                case 0x1B: return "Esc";
                case 0x08: return "Backspace";
                case 0x09: return "Tab";
                case 0x10: case 0xA0: case 0xA1: return "Shift";
                case 0x11: case 0xA2: case 0xA3: return "Ctrl";
                case 0x12: case 0xA4: case 0xA5: return "Alt";
                default: return "VK 0x" + vk.ToString("X2");
            }
        }

        private void OnGridValue(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridRows.Count) return;
            EventRecord ev = gridRows[e.RowIndex];
            PressRecord p = ev.Press;
            switch (e.ColumnIndex)
            {
                case 0: e.Value = ev.Seq; break;
                case 1: e.Value = ev.Round; break;
                case 2: e.Value = AppInfo.Name(ev.Device); break;
                case 3: e.Value = p != null ? p.PressInPhase + "/" + session.Config.PressesPerDevice : Fmt.Dash; break;
                case 4: e.Value = KeyName(ev.VKey); break;
                case 5: e.Value = Fmt.Num(ev.MsFromStart, 2); break;
                case 6:
                    if (p == null) e.Value = Fmt.Dash;
                    else if (double.IsNaN(p.IntervalMs)) e.Value = L.S("mốc đầu giai đoạn", "phase start");
                    else e.Value = Fmt.Num(p.IntervalMs, 2);
                    break;
                case 7: e.Value = StatusText(ev); break;
            }
        }

        private string StatusText(EventRecord ev)
        {
            switch (ev.Status)
            {
                case EventStatus.Valid:
                {
                    DeviceResult dr = analysis != null ? analysis.Devices[ev.Device] : null;
                    if (dr != null && !double.IsNaN(dr.LongPauseThreshold) && !double.IsNaN(ev.Press.IntervalMs) && ev.Press.IntervalMs > dr.LongPauseThreshold)
                        return L.S("Hợp lệ · khoảng nghỉ dài", "Valid · long pause");
                    return L.S("Hợp lệ", "Valid");
                }
                case EventStatus.WrongDevice: return L.S("Bỏ qua · nhấn nhầm (đang test ", "Ignored · out of turn (testing ") + AppInfo.Name(ev.TargetDevice) + ")";
                case EventStatus.WrongKey: return L.S("Bỏ qua · không phải phím ", "Ignored · not key ") + (session != null ? session.Config.TestKeyName : "J");
                default: return L.S("Bỏ qua · thiết bị khác", "Ignored · other device");
            }
        }

        private void OnGridFormat(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= gridRows.Count) return;
            EventRecord ev = gridRows[e.RowIndex];
            bool valid = ev.Status == EventStatus.Valid;
            if (e.ColumnIndex == 2 && ev.Device >= 0)
            {
                e.CellStyle.ForeColor = valid ? Theme.DeviceColor[ev.Device] : Theme.Muted;
                e.CellStyle.Font = Theme.Semi(9f);
            }
            else if (e.ColumnIndex == 7)
            {
                e.CellStyle.ForeColor = valid ? Theme.AccentHover : Theme.Warn;
            }
            else if (!valid) e.CellStyle.ForeColor = Theme.Muted;
        }

        // ================= CSV export =================

        private void ExportCsv()
        {
            string title = L.S("Xuất CSV", "Export CSV");
            if (session == null || session.Events.Count == 0)
            {
                MessageBox.Show(this, L.S("Chưa có dữ liệu để xuất.", "There is no data to export yet."), title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV UTF-8 (*.csv)|*.csv";
                dialog.FileName = "KeyboardSpeedTest_" + session.StartWallClock.ToString("yyyyMMdd_HHmmss") + ".csv";
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Analysis a = Analyzer.Analyze(session);
                    string csv = CsvExporter.Build(session, a, descriptors, CultureInfo.CurrentCulture, DateTime.Now);
                    File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
                    exported = true;
                    SetNotice(L.S("Đã xuất CSV: ", "Exported CSV: ") + dialog.FileName, Theme.AccentHover);
                    RefreshStage();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.S("Không ghi được file:", "Could not write the file:") + "\r\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================= Hooks for automated tests (not used in normal operation) =================

        internal void TestAssign(int device, IntPtr handle, string path)
        {
            Assign(device, handle, path, false);
            UpdateNamesUi();
            RefreshAll();
        }

        internal void TestSetName(int device, string name) { AppInfo.DeviceNames[device] = AppInfo.CleanName(name); UpdateNamesUi(); RefreshAll(); }
        internal void TestInject(RawKeyEvent e) { OnRawKey(e); }
        internal void TestStart() { exported = true; StartTest(); }
        internal TestSession TestSessionObject { get { return session; } }
        internal void TestSelectTab(int i) { tabs.SelectedIndex = i; }
        internal void TestToggleAdvanced() { SetAdvanced(!advancedToggle.Checked); }
        internal void TestIdentify(int d) { ToggleIdentify(d); }
        internal void TestMarkExported() { exported = true; }
        internal string TestLayoutInfo()
        {
            return "host=" + host.Size + " client=" + host.ClientSize + " content=" + content.Bounds + " hostH=" + host.HorizontalScroll.Visible + " hostV=" + host.VerticalScroll.Visible +
                " page=" + comparePage.Size + " scroll=" + compareScroll.Visible + "/" + compareScroll.Value + " cmp=" + comparison.Bounds + " concl=" + conclusion.Bounds;
        }
    }
}
