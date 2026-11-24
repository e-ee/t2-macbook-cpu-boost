using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinCpuBoostApp
{
    internal sealed class ProfileSpec
    {
        public string Id;
        public string MenuTitle;
        public double Pl1Watts;
        public double Pl2Watts;
        public int TauY;       // 11 => 2.0s, -1 => stock 28.0s (0xDD)
        public bool Pl2Clamp;
        public int TccOffset;  // degrees below 100C TjMax

        public ProfileSpec(string id, string title, double pl1, double pl2, int tauY, bool pl2Clamp, int tcc)
        {
            Id = id;
            MenuTitle = title;
            Pl1Watts = pl1;
            Pl2Watts = pl2;
            TauY = tauY;
            Pl2Clamp = pl2Clamp;
            TccOffset = tcc;
        }
    }

    internal sealed class AppSettings
    {
        public string CpuProfile = "max";
        public string FanMode = "curve";      // "curve" | "full"
        public int LeftMinC = 68;
        public int LeftMaxC = 88;
        public int RightMinC = 66;
        public int RightMaxC = 86;

        public static AppSettings Load(string path)
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(path)) return s;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    int parsed;
                    if (String.Equals(k, "cpu_profile", StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                        s.CpuProfile = String.Equals(v, "quiet", StringComparison.OrdinalIgnoreCase) ? "quiet" : "max";
                    else if (String.Equals(k, "fan_mode", StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                        s.FanMode = String.Equals(v, "full", StringComparison.OrdinalIgnoreCase) ? "full" : "curve";
                    else if (String.Equals(k, "left_min_c", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out parsed)) s.LeftMinC = parsed;
                    else if (String.Equals(k, "left_max_c", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out parsed)) s.LeftMaxC = parsed;
                    else if (String.Equals(k, "right_min_c", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out parsed)) s.RightMinC = parsed;
                    else if (String.Equals(k, "right_max_c", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out parsed)) s.RightMaxC = parsed;
                }
            }
            catch { }
            return s;
        }

        public void Save(string path)
        {
            try
            {
                string content = String.Format(
                    "cpu_profile={0}\r\nfan_mode={1}\r\nleft_min_c={2}\r\nleft_max_c={3}\r\nright_min_c={4}\r\nright_max_c={5}\r\n",
                    CpuProfile, FanMode, LeftMinC, LeftMaxC, RightMinC, RightMaxC);
                File.WriteAllText(path, content);
            }
            catch { }
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        public static readonly Color BgDark       = Color.FromArgb(30, 32, 38);
        public static readonly Color BorderDark   = Color.FromArgb(62, 66, 78);
        public static readonly Color HoverFill    = Color.FromArgb(50, 55, 66);
        public static readonly Color AccentCyan   = Color.FromArgb(76, 194, 255);
        public static readonly Color SepColor     = Color.FromArgb(56, 60, 70);

        public override Color ToolStripDropDownBackground { get { return BgDark; } }
        public override Color MenuBorder { get { return BorderDark; } }
        public override Color MenuItemBorder { get { return HoverFill; } }
        public override Color MenuItemSelected { get { return HoverFill; } }
        public override Color MenuItemSelectedGradientBegin { get { return HoverFill; } }
        public override Color MenuItemSelectedGradientEnd { get { return HoverFill; } }
        public override Color MenuItemPressedGradientBegin { get { return HoverFill; } }
        public override Color MenuItemPressedGradientEnd { get { return HoverFill; } }
        public override Color ImageMarginGradientBegin { get { return BgDark; } }
        public override Color ImageMarginGradientMiddle { get { return BgDark; } }
        public override Color ImageMarginGradientEnd { get { return BgDark; } }
        public override Color CheckBackground { get { return Color.Transparent; } }
        public override Color CheckSelectedBackground { get { return Color.Transparent; } }
        public override Color CheckPressedBackground { get { return Color.Transparent; } }
        public override Color SeparatorDark { get { return SepColor; } }
        public override Color SeparatorLight { get { return Color.Transparent; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly ToolStripItem _headerItem;

        public DarkMenuRenderer(ToolStripItem headerItem) : base(new DarkColorTable())
        {
            RoundedEdges = false;
            _headerItem = headerItem;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled) return;
            if (e.Item.Selected)
            {
                Rectangle rc = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = BuildRoundRect(rc, 4))
                using (SolidBrush b = new SolidBrush(DarkColorTable.HoverFill))
                {
                    SmoothingMode prev = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(b, path);
                    e.Graphics.SmoothingMode = prev;
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item == _headerItem)
                e.TextColor = Color.FromArgb(140, 198, 255);
            else if (!e.Item.Enabled)
                e.TextColor = Color.FromArgb(182, 190, 204);
            else
                e.TextColor = Color.FromArgb(242, 244, 248);

            TextRenderer.DrawText(
                e.Graphics,
                e.Text,
                e.TextFont,
                e.TextRectangle,
                e.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(DarkColorTable.SepColor, 1f))
            {
                e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            SmoothingMode prev = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle box = new Rectangle(e.ImageRectangle.X + 1, (e.Item.Height - 16) / 2, 16, 16);
            using (GraphicsPath gp = BuildRoundRect(box, 4))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(45, 76, 194, 255)))
            using (Pen border = new Pen(DarkColorTable.AccentCyan, 1.3f))
            {
                e.Graphics.FillPath(fill, gp);
                e.Graphics.DrawPath(border, gp);
            }

            using (Pen checkPen = new Pen(DarkColorTable.AccentCyan, 2.0f))
            {
                checkPen.StartCap = LineCap.Round;
                checkPen.EndCap = LineCap.Round;
                checkPen.LineJoin = LineJoin.Round;
                PointF[] pts = new PointF[]
                {
                    new PointF(box.X + 3.8f, box.Y + 8.3f),
                    new PointF(box.X + 7.0f, box.Y + 11.5f),
                    new PointF(box.X + 12.4f, box.Y + 5.0f)
                };
                e.Graphics.DrawLines(checkPen, pts);
            }

            e.Graphics.SmoothingMode = prev;
        }

        private static GraphicsPath BuildRoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath gp = new GraphicsPath();
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    internal sealed class SiliconEngine : IDisposable
    {
        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int pawnio_open(out IntPtr handle);

        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int pawnio_load(IntPtr handle, byte[] blob, UIntPtr size);

        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
        private static extern int pawnio_execute(IntPtr handle, string name, ulong[] inBuf, UIntPtr inSize, ulong[] outBuf, UIntPtr outSize, out UIntPtr returnSize);

        [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
        private static extern int pawnio_close(IntPtr handle);

        private static readonly string[] CpuCoreSmcKeys = new string[] {
            "TC1C", "TC2C", "TC3C", "TC4C", "TC5C", "TC6C", "TC7C", "TC8C"
        };

        private IntPtr _msrHandle = IntPtr.Zero;
        private IntPtr _smcHandle = IntPtr.Zero;
        private readonly object _lock = new object();

        private double _powerUnit = 0.125;
        private double _energyUnit = 0.00006103515625;
        private double _timeUnit = 0.0009765625;
        private int _tjMax = 100;
        private ulong _bootPkgLimit = 0x004283E800DD8320UL;
        private ulong _bootTempTarget = 0x0000000000640000UL;
        private uint _lastEnergyRaw;
        private long _lastEnergyTicks;
        private PerformanceCounter _pcPerf;
        private PerformanceCounter _pcLimit;

        private float _f0Min = 1836f, _f0Max = 5616f;
        private float _f1Min = 1700f, _f1Max = 5200f;
        private double _smoothedCpuAvg = -1.0;
        private double _smoothedGpu5500M = -1.0;
        private float _lastAppliedF0 = -1f;
        private float _lastAppliedF1 = -1f;

        public string LastError = "";

        public bool Initialize(string msrModulePath, string smcModulePath)
        {
            lock (_lock)
            {
                if (_msrHandle == IntPtr.Zero)
                {
                    if (!File.Exists(msrModulePath))
                    {
                        LastError = "Missing IntelMSR.bin: " + msrModulePath;
                        return false;
                    }
                    IntPtr hMsr;
                    int hr = pawnio_open(out hMsr);
                    if (hr < 0)
                    {
                        LastError = String.Format("pawnio_open failed (0x{0:X8}) - run as Administrator", hr);
                        return false;
                    }
                    byte[] msrBlob = File.ReadAllBytes(msrModulePath);
                    hr = pawnio_load(hMsr, msrBlob, new UIntPtr((uint)msrBlob.Length));
                    if (hr < 0)
                    {
                        pawnio_close(hMsr);
                        LastError = String.Format("pawnio_load(IntelMSR) failed (0x{0:X8})", hr);
                        return false;
                    }
                    _msrHandle = hMsr;

                    ulong raplUnit;
                    if (TryReadMsrUnlocked(0x606, out raplUnit))
                    {
                        _powerUnit = 1.0 / (1 << (int)(raplUnit & 0xF));
                        _energyUnit = 1.0 / (1 << (int)((raplUnit >> 8) & 0x1F));
                        _timeUnit = 1.0 / (1 << (int)((raplUnit >> 16) & 0xF));
                    }
                    ulong tt;
                    if (TryReadMsrUnlocked(0x1A2, out tt))
                    {
                        _bootTempTarget = tt;
                        int tj = (int)((tt >> 16) & 0xFF);
                        if (tj >= 70 && tj <= 115) _tjMax = tj;
                    }
                    ulong pkg;
                    if (TryReadMsrUnlocked(0x610, out pkg))
                    {
                        _bootPkgLimit = pkg;
                    }
                    ulong e0;
                    if (TryReadMsrUnlocked(0x611, out e0))
                    {
                        _lastEnergyRaw = (uint)(e0 & 0xFFFFFFFFUL);
                        _lastEnergyTicks = Stopwatch.GetTimestamp();
                    }

                    try
                    {
                        _pcPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                        _pcLimit = new PerformanceCounter("Processor Information", "% Performance Limit", "_Total");
                        _pcPerf.NextValue();
                        _pcLimit.NextValue();
                    }
                    catch { }
                }

                if (_smcHandle == IntPtr.Zero && File.Exists(smcModulePath))
                {
                    IntPtr hSmc;
                    if (pawnio_open(out hSmc) >= 0)
                    {
                        byte[] smcBlob = File.ReadAllBytes(smcModulePath);
                        if (pawnio_load(hSmc, smcBlob, new UIntPtr((uint)smcBlob.Length)) >= 0)
                        {
                            _smcHandle = hSmc;
                            float v;
                            if (TryReadSmcFloatUnlocked("F0Mn", out v) && v > 500f) _f0Min = v;
                            if (TryReadSmcFloatUnlocked("F0Mx", out v) && v > 2000f) _f0Max = v;
                            if (TryReadSmcFloatUnlocked("F1Mn", out v) && v > 500f) _f1Min = v;
                            if (TryReadSmcFloatUnlocked("F1Mx", out v) && v > 2000f) _f1Max = v;
                        }
                        else
                        {
                            pawnio_close(hSmc);
                        }
                    }
                }

                return true;
            }
        }

        private static uint PackSmcKey(string k)
        {
            return ((uint)k[0]) | (((uint)k[1]) << 8) | (((uint)k[2]) << 16) | (((uint)k[3]) << 24);
        }

        private bool TryReadSmcFloatUnlocked(string key, out float val)
        {
            val = 0f;
            if (_smcHandle == IntPtr.Zero) return false;
            ulong[] inBuf = new ulong[] { 0x10UL, PackSmcKey(key), 4UL };
            ulong[] outBuf = new ulong[32];
            UIntPtr ret;
            int hr = pawnio_execute(_smcHandle, "ioctl_smc_read", inBuf, new UIntPtr(3), outBuf, new UIntPtr(32), out ret);
            if (hr < 0) return false;
            byte[] raw = new byte[] {
                (byte)(outBuf[0] & 0xFF),
                (byte)(outBuf[1] & 0xFF),
                (byte)(outBuf[2] & 0xFF),
                (byte)(outBuf[3] & 0xFF)
            };
            val = BitConverter.ToSingle(raw, 0);
            return true;
        }

        private bool TryReadSmcSp78Unlocked(string key, out double val)
        {
            val = 0.0;
            if (_smcHandle == IntPtr.Zero) return false;
            ulong[] inBuf = new ulong[] { 0x10UL, PackSmcKey(key), 2UL };
            ulong[] outBuf = new ulong[32];
            UIntPtr ret;
            int hr = pawnio_execute(_smcHandle, "ioctl_smc_read", inBuf, new UIntPtr(3), outBuf, new UIntPtr(32), out ret);
            if (hr < 0) return false;
            short raw = (short)(((outBuf[0] & 0xFFUL) << 8) | (outBuf[1] & 0xFFUL));
            val = raw / 256.0;
            return val > 1.0 && val < 125.0;
        }

        private bool TryReadSmcByteUnlocked(string key, out byte val)
        {
            val = 0;
            if (_smcHandle == IntPtr.Zero) return false;
            ulong[] inBuf = new ulong[] { 0x10UL, PackSmcKey(key), 1UL };
            ulong[] outBuf = new ulong[32];
            UIntPtr ret;
            int hr = pawnio_execute(_smcHandle, "ioctl_smc_read", inBuf, new UIntPtr(3), outBuf, new UIntPtr(32), out ret);
            if (hr < 0) return false;
            val = (byte)(outBuf[0] & 0xFF);
            return true;
        }

        private bool WriteSmcByteUnlocked(string key, byte val)
        {
            if (_smcHandle == IntPtr.Zero) return false;
            ulong[] inBuf = new ulong[34];
            inBuf[0] = PackSmcKey(key);
            inBuf[1] = 1UL;
            inBuf[2] = val;
            UIntPtr ret;
            return pawnio_execute(_smcHandle, "ioctl_smc_write", inBuf, new UIntPtr(34), null, UIntPtr.Zero, out ret) >= 0;
        }

        private bool WriteSmcFloatUnlocked(string key, float val)
        {
            if (_smcHandle == IntPtr.Zero) return false;
            byte[] raw = BitConverter.GetBytes(val);
            ulong[] inBuf = new ulong[34];
            inBuf[0] = PackSmcKey(key);
            inBuf[1] = 4UL;
            inBuf[2] = raw[0];
            inBuf[3] = raw[1];
            inBuf[4] = raw[2];
            inBuf[5] = raw[3];
            UIntPtr ret;
            return pawnio_execute(_smcHandle, "ioctl_smc_write", inBuf, new UIntPtr(34), null, UIntPtr.Zero, out ret) >= 0;
        }

        private double ReadCpuCoreAverageUnlocked()
        {
            if (_smcHandle != IntPtr.Zero)
            {
                double sum = 0.0;
                int count = 0;
                for (int i = 0; i < CpuCoreSmcKeys.Length; i++)
                {
                    double c;
                    if (TryReadSmcSp78Unlocked(CpuCoreSmcKeys[i], out c))
                    {
                        sum += c;
                        count++;
                    }
                }
                if (count > 0) return sum / count;
            }
            return ReadPackageTempUnlocked();
        }

        private double ReadGpu5500MTempUnlocked()
        {
            if (_smcHandle != IntPtr.Zero)
            {
                double g;
                if (TryReadSmcSp78Unlocked("TGDD", out g) && g >= 15.0) return g;
                if (TryReadSmcSp78Unlocked("TG0P", out g) && g >= 15.0) return g;
            }
            return 45.0;
        }

        public int SampleSensorsAndUpdateFans(AppSettings cfg, bool forceWrite)
        {
            lock (_lock)
            {
                double cpuAvg = ReadCpuCoreAverageUnlocked();
                double gpuTemp = ReadGpu5500MTempUnlocked();
                int roundedCpuAvg = (int)Math.Round(cpuAvg);

                if (_smcHandle == IntPtr.Zero) return roundedCpuAvg;

                if (String.Equals(cfg.FanMode, "full", StringComparison.OrdinalIgnoreCase))
                {
                    byte m0, m1;
                    float t0, t1;
                    bool needWrite = forceWrite
                        || !TryReadSmcByteUnlocked("F0Md", out m0) || m0 != 1
                        || !TryReadSmcByteUnlocked("F1Md", out m1) || m1 != 1
                        || !TryReadSmcFloatUnlocked("F0Tg", out t0) || Math.Abs(t0 - _f0Max) > 25f
                        || !TryReadSmcFloatUnlocked("F1Tg", out t1) || Math.Abs(t1 - _f1Max) > 25f;

                    if (needWrite)
                    {
                        WriteSmcByteUnlocked("F0Md", 1);
                        WriteSmcFloatUnlocked("F0Tg", _f0Max);
                        WriteSmcByteUnlocked("F1Md", 1);
                        WriteSmcFloatUnlocked("F1Tg", _f1Max);
                        _lastAppliedF0 = _f0Max;
                        _lastAppliedF1 = _f1Max;
                    }
                    return roundedCpuAvg;
                }

                // "curve": Progressive (r^1.5) Boot Camp curve — silent at ~68-72C dGPU/pinned idle, 100% by 88C/86C
                if (_smoothedCpuAvg < 0 || forceWrite) _smoothedCpuAvg = cpuAvg;
                else _smoothedCpuAvg = 0.45 * cpuAvg + 0.55 * _smoothedCpuAvg;

                if (_smoothedGpu5500M < 0 || forceWrite) _smoothedGpu5500M = gpuTemp;
                else _smoothedGpu5500M = 0.45 * gpuTemp + 0.55 * _smoothedGpu5500M;

                double rLeft = (_smoothedCpuAvg - cfg.LeftMinC) / (double)Math.Max(1, cfg.LeftMaxC - cfg.LeftMinC);
                if (rLeft < 0.0) rLeft = 0.0;
                if (rLeft > 1.0) rLeft = 1.0;
                rLeft = Math.Pow(rLeft, 1.5);

                double rRight = (_smoothedGpu5500M - cfg.RightMinC) / (double)Math.Max(1, cfg.RightMaxC - cfg.RightMinC);
                if (rRight < 0.0) rRight = 0.0;
                if (rRight > 1.0) rRight = 1.0;
                rRight = Math.Pow(rRight, 1.5);

                float wantF0 = (float)Math.Round(_f0Min + rLeft * (_f0Max - _f0Min));
                float wantF1 = (float)Math.Round(_f1Min + rRight * (_f1Max - _f1Min));

                byte cm0, cm1;
                bool modeDrifted = !TryReadSmcByteUnlocked("F0Md", out cm0) || cm0 != 1
                                || !TryReadSmcByteUnlocked("F1Md", out cm1) || cm1 != 1;

                if (forceWrite || modeDrifted || Math.Abs(wantF0 - _lastAppliedF0) >= 40f || Math.Abs(wantF1 - _lastAppliedF1) >= 40f)
                {
                    WriteSmcByteUnlocked("F0Md", 1);
                    WriteSmcFloatUnlocked("F0Tg", wantF0);
                    WriteSmcByteUnlocked("F1Md", 1);
                    WriteSmcFloatUnlocked("F1Tg", wantF1);
                    _lastAppliedF0 = wantF0;
                    _lastAppliedF1 = wantF1;
                }
                return roundedCpuAvg;
            }
        }

        // Internal safety fail-safe only: hand fan control back to Apple T2 on app Quit / Windows shutdown
        public void RestoreHardwareFanAutoSafe()
        {
            lock (_lock)
            {
                if (_smcHandle == IntPtr.Zero) return;
                WriteSmcByteUnlocked("F0Md", 0);
                WriteSmcByteUnlocked("F1Md", 0);
            }
        }

        private int ReadPackageTempUnlocked()
        {
            ulong pkgTherm;
            if (TryReadMsrUnlocked(0x1B1, out pkgTherm))
                return _tjMax - (int)((pkgTherm >> 16) & 0x7F);
            return 60;
        }

        private bool TryReadMsrUnlocked(uint msr, out ulong val)
        {
            ulong[] inBuf = new ulong[] { msr };
            ulong[] outBuf = new ulong[1];
            UIntPtr ret;
            int hr = pawnio_execute(_msrHandle, "ioctl_read_msr", inBuf, new UIntPtr(1), outBuf, new UIntPtr(1), out ret);
            val = outBuf[0];
            return hr >= 0;
        }

        private bool WriteMsrUnlocked(uint msr, ulong val)
        {
            ulong[] inBuf = new ulong[] { msr, val };
            UIntPtr ret;
            int hr = pawnio_execute(_msrHandle, "ioctl_write_msr", inBuf, new UIntPtr(2), null, UIntPtr.Zero, out ret);
            return hr >= 0;
        }

        public ulong BuildPkgLimit(ProfileSpec p, ulong baseVal)
        {
            ulong rawPl1 = (ulong)Math.Round(p.Pl1Watts / _powerUnit) & 0x7FFFUL;
            ulong rawPl2 = (ulong)Math.Round(p.Pl2Watts / _powerUnit) & 0x7FFFUL;
            ulong tauBits = (p.TauY < 0)
                ? (0x00DD0000UL & 0x00FE0000UL)
                : (((ulong)(p.TauY & 0x1F)) << 17);
            ulong lo = (baseVal & ~0x00FF7FFFUL) | rawPl1 | (1UL << 15) | (1UL << 16) | tauBits;
            ulong hiOrig = (baseVal >> 32) & ~0x1FFFFUL;
            ulong hi = hiOrig | rawPl2 | (1UL << 15) | (p.Pl2Clamp ? (1UL << 16) : 0UL);
            return (lo & 0xFFFFFFFFUL) | (hi << 32);
        }

        public ulong BuildTempTarget(ProfileSpec p, ulong baseVal)
        {
            return (baseVal & ~(0x3FUL << 24)) | (((ulong)(p.TccOffset & 0x3F)) << 24);
        }

        public bool ApplyProfile(ProfileSpec p, bool enforcePowerCfg)
        {
            lock (_lock)
            {
                if (_msrHandle == IntPtr.Zero) return false;
                ulong curPkg, curTemp;
                if (!TryReadMsrUnlocked(0x610, out curPkg)) curPkg = _bootPkgLimit;
                if (!TryReadMsrUnlocked(0x1A2, out curTemp)) curTemp = _bootTempTarget;

                ulong wantPkg = BuildPkgLimit(p, curPkg);
                ulong wantTemp = BuildTempTarget(p, curTemp);

                bool ok1 = WriteMsrUnlocked(0x1A2, wantTemp);
                bool ok2 = WriteMsrUnlocked(0x610, wantPkg);

                if (enforcePowerCfg)
                {
                    EnforceAcPowerCfg();
                }
                return ok1 && ok2;
            }
        }

        public bool EnsureNoCpuDrift(ProfileSpec p)
        {
            lock (_lock)
            {
                if (_msrHandle == IntPtr.Zero) return false;
                ulong curPkg, curTemp;
                if (!TryReadMsrUnlocked(0x610, out curPkg) || !TryReadMsrUnlocked(0x1A2, out curTemp))
                    return false;

                ulong wantPkg = BuildPkgLimit(p, curPkg);
                ulong wantTemp = BuildTempTarget(p, curTemp);
                if ((curPkg & 0x0001FFFF00FF7FFFUL) != (wantPkg & 0x0001FFFF00FF7FFFUL) ||
                    ((curTemp >> 24) & 0x3FUL) != (ulong)(p.TccOffset & 0x3F))
                {
                    WriteMsrUnlocked(0x1A2, wantTemp);
                    WriteMsrUnlocked(0x610, wantPkg);
                    return true;
                }
                return false;
            }
        }

        public static void EnforceAcPowerCfg()
        {
            try
            {
                RunHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 893dee8e-2bef-41e0-89c6-b55d0929964c 100");
                RunHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 4b70f900-cdd9-4e66-aa26-ae8417f98173 0");
                RunHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 4b70f900-cdd9-4e66-aa26-ae8417f98174 0");
                RunHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 4b70f900-cdd9-4e66-aa26-ae8417f98175 0");
                RunHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
            }
            catch { }
        }

        public static int RunHidden(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process proc = Process.Start(psi))
            {
                proc.WaitForExit(6000);
                return proc.ExitCode;
            }
        }

        public string ReadTelemetrySummary(AppSettings cfg, out string sensorLine, out string fanLine, out string siliconLine, out string clipLine, out string gpuLine)
        {
            lock (_lock)
            {
                sensorLine = "Sensors: unavailable";
                fanLine = "Fans: T2 SMC unavailable";
                siliconLine = "Silicon: unavailable";
                clipLine = "Limits:  unavailable";
                gpuLine = DetectGpuRouting();

                double cpuAvg = ReadCpuCoreAverageUnlocked();
                double gpu5500M = ReadGpu5500MTempUnlocked();
                int pkgTemp = ReadPackageTempUnlocked();
                sensorLine = String.Format("Sensors: CPU Avg {0:F0} °C (Pkg {1} °C)  |  5500M {2:F0} °C", cpuAvg, pkgTemp, gpu5500M);

                if (_smcHandle != IntPtr.Zero)
                {
                    float f0Ac, f1Ac, f0Tg, f1Tg;
                    TryReadSmcFloatUnlocked("F0Ac", out f0Ac);
                    TryReadSmcFloatUnlocked("F1Ac", out f1Ac);
                    TryReadSmcFloatUnlocked("F0Tg", out f0Tg);
                    TryReadSmcFloatUnlocked("F1Tg", out f1Tg);

                    string modeDesc = String.Equals(cfg.FanMode, "full", StringComparison.OrdinalIgnoreCase)
                        ? "Full Blast 100%"
                        : String.Format("Auto Curve Tg {0:F0}/{1:F0}", f0Tg, f1Tg);

                    fanLine = String.Format("Fans: Left {0:F0} / Right {1:F0} RPM  ({2})", f0Ac, f1Ac, modeDesc);
                }

                if (_msrHandle == IntPtr.Zero) return "PawnIO not initialized";

                ulong eNow, pkgLim, tempTarget, reasons;
                TryReadMsrUnlocked(0x611, out eNow);
                long nowTicks = Stopwatch.GetTimestamp();
                uint curRaw = (uint)(eNow & 0xFFFFFFFFUL);
                double dt = (double)(nowTicks - _lastEnergyTicks) / Stopwatch.Frequency;
                double watts = 0.0;
                if (dt >= 0.05 && dt < 120.0)
                {
                    uint de = curRaw - _lastEnergyRaw;
                    watts = (de * _energyUnit) / dt;
                }
                else
                {
                    uint e1 = curRaw;
                    Thread.Sleep(120);
                    ulong e2;
                    TryReadMsrUnlocked(0x611, out e2);
                    nowTicks = Stopwatch.GetTimestamp();
                    curRaw = (uint)(e2 & 0xFFFFFFFFUL);
                    watts = ((curRaw - e1) * _energyUnit) / 0.120;
                }
                _lastEnergyRaw = curRaw;
                _lastEnergyTicks = nowTicks;

                TryReadMsrUnlocked(0x610, out pkgLim);
                TryReadMsrUnlocked(0x1A2, out tempTarget);
                TryReadMsrUnlocked(0x64F, out reasons);

                double pl1 = (pkgLim & 0x7FFFUL) * _powerUnit;
                double pl2 = ((pkgLim >> 32) & 0x7FFFUL) * _powerUnit;
                int tauY = (int)((pkgLim >> 17) & 0x1F);
                int tauZ = (int)((pkgLim >> 22) & 0x3);
                double tauSec = Math.Pow(2.0, tauY) * (1.0 + tauZ / 4.0) * _timeUnit;
                int tccOff = (int)((tempTarget >> 24) & 0x3F);

                double mhz = 0;
                double ppc = 100;
                if (_pcPerf != null && _pcLimit != null)
                {
                    try
                    {
                        mhz = _pcPerf.NextValue() * 23.01;
                        ppc = _pcLimit.NextValue();
                    }
                    catch { }
                }

                siliconLine = String.Format("Silicon: PL1={0:F0}W / PL2={1:F0}W (Tau={2:F0}s) | TjGuard={3}C",
                    pl1, pl2, tauSec, _tjMax - tccOff);

                string flags = "";
                if ((reasons & (1UL << 0)) != 0) flags += "PROCHOT ";
                if ((reasons & (1UL << 1)) != 0) flags += "ThermGuard ";
                if ((reasons & (1UL << 10)) != 0) flags += "PL1 ";
                if ((reasons & (1UL << 11)) != 0) flags += "PL2 ";
                if (flags.Length == 0) flags = "Unthrottled";

                clipLine = String.Format("PCU State: {0} | Apple T2 _PPC={1:F0}%", flags.Trim(), ppc);

                if (mhz > 100)
                    return String.Format("CPU Power: {0:F1} W  |  {1:F0} MHz", watts, mhz);
                return String.Format("CPU Power: {0:F1} W", watts);
            }
        }

        private static string DetectGpuRouting()
        {
            try
            {
                bool hasVega = false;
                bool has5500M = false;
                using (ManagementObjectSearcher mos = new ManagementObjectSearcher("SELECT Name, Status FROM Win32_VideoController"))
                {
                    foreach (ManagementObject mo in mos.Get())
                    {
                        string name = Convert.ToString(mo["Name"]);
                        string st = Convert.ToString(mo["Status"]);
                        if (st == "OK")
                        {
                            if (name.IndexOf("Vega", StringComparison.OrdinalIgnoreCase) >= 0) hasVega = true;
                            if (name.IndexOf("5500M", StringComparison.OrdinalIgnoreCase) >= 0) has5500M = true;
                        }
                    }
                }
                if (hasVega) return "GPU Offload: RX Vega 56 eGPU Active (5500M offloaded)";
                if (has5500M) return "GPU Offload: Radeon Pro 5500M Only (Vega 56 unplugged)";
            }
            catch { }
            return "GPU Offload: Unknown";
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_pcPerf != null) { _pcPerf.Dispose(); _pcPerf = null; }
                if (_pcLimit != null) { _pcLimit.Dispose(); _pcLimit = null; }
                if (_smcHandle != IntPtr.Zero)
                {
                    pawnio_close(_smcHandle);
                    _smcHandle = IntPtr.Zero;
                }
                if (_msrHandle != IntPtr.Zero)
                {
                    pawnio_close(_msrHandle);
                    _msrHandle = IntPtr.Zero;
                }
            }
        }
    }

    internal sealed class TrayBoostContext : ApplicationContext
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const string TaskName = "WinCpuBoost";
        private readonly ProfileSpec[] _profiles;
        private readonly SiliconEngine _engine;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _powerItem;
        private readonly ToolStripMenuItem _sensorItem;
        private readonly ToolStripMenuItem _fanLiveItem;
        private readonly ToolStripMenuItem _siliconItem;
        private readonly ToolStripMenuItem _clipItem;
        private readonly ToolStripMenuItem _gpuItem;

        private readonly ToolStripMenuItem _fanCurveItem;
        private readonly ToolStripMenuItem _fanFullItem;
        private readonly ToolStripMenuItem[] _profileItems;
        private readonly ToolStripMenuItem _autoStartItem;

        private readonly System.Windows.Forms.Timer _loopTimer;
        private readonly string _settingsFile;
        private readonly AppSettings _settings;
        private ProfileSpec _activeProfile;
        private int _tickCounter = 0;
        private int _lastRenderedTemp = -999;
        private string _lastRenderedMode = "";
        private IntPtr _currentIconHandle = IntPtr.Zero;

        public TrayBoostContext(ProfileSpec[] profiles, SiliconEngine engine, string settingsFile)
        {
            _profiles = profiles;
            _engine = engine;
            _settingsFile = settingsFile;

            _settings = AppSettings.Load(settingsFile);
            _activeProfile = FindProfile(profiles, _settings.CpuProfile) ?? profiles[0];

            _engine.ApplyProfile(_activeProfile, true);
            int initCpuAvg = _engine.SampleSensorsAndUpdateFans(_settings, true);

            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Segoe UI", 9.25f, FontStyle.Regular);
            _menu.BackColor = DarkColorTable.BgDark;
            _menu.ForeColor = Color.FromArgb(242, 244, 248);
            _menu.Padding = new Padding(2, 4, 2, 4);

            ToolStripMenuItem header = new ToolStripMenuItem("WinCpuBoost — i9-9880H + T2 Fans");
            header.Font = new Font("Segoe UI Semibold", 9.25f, FontStyle.Bold);
            header.Enabled = false;
            _menu.Renderer = new DarkMenuRenderer(header);
            _menu.Opening += OnMenuOpening;
            _menu.Opened += OnMenuOpened;
            _menu.Items.Add(header);

            _sensorItem = new ToolStripMenuItem("Sensors: sampling...") { Enabled = false };
            _fanLiveItem = new ToolStripMenuItem("Fans: sampling...") { Enabled = false };
            _powerItem = new ToolStripMenuItem("CPU Power: sampling...") { Enabled = false };
            _siliconItem = new ToolStripMenuItem("Silicon: ...") { Enabled = false };
            _clipItem = new ToolStripMenuItem("PCU State: ...") { Enabled = false };
            _gpuItem = new ToolStripMenuItem("GPU Offload: ...") { Enabled = false };
            _menu.Items.Add(_sensorItem);
            _menu.Items.Add(_fanLiveItem);
            _menu.Items.Add(_powerItem);
            _menu.Items.Add(_siliconItem);
            _menu.Items.Add(_clipItem);
            _menu.Items.Add(_gpuItem);
            _menu.Items.Add(new ToolStripSeparator());

            // Only Automatic (saved Boot Camp progressive curve) vs Full Blast
            _fanCurveItem = new ToolStripMenuItem(String.Format(
                "Fans: Automatic (L: CPU {0}→{1}°C | R: 5500M {2}→{3}°C)",
                _settings.LeftMinC, _settings.LeftMaxC, _settings.RightMinC, _settings.RightMaxC));
            _fanCurveItem.Click += (s, e) => SetFanMode("curve");
            _menu.Items.Add(_fanCurveItem);

            _fanFullItem = new ToolStripMenuItem("Fans: Full Blast (L: 5616 RPM | R: 5200 RPM)");
            _fanFullItem.Click += (s, e) => SetFanMode("full");
            _menu.Items.Add(_fanFullItem);
            _menu.Items.Add(new ToolStripSeparator());

            _profileItems = new ToolStripMenuItem[_profiles.Length];
            for (int i = 0; i < _profiles.Length; i++)
            {
                ProfileSpec p = _profiles[i];
                ToolStripMenuItem item = new ToolStripMenuItem(p.MenuTitle);
                item.Tag = p;
                item.Click += OnProfileClick;
                _profileItems[i] = item;
                _menu.Items.Add(item);
            }

            _menu.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem("Start Silently at Windows Login");
            _autoStartItem.Click += OnToggleAutoStart;
            _menu.Items.Add(_autoStartItem);

            ToolStripMenuItem quitItem = new ToolStripMenuItem("Quit WinCpuBoost");
            quitItem.Click += OnQuit;
            _menu.Items.Add(quitItem);

            _tray = new NotifyIcon();
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            UpdateTrayAppearance(initCpuAvg);

            _loopTimer = new System.Windows.Forms.Timer();
            _loopTimer.Interval = 3000;
            _loopTimer.Tick += OnLoopTick;
            _loopTimer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;
        }

        private void OnMenuOpened(object sender, EventArgs e)
        {
            try
            {
                int dark = 1;
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(_menu.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(_menu.Handle, 33, ref round, 4);
            }
            catch { }
        }

        private void OnLoopTick(object sender, EventArgs e)
        {
            _tickCounter++;
            int cpuAvg = _engine.SampleSensorsAndUpdateFans(_settings, false);
            UpdateTrayAppearance(cpuAvg);
            if (_tickCounter % 10 == 0)
            {
                _engine.EnsureNoCpuDrift(_activeProfile);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume || e.Mode == PowerModes.StatusChange)
            {
                _engine.ApplyProfile(_activeProfile, false);
                int cpuAvg = _engine.SampleSensorsAndUpdateFans(_settings, true);
                UpdateTrayAppearance(cpuAvg);
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            _engine.RestoreHardwareFanAutoSafe();
        }

        private void SetFanMode(string mode)
        {
            _settings.FanMode = mode;
            _settings.Save(_settingsFile);
            int cpuAvg = _engine.SampleSensorsAndUpdateFans(_settings, true);
            UpdateTrayAppearance(cpuAvg);
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            string sLine, fLine, silLine, cLine, gLine;
            _powerItem.Text = _engine.ReadTelemetrySummary(_settings, out sLine, out fLine, out silLine, out cLine, out gLine);
            _sensorItem.Text = sLine;
            _fanLiveItem.Text = fLine;
            _siliconItem.Text = silLine;
            _clipItem.Text = cLine;
            _gpuItem.Text = gLine;

            _fanCurveItem.Checked = !String.Equals(_settings.FanMode, "full", StringComparison.OrdinalIgnoreCase);
            _fanFullItem.Checked = String.Equals(_settings.FanMode, "full", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < _profileItems.Length; i++)
                _profileItems[i].Checked = (_profileItems[i].Tag == _activeProfile);

            _autoStartItem.Checked = IsAutoStartRegistered();
        }

        private void OnProfileClick(object sender, EventArgs e)
        {
            ToolStripMenuItem item = sender as ToolStripMenuItem;
            if (item == null) return;
            ProfileSpec p = item.Tag as ProfileSpec;
            if (p == null) return;
            _activeProfile = p;
            _settings.CpuProfile = p.Id;
            _settings.Save(_settingsFile);
            _engine.ApplyProfile(_activeProfile, true);
            int cpuAvg = _engine.SampleSensorsAndUpdateFans(_settings, false);
            _lastRenderedMode = "";
            UpdateTrayAppearance(cpuAvg);
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            if (IsAutoStartRegistered())
                UnregisterAutoStart();
            else
                RegisterAutoStart();
        }

        public static bool IsAutoStartRegistered()
        {
            return SiliconEngine.RunHidden("schtasks.exe", "/Query /TN \"" + TaskName + "\"") == 0;
        }

        public static bool RegisterAutoStart()
        {
            string exePath = Application.ExecutablePath;
            string ps = String.Format(
                "$a = New-ScheduledTaskAction -Execute '{0}'; " +
                "$t = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME; " +
                "$p = New-ScheduledTaskPrincipal -UserId \"$env:USERDOMAIN\\$env:USERNAME\" -LogonType Interactive -RunLevel Highest; " +
                "$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero); " +
                "Register-ScheduledTask -TaskName '{1}' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null",
                exePath.Replace("'", "''"), TaskName);
            return SiliconEngine.RunHidden("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps.Replace("\"", "\\\"") + "\"") == 0;
        }

        public static void UnregisterAutoStart()
        {
            SiliconEngine.RunHidden("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"");
        }

        private void UpdateTrayAppearance(int cpuCoreAvgC)
        {
            string modeKey = _activeProfile.Id + "|" + _settings.FanMode;
            if (cpuCoreAvgC != _lastRenderedTemp)
            {
                _tray.Icon = RenderMacsFanStyleSquareIcon(cpuCoreAvgC);
                _lastRenderedTemp = cpuCoreAvgC;
            }
            if (modeKey != _lastRenderedMode || cpuCoreAvgC != _lastRenderedTemp)
            {
                string fanShort = String.Equals(_settings.FanMode, "full", StringComparison.OrdinalIgnoreCase)
                    ? "Fans:FullBlast" : "Fans:Auto(68-88)";
                string tip = String.Format("CPU Avg: {0}°C | {1:F0}/{2:F0}W | {3}", cpuCoreAvgC, _activeProfile.Pl1Watts, _activeProfile.Pl2Watts, fanShort);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                _tray.Text = tip;
                _lastRenderedMode = modeKey;
            }
        }

        private Icon RenderMacsFanStyleSquareIcon(int tempC)
        {
            string text = tempC.ToString();
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(255, 0, 0, 128)); // #000080 navy square

                float pxSize = text.Length <= 2 ? 21.0f : 15.5f;
                using (Font f = new Font("Segoe UI", pxSize, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(text, f, fg, new RectangleF(0.5f, 1.0f, 31.5f, 31.0f), sf);
                }

                IntPtr hIcon = bmp.GetHicon();
                if (_currentIconHandle != IntPtr.Zero) DestroyIcon(_currentIconHandle);
                _currentIconHandle = hIcon;
                return Icon.FromHandle(hIcon);
            }
        }

        private void OnQuit(object sender, EventArgs e)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionEnding -= OnSessionEnding;
            _loopTimer.Stop();
            _engine.RestoreHardwareFanAutoSafe();
            _tray.Visible = false;
            if (_currentIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_currentIconHandle);
                _currentIconHandle = IntPtr.Zero;
            }
            _tray.Dispose();
            _engine.Dispose();
            Application.Exit();
        }

        public static ProfileSpec FindProfile(ProfileSpec[] list, string id)
        {
            if (String.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < list.Length; i++)
            {
                if (String.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return list[i];
            }
            return null;
        }
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static ProfileSpec[] BuildProfiles()
        {
            return new ProfileSpec[]
            {
                new ProfileSpec("max",   "CPU: MAX Boost (75W / 80W, 97°C Guard)", 75.0, 80.0, 11, true, 3),
                new ProfileSpec("quiet", "CPU: Quiet / Eco (45W / 60W, 95°C Guard)", 45.0, 60.0, 11, true, 5)
            };
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string msrModPath = Path.Combine(baseDir, "IntelMSR.bin");
            string smcModPath = Path.Combine(baseDir, "AppleT2Smc.bin");
            string settingsFile = Path.Combine(baseDir, "settings.ini");
            ProfileSpec[] profiles = BuildProfiles();

            if (args != null && args.Length > 0)
            {
                using (SiliconEngine eng = new SiliconEngine())
                {
                    if (!eng.Initialize(msrModPath, smcModPath))
                    {
                        Console.Error.WriteLine(eng.LastError);
                        return 2;
                    }
                    AppSettings cfg = AppSettings.Load(settingsFile);
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "--apply" && i + 1 < args.Length)
                        {
                            ProfileSpec target = TrayBoostContext.FindProfile(profiles, args[++i]);
                            if (target == null) { Console.Error.WriteLine("Unknown profile"); return 3; }
                            eng.ApplyProfile(target, true);
                            cfg.CpuProfile = target.Id;
                            cfg.Save(settingsFile);
                        }
                        else if (args[i] == "--fans" && i + 1 < args.Length)
                        {
                            string fm = args[++i].ToLowerInvariant();
                            if (fm != "full" && fm != "curve") { Console.Error.WriteLine("Unknown fan mode (use curve or full)"); return 4; }
                            cfg.FanMode = fm;
                            cfg.Save(settingsFile);
                            eng.SampleSensorsAndUpdateFans(cfg, true);
                        }
                        else if (args[i] == "--install-autostart")
                        {
                            TrayBoostContext.RegisterAutoStart();
                        }
                    }

                    string sLine, fLine, silLine, cLine, gLine;
                    string pwr = eng.ReadTelemetrySummary(cfg, out sLine, out fLine, out silLine, out cLine, out gLine);
                    Console.WriteLine(String.Format("SavedConfig: cpu={0} | fans={1} (L:CPU {2}->{3}C, R:5500M {4}->{5}C)",
                        cfg.CpuProfile, cfg.FanMode, cfg.LeftMinC, cfg.LeftMaxC, cfg.RightMinC, cfg.RightMaxC));
                    Console.WriteLine(sLine);
                    Console.WriteLine(fLine);
                    Console.WriteLine(pwr);
                    Console.WriteLine(silLine);
                    Console.WriteLine(cLine);
                    Console.WriteLine(gLine);
                    Console.WriteLine("AutoStartTask: " + TrayBoostContext.IsAutoStartRegistered());
                    return 0;
                }
            }

            bool createdNew;
            using (Mutex mtx = new Mutex(true, "Local\\WinCpuBoost_MacBookPro16_1_Tray", out createdNew))
            {
                if (!createdNew) return 0;

                bool isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                if (!isAdmin)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                        psi.Verb = "runas";
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch { }
                    return 0;
                }

                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                SiliconEngine engine = new SiliconEngine();
                if (!engine.Initialize(msrModPath, smcModPath))
                {
                    MessageBox.Show(engine.LastError, "WinCpuBoost Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }

                Application.Run(new TrayBoostContext(profiles, engine, settingsFile));
                return 0;
            }
        }
    }
}
