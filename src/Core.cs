// Keyboard Speed Test
// Core: data model, continuous test session, statistics, conclusions, CSV export.
// No UI dependency so it can be unit-tested.
// Note: C# 5 syntax only (Windows PowerShell 5.1 Add-Type uses the .NET Framework compiler).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KeyboardSpeedTest
{
    public static class AppInfo
    {
        public const string Version = "2.1";
        public const int A = 0;
        public const int B = 1;
        public const int MaxNameLength = 24;

        /// <summary>Display names chosen for the two slots. Empty = default "Keyboard A / B".</summary>
        public static readonly string[] DeviceNames = new string[] { "", "" };

        public static string DefaultName(int device)
        {
            return L.S("Bàn phím ", "Keyboard ") + (device == B ? "B" : "A");
        }

        public static string Name(int device)
        {
            if (device < 0 || device > 1) return L.S("Thiết bị khác", "Other device");
            string n = DeviceNames[device];
            return String.IsNullOrEmpty(n) ? DefaultName(device) : n;
        }

        public static string CleanName(string name)
        {
            if (name == null) return "";
            string s = name.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).Trim();
            return s;
        }

        public static int Other(int device) { return device == A ? B : A; }

        public static string VersusTitle { get { return Name(A) + " vs " + Name(B); } }

        public static string MethodNote
        {
            get
            {
                return L.S("Kết quả phản ánh nhịp nhấn của người dùng và thời điểm Windows nhận sự kiện. " +
                           "Đây không phải phép đo latency thuần của switch hoặc USB.",
                           "Results reflect your tapping rhythm and the moment Windows delivers each key event. " +
                           "This is not a measurement of pure switch or USB latency.");
            }
        }
    }

    /// <summary>A raw keyboard event read from WM_INPUT. Timestamp = Stopwatch ticks taken as soon as WM_INPUT arrives.</summary>
    public struct RawKeyEvent
    {
        public long Timestamp;
        public IntPtr Device;
        public ushort VKey;
        public ushort MakeCode;
        public ushort Flags;
        public bool IsBreak;
    }

    /// <summary>
    /// Lets through only the first KeyDown of each physical press. Repeated KeyDowns (auto-repeat while held)
    /// are blocked until the KeyUp of that key on that device arrives.
    /// </summary>
    public sealed class KeyFilter
    {
        private const ushort RI_KEY_E0 = 0x0002;
        private readonly HashSet<string> down = new HashSet<string>();

        private static string Id(IntPtr device, ushort vkey, ushort makeCode, ushort flags)
        {
            return device.ToInt64().ToString(CultureInfo.InvariantCulture) + ":" + vkey + ":" + makeCode + ":" + (flags & RI_KEY_E0);
        }

        /// <returns>true if this is a new press (valid KeyDown).</returns>
        public bool IsNewPress(RawKeyEvent e)
        {
            if (e.VKey == 0xFF) return false; // fake key injected by some drivers
            string id = Id(e.Device, e.VKey, e.MakeCode, e.Flags);
            if (e.IsBreak)
            {
                down.Remove(id);
                return false;
            }
            return down.Add(id);
        }

        public void ForgetDevice(IntPtr device)
        {
            string prefix = device.ToInt64().ToString(CultureInfo.InvariantCulture) + ":";
            down.RemoveWhere(s => s.StartsWith(prefix, StringComparison.Ordinal));
        }

        public int HeldCount { get { return down.Count; } }
    }

    public sealed class TestConfig
    {
        public int PressesPerDevice = 10;
        public int Rounds = 1;
        public int FirstDevice = AppInfo.A;
        public bool AlternateOrder = true;
        public ushort TestKey = 0x4A; // J
        public string TestKeyName = "J";

        public string OrderText
        {
            get { return AppInfo.Name(FirstDevice) + " → " + AppInfo.Name(AppInfo.Other(FirstDevice)); }
        }
    }

    public sealed class PhaseInfo
    {
        public int Index;       // 0-based within the whole test
        public int Round;       // 1-based
        public int Device;
        public int Count;
        public long FirstTimestamp;
        public long LastTimestamp;
        public bool Completed;
    }

    public sealed class PressRecord
    {
        public int Seq;             // order among valid presses of the whole test
        public int Round;
        public int PhaseIndex;
        public int Device;
        public int PressInPhase;    // 1..N
        public IntPtr Handle;
        public ushort VKey;
        public string KeyName;
        public long Timestamp;
        public double MsFromStart;
        public double IntervalMs = double.NaN; // NaN for the first press of each phase
    }

    public enum EventStatus { Valid, WrongDevice, WrongKey, UnknownDevice }

    public sealed class EventRecord
    {
        public int Seq;
        public long Timestamp;
        public double MsFromStart;
        public int Device;          // -1 = not one of the two identified keyboards
        public IntPtr Handle;
        public ushort VKey;
        public int Round;
        public int PhaseIndex;
        public int TargetDevice;
        public EventStatus Status;
        public PressRecord Press;   // != null when valid
    }

    public enum SessionState { Running, Completed, Stopped }

    public enum FeedResult { NotRunning, Counted, PhaseCompleted, TestCompleted, WrongDevice, WrongKey, UnknownDevice }

    /// <summary>
    /// Continuous test: one phase per keyboard (N presses each), phases run back to back.
    /// No timers and no waiting: every valid KeyDown is recorded the moment it is fed in.
    /// Intervals are only computed between consecutive presses of the same keyboard within the same phase.
    /// </summary>
    public sealed class TestSession
    {
        public readonly TestConfig Config;
        public readonly List<PhaseInfo> Phases;
        public readonly List<PressRecord> Presses = new List<PressRecord>();
        public readonly List<EventRecord> Events = new List<EventRecord>();
        public readonly long StartTimestamp;
        public readonly long TickFrequency;
        public readonly DateTime StartWallClock;
        public readonly int[] WrongDeviceCount = new int[2];
        public int WrongKeyCount;
        public int UnknownDeviceCount;
        public SessionState State = SessionState.Running;
        public int CurrentPhaseIndex;
        public long EndTimestamp;
        private readonly IntPtr[] handles = new IntPtr[2];

        public TestSession(TestConfig config, IntPtr deviceA, IntPtr deviceB, long startTimestamp, long tickFrequency, DateTime startWallClock)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.PressesPerDevice < 2) throw new ArgumentException("PressesPerDevice must be >= 2.");
            if (config.Rounds < 1) throw new ArgumentException("Rounds must be >= 1.");
            if (deviceA == IntPtr.Zero || deviceB == IntPtr.Zero || deviceA == deviceB) throw new ArgumentException("Two different devices are required.");
            Config = config;
            handles[AppInfo.A] = deviceA;
            handles[AppInfo.B] = deviceB;
            StartTimestamp = startTimestamp;
            TickFrequency = tickFrequency;
            StartWallClock = startWallClock;
            Phases = BuildPhases(config);
        }

        public static List<PhaseInfo> BuildPhases(TestConfig config)
        {
            List<PhaseInfo> list = new List<PhaseInfo>();
            for (int r = 0; r < config.Rounds; r++)
            {
                int first = (config.AlternateOrder && r % 2 == 1) ? AppInfo.Other(config.FirstDevice) : config.FirstDevice;
                PhaseInfo a = new PhaseInfo(); a.Index = list.Count; a.Round = r + 1; a.Device = first; list.Add(a);
                PhaseInfo b = new PhaseInfo(); b.Index = list.Count; b.Round = r + 1; b.Device = AppInfo.Other(first); list.Add(b);
            }
            return list;
        }

        public PhaseInfo CurrentPhase
        {
            get { return CurrentPhaseIndex >= 0 && CurrentPhaseIndex < Phases.Count ? Phases[CurrentPhaseIndex] : null; }
        }

        public int TargetPerDevice { get { return Config.PressesPerDevice * Config.Rounds; } }

        public IntPtr GetHandle(int device) { return handles[device]; }

        /// <summary>When a keyboard is re-plugged Windows assigns a new handle; call this to keep tracking it.</summary>
        public void UpdateHandle(int device, IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            if (handle == handles[AppInfo.Other(device)]) return;
            handles[device] = handle;
        }

        public int ResolveDevice(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return -1;
            if (handle == handles[AppInfo.A]) return AppInfo.A;
            if (handle == handles[AppInfo.B]) return AppInfo.B;
            return -1;
        }

        public double TicksToMs(long ticks) { return ticks * 1000.0 / TickFrequency; }

        public int PressCount(int device)
        {
            int n = 0;
            foreach (PressRecord p in Presses) if (p.Device == device) n++;
            return n;
        }

        public FeedResult FeedKeyDown(IntPtr handle, ushort vkey, long timestamp)
        {
            if (State != SessionState.Running) return FeedResult.NotRunning;
            if (timestamp < StartTimestamp) return FeedResult.NotRunning; // happened before Start was clicked
            PhaseInfo phase = CurrentPhase;
            if (phase == null) return FeedResult.NotRunning;

            int device = ResolveDevice(handle);
            EventRecord ev = new EventRecord();
            ev.Seq = Events.Count + 1;
            ev.Timestamp = timestamp;
            ev.MsFromStart = TicksToMs(timestamp - StartTimestamp);
            ev.Device = device;
            ev.Handle = handle;
            ev.VKey = vkey;
            ev.Round = phase.Round;
            ev.PhaseIndex = phase.Index;
            ev.TargetDevice = phase.Device;
            Events.Add(ev);

            if (device < 0)
            {
                ev.Status = EventStatus.UnknownDevice;
                UnknownDeviceCount++;
                return FeedResult.UnknownDevice;
            }
            if (device != phase.Device)
            {
                // Pressed on the keyboard whose turn it is not: log it, do NOT count it, do NOT interrupt the test.
                ev.Status = EventStatus.WrongDevice;
                WrongDeviceCount[device]++;
                return FeedResult.WrongDevice;
            }
            if (vkey != Config.TestKey)
            {
                ev.Status = EventStatus.WrongKey;
                WrongKeyCount++;
                return FeedResult.WrongKey;
            }

            PressRecord press = new PressRecord();
            press.Seq = Presses.Count + 1;
            press.Round = phase.Round;
            press.PhaseIndex = phase.Index;
            press.Device = device;
            press.PressInPhase = phase.Count + 1;
            press.Handle = handle;
            press.VKey = vkey;
            press.KeyName = Config.TestKeyName;
            press.Timestamp = timestamp;
            press.MsFromStart = ev.MsFromStart;
            if (phase.Count == 0) phase.FirstTimestamp = timestamp;
            else press.IntervalMs = TicksToMs(timestamp - phase.LastTimestamp);
            phase.LastTimestamp = timestamp;
            phase.Count++;
            Presses.Add(press);
            ev.Status = EventStatus.Valid;
            ev.Press = press;

            if (phase.Count >= Config.PressesPerDevice)
            {
                phase.Completed = true;
                CurrentPhaseIndex++; // switch target immediately; the very next event is judged against the new phase
                if (CurrentPhaseIndex >= Phases.Count)
                {
                    State = SessionState.Completed;
                    EndTimestamp = timestamp;
                    return FeedResult.TestCompleted;
                }
                return FeedResult.PhaseCompleted;
            }
            return FeedResult.Counted;
        }

        public void Stop(long timestamp)
        {
            if (State != SessionState.Running) return;
            State = SessionState.Stopped;
            EndTimestamp = timestamp;
        }
    }

    public sealed class IntervalStats
    {
        public int Count;
        public double Sum, Mean = double.NaN, Median = double.NaN, Min = double.NaN, Max = double.NaN;
        public double StdDev = double.NaN; // sample standard deviation (n-1)
        public double P95 = double.NaN;    // linear interpolation

        public double Cv { get { return Count >= 2 && Mean > 0 ? StdDev / Mean : double.NaN; } }
        public double PressesPerSecond { get { return Sum > 0 ? Count * 1000.0 / Sum : double.NaN; } }

        public static IntervalStats From(IEnumerable<double> values)
        {
            IntervalStats s = new IntervalStats();
            List<double> sorted = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
            s.Count = sorted.Count;
            if (s.Count == 0) return s;
            s.Sum = sorted.Sum();
            s.Mean = s.Sum / s.Count;
            s.Min = sorted[0];
            s.Max = sorted[s.Count - 1];
            s.Median = Percentile(sorted, 0.5);
            s.P95 = Percentile(sorted, 0.95);
            if (s.Count >= 2)
            {
                double mean = s.Mean;
                double ss = sorted.Sum(v => (v - mean) * (v - mean));
                s.StdDev = Math.Sqrt(ss / (s.Count - 1));
            }
            return s;
        }

        public static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return double.NaN;
            if (sorted.Count == 1) return sorted[0];
            double pos = p * (sorted.Count - 1);
            int lo = (int)Math.Floor(pos);
            int hi = (int)Math.Ceiling(pos);
            double frac = pos - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
        }
    }

    public sealed class DeviceResult
    {
        public int Device;
        public int Presses;
        public int Target;
        public List<double> Intervals = new List<double>();
        public IntervalStats Stats = IntervalStats.From(new double[0]);
        public List<double> CompletedPhaseDurations = new List<double>();
        public double PartialDuration = double.NaN; // phase stopped before reaching N presses
        public int WrongDevicePresses;              // presses on this keyboard while it was not its turn
        public int LongPauses;
        public double LongPauseThreshold = double.NaN;

        public double PhaseDuration
        {
            get
            {
                if (CompletedPhaseDurations.Count > 0) return CompletedPhaseDurations.Average();
                return PartialDuration;
            }
        }

        public bool PhaseDurationIsPartial { get { return CompletedPhaseDurations.Count == 0 && !double.IsNaN(PartialDuration); } }

        /// <summary>Plain-language consistency label based on coefficient of variation (SD / mean).</summary>
        public string StabilityLabel
        {
            get
            {
                double cv = Stats == null ? double.NaN : Stats.Cv;
                if (double.IsNaN(cv)) return L.S("Chưa đủ dữ liệu", "Not enough data");
                if (cv < 0.10) return L.S("Rất đều", "Very steady");
                if (cv < 0.20) return L.S("Đều", "Steady");
                if (cv < 0.35) return L.S("Chưa đều", "Uneven");
                return L.S("Không đều", "Irregular");
            }
        }
    }

    public sealed class Analysis
    {
        public DeviceResult[] Devices = new DeviceResult[2];
        public bool Comparable;
        public string Headline;
        public List<string> Details = new List<string>();
        public int LowerMeanDevice = -1; // device with the lower mean interval, -1 if tie / not comparable
        public Verdict Verdict = new Verdict();
    }

    public static class Fmt
    {
        public static CultureInfo Culture = CultureInfo.CurrentCulture;
        public const string Dash = "—";

        public static string Num(double v, int decimals)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return Dash;
            return v.ToString("F" + decimals, Culture);
        }

        public static string Ms(double v) { return double.IsNaN(v) ? Dash : Num(v, 1) + " ms"; }

        public static string Duration(double ms)
        {
            if (double.IsNaN(ms)) return Dash;
            if (ms >= 1000) return Num(ms / 1000.0, 2) + " s";
            return Num(ms, 0) + " ms";
        }

        public static string Percent(double ratio) { return double.IsNaN(ratio) ? Dash : Num(ratio * 100.0, 1) + "%"; }
    }

    public static class Analyzer
    {
        public const int MinIntervalsForP95 = 20;
        public const int RecommendedIntervals = 20;

        public static Analysis Analyze(TestSession s)
        {
            Analysis a = new Analysis();
            for (int d = 0; d < 2; d++)
            {
                DeviceResult r = new DeviceResult();
                r.Device = d;
                r.Target = s == null ? 0 : s.TargetPerDevice;
                a.Devices[d] = r;
            }
            if (s == null)
            {
                a.Headline = L.S("Chưa có dữ liệu. Nhận diện hai bàn phím rồi bấm BẮT ĐẦU TEST.",
                                 "No data yet. Identify both keyboards, then click START TEST.");
                return a;
            }

            foreach (PressRecord p in s.Presses)
            {
                DeviceResult r = a.Devices[p.Device];
                r.Presses++;
                if (!double.IsNaN(p.IntervalMs)) r.Intervals.Add(p.IntervalMs);
            }
            foreach (PhaseInfo ph in s.Phases)
            {
                if (ph.Count < 2) continue;
                double dur = s.TicksToMs(ph.LastTimestamp - ph.FirstTimestamp);
                if (ph.Completed) a.Devices[ph.Device].CompletedPhaseDurations.Add(dur);
                else a.Devices[ph.Device].PartialDuration = dur;
            }
            for (int d = 0; d < 2; d++)
            {
                DeviceResult r = a.Devices[d];
                r.Stats = IntervalStats.From(r.Intervals);
                r.WrongDevicePresses = s.WrongDeviceCount[d];
                if (r.Stats.Count >= 3)
                {
                    r.LongPauseThreshold = Math.Max(3.0 * r.Stats.Median, r.Stats.Median + 250.0);
                    double thr = r.LongPauseThreshold;
                    r.LongPauses = r.Intervals.Count(v => v > thr);
                }
            }

            BuildConclusion(s, a);
            a.Verdict = Verdict.Build(s, a);
            return a;
        }

        private static void BuildConclusion(TestSession s, Analysis a)
        {
            DeviceResult ra = a.Devices[AppInfo.A];
            DeviceResult rb = a.Devices[AppInfo.B];
            string na = AppInfo.Name(AppInfo.A), nb = AppInfo.Name(AppInfo.B);
            if (ra.Stats.Count < 2 || rb.Stats.Count < 2)
            {
                a.Comparable = false;
                if (s.Presses.Count == 0)
                    a.Headline = s.State == SessionState.Running ? L.S("Đang chờ lần nhấn đầu tiên...", "Waiting for the first press...")
                                                                 : L.S("Chưa có lần nhấn hợp lệ nào.", "No valid presses yet.");
                else
                    a.Headline = L.S("Chưa đủ dữ liệu để so sánh: mỗi bàn phím cần ít nhất 3 lần nhấn (2 khoảng thời gian).",
                                     "Not enough data to compare: each keyboard needs at least 3 presses (2 intervals).");
                if (s.State == SessionState.Stopped)
                    a.Details.Add(L.S("Bài test đã dừng trước khi hoàn thành. Dữ liệu đã thu thập vẫn được giữ lại.",
                                      "The test was stopped before finishing. Collected data has been kept."));
                return;
            }

            a.Comparable = true;
            double diff = rb.Stats.Mean - ra.Stats.Mean;          // > 0: A has the shorter interval
            double rel = Math.Abs(diff) / Math.Min(ra.Stats.Mean, rb.Stats.Mean);
            if (rel < 0.03)
            {
                a.LowerMeanDevice = -1;
                a.Headline = L.S("Trong lượt thử này, khoảng cách trung bình giữa các lần nhấn trên " + na + " và " + nb +
                                 " gần như tương đương (chênh " + Fmt.Ms(Math.Abs(diff)) + ", " + Fmt.Percent(rel) + ").",
                                 "In this run, the average interval between presses on " + na + " and " + nb +
                                 " is practically the same (difference " + Fmt.Ms(Math.Abs(diff)) + ", " + Fmt.Percent(rel) + ").");
            }
            else
            {
                int lower = diff > 0 ? AppInfo.A : AppInfo.B;
                int higher = AppInfo.Other(lower);
                a.LowerMeanDevice = lower;
                a.Headline = L.S("Trong lượt thử này, khoảng cách trung bình giữa các lần nhấn trên " + AppInfo.Name(lower) +
                                 " thấp hơn " + AppInfo.Name(higher) + " " + Fmt.Ms(Math.Abs(diff)) + " (" + Fmt.Percent(rel) + ").",
                                 "In this run, the average interval between presses on " + AppInfo.Name(lower) +
                                 " was " + Fmt.Ms(Math.Abs(diff)) + " (" + Fmt.Percent(rel) + ") lower than on " + AppInfo.Name(higher) + ".");
                a.Details.Add(L.S("Người dùng thực hiện nhịp nhấn liên tục nhanh hơn trên " + AppInfo.Name(lower) + " trong lượt thử này.",
                                  "You tapped faster on " + AppInfo.Name(lower) + " in this run."));

                double medDiff = rb.Stats.Median - ra.Stats.Median;
                if (medDiff != 0 && Math.Sign(medDiff) != Math.Sign(diff))
                {
                    string medLower = AppInfo.Name(diff > 0 ? AppInfo.B : AppInfo.A);
                    a.Details.Add(L.S("Lưu ý: trung vị cho kết quả ngược lại (" + medLower + " có trung vị thấp hơn). Chênh lệch trung bình có thể do một vài khoảng nghỉ dài.",
                                      "Note: the median points the other way (" + medLower + " has the lower median). The average difference may come from a few long pauses."));
                }
            }

            double sdA = ra.Stats.StdDev, sdB = rb.Stats.StdDev;
            if (!double.IsNaN(sdA) && !double.IsNaN(sdB) && Math.Max(sdA, sdB) > 0)
            {
                if (Math.Abs(sdA - sdB) / Math.Max(sdA, sdB) >= 0.10)
                {
                    string steadier = AppInfo.Name(sdA < sdB ? AppInfo.A : AppInfo.B);
                    a.Details.Add(L.S("Nhịp nhấn trên " + steadier + " đều hơn (độ lệch chuẩn " + Fmt.Ms(Math.Min(sdA, sdB)) + " so với " + Fmt.Ms(Math.Max(sdA, sdB)) + ").",
                                      "Your rhythm on " + steadier + " was steadier (standard deviation " + Fmt.Ms(Math.Min(sdA, sdB)) + " vs " + Fmt.Ms(Math.Max(sdA, sdB)) + ")."));
                }
                else a.Details.Add(L.S("Độ đều của nhịp nhấn trên hai bàn phím tương tự nhau.", "Rhythm consistency was similar on both keyboards."));
            }

            if (ra.LongPauses > 0 || rb.LongPauses > 0)
                a.Details.Add(L.S("Có khoảng nghỉ dài bất thường (" + na + ": " + ra.LongPauses + ", " + nb + ": " + rb.LongPauses + "). Hãy xem trung vị để đánh giá nhịp nhấn điển hình.",
                                  "Unusually long pauses detected (" + na + ": " + ra.LongPauses + ", " + nb + ": " + rb.LongPauses + "). Check the median for your typical rhythm."));
            if (ra.Stats.Count < RecommendedIntervals || rb.Stats.Count < RecommendedIntervals)
                a.Details.Add(L.S("Số mẫu còn ít (" + ra.Stats.Count + " và " + rb.Stats.Count + " khoảng). Tăng số lần nhấn hoặc số vòng để kết quả ổn định hơn.",
                                  "Small sample (" + ra.Stats.Count + " and " + rb.Stats.Count + " intervals). Use more presses or rounds for more stable results."));
            if (ra.Presses != rb.Presses)
                a.Details.Add(L.S("Số lần nhấn của hai bàn phím chưa bằng nhau (" + na + ": " + ra.Presses + ", " + nb + ": " + rb.Presses + ").",
                                  "The two keyboards have different press counts (" + na + ": " + ra.Presses + ", " + nb + ": " + rb.Presses + ")."));
            if (s.State == SessionState.Stopped)
                a.Details.Add(L.S("Bài test đã dừng trước khi hoàn thành. Kết quả chỉ dựa trên dữ liệu đã thu được.",
                                  "The test was stopped before finishing. Results only use the data collected so far."));
            if (s.State == SessionState.Running)
                a.Details.Insert(0, L.S("Kết quả tạm thời – bài test đang chạy, số liệu còn thay đổi đến khi kết thúc.",
                                        "Provisional – the test is still running, numbers will change until it ends."));
        }
    }

    /// <summary>One criterion of the final verdict.</summary>
    public sealed class VerdictCriterion
    {
        public string Name;
        public int Winner = -1;    // -1 = tie
        public string Detail;
    }

    /// <summary>
    /// Final verdict once both keyboards are done: which one you tapped faster on, by how much,
    /// and whether the gap is clear (Welch 95% confidence interval of the difference of means).
    /// It only talks about the user's tapping rhythm, never about hardware latency.
    /// </summary>
    public sealed class Verdict
    {
        public const double TieRatio = 0.03;
        public bool Ready;
        public string Placeholder = "";
        public int Winner = -1;          // -1 = tie
        public double DiffMs = double.NaN, DiffRatio = double.NaN;
        public double CiLow = double.NaN, CiHigh = double.NaN;
        public bool Clear;               // 95% CI excludes 0
        public int NeededPresses;        // estimated presses per keyboard needed to confirm the current gap
        public int Wins;
        public string Headline = "", Sub = "", ConfidenceTitle = "", ConfidenceText = "";
        public List<VerdictCriterion> Criteria = new List<VerdictCriterion>();

        public Verdict()
        {
            Placeholder = L.S("Kết luận cuối cùng sẽ hiện ở đây khi test xong cả hai bàn phím.",
                              "The final verdict will appear here once both keyboards have been tested.");
        }

        public static Verdict Build(TestSession s, Analysis a)
        {
            Verdict v = new Verdict();
            if (s == null) return v;
            if (s.State == SessionState.Running) return v;
            if (s.State == SessionState.Stopped)
            {
                v.Placeholder = L.S("Bài test đã dừng trước khi xong cả hai bàn phím nên chưa có kết luận cuối cùng. Hãy chạy lại đủ lượt.",
                                    "The test was stopped before both keyboards were done, so there is no final verdict. Run a full test.");
                return v;
            }
            if (!a.Comparable) { v.Placeholder = L.S("Chưa đủ dữ liệu để kết luận.", "Not enough data for a verdict."); return v; }

            IntervalStats sa = a.Devices[AppInfo.A].Stats, sb = a.Devices[AppInfo.B].Stats;
            string na = AppInfo.Name(AppInfo.A), nb = AppInfo.Name(AppInfo.B);
            v.Ready = true;
            double diff = sb.Mean - sa.Mean; // > 0: A has the shorter interval = faster rhythm
            v.DiffMs = Math.Abs(diff);
            v.DiffRatio = v.DiffMs / Math.Max(sa.Mean, sb.Mean);
            v.Winner = v.DiffRatio < TieRatio ? -1 : (diff > 0 ? AppInfo.A : AppInfo.B);

            double va = sa.StdDev * sa.StdDev / sa.Count, vb = sb.StdDev * sb.StdDev / sb.Count;
            double se = Math.Sqrt(va + vb);
            if (se > 0)
            {
                double df = (va + vb) * (va + vb) / (va * va / (sa.Count - 1) + vb * vb / (sb.Count - 1));
                double t = TQuantile975(df);
                v.CiLow = diff - t * se;
                v.CiHigh = diff + t * se;
                v.Clear = v.CiLow > 0 || v.CiHigh < 0;
                double pooledVar = (sa.StdDev * sa.StdDev + sb.StdDev * sb.StdDev) / 2.0;
                // sample size estimate (95% confidence, 80% power): n = 2 (1.96 + 0.84)^2 σ² / Δ²
                double n = diff != 0 ? 2.0 * 7.84 * pooledVar / (diff * diff) : double.PositiveInfinity;
                v.NeededPresses = double.IsInfinity(n) || n > 9999 ? 10000 : (int)Math.Ceiling(n) + 1;
            }
            else { v.Clear = diff != 0; }

            string avgPair = "(" + na + " " + Fmt.Ms(sa.Mean) + ", " + nb + " " + Fmt.Ms(sb.Mean) + ")";
            if (v.Winner < 0)
            {
                v.Headline = L.S("Hai bàn phím ngang nhau", "Both keyboards are even");
                v.Sub = L.S("Chỉ chênh " + Fmt.Ms(v.DiffMs) + " mỗi lần nhấn " + avgPair + ".",
                            "Only " + Fmt.Ms(v.DiffMs) + " apart per press " + avgPair + ".");
            }
            else
            {
                v.Headline = L.S("Nhấn trên " + AppInfo.Name(v.Winner) + " nhanh hơn " + Fmt.Percent(v.DiffRatio),
                                 "You tapped " + Fmt.Percent(v.DiffRatio) + " faster on " + AppInfo.Name(v.Winner));
                v.Sub = L.S("Nhanh hơn trung bình " + Fmt.Ms(v.DiffMs) + " mỗi lần nhấn " + avgPair + ".",
                            "On average " + Fmt.Ms(v.DiffMs) + " quicker per press " + avgPair + ".");
            }

            double handSd = Math.Sqrt((sa.StdDev * sa.StdDev + sb.StdDev * sb.StdDev) / 2.0);
            if (v.Clear && v.Winner >= 0)
            {
                v.ConfidenceTitle = L.S("Khác biệt rõ ràng", "Clear difference");
                v.ConfidenceText = L.S("Chênh lệch lớn hơn dao động của tay (tin cậy 95%: ", "The gap is larger than your natural variation (95% CI: ") +
                    Fmt.Num(Math.Min(Math.Abs(v.CiLow), Math.Abs(v.CiHigh)), 1) + "–" + Fmt.Num(Math.Max(Math.Abs(v.CiLow), Math.Abs(v.CiHigh)), 1) + " ms).";
            }
            else
            {
                v.ConfidenceTitle = L.S("Chưa đủ chắc chắn", "Not conclusive yet");
                string need = v.NeededPresses >= 10000 ? L.S("Hai bàn phím cho nhịp nhấn như nhau.", "Both keyboards gave the same rhythm.")
                    : v.NeededPresses > 500 ? L.S("Cần trên 500 lần/bàn phím để xác nhận, coi như ngang nhau.", "Would need 500+ presses/keyboard to confirm; treat as even.")
                    : L.S("Test lại ~" + v.NeededPresses + " lần/bàn phím để xác nhận.", "Retest with ~" + v.NeededPresses + " presses/keyboard to confirm.");
                v.ConfidenceText = L.S("Chênh lệch nhỏ hơn dao động của tay (±", "The gap is smaller than your natural variation (±") + Fmt.Ms(handSd) + "). " + need;
            }

            v.Criteria.Add(Criterion(L.S("Tốc độ trung bình", "Average speed"), sa.Mean, sb.Mean, true));
            v.Criteria.Add(Criterion(L.S("Nhịp điển hình (trung vị)", "Typical rhythm (median)"), sa.Median, sb.Median, true));
            v.Criteria.Add(Criterion(L.S("Độ đều (độ lệch chuẩn)", "Consistency (std. deviation)"), sa.StdDev, sb.StdDev, false));
            foreach (VerdictCriterion c in v.Criteria) if (c.Winner >= 0 && c.Winner == v.Winner) v.Wins++;
            return v;
        }

        private static VerdictCriterion Criterion(string name, double a, double b, bool isSpeed)
        {
            VerdictCriterion c = new VerdictCriterion();
            c.Name = name;
            double hi = Math.Max(a, b);
            double d = Math.Abs(a - b);
            c.Winner = hi <= 0 || d / hi < (isSpeed ? TieRatio : 0.10) ? -1 : (a < b ? AppInfo.A : AppInfo.B);
            string w = c.Winner >= 0 ? AppInfo.Name(c.Winner) : "";
            if (isSpeed)
                c.Detail = c.Winner < 0 ? L.S("ngang nhau (chênh " + Fmt.Ms(d) + ")", "even (" + Fmt.Ms(d) + " apart)")
                                        : L.S(w + " nhanh hơn " + Fmt.Ms(d), w + " faster by " + Fmt.Ms(d));
            else
                c.Detail = c.Winner < 0 ? L.S("ngang nhau", "even") + " (±" + Fmt.Num(a, 1) + " / ±" + Fmt.Num(b, 1) + " ms)"
                    : L.S(w + " đều hơn (±" + Fmt.Num(Math.Min(a, b), 1) + " so với ±" + Fmt.Num(hi, 1) + " ms)",
                          w + " steadier (±" + Fmt.Num(Math.Min(a, b), 1) + " vs ±" + Fmt.Num(hi, 1) + " ms)");
            return c;
        }

        /// <summary>97.5% quantile of Student's t (Cornish-Fisher approximation, error &lt; 0.01 for df ≥ 3).</summary>
        public static double TQuantile975(double df)
        {
            if (double.IsNaN(df) || df < 1) df = 1;
            const double z = 1.959963985;
            double z3 = z * z * z, z5 = z3 * z * z, z7 = z5 * z * z;
            return z + (z3 + z) / (4 * df) + (5 * z5 + 16 * z3 + 3 * z) / (96 * df * df) +
                   (3 * z7 + 19 * z5 + 17 * z3 - 15 * z) / (384 * df * df * df);
        }
    }

    public sealed class DeviceDescriptor
    {
        public string Label;
        public string Path;
        public string Product;
        public string Manufacturer;
        public string Vid;
        public string Pid;
        public string Interface;
        public IntPtr Handle;
    }

    /// <summary>Excel-friendly CSV: UTF-8 BOM, list separator and decimal mark from Windows regional settings.</summary>
    public static class CsvExporter
    {
        public static string Build(TestSession s, Analysis a, DeviceDescriptor[] devices, CultureInfo culture, DateTime exportTime)
        {
            // Excel (double-click on .csv) splits columns by the Windows "List separator".
            // If that equals the decimal mark (e.g. Vietnamese: both ","), numeric cells are quoted so Excel still reads them.
            string sep = culture.TextInfo.ListSeparator;
            if (String.IsNullOrEmpty(sep)) sep = ",";
            sep = sep.Trim().Length > 0 ? sep.Trim().Substring(0, 1) : ",";
            CsvWriter w = new CsvWriter(sep, culture);
            string na = AppInfo.Name(AppInfo.A), nb = AppInfo.Name(AppInfo.B);
            string need = L.S("cần >= ", "needs >= ") + Analyzer.MinIntervalsForP95 + L.S(" khoảng", " intervals");

            w.Row("KEYBOARD SPEED TEST - " + AppInfo.VersusTitle);
            w.Row(L.S("Phiên bản", "Version"), AppInfo.Version);
            w.Row(L.S("Thời điểm xuất file", "Exported at"), exportTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            w.Row(L.S("Thời điểm bắt đầu test", "Test started at"), s.StartWallClock.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            w.Blank();

            w.Row(L.S("[CẤU HÌNH BÀI TEST]", "[TEST SETTINGS]"));
            w.Row(L.S("Số lần nhấn mỗi bàn phím mỗi vòng", "Presses per keyboard per round"), s.Config.PressesPerDevice.ToString(CultureInfo.InvariantCulture));
            w.Row(L.S("Số vòng", "Rounds"), s.Config.Rounds.ToString(CultureInfo.InvariantCulture));
            w.Row(L.S("Thứ tự vòng 1", "Round 1 order"), s.Config.OrderText);
            w.Row(L.S("Đảo thứ tự giữa các vòng", "Alternate order between rounds"), s.Config.AlternateOrder ? L.S("Có", "Yes") : L.S("Không", "No"));
            w.Row(L.S("Phím test", "Test key"), s.Config.TestKeyName + " (VK 0x" + s.Config.TestKey.ToString("X2") + ")");
            w.Row(L.S("Trạng thái", "Status"), StateText(s.State));
            w.Row(L.S("Stopwatch.Frequency (ticks/giây)", "Stopwatch.Frequency (ticks/second)"), s.TickFrequency.ToString(CultureInfo.InvariantCulture));
            w.Row(L.S("Thứ tự giai đoạn", "Phase order"), String.Join(" | ", s.Phases.Select(p => L.S("V", "R") + p.Round + ": " + AppInfo.Name(p.Device) + " (" + p.Count + "/" + s.Config.PressesPerDevice + ")").ToArray()));
            w.Blank();

            w.Row(L.S("[THIẾT BỊ]", "[DEVICES]"));
            w.Row(L.S("Vị trí", "Slot"), L.S("Tên hiển thị", "Display name"), L.S("Tên sản phẩm (HID)", "Product name (HID)"), L.S("Nhà sản xuất", "Manufacturer"), "VID", "PID", "Interface", "Device Path");
            for (int d = 0; d < 2; d++)
            {
                string slot = d == AppInfo.A ? "A" : "B";
                DeviceDescriptor dd = devices != null && d < devices.Length ? devices[d] : null;
                if (dd == null) { w.Row(slot, AppInfo.Name(d), "", "", "", "", "", ""); continue; }
                w.Row(slot, AppInfo.Name(d), dd.Product ?? "", dd.Manufacturer ?? "", dd.Vid ?? "", dd.Pid ?? "", dd.Interface ?? "", dd.Path ?? "");
            }
            w.Blank();

            DeviceResult ra = a.Devices[AppInfo.A], rb = a.Devices[AppInfo.B];
            w.Row(L.S("[THỐNG KÊ TỔNG HỢP] (khoảng cách = thời gian giữa hai lần nhấn liên tiếp trên CÙNG một bàn phím, trong cùng một giai đoạn)",
                      "[SUMMARY] (interval = time between two consecutive presses on the SAME keyboard, within the same phase)"));
            w.Row(L.S("Chỉ số", "Metric"), na, nb, L.S("Giải thích", "Explanation"));
            w.Row(L.S("Số lần nhấn hợp lệ", "Valid presses"), ra.Presses + "/" + ra.Target, rb.Presses + "/" + rb.Target,
                L.S("Chỉ đếm KeyDown đầu tiên của mỗi lần nhấn phím test trên đúng bàn phím", "Only the first KeyDown of each test-key press on the right keyboard"));
            w.Row(L.S("Số khoảng thời gian", "Intervals"), w.Int(ra.Stats.Count), w.Int(rb.Stats.Count), L.S("N lần nhấn trong một giai đoạn tạo ra N-1 khoảng", "N presses in a phase give N-1 intervals"));
            w.Row(L.S("Thời gian một lượt (ms)", "Time per run (ms)"), w.Num(ra.PhaseDuration), w.Num(rb.PhaseDuration),
                L.S("Từ lần nhấn đầu đến lần nhấn cuối của một giai đoạn; trung bình nếu nhiều vòng", "From first to last press of a phase; averaged over rounds"));
            w.Row(L.S("Khoảng cách trung bình (ms)", "Average interval (ms)"), w.Num(ra.Stats.Mean), w.Num(rb.Stats.Mean), L.S("Thấp hơn = nhịp nhấn nhanh hơn", "Lower = faster tapping"));
            w.Row(L.S("Trung vị (ms)", "Median (ms)"), w.Num(ra.Stats.Median), w.Num(rb.Stats.Median), L.S("Nhịp điển hình, ít bị ảnh hưởng bởi khoảng nghỉ dài", "Typical rhythm, robust to long pauses"));
            w.Row(L.S("Khoảng cách ngắn nhất (ms)", "Shortest interval (ms)"), w.Num(ra.Stats.Min), w.Num(rb.Stats.Min), "");
            w.Row(L.S("Khoảng cách dài nhất (ms)", "Longest interval (ms)"), w.Num(ra.Stats.Max), w.Num(rb.Stats.Max), "");
            w.Row(L.S("Độ lệch chuẩn (ms)", "Standard deviation (ms)"), w.Num(ra.Stats.StdDev), w.Num(rb.Stats.StdDev),
                L.S("Độ lệch chuẩn mẫu (n-1). Thấp = nhịp đều; cao = có lần nhanh/chậm bất thường", "Sample SD (n-1). Low = steady rhythm; high = some unusually fast/slow presses"));
            w.Row(L.S("Hệ số biến thiên (%)", "Coefficient of variation (%)"), w.Num(ra.Stats.Cv * 100), w.Num(rb.Stats.Cv * 100), L.S("Độ lệch chuẩn / trung bình", "SD / mean"));
            w.Row(L.S("Đánh giá độ ổn định", "Consistency rating"), ra.StabilityLabel, rb.StabilityLabel,
                L.S("<10% rất đều; <20% đều; <35% chưa đều; còn lại không đều", "<10% very steady; <20% steady; <35% uneven; otherwise irregular"));
            w.Row(L.S("Tốc độ nhịp (lần/giây)", "Tap rate (presses/second)"), w.Num(ra.Stats.PressesPerSecond), w.Num(rb.Stats.PressesPerSecond),
                L.S("Số khoảng / tổng thời gian các khoảng", "Intervals / total interval time"));
            w.Row("P95 (ms)",
                ra.Stats.Count >= Analyzer.MinIntervalsForP95 ? w.Num(ra.Stats.P95) : need,
                rb.Stats.Count >= Analyzer.MinIntervalsForP95 ? w.Num(rb.Stats.P95) : need,
                L.S("95% khoảng cách nhỏ hơn giá trị này (nội suy tuyến tính)", "95% of intervals are below this value (linear interpolation)"));
            w.Row(L.S("Khoảng nghỉ dài", "Long pauses"), w.Int(ra.LongPauses), w.Int(rb.LongPauses), L.S("Khoảng > max(3 x trung vị; trung vị + 250 ms)", "Interval > max(3 x median; median + 250 ms)"));
            w.Row(L.S("Nhấn khi chưa đến lượt (bị bỏ qua)", "Presses out of turn (ignored)"), w.Int(ra.WrongDevicePresses), w.Int(rb.WrongDevicePresses), L.S("Không tính vào kết quả", "Not counted"));
            w.Row(L.S("Nhấn phím khác phím test (bị bỏ qua)", "Other keys pressed (ignored)"), w.Int(s.WrongKeyCount), "", L.S("Tính chung cho cả hai bàn phím", "Both keyboards combined"));
            w.Row(L.S("Phím từ thiết bị khác (bị bỏ qua)", "Keys from other devices (ignored)"), w.Int(s.UnknownDeviceCount), "", L.S("Không phải hai bàn phím đang test", "Not one of the two tested keyboards"));
            w.Blank();

            w.Row(L.S("[KẾT LUẬN]", "[CONCLUSION]"));
            w.Row(a.Headline ?? "");
            foreach (string d in a.Details) w.Row(d);
            if (a.Verdict != null && a.Verdict.Ready)
            {
                w.Row(L.S("Kết luận cuối cùng", "Final verdict"), a.Verdict.Headline);
                w.Row("", a.Verdict.Sub);
                w.Row("", a.Verdict.ConfidenceTitle + ": " + a.Verdict.ConfidenceText);
                foreach (VerdictCriterion c in a.Verdict.Criteria) w.Row("", c.Name, c.Detail);
            }
            w.Blank();

            w.Row(L.S("[PHƯƠNG PHÁP ĐO VÀ GIỚI HẠN]", "[METHOD AND LIMITATIONS]"));
            w.Row(L.S("Ứng dụng đọc bàn phím bằng Windows Raw Input (WM_INPUT) và phân biệt thiết bị bằng Device Handle / Device Path.",
                      "Keyboards are read via Windows Raw Input (WM_INPUT) and told apart by Device Handle / Device Path."));
            w.Row(L.S("Timestamp = Stopwatch.GetTimestamp() lấy ngay khi luồng nhận Raw Input nhận được WM_INPUT; đây là thời điểm phần mềm nhận sự kiện từ Windows, không phải thời điểm vật lý switch tác động.",
                      "Timestamp = Stopwatch.GetTimestamp() taken as soon as the Raw Input thread receives WM_INPUT; this is when the software receives the event from Windows, not when the switch physically actuates."));
            w.Row(L.S("Khoảng cách chỉ tính giữa hai lần nhấn liên tiếp trên cùng một bàn phím trong cùng một giai đoạn. Không tính khoảng chuyển giữa hai bàn phím.",
                      "Intervals are only measured between consecutive presses on the same keyboard within the same phase. The switch-over between keyboards is never counted."));
            w.Row(L.S("Giữ phím (auto-repeat) không được đếm: mỗi lần nhấn chỉ được tính sau khi đã nhận KeyUp của lần nhấn trước.",
                      "Held keys (auto-repeat) are not counted: a press only counts after the KeyUp of the previous press."));
            w.Row(AppInfo.MethodNote);
            w.Blank();

            w.Row(L.S("[DỮ LIỆU TỪNG LẦN NHẤN HỢP LỆ]", "[VALID PRESSES]"));
            w.Row(L.S("STT", "No."), L.S("Vòng", "Round"), L.S("Giai đoạn", "Phase"), L.S("Bàn phím", "Keyboard"), L.S("Lần nhấn trong giai đoạn", "Press in phase"),
                L.S("Phím", "Key"), "VK", "Device Handle", "Timestamp (Stopwatch ticks)", L.S("Thời điểm từ lúc bắt đầu test (ms)", "Time since test start (ms)"),
                L.S("Khoảng cách từ lần nhấn trước cùng bàn phím (ms)", "Interval since previous press on same keyboard (ms)"));
            foreach (PressRecord p in s.Presses)
            {
                w.Row(w.Int(p.Seq), w.Int(p.Round), w.Int(p.PhaseIndex + 1), AppInfo.Name(p.Device), w.Int(p.PressInPhase), p.KeyName,
                    "0x" + p.VKey.ToString("X2"), "0x" + p.Handle.ToInt64().ToString("X"), p.Timestamp.ToString(CultureInfo.InvariantCulture),
                    w.Num(p.MsFromStart, 3), double.IsNaN(p.IntervalMs) ? "" : w.Num(p.IntervalMs, 3));
            }
            w.Blank();

            w.Row(L.S("[NHẬT KÝ SỰ KIỆN THÔ] (mọi KeyDown nhận được trong lúc test, kể cả bị bỏ qua)", "[RAW EVENT LOG] (every KeyDown received during the test, including ignored ones)"));
            w.Row(L.S("STT", "No."), L.S("Thời điểm từ lúc bắt đầu test (ms)", "Time since test start (ms)"), "Timestamp (Stopwatch ticks)", L.S("Vòng", "Round"),
                L.S("Đang test", "Testing"), L.S("Thiết bị gửi", "Sent by"), "Device Handle", "VK", L.S("Trạng thái", "Status"));
            foreach (EventRecord e in s.Events)
            {
                w.Row(w.Int(e.Seq), w.Num(e.MsFromStart, 3), e.Timestamp.ToString(CultureInfo.InvariantCulture), w.Int(e.Round),
                    AppInfo.Name(e.TargetDevice), AppInfo.Name(e.Device), "0x" + e.Handle.ToInt64().ToString("X"),
                    "0x" + e.VKey.ToString("X2"), StatusText(e.Status));
            }
            return w.ToString();
        }

        public static string StateText(SessionState st)
        {
            switch (st)
            {
                case SessionState.Completed: return L.S("Hoàn thành", "Completed");
                case SessionState.Stopped: return L.S("Đã dừng giữa chừng", "Stopped early");
                default: return L.S("Đang chạy", "Running");
            }
        }

        public static string StatusText(EventStatus st)
        {
            switch (st)
            {
                case EventStatus.Valid: return L.S("Hợp lệ", "Valid");
                case EventStatus.WrongDevice: return L.S("Bỏ qua - nhấn nhầm bàn phím (chưa đến lượt)", "Ignored - wrong keyboard (not its turn)");
                case EventStatus.WrongKey: return L.S("Bỏ qua - không phải phím test", "Ignored - not the test key");
                default: return L.S("Bỏ qua - thiết bị không được chỉ định", "Ignored - unassigned device");
            }
        }

        private sealed class CsvWriter
        {
            private readonly StringBuilder sb = new StringBuilder();
            private readonly string sep;
            private readonly CultureInfo culture;

            public CsvWriter(string sep, CultureInfo culture) { this.sep = sep; this.culture = culture; }

            public string Num(double v) { return Num(v, 2); }
            public string Num(double v, int decimals)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) return "";
                NumberFormatInfo nf = (NumberFormatInfo)culture.NumberFormat.Clone();
                nf.NumberGroupSeparator = "";
                return v.ToString("F" + decimals, nf);
            }
            public string Int(int v) { return v.ToString(CultureInfo.InvariantCulture); }

            public void Blank() { sb.Append("\r\n"); }

            public void Row(params string[] cells)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0) sb.Append(sep);
                    sb.Append(Escape(cells[i] ?? ""));
                }
                sb.Append("\r\n");
            }

            private string Escape(string v)
            {
                if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '@')) v = "'" + v; // never let Excel treat a cell as a formula
                bool needs = v.Contains(sep) || v.Contains("\"") || v.Contains("\n") || v.Contains("\r");
                if (!needs) return v;
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }

            public override string ToString() { return sb.ToString(); }
        }
    }
}
