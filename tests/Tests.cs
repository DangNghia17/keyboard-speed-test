// Kiểm thử tự động cho Keyboard Speed Test (logic + đường Raw Input + chụp giao diện).
// Biên dịch cùng src\*.cs bằng tools\Build.ps1. Không cần bàn phím thật: dùng handle giả cho logic,
// và SendInput (phím F24, không ảnh hưởng ứng dụng khác) để kiểm tra luồng Raw Input.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace KeyboardSpeedTest.Tests
{
    public static class TestMain
    {
        static int passed, failed;
        static readonly IntPtr MG = new IntPtr(0x1111), M30 = new IntPtr(0x2222), OTHER = new IntPtr(0x3333);
        const ushort J = 0x4A, K = 0x4B;
        const long F = 10000000; // 10 MHz giả lập

        static void Check(bool cond, string name)
        {
            if (cond) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }

        static void Near(double a, double b, string name) { Check(Math.Abs(a - b) < 1e-6, name + " (" + a.ToString("F6", CultureInfo.InvariantCulture) + " ~ " + b.ToString("F6", CultureInfo.InvariantCulture) + ")"); }

        static long Ms(double ms) { return (long)Math.Round(ms * F / 1000.0); }

        static RawKeyEvent Ev(IntPtr dev, ushort vk, bool up) { RawKeyEvent e = new RawKeyEvent(); e.Device = dev; e.VKey = vk; e.MakeCode = 0x24; e.IsBreak = up; e.Flags = (ushort)(up ? 1 : 0); return e; }

        [STAThread]
        public static int Main(string[] args)
        {
            Fmt.Culture = CultureInfo.InvariantCulture;
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            string shotDir = args.Length > 0 ? args[0] : null;
            // Logic tests run in Vietnamese with the author's keyboard names; English is tested separately.
            L.Current = Language.Vietnamese;
            AppInfo.DeviceNames[0] = "MG75"; AppInfo.DeviceNames[1] = "M30 HE";
            Console.WriteLine("== KeyFilter ==");
            TestKeyFilter();
            Console.WriteLine("== TestSession: luồng mặc định 10 lần, M30 HE trước ==");
            TestDefaultFlow();
            Console.WriteLine("== TestSession: nhấn nhầm / phím khác / thiết bị lạ ==");
            TestWrongInputs();
            Console.WriteLine("== TestSession: nhiều vòng, đảo thứ tự ==");
            TestRounds();
            Console.WriteLine("== TestSession: dừng giữa chừng, cắm lại thiết bị ==");
            TestStopAndReconnect();
            Console.WriteLine("== Thống kê ==");
            TestStats();
            Console.WriteLine("== Kết luận ==");
            TestConclusion();
            Console.WriteLine("== Kết luận cuối cùng ==");
            TestVerdict();
            Console.WriteLine("== CSV ==");
            TestCsv();
            Console.WriteLine("== English ==");
            TestEnglish();
            L.Current = Language.Vietnamese;
            AppInfo.DeviceNames[0] = "MG75"; AppInfo.DeviceNames[1] = "M30 HE";
            Console.WriteLine("== Raw Input listener (SendInput F24) ==");
            TestListener();
            if (shotDir != null)
            {
                Console.WriteLine("== Giao diện (chụp ảnh off-screen) ==");
                Program.RunInitOnly();
                L.Current = Language.Vietnamese;
                TestUi(Path.Combine(shotDir, "vi"));
                L.Current = Language.English;
                AppInfo.DeviceNames[0] = ""; AppInfo.DeviceNames[1] = "";
                TestUi(Path.Combine(shotDir, "en"));
            }
            Console.WriteLine();
            Console.WriteLine("KẾT QUẢ: " + passed + " đạt, " + failed + " lỗi");
            return failed == 0 ? 0 : 1;
        }

        static void TestKeyFilter()
        {
            KeyFilter f = new KeyFilter();
            Check(f.IsNewPress(Ev(MG, J, false)), "KeyDown đầu tiên được tính");
            Check(!f.IsNewPress(Ev(MG, J, false)), "Auto-repeat (KeyDown lặp khi giữ) bị chặn");
            Check(!f.IsNewPress(Ev(MG, J, false)), "Auto-repeat lần 2 bị chặn");
            Check(f.IsNewPress(Ev(M30, J, false)), "Cùng phím trên bàn phím khác được tính độc lập");
            Check(!f.IsNewPress(Ev(MG, J, true)), "KeyUp không phải lần nhấn");
            Check(f.IsNewPress(Ev(MG, J, false)), "Sau KeyUp, KeyDown mới được tính");
            Check(!f.IsNewPress(Ev(MG, 0xFF, false)), "VK 0xFF (phím giả của driver) bị bỏ qua");
            f.ForgetDevice(M30);
            Check(f.IsNewPress(Ev(M30, J, false)), "ForgetDevice xoá phím đang giữ của thiết bị bị rút");
        }

        static TestSession NewSession(int presses, int rounds, int first, bool alternate)
        {
            TestConfig c = new TestConfig();
            c.PressesPerDevice = presses; c.Rounds = rounds; c.FirstDevice = first; c.AlternateOrder = alternate;
            return new TestSession(c, MG, M30, 1000, F, new DateTime(2026, 11, 20, 10, 0, 0));
        }

        static void TestDefaultFlow()
        {
            TestSession s = NewSession(10, 1, AppInfo.B, true);
            Check(s.CurrentPhase.Device == AppInfo.B, "Giai đoạn 1 là M30 HE");
            long t = 1000 + Ms(500);
            double[] m30Gaps = { 0, 120, 118, 125, 130, 110, 115, 122, 119, 121 };
            FeedResult last = FeedResult.NotRunning;
            for (int i = 0; i < 10; i++)
            {
                t += Ms(m30Gaps[i]);
                last = s.FeedKeyDown(M30, J, t);
                if (i < 9) Check(last == FeedResult.Counted && s.CurrentPhase.Count == i + 1, "M30 HE lần " + (i + 1) + "/10 được ghi ngay");
            }
            Check(last == FeedResult.PhaseCompleted, "Lần thứ 10 hoàn thành giai đoạn 1");
            Check(s.CurrentPhase.Device == AppInfo.A && s.CurrentPhase.Count == 0, "Mục tiêu chuyển ngay sang MG75");
            Check(s.State == SessionState.Running, "Bài test vẫn chạy, không cần xác nhận");

            // lần nhấn MG75 đầu tiên chỉ 3 ms sau lần cuối của M30 HE vẫn phải được ghi
            t += Ms(3);
            FeedResult r = s.FeedKeyDown(MG, J, t);
            Check(r == FeedResult.Counted, "Lần nhấn đầu tiên của MG75 (ngay sau khi chuyển) không bị mất");
            Check(double.IsNaN(s.Presses[s.Presses.Count - 1].IntervalMs), "Không tính khoảng giữa M30 HE cuối và MG75 đầu");
            double[] mgGaps = { 100, 102, 98, 105, 99, 101, 97, 103, 100 };
            for (int i = 0; i < 9; i++) { t += Ms(mgGaps[i]); last = s.FeedKeyDown(MG, J, t); }
            Check(last == FeedResult.TestCompleted && s.State == SessionState.Completed, "MG75 đủ 10 lần thì kết thúc");
            Check(s.PressCount(AppInfo.A) == 10 && s.PressCount(AppInfo.B) == 10, "Mỗi bàn phím đúng 10 mẫu (không mất cân bằng)");
            Check(s.FeedKeyDown(MG, J, t + Ms(100)) == FeedResult.NotRunning, "Sau khi kết thúc, không ghi thêm");

            Analysis a = Analyzer.Analyze(s);
            Check(a.Devices[0].Stats.Count == 9 && a.Devices[1].Stats.Count == 9, "10 lần nhấn tạo đúng 9 khoảng mỗi bàn phím");
            Near(a.Devices[0].Stats.Mean, mgGaps.Average(), "Khoảng TB MG75");
            Near(a.Devices[1].Stats.Mean, m30Gaps.Skip(1).Average(), "Khoảng TB M30 HE (bỏ mốc đầu)");
            Near(a.Devices[0].PhaseDuration, mgGaps.Sum(), "Thời gian một lượt MG75 = lần đầu → lần cuối");
            Near(a.Devices[1].PhaseDuration, m30Gaps.Sum(), "Thời gian một lượt M30 HE không tính thời gian chờ trước lần nhấn đầu");
            Check(s.Presses.All(p => p.Device == (p.Handle == MG ? AppInfo.A : AppInfo.B)), "Mỗi bản ghi gắn đúng thiết bị theo handle");
        }

        static void TestWrongInputs()
        {
            TestSession s = NewSession(3, 1, AppInfo.B, false);
            long t = 2000;
            Check(s.FeedKeyDown(M30, J, 500) == FeedResult.NotRunning, "Sự kiện trước lúc bấm Bắt đầu bị bỏ qua");
            s.FeedKeyDown(M30, J, t += Ms(100));
            FeedResult r = s.FeedKeyDown(MG, J, t += Ms(50));
            Check(r == FeedResult.WrongDevice, "Nhấn MG75 trong giai đoạn M30 HE = nhấn nhầm");
            Check(s.PressCount(AppInfo.A) == 0 && s.WrongDeviceCount[AppInfo.A] == 1, "Nhấn nhầm không tính vào MG75");
            Check(s.CurrentPhase.Device == AppInfo.B && s.CurrentPhase.Count == 1 && s.State == SessionState.Running, "Nhấn nhầm không làm gián đoạn / đổi giai đoạn");
            Check(s.FeedKeyDown(M30, K, t += Ms(10)) == FeedResult.WrongKey && s.CurrentPhase.Count == 1, "Phím khác J trên đúng bàn phím không được tính");
            Check(s.FeedKeyDown(OTHER, J, t += Ms(10)) == FeedResult.UnknownDevice && s.UnknownDeviceCount == 1, "Bàn phím thứ ba bị bỏ qua");
            Check(s.FeedKeyDown(IntPtr.Zero, J, t += Ms(10)) == FeedResult.UnknownDevice, "Handle 0 (bàn phím ảo/injected) bị bỏ qua");
            s.FeedKeyDown(M30, J, t += Ms(100));
            Near(s.Presses[1].IntervalMs, 50 + 10 + 10 + 10 + 100,"Khoảng M30 HE tính giữa hai lần nhấn hợp lệ liên tiếp (bỏ qua sự kiện không hợp lệ)");
            Check(s.Events.Count == 6 && s.Events.Count(e => e.Status == EventStatus.Valid) == 2, "Nhật ký thô giữ mọi KeyDown kể cả bị bỏ qua");
        }

        static void TestRounds()
        {
            TestSession s = NewSession(3, 2, AppInfo.B, true);
            string order = String.Join(",", s.Phases.Select(p => AppInfo.Name(p.Device)).ToArray());
            Check(order == "M30 HE,MG75,MG75,M30 HE", "Đảo thứ tự: " + order);
            TestSession s2 = NewSession(3, 2, AppInfo.A, false);
            string order2 = String.Join(",", s2.Phases.Select(p => AppInfo.Name(p.Device)).ToArray());
            Check(order2 == "MG75,M30 HE,MG75,M30 HE", "Không đảo, MG75 trước: " + order2);

            long t = 5000;
            for (int i = 0; i < 3; i++) s.FeedKeyDown(M30, J, t += Ms(100));
            for (int i = 0; i < 3; i++) s.FeedKeyDown(MG, J, t += Ms(100));
            Check(s.CurrentPhase.Device == AppInfo.A && s.CurrentPhase.Round == 2, "Vòng 2 bắt đầu bằng MG75");
            s.FeedKeyDown(MG, J, t += Ms(80));
            Check(double.IsNaN(s.Presses[s.Presses.Count - 1].IntervalMs), "Không nối khoảng MG75 giữa vòng 1 và vòng 2");
            for (int i = 0; i < 2; i++) s.FeedKeyDown(MG, J, t += Ms(100));
            for (int i = 0; i < 3; i++) s.FeedKeyDown(M30, J, t += Ms(100));
            Check(s.State == SessionState.Completed, "Hoàn thành sau 4 giai đoạn");
            Analysis a = Analyzer.Analyze(s);
            Check(a.Devices[0].Stats.Count == 4 && a.Devices[1].Stats.Count == 4, "2 vòng × 3 lần = 2 × 2 khoảng mỗi bàn phím");
            Check(a.Devices[0].CompletedPhaseDurations.Count == 2, "Thời gian một lượt tính theo từng vòng");
        }

        static void TestStopAndReconnect()
        {
            TestSession s = NewSession(10, 1, AppInfo.B, true);
            long t = 1000;
            for (int i = 0; i < 4; i++) s.FeedKeyDown(M30, J, t += Ms(100));
            IntPtr newM30 = new IntPtr(0x4444);
            s.UpdateHandle(AppInfo.B, newM30);
            Check(s.FeedKeyDown(newM30, J, t += Ms(100)) == FeedResult.Counted, "Sau khi cắm lại (handle mới), M30 HE vẫn được nhận đúng");
            Check(s.FeedKeyDown(M30, J, t += Ms(100)) == FeedResult.UnknownDevice, "Handle cũ không còn được tính");
            s.UpdateHandle(AppInfo.B, MG);
            Check(s.GetHandle(AppInfo.B) == newM30, "Không cho gán handle của MG75 sang M30 HE");
            s.Stop(t += Ms(1000));
            Check(s.State == SessionState.Stopped && s.Presses.Count == 5, "Dừng giữa chừng giữ nguyên dữ liệu");
            Check(s.FeedKeyDown(newM30, J, t += Ms(100)) == FeedResult.NotRunning, "Sau khi dừng không ghi thêm");
            Analysis a = Analyzer.Analyze(s);
            Check(!a.Comparable && a.Headline.Contains("Chưa đủ dữ liệu"), "Dừng khi MG75 chưa có dữ liệu: không đưa ra so sánh");
            Check(a.Devices[1].PhaseDurationIsPartial, "Thời gian lượt chưa đủ được đánh dấu");
        }

        static void TestStats()
        {
            IntervalStats s = IntervalStats.From(new double[] { 140, 100, 180, 110, 170, 120, 160, 130, 150 });
            Near(s.Mean, 140, "Trung bình");
            Near(s.Median, 140, "Trung vị (lẻ)");
            Near(s.Min, 100, "Nhỏ nhất");
            Near(s.Max, 180, "Lớn nhất");
            Near(s.StdDev, Math.Sqrt(750), "Độ lệch chuẩn mẫu (n-1)");
            IntervalStats e = IntervalStats.From(new double[] { 10, 20, 30, 40 });
            Near(e.Median, 25, "Trung vị (chẵn)");
            Near(e.P95, 38.5, "P95 nội suy tuyến tính");
            IntervalStats one = IntervalStats.From(new double[] { 42 });
            Check(double.IsNaN(one.StdDev) && one.Mean == 42, "1 khoảng: không có độ lệch chuẩn");
            IntervalStats none = IntervalStats.From(new double[0]);
            Check(none.Count == 0 && double.IsNaN(none.Mean), "Không có dữ liệu");
            Near(IntervalStats.From(new double[] { 100, 100, 100, 100 }).PressesPerSecond, 10, "Tốc độ nhịp 100 ms = 10 lần/giây");
        }

        static TestSession Run(double[] mgGaps, double[] m30Gaps)
        {
            TestSession s = NewSession(mgGaps.Length + 1, 1, AppInfo.B, false);
            long t = 1000;
            s.FeedKeyDown(M30, J, t += Ms(1));
            foreach (double g in m30Gaps) s.FeedKeyDown(M30, J, t += Ms(g));
            s.FeedKeyDown(MG, J, t += Ms(1));
            foreach (double g in mgGaps) s.FeedKeyDown(MG, J, t += Ms(g));
            return s;
        }

        static void TestConclusion()
        {
            Analysis a = Analyzer.Analyze(Run(new double[] { 100, 100, 100, 100 }, new double[] { 120, 120, 120, 120 }));
            Check(a.Comparable && a.LowerMeanDevice == AppInfo.A, "MG75 có khoảng cách TB thấp hơn");
            Check(a.Headline.StartsWith("Trong lượt thử này, khoảng cách trung bình giữa các lần nhấn trên MG75 thấp hơn M30 HE 20.0 ms"), "Câu kết luận đúng mẫu: " + a.Headline);
            string all = (a.Headline + " " + String.Join(" ", a.Details.ToArray())).ToLowerInvariant();
            Check(!all.Contains("latency") && !all.Contains("độ trễ"), "Kết luận không nhắc latency / độ trễ phần cứng");
            Check(a.Details.Any(d => d.Contains("nhịp nhấn liên tục nhanh hơn trên MG75")), "Có câu về nhịp nhấn của người dùng");
            Check(a.Details.Any(d => d.Contains("Số mẫu còn ít")), "Cảnh báo số mẫu ít");

            Analysis tie = Analyzer.Analyze(Run(new double[] { 100, 101, 99, 100 }, new double[] { 101, 100, 100, 101 }));
            Check(tie.LowerMeanDevice == -1 && tie.Headline.Contains("gần như tương đương"), "Chênh < 3%: gần như tương đương");

            Analysis rev = Analyzer.Analyze(Run(new double[] { 90, 90, 90, 600 }, new double[] { 120, 120, 120, 120 }));
            Check(rev.LowerMeanDevice == AppInfo.B, "Khoảng nghỉ dài làm trung bình MG75 cao hơn");
            Check(rev.Details.Any(d => d.Contains("trung vị cho kết quả ngược lại")), "Báo khi trung vị ngược chiều trung bình");
        }

        static void TestVerdict()
        {
            Near(Math.Round(Verdict.TQuantile975(8), 2), 2.31, "t(0,975; df=8) ≈ 2,306");
            Near(Math.Round(Verdict.TQuantile975(30), 2), 2.04, "t(0,975; df=30) ≈ 2,042");

            TestSession run = Run(new double[] { 100, 101, 99, 100, 102, 98, 100, 101, 99 }, new double[] { 101, 100, 102, 99, 100, 101, 100, 99, 100 });
            Verdict tie = Analyzer.Analyze(run).Verdict;
            Check(tie.Ready && tie.Winner == -1 && tie.Headline == "Hai bàn phím ngang nhau", "Chênh < 3%: ngang nhau");

            // giống kết quả thật của người dùng: chênh ~4%, dao động lớn -> chưa chắc chắn
            Verdict close = Analyzer.Analyze(Run(new double[] { 128, 168, 136, 130, 150, 136, 140, 129, 140 },
                                                 new double[] { 140, 150, 146, 131, 160, 147, 145, 143, 147 })).Verdict;
            Check(close.Winner == AppInfo.A && close.Headline.StartsWith("Nhấn trên MG75 nhanh hơn"), "Người thắng + %: " + close.Headline);
            Check(!close.Clear && close.ConfidenceTitle == "Chưa đủ chắc chắn", "Chênh nhỏ so với dao động: chưa đủ chắc chắn");
            Check(close.NeededPresses > 10 && close.ConfidenceText.Contains("lần/bàn phím"), "Gợi ý số lần nhấn cần thiết: " + close.NeededPresses);
            Check(close.Criteria.Count == 3, "3 tiêu chí so sánh");

            Verdict clear = Analyzer.Analyze(Run(new double[] { 100, 102, 98, 101, 99, 100, 103, 97, 100 },
                                                 new double[] { 130, 128, 132, 131, 129, 130, 127, 133, 130 })).Verdict;
            Check(clear.Winner == AppInfo.A && clear.Clear && clear.ConfidenceTitle == "Khác biệt rõ ràng", "Chênh lớn, nhịp đều: khác biệt rõ ràng");
            Check(clear.Wins == 2, "MG75 hơn ở tốc độ TB và trung vị (độ đều ngang nhau)");
            string all = (clear.Headline + clear.Sub + clear.ConfidenceText).ToLowerInvariant();
            Check(!all.Contains("latency") && !all.Contains("độ trễ"), "Kết luận cuối không nói về độ trễ phần cứng");

            TestSession stopped = NewSession(10, 1, AppInfo.B, true);
            stopped.FeedKeyDown(M30, J, 2000); stopped.Stop(3000);
            Check(!Analyzer.Analyze(stopped).Verdict.Ready, "Dừng giữa chừng: không đưa kết luận cuối cùng");
            TestSession running = NewSession(3, 1, AppInfo.B, true);
            Check(!Analyzer.Analyze(running).Verdict.Ready, "Đang chạy: chưa đưa kết luận cuối cùng");
        }

        static bool HasVietnamese(string s)
        {
            foreach (char c in s) if ((c >= '\u00C0' && c <= '\u1EF9') && "×÷".IndexOf(c) < 0) return true;
            return false;
        }

        static void TestEnglish()
        {
            string[] saved = { AppInfo.DeviceNames[0], AppInfo.DeviceNames[1] };
            AppInfo.DeviceNames[0] = ""; AppInfo.DeviceNames[1] = "";
            L.Current = Language.Vietnamese;
            Check(AppInfo.Name(AppInfo.A) == "Bàn phím A" && AppInfo.Name(AppInfo.B) == "Bàn phím B", "Tên mặc định tiếng Việt: Bàn phím A / B");
            L.Current = Language.English;
            Check(AppInfo.Name(AppInfo.A) == "Keyboard A" && AppInfo.Name(AppInfo.B) == "Keyboard B", "English default names: Keyboard A / B");
            Check(AppInfo.CleanName("  A very very long keyboard product name\r\n ").Length <= AppInfo.MaxNameLength, "Names are trimmed to " + AppInfo.MaxNameLength + " characters");

            TestSession s = Run(new double[] { 100, 100, 100, 100 }, new double[] { 120, 120, 120, 120 });
            Analysis a = Analyzer.Analyze(s);
            Check(a.Headline.StartsWith("In this run, the average interval between presses on Keyboard A was 20.0 ms"), "English conclusion: " + a.Headline);
            Check(a.Verdict.Ready && a.Verdict.Headline == "You tapped 16.7% faster on Keyboard A", "English verdict: " + a.Verdict.Headline);
            string all = a.Headline + " " + String.Join(" ", a.Details.ToArray()) + " " + a.Verdict.Headline + " " + a.Verdict.Sub + " " +
                         a.Verdict.ConfidenceTitle + " " + a.Verdict.ConfidenceText + " " + String.Join(" ", a.Verdict.Criteria.Select(c => c.Name + " " + c.Detail).ToArray());
            Check(!HasVietnamese(all), "English results contain no Vietnamese text");
            Check(!all.ToLowerInvariant().Contains("latency"), "English verdict makes no latency claim");
            string csv = CsvExporter.Build(s, a, null, new CultureInfo("en-US", false), DateTime.Now);
            Check(csv.Contains("[TEST SETTINGS]") && csv.Contains("Average interval (ms),100.00,120.00") && csv.Contains("[RAW EVENT LOG]"), "English CSV sections and metrics");
            Check(!HasVietnamese(csv), "English CSV contains no Vietnamese text");
            AppInfo.DeviceNames[0] = saved[0]; AppInfo.DeviceNames[1] = saved[1];
        }

        static void TestCsv()
        {
            TestSession s = Run(new double[] { 100, 105, 95 }, new double[] { 120, 125, 115 });
            Analysis a = Analyzer.Analyze(s);
            DeviceDescriptor[] dd = new DeviceDescriptor[2];
            dd[0] = new DeviceDescriptor(); dd[0].Path = @"\\?\HID#VID_1234&PID_0001&MI_00#x"; dd[0].Vid = "1234"; dd[0].Pid = "0001"; dd[0].Product = "MG75, \"Pro\"";
            string en = CsvExporter.Build(s, a, dd, new CultureInfo("en-US", false), DateTime.Now);
            string vi = CsvExporter.Build(s, a, dd, new CultureInfo("vi-VN", false), DateTime.Now);
            Check(en.Contains("[CẤU HÌNH BÀI TEST]") && en.Contains("[THỐNG KÊ TỔNG HỢP]") && en.Contains("[PHƯƠNG PHÁP ĐO VÀ GIỚI HẠN]") &&
                  en.Contains("[DỮ LIỆU TỪNG LẦN NHẤN HỢP LỆ]") && en.Contains("[NHẬT KÝ SỰ KIỆN THÔ]"), "CSV có đủ các phần");
            Check(en.Contains("Khoảng cách trung bình (ms),100.00,120.00"), "en-US: dấu phẩy phân cách, dấu chấm thập phân");
            Check(vi.Contains("Khoảng cách trung bình (ms),\"100,00\",\"120,00\""), "vi-VN (Windows: phân cách \",\" và thập phân \",\"): số được bao ngoặc kép");
            string de = CsvExporter.Build(s, a, dd, new CultureInfo("de-DE", false), DateTime.Now);
            Check(de.Contains("Khoảng cách trung bình (ms);100,00;120,00"), "de-DE: dấu chấm phẩy phân cách, dấu phẩy thập phân");
            Console.WriteLine("        (cài đặt vùng máy này: phân cách \"" + CultureInfo.CurrentCulture.TextInfo.ListSeparator + "\", thập phân \"" + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "\")");
            Check(en.Contains("\"MG75, \"\"Pro\"\"\""), "Ô chứa dấu phẩy / ngoặc kép được bao đúng chuẩn CSV");
            int dataRows = en.Split(new[] { "\r\n" }, StringSplitOptions.None).Count(l => l.Length > 0 && char.IsDigit(l[0]) && l.Contains(",J,"));
            Check(dataRows == 8,"Có dữ liệu từng lần nhấn (" + dataRows + " dòng)");
            Check(en.Contains(AppInfo.MethodNote), "CSV ghi chú giới hạn phép đo");
            string tmp = Path.Combine(Path.GetTempPath(), "kst_test.csv");
            File.WriteAllText(tmp, vi, new System.Text.UTF8Encoding(true));
            byte[] head = File.ReadAllBytes(tmp).Take(3).ToArray();
            Check(head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF, "File CSV có UTF-8 BOM (Excel hiển thị tiếng Việt đúng)");
            File.Delete(tmp);
        }

        // ---------- Raw Input ----------
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);

        static void SendKey(ushort vk, bool up)
        {
            INPUT[] i = new INPUT[1];
            i[0].type = 1;
            i[0].u.ki.wVk = vk;
            i[0].u.ki.dwFlags = up ? 2u : 0u;
            SendInput(1, i, Marshal.SizeOf(typeof(INPUT)));
        }

        static void TestListener()
        {
            List<RawKeyEvent> got = new List<RawKeyEvent>();
            int threadId = -1;
            using (RawInputListener l = new RawInputListener())
            {
                l.KeyEvent += e => { lock (got) { got.Add(e); threadId = Thread.CurrentThread.ManagedThreadId; } };
                Check(l.Start(), "Đăng ký Raw Input trên luồng riêng thành công");
                long before = Stopwatch.GetTimestamp();
                const ushort F24 = 0x87;
                SendKey(F24, false); SendKey(F24, true); SendKey(F24, false); SendKey(F24, true);
                for (int i = 0; i < 100; i++) { lock (got) if (got.Count(e => e.VKey == F24) >= 4) break; Thread.Sleep(10); }
                List<RawKeyEvent> f24;
                lock (got) f24 = got.Where(e => e.VKey == F24).ToList();
                Check(f24.Count == 4, "Nhận đủ 4 sự kiện WM_INPUT (2 KeyDown + 2 KeyUp), có " + f24.Count);
                Check(f24.Count == 4 && !f24[0].IsBreak && f24[1].IsBreak && !f24[2].IsBreak && f24[3].IsBreak, "Phân biệt đúng KeyDown / KeyUp (RI_KEY_BREAK)");
                bool mono = true;
                for (int i = 1; i < f24.Count; i++) if (f24[i].Timestamp < f24[i - 1].Timestamp) mono = false;
                Check(f24.Count > 0 && f24[0].Timestamp >= before && mono, "Timestamp Stopwatch đơn điệu, lấy sau thời điểm gửi");
                Check(threadId != Thread.CurrentThread.ManagedThreadId, "Sự kiện được xử lý trên luồng Raw Input riêng");
                KeyFilter kf = new KeyFilter();
                int presses = f24.Count(e => kf.IsNewPress(e));
                Check(presses == 2, "Qua bộ lọc: 2 lần nhấn thực tế");
                Dictionary<IntPtr, string> kbs = DeviceInfo.ListKeyboards();
                Console.WriteLine("        (Windows đang thấy " + kbs.Count + " bàn phím Raw Input)");
                foreach (KeyValuePair<IntPtr, string> kv in kbs)
                    Console.WriteLine("        0x" + kv.Key.ToInt64().ToString("X") + "  " + DeviceInfo.ShortName(DeviceInfo.Describe("", kv.Key, kv.Value)));
            }
        }

        // ---------- Giao diện ----------
        static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(5); }
        }

        static void Shot(Form f, string dir, string name)
        {
            Pump(150);
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                b.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
            Console.WriteLine("        ảnh: " + name + ".png");
        }

        static void Press(MainForm f, IntPtr dev, ushort vk)
        {
            RawKeyEvent d = Ev(dev, vk, false); d.Timestamp = Stopwatch.GetTimestamp();
            f.TestInject(d);
            Pump(25);
            RawKeyEvent u = Ev(dev, vk, true); u.Timestamp = Stopwatch.GetTimestamp();
            f.TestInject(u);
        }

        static void TestUi(string dir)
        {
            Directory.CreateDirectory(dir);
            Program.RunInitOnly();
            Size[] sizes = new Size[] { new Size(1366, 728), new Size(1280, 860), new Size(1000, 640) };
            using (MainForm f = new MainForm())
            {
                f.StartPosition = FormStartPosition.Manual;
                f.WindowState = FormWindowState.Normal;
                f.Location = new Point(-20000, -20000);
                f.Size = sizes[0];
                f.Show();
                Pump(300);
                Shot(f, dir, "01_chua_nhan_dien");
                f.TestIdentify(AppInfo.A);
                Shot(f, dir, "02_dang_nhan_dien");
                f.TestIdentify(AppInfo.A);
                f.TestAssign(AppInfo.A, MG, @"\\?\HID#VID_258A&PID_0049&MI_00#7&1a2b3c&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}");
                f.TestAssign(AppInfo.B, M30, @"\\?\HID#VID_3151&PID_4015&MI_00#7&4d5e6f&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}");
                Check(AppInfo.Name(AppInfo.A) == "VID 258A PID 0049", "UI: tên tự lấy từ HID (khi không có tên sản phẩm thì dùng VID/PID): " + AppInfo.Name(AppInfo.A));
                f.TestSetName(AppInfo.A, "MonsGeek Keyboard"); f.TestSetName(AppInfo.B, "M30 HE");
                Check(AppInfo.Name(AppInfo.A) == "MonsGeek Keyboard", "UI: đổi tên bàn phím");
                Shot(f, dir, "03_san_sang");
                f.TestStart();
                Check(f.TestSessionObject != null && f.TestSessionObject.State == SessionState.Running, "UI: bấm Bắt đầu tạo phiên test");
                Random rnd = new Random(7);
                for (int i = 0; i < 4; i++) { Press(f, MG, J); Pump(60 + rnd.Next(60)); }
                Press(f, M30, J); // nhấn nhầm
                Pump(50);
                RawKeyEvent hold = Ev(MG, J, false);
                for (int i = 0; i < 5; i++) { hold.Timestamp = Stopwatch.GetTimestamp(); f.TestInject(hold); Pump(10); } // giữ phím: 1 lần
                RawKeyEvent rel = Ev(MG, J, true); rel.Timestamp = Stopwatch.GetTimestamp(); f.TestInject(rel);
                Pump(80);
                Check(f.TestSessionObject.CurrentPhase.Count == 5, "UI: giữ phím chỉ tính 1 lần, nhấn nhầm không tính (đếm = " + f.TestSessionObject.CurrentPhase.Count + ")");
                Shot(f, dir, "04_dang_test_a");
                for (int i = 0; i < 5; i++) { Press(f, MG, J); Pump(60 + rnd.Next(60)); }
                Pump(50);
                Check(f.TestSessionObject.CurrentPhase.Device == AppInfo.B, "UI: tự chuyển sang bàn phím B sau 10 lần trên A");
                for (int i = 0; i < 3; i++) { Press(f, M30, J); Pump(50 + rnd.Next(50)); }
                Pump(60);
                Shot(f, dir, "05_dang_test_b");
                for (int i = 0; i < 7; i++) { Press(f, M30, J); Pump(50 + rnd.Next(50)); }
                Pump(200);
                TestSession s = f.TestSessionObject;
                Check(s.State == SessionState.Completed && s.PressCount(0) == 10 && s.PressCount(1) == 10, "UI: hoàn thành 10/10 mỗi bàn phím");
                Shot(f, dir, "06_ket_qua");
                Console.WriteLine("        " + f.TestLayoutInfo());
                f.TestToggleAdvanced();
                Shot(f, dir, "07_thong_ke_mo_rong");
                f.TestSelectTab(1);
                Shot(f, dir, "08_bieu_do");
                f.TestSelectTab(2);
                Shot(f, dir, "09_lich_su");
                f.TestSelectTab(0);
                f.TestToggleAdvanced();
                for (int i = 1; i < sizes.Length; i++)
                {
                    f.Size = sizes[i];
                    Pump(200);
                    Shot(f, dir, "10_kich_thuoc_" + sizes[i].Width + "x" + sizes[i].Height);
                    Console.WriteLine("        " + f.TestLayoutInfo());
                }
                f.TestMarkExported();
                f.Close();
            }
        }
    }
}
