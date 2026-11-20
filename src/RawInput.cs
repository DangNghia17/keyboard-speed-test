// Keyboard Speed Test - Raw Input
// Receives WM_INPUT on a dedicated thread (message-only window) so UI painting never delays timestamps.
// Devices are identified by Device Handle (hDevice in RAWINPUTHEADER) + Device Path (RIDI_DEVICENAME).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace KeyboardSpeedTest
{
    internal static class NativeMethods
    {
        public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
        public const int WM_INPUT = 0x00FF;
        public const int WM_APP_QUIT = 0x8000 + 0x51;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIM_TYPEKEYBOARD = 1;
        public const uint RIDEV_REMOVE = 0x00000001;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_DEVNOTIFY = 0x00002000;
        public const ushort RI_KEY_BREAK = 0x0001;
        public const int GIDC_ARRIVAL = 1;
        public const int GIDC_REMOVAL = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);

        [DllImport("hid.dll")]
        public static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, byte[] buffer, int bufferLength);
    }

    /// <summary>
    /// Luồng lắng nghe Raw Input. Timestamp được lấy bằng Stopwatch.GetTimestamp() ngay dòng đầu tiên khi WM_INPUT đến,
    /// trước mọi xử lý khác. Sự kiện được đưa sang luồng giao diện theo đúng thứ tự nhận.
    /// </summary>
    public sealed class RawInputListener : IDisposable
    {
        public event Action<RawKeyEvent> KeyEvent;            // gọi trên luồng Raw Input
        public event Action<IntPtr, bool> DeviceChanged;     // (handle, arrived) - gọi trên luồng Raw Input

        private Thread thread;
        private ListenerWindow window;
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private bool registered;
        private int registerError;

        public bool IsRunning { get { return registered; } }
        public int LastError { get { return registerError; } }

        public bool Start()
        {
            thread = new Thread(ThreadMain);
            thread.Name = "RawInputListener";
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(5000);
            return registered;
        }

        private void ThreadMain()
        {
            try
            {
                window = new ListenerWindow(this);
                NativeMethods.RAWINPUTDEVICE[] devices = new NativeMethods.RAWINPUTDEVICE[1];
                devices[0].usUsagePage = 0x01;
                devices[0].usUsage = 0x06; // keyboard
                devices[0].dwFlags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_DEVNOTIFY;
                devices[0].hwndTarget = window.Handle;
                registered = NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)));
                if (!registered) registerError = Marshal.GetLastWin32Error();
            }
            catch (Exception)
            {
                registered = false;
                if (registerError == 0) registerError = -1;
            }
            ready.Set();
            if (!registered) return;

            NativeMethods.MSG msg;
            while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }

            try
            {
                NativeMethods.RAWINPUTDEVICE[] devices = new NativeMethods.RAWINPUTDEVICE[1];
                devices[0].usUsagePage = 0x01;
                devices[0].usUsage = 0x06;
                devices[0].dwFlags = NativeMethods.RIDEV_REMOVE;
                devices[0].hwndTarget = IntPtr.Zero;
                NativeMethods.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICE)));
                window.DestroyHandle();
            }
            catch { }
            registered = false;
        }

        public void Dispose()
        {
            ListenerWindow w = window;
            if (w != null && w.Handle != IntPtr.Zero) NativeMethods.PostMessage(w.Handle, NativeMethods.WM_APP_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (thread != null) thread.Join(2000);
        }

        private void RaiseKey(RawKeyEvent e)
        {
            Action<RawKeyEvent> h = KeyEvent;
            if (h != null) h(e);
        }

        private void RaiseDevice(IntPtr device, bool arrived)
        {
            Action<IntPtr, bool> h = DeviceChanged;
            if (h != null) h(device, arrived);
        }

        private sealed class ListenerWindow : NativeWindow
        {
            private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
            private readonly RawInputListener owner;
            private readonly uint headerSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTHEADER));
            private readonly uint keyboardSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWKEYBOARD));
            private IntPtr buffer;
            private uint bufferSize;

            public ListenerWindow(RawInputListener owner)
            {
                this.owner = owner;
                bufferSize = 1024;
                buffer = Marshal.AllocHGlobal((int)bufferSize);
                CreateParams cp = new CreateParams();
                cp.Caption = "KeyboardSpeedTest.RawInput";
                cp.Parent = HWND_MESSAGE;
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_INPUT)
                {
                    long ts = Stopwatch.GetTimestamp(); // càng gần thời điểm nhận WM_INPUT càng tốt
                    try { Read(m.LParam, ts); }
                    catch { }
                }
                else if (m.Msg == NativeMethods.WM_INPUT_DEVICE_CHANGE)
                {
                    try { owner.RaiseDevice(m.LParam, m.WParam.ToInt64() == NativeMethods.GIDC_ARRIVAL); }
                    catch { }
                }
                else if (m.Msg == NativeMethods.WM_APP_QUIT)
                {
                    NativeMethods.PostQuitMessage(0);
                    return;
                }
                base.WndProc(ref m);
            }

            private void Read(IntPtr rawHandle, long ts)
            {
                uint size = 0;
                NativeMethods.GetRawInputData(rawHandle, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
                if (size == 0 || size > 65536) return;
                if (size > bufferSize)
                {
                    Marshal.FreeHGlobal(buffer);
                    bufferSize = size;
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                }
                uint read = NativeMethods.GetRawInputData(rawHandle, NativeMethods.RID_INPUT, buffer, ref size, headerSize);
                if (read == UInt32.MaxValue || read < headerSize + keyboardSize) return;
                NativeMethods.RAWINPUTHEADER header = (NativeMethods.RAWINPUTHEADER)Marshal.PtrToStructure(buffer, typeof(NativeMethods.RAWINPUTHEADER));
                if (header.dwType != NativeMethods.RIM_TYPEKEYBOARD) return;
                NativeMethods.RAWKEYBOARD kb = (NativeMethods.RAWKEYBOARD)Marshal.PtrToStructure(IntPtr.Add(buffer, (int)headerSize), typeof(NativeMethods.RAWKEYBOARD));
                RawKeyEvent e = new RawKeyEvent();
                e.Timestamp = ts;
                e.Device = header.hDevice;
                e.VKey = kb.VKey;
                e.MakeCode = kb.MakeCode;
                e.Flags = kb.Flags;
                e.IsBreak = (kb.Flags & NativeMethods.RI_KEY_BREAK) != 0;
                owner.RaiseKey(e);
            }

            public override void DestroyHandle()
            {
                base.DestroyHandle();
                if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
            }
        }
    }

    public static class DeviceInfo
    {
        public static string GetDevicePath(IntPtr hDevice)
        {
            try
            {
                uint chars = 0;
                NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, IntPtr.Zero, ref chars);
                if (chars == 0) return "";
                IntPtr buf = Marshal.AllocHGlobal((int)chars * 2 + 2);
                try
                {
                    uint result = NativeMethods.GetRawInputDeviceInfo(hDevice, NativeMethods.RIDI_DEVICENAME, buf, ref chars);
                    if (result == UInt32.MaxValue) return "";
                    return Marshal.PtrToStringUni(buf) ?? "";
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return ""; }
        }

        /// <summary>Danh sách bàn phím Raw Input hiện có: handle -> device path.</summary>
        public static Dictionary<IntPtr, string> ListKeyboards()
        {
            Dictionary<IntPtr, string> result = new Dictionary<IntPtr, string>();
            try
            {
                uint count = 0;
                uint itemSize = (uint)Marshal.SizeOf(typeof(NativeMethods.RAWINPUTDEVICELIST));
                NativeMethods.GetRawInputDeviceList(IntPtr.Zero, ref count, itemSize);
                if (count == 0) return result;
                IntPtr buf = Marshal.AllocHGlobal((int)(count * itemSize));
                try
                {
                    uint got = NativeMethods.GetRawInputDeviceList(buf, ref count, itemSize);
                    if (got == UInt32.MaxValue) return result;
                    for (int i = 0; i < got; i++)
                    {
                        NativeMethods.RAWINPUTDEVICELIST item = (NativeMethods.RAWINPUTDEVICELIST)Marshal.PtrToStructure(
                            IntPtr.Add(buf, (int)(i * itemSize)), typeof(NativeMethods.RAWINPUTDEVICELIST));
                        if (item.dwType == NativeMethods.RIM_TYPEKEYBOARD) result[item.hDevice] = GetDevicePath(item.hDevice);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            return result;
        }

        public static DeviceDescriptor Describe(string label, IntPtr handle, string path)
        {
            DeviceDescriptor d = new DeviceDescriptor();
            d.Label = label;
            d.Handle = handle;
            d.Path = path ?? "";
            Match m = Regex.Match(d.Path, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (m.Success) d.Vid = m.Groups[1].Value.ToUpperInvariant();
            m = Regex.Match(d.Path, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
            if (m.Success) d.Pid = m.Groups[1].Value.ToUpperInvariant();
            m = Regex.Match(d.Path, @"MI_([0-9A-Fa-f]{2})", RegexOptions.IgnoreCase);
            if (m.Success) d.Interface = m.Groups[1].Value.ToUpperInvariant();
            if (d.Path.Length > 0) ReadHidStrings(d);
            return d;
        }

        private static void ReadHidStrings(DeviceDescriptor d)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
                h = NativeMethods.CreateFile(d.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) { h = IntPtr.Zero; return; }
                byte[] buf = new byte[512];
                if (NativeMethods.HidD_GetProductString(h, buf, buf.Length)) d.Product = Clean(buf);
                buf = new byte[512];
                if (NativeMethods.HidD_GetManufacturerString(h, buf, buf.Length)) d.Manufacturer = Clean(buf);
            }
            catch { }
            finally { if (h != IntPtr.Zero) NativeMethods.CloseHandle(h); }
        }

        private static string Clean(byte[] buf)
        {
            string s = Encoding.Unicode.GetString(buf);
            int z = s.IndexOf('\0');
            if (z >= 0) s = s.Substring(0, z);
            s = s.Trim();
            return s.Length == 0 ? null : s;
        }

        public static string ShortName(DeviceDescriptor d)
        {
            if (d == null || String.IsNullOrEmpty(d.Path)) return "";
            List<string> parts = new List<string>();
            if (!String.IsNullOrEmpty(d.Product)) parts.Add(d.Product);
            string ids = "";
            if (d.Vid != null) ids += "VID " + d.Vid;
            if (d.Pid != null) ids += (ids.Length > 0 ? " · " : "") + "PID " + d.Pid;
            if (d.Interface != null) ids += (ids.Length > 0 ? " · " : "") + "MI " + d.Interface;
            if (ids.Length > 0) parts.Add(ids);
            if (parts.Count == 0)
            {
                string s = d.Path.Replace("\\\\?\\", "");
                if (s.Length > 40) s = s.Substring(0, 37) + "...";
                parts.Add(s);
            }
            return String.Join("  ·  ", parts.ToArray());
        }
    }

    /// <summary>
    /// Remembers identified keyboards (Device Path + display name) so they are recognised next time.
    /// Stored only on this PC in %LOCALAPPDATA%\KeyboardSpeedTest\devices.txt.
    /// </summary>
    public static class DeviceStore
    {
        private static string FilePath
        {
            get
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KeyboardSpeedTest", "devices.txt");
            }
        }

        public sealed class Entry
        {
            public string[] Paths = new string[] { "", "" };
            public string[] Names = new string[] { "", "" };
        }

        public static Entry Load()
        {
            Entry e = new Entry();
            try
            {
                string f = FilePath;
                if (!File.Exists(f)) return e;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "A": e.Paths[AppInfo.A] = val; break;
                        case "B": e.Paths[AppInfo.B] = val; break;
                        case "AName": e.Names[AppInfo.A] = AppInfo.CleanName(val); break;
                        case "BName": e.Names[AppInfo.B] = AppInfo.CleanName(val); break;
                        // file format of version 2.0
                        case "MG75": e.Paths[AppInfo.A] = val; if (e.Names[AppInfo.A] == "") e.Names[AppInfo.A] = "MG75"; break;
                        case "M30HE": e.Paths[AppInfo.B] = val; if (e.Names[AppInfo.B] == "") e.Names[AppInfo.B] = "M30 HE"; break;
                    }
                }
            }
            catch { }
            return e;
        }

        public static void Save(string[] paths, string[] names)
        {
            try
            {
                string f = FilePath;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
                File.WriteAllText(f,
                    "A=" + (paths[AppInfo.A] ?? "") + "\r\nAName=" + (names[AppInfo.A] ?? "") + "\r\n" +
                    "B=" + (paths[AppInfo.B] ?? "") + "\r\nBName=" + (names[AppInfo.B] ?? "") + "\r\n", new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
