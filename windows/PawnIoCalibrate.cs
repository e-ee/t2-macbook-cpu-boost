using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class PawnIoCalibrate
{
    [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    static extern int pawnio_open(out IntPtr handle);

    [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    static extern int pawnio_load(IntPtr handle, byte[] blob, UIntPtr size);

    [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true, CharSet = CharSet.Ansi)]
    static extern int pawnio_execute(IntPtr handle, string name, ulong[] inBuf, UIntPtr inSize, ulong[] outBuf, UIntPtr outSize, out UIntPtr returnSize);

    [DllImport(@"C:\Program Files\PawnIO\PawnIOLib.dll", CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    static extern int pawnio_close(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    static volatile bool s_stop = false;

    static bool TryReadMsr(IntPtr h, uint msr, out ulong val)
    {
        ulong[] inBuf = new ulong[] { msr };
        ulong[] outBuf = new ulong[1];
        UIntPtr ret;
        int hr = pawnio_execute(h, "ioctl_read_msr", inBuf, new UIntPtr(1), outBuf, new UIntPtr(1), out ret);
        val = outBuf[0];
        return hr >= 0;
    }

    static int WriteMsr(IntPtr h, uint msr, ulong val)
    {
        ulong[] inBuf = new ulong[] { msr, val };
        UIntPtr ret;
        return pawnio_execute(h, "ioctl_write_msr", inBuf, new UIntPtr(2), null, UIntPtr.Zero, out ret);
    }

    static string DecodePerfLimitReasons(ulong v)
    {
        string[] names = new string[] {
            "PROCHOT(0)", "Thermal(1)", "Res2", "Res3", "Residency(4)", "RATL(5)",
            "VRThermAlert(6)", "VRThermDesignCurr(7)", "Other(8)", "Res9",
            "PL1(10)", "PL2(11)", "MaxTurbo(12)", "TurboTransAtten(13)"
        };
        string active = "";
        for (int b = 0; b < names.Length; b++)
        {
            if (((v >> b) & 1UL) != 0) active += (active.Length > 0 ? "," : "") + names[b];
        }
        return active.Length == 0 ? "none" : active;
    }

    static double BurnScalar(int iterations)
    {
        ulong a = 0x9E3779B97F4A7C15UL, b = 0xBF58476D1CE4E5B9UL, c = 0x94D049BB133111EBUL;
        double x = 1.0000001192092896, y = 1.0000002384185791;
        for (int i = 0; i < iterations; i++)
        {
            a ^= a >> 12; a ^= a << 25; a ^= a >> 27;
            b = (b + a) * 0x2545F4914F6CDD1DUL;
            c = (c ^ b) + 0x9E3779B97F4A7C15UL;
            x = x * 1.0000000001 + y * 0.0000000001;
            y = y * 0.9999999999 + x * 0.0000000001;
        }
        return x + y + (c & 0xFF);
    }

    static ulong EncodePowerLimitFastTau(ulong orig, double pl1Watts, double pl2Watts, double powerUnit, int tauY)
    {
        // tauY=11, tauZ=0 -> 2^11 / 1024 = 2.0s window (replaces 28s Tau that let CPU overshoot 85W into 100C!)
        ulong rawPl1 = (ulong)Math.Round(pl1Watts / powerUnit) & 0x7FFFUL;
        ulong rawPl2 = (ulong)Math.Round(pl2Watts / powerUnit) & 0x7FFFUL;
        ulong tauBits = ((ulong)(tauY & 0x1F)) << 17; // Z=0 in bits 23:22
        ulong lo = (orig & ~0x00FF7FFFUL) | rawPl1 | (1UL << 15) | (1UL << 16) | tauBits;
        ulong hiOrig = (orig >> 32) & ~0x1FFFFUL;
        ulong hi = hiOrig | rawPl2 | (1UL << 15) | (1UL << 16);
        return (lo & 0xFFFFFFFFUL) | (hi << 32);
    }

    static void RunTestMode(IntPtr h, string label, double pl1W, double pl2W, int tauY, int tccOffset, int seconds, double powerUnit, double energyUnit, int tjMax, ulong origPkgLimit, ulong origTempTarget)
    {
        ulong newTempTarget = (origTempTarget & ~(0x3FUL << 24)) | (((ulong)(tccOffset & 0x3F)) << 24);
        int hrTemp = WriteMsr(h, 0x1A2, newTempTarget);
        ulong rbTemp;
        TryReadMsr(h, 0x1A2, out rbTemp);
        int rbOffset = (int)((rbTemp >> 24) & 0x3F);

        ulong targetMsr = EncodePowerLimitFastTau(origPkgLimit, pl1W, pl2W, powerUnit, tauY);
        int hrPwr = WriteMsr(h, 0x610, targetMsr);
        ulong rbPwr;
        TryReadMsr(h, 0x610, out rbPwr);

        Console.WriteLine(String.Format("\n=== SWEEP2 [{0}]: PL1={1:F0}W / PL2={2:F0}W (Tau=2s, hr=0x{3:X8}, rb=0x{4:X16}) | TCC_Offset={5}C->rb={6}C (hr=0x{7:X8}, raw=0x{8:X16}) ===",
            label, pl1W, pl2W, hrPwr, rbPwr, tccOffset, rbOffset, hrTemp, rbTemp));

        const double nominalMhz = 2301.0;
        using (var pcTotalPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"))
        using (var pcLimit     = new PerformanceCounter("Processor Information", "% Performance Limit", "_Total"))
        {
            pcTotalPerf.NextValue(); pcLimit.NextValue();
            s_stop = false;
            long totalOps = 0;
            int threads = Environment.ProcessorCount;
            Thread[] pool = new Thread[threads];
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < threads; i++)
            {
                int idx = i;
                pool[i] = new Thread(() =>
                {
                    SetThreadAffinityMask(GetCurrentThread(), new UIntPtr(1UL << idx));
                    long local = 0;
                    double acc = 0;
                    while (!s_stop) { acc += BurnScalar(250000); local += 250000; }
                    Interlocked.Add(ref totalOps, local);
                    if (acc == -99999.0) Console.WriteLine(acc);
                });
                pool[i].IsBackground = true;
                pool[i].Start();
            }

            ulong prevE;
            TryReadMsr(h, 0x611, out prevE);
            var eSw = Stopwatch.StartNew();
            int minLimit = 100;
            double minMhz = 99999;
            double sumMhz = 0;

            for (int t = 1; t <= seconds; t++)
            {
                Thread.Sleep(1000);
                ulong curE, pkgTherm, reasons;
                TryReadMsr(h, 0x611, out curE);
                double dt = eSw.Elapsed.TotalSeconds;
                eSw.Restart();
                uint de = (uint)(curE & 0xFFFFFFFFUL) - (uint)(prevE & 0xFFFFFFFFUL);
                prevE = curE;
                double watts = (de * energyUnit) / dt;

                TryReadMsr(h, 0x1B1, out pkgTherm);
                TryReadMsr(h, 0x64F, out reasons);
                int tempC = tjMax - (int)((pkgTherm >> 16) & 0x7F);
                float perfPct = pcTotalPerf.NextValue();
                float limPct  = pcLimit.NextValue();
                double mhz    = perfPct * nominalMhz / 100.0;
                sumMhz += mhz;
                if ((int)limPct < minLimit) minLimit = (int)limPct;
                if (t >= 3 && mhz < minMhz) minMhz = mhz;

                Console.WriteLine(String.Format("  t={0,2}s | {1,6:F2} W | {2,3} C | {3,4:F0} MHz | _PPC={4,3:F0}% | Clip=[{5}]",
                    t, watts, tempC, mhz, limPct, DecodePerfLimitReasons(reasons & 0xFFFFUL)));
            }

            s_stop = true;
            foreach (var th in pool) th.Join();
            sw.Stop();
            double mops = (totalOps / 1e6) / sw.Elapsed.TotalSeconds;
            Console.WriteLine(String.Format("  RESULT [{0}]: AvgThroughput={1:F1} M-iter/s | AvgClock={2:F0} MHz | MinClock(t>=3s)={3:F0} MHz | Min_PPC={4}%",
                label, mops, sumMhz / seconds, minMhz, minLimit));
        }
    }

    static int Main(string[] args)
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IntelMSR.bin");
        IntPtr h;
        int hr = pawnio_open(out h);
        if (hr < 0) { Console.Error.WriteLine("pawnio_open failed"); return 3; }

        try
        {
            byte[] blob = File.ReadAllBytes(binPath);
            if (pawnio_load(h, blob, new UIntPtr((uint)blob.Length)) < 0) return 4;

            ulong raplUnit, origTempTarget, origPkgLimit;
            TryReadMsr(h, 0x606, out raplUnit);
            double powerUnit  = 1.0 / (1 << (int)(raplUnit & 0xF));
            double energyUnit = 1.0 / (1 << (int)((raplUnit >> 8) & 0x1F));
            TryReadMsr(h, 0x1A2, out origTempTarget);
            int tjMax = (int)((origTempTarget >> 16) & 0xFF);
            TryReadMsr(h, 0x610, out origPkgLimit);
            Console.WriteLine(String.Format("Initial 0x1A2=0x{0:X16} (Lock={1}), 0x610=0x{2:X16}",
                origTempTarget, ((origTempTarget >> 30) & 1UL) != 0, origPkgLimit));

            try
            {
                // Wait 3s for cool baseline
                Thread.Sleep(3000);
                // 1. SweetSpot 64W/72W + 2s Tau + TCC_Offset=3 (97C soft guard) for 30s
                RunTestMode(h, "SWEETSPOT_64_72W_TCC3", 64.0, 72.0, 11, 3, 30, powerUnit, energyUnit, tjMax, origPkgLimit, origTempTarget);
                Thread.Sleep(6000);
                // 2. GamingMax 68W/76W + 2s Tau + TCC_Offset=3 (97C soft guard) for 30s
                RunTestMode(h, "GAMINGMAX_68_76W_TCC3", 68.0, 76.0, 11, 3, 30, powerUnit, energyUnit, tjMax, origPkgLimit, origTempTarget);
                Thread.Sleep(6000);
                // 3. FullBlast 74W/80W + 2s Tau + TCC_Offset=3 (97C soft guard) for 25s
                RunTestMode(h, "FULLBLAST_74_80W_TCC3", 74.0, 80.0, 11, 3, 25, powerUnit, energyUnit, tjMax, origPkgLimit, origTempTarget);
            }
            finally
            {
                WriteMsr(h, 0x610, origPkgLimit);
                WriteMsr(h, 0x1A2, origTempTarget);
                Console.WriteLine("\nRestored original 0x610 and 0x1A2.");
            }
            return 0;
        }
        finally
        {
            pawnio_close(h);
        }
    }
}
