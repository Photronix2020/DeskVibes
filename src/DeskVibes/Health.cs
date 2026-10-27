using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeskVibes;

public sealed record DriveStat(string Name, double FreeGb, double TotalGb);

/// <summary>One reading of the PC's health, for the PC health widget.</summary>
public sealed class HealthStats
{
    public double Cpu;
    public double CpuGhz;
    public string CpuName = "";
    public int Threads = Environment.ProcessorCount;
    public double MemUsedGb, MemTotalGb, MemPercent;
    /// <summary>Null when Windows doesn't report the graphics card.</summary>
    public double? Gpu, GpuMemUsedGb;
    public double GpuMemTotalGb;
    public string GpuName = "";
    public double? DiskBusy;
    public double DiskReadMBs, DiskWriteMBs;
    public List<DriveStat> Drives = new();
    public double DownMbps, UpMbps;
    public bool Online = true;
    /// <summary>Round trip to the internet in ms; null when not measured yet or it failed.</summary>
    public int? PingMs;
    public bool PingFailed;
    public int Processes;
    public string TopCpuApp = "", TopMemApp = "";
    public double TopCpuPercent, TopMemGb;
    public TimeSpan Uptime;
    public int? Battery;
    public bool Charging;
    public TimeSpan? BatteryLeft;
    public DateTime Taken;
}

/// <summary>Measures the PC every 2 seconds on a background thread, only while a PC health widget is asking.
/// Uses Windows performance counters (PDH) for the graphics card and disks, so there are no extra packages or admin rights.</summary>
public static class Health
{
    public const string PingHost = "1.1.1.1";
    static volatile HealthStats current = new() { Taken = DateTime.MinValue };
    static long wantedTicks;
    static int running;

    /// <summary>The latest reading (starts measuring if it isn't already).</summary>
    public static HealthStats Get()
    {
        Interlocked.Exchange(ref wantedTicks, DateTime.UtcNow.Ticks);
        if (Interlocked.CompareExchange(ref running, 1, 0) == 0)
            new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "DeskVibes health" }.Start();
        return current;
    }

    static void Loop()
    {
        using var s = new Sampler();
        try
        {
            while (DateTime.UtcNow - new DateTime(Interlocked.Read(ref wantedTicks)) < TimeSpan.FromSeconds(30))
            {
                try { current = s.Sample(ping: true); } catch { }
                Thread.Sleep(2000);
            }
        }
        finally { running = 0; }
    }

    // ---------- the sums (kept apart so they can be tested) ----------

    /// <summary>Graphics card use like Task Manager shows it: each engine's use added up over all apps, then the busiest engine.
    /// Instance names look like "pid_1234_luid_0x0_0xD1A4_phys_0_eng_0_engtype_3D".</summary>
    public static double GpuPercent(IEnumerable<(string name, double value)> engines)
    {
        var per = new Dictionary<string, double>();
        foreach (var (name, value) in engines)
        {
            var luid = Regex.Match(name, @"luid_0x[0-9a-f]+_0x[0-9a-f]+", RegexOptions.IgnoreCase).Value;
            var eng = Regex.Match(name, @"eng_(\d+)").Value;
            var key = luid + "|" + eng;
            per[key] = per.GetValueOrDefault(key) + Math.Max(0, value);
        }
        return per.Count == 0 ? 0 : Math.Clamp(per.Values.Max(), 0, 100);
    }

    /// <summary>Bytes counted between two readings → megabits per second.</summary>
    public static double Mbps(long now, long before, double seconds) =>
        seconds <= 0 || now < before ? 0 : (now - before) * 8 / 1_000_000.0 / seconds;

    /// <summary>"12.3 Mbps", "850 kbps".</summary>
    public static string Speed(double mbps) => mbps >= 100 ? $"{mbps:0} Mbps" : mbps >= 1 ? $"{mbps:0.0} Mbps" : $"{mbps * 1000:0} kbps";

    public static string Gb(double gb) => gb >= 100 ? $"{gb:0}" : $"{gb:0.0}";

    // ---------- measuring ----------

    public sealed class Sampler : IDisposable
    {
        readonly Pdh pdh = new();
        readonly IntPtr gpuEngines, gpuMem, diskIdle, diskRead, diskWrite, cpuPerf;
        readonly double baseMhz;
        readonly string cpuName, gpuName;
        readonly double gpuTotalGb;
        readonly Dictionary<int, (string name, TimeSpan cpu)> procCpu = new();
        long lastDown, lastUp;
        DateTime last = DateTime.MinValue;
        int count;
        int? ping; bool pingFailed;

        public Sampler()
        {
            gpuEngines = pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
            gpuMem = pdh.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            diskIdle = pdh.Add(@"\PhysicalDisk(_Total)\% Idle Time");
            diskRead = pdh.Add(@"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            diskWrite = pdh.Add(@"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            cpuPerf = pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
            pdh.Collect();
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                baseMhz = Convert.ToDouble(k?.GetValue("~MHz") ?? 0);
                cpuName = Regex.Replace((k?.GetValue("ProcessorNameString") as string ?? "").Trim(), @"\s+", " ");
            }
            catch { cpuName = ""; }
            (gpuName, gpuTotalGb) = BiggestGpu();
        }

        public HealthStats Sample(bool ping)
        {
            var now = DateTime.Now;
            double dt = last == DateTime.MinValue ? 0 : (now - last).TotalSeconds;
            last = now; count++;
            pdh.Collect();
            var s = new HealthStats { Taken = now, CpuName = cpuName, GpuName = gpuName, GpuMemTotalGb = gpuTotalGb };

            s.Cpu = Native.CpuPercent();
            var perf = pdh.Value(cpuPerf);
            if (perf != null && baseMhz > 0) s.CpuGhz = baseMhz * perf.Value / 100 / 1000;
            var (memPct, memTotal) = Native.Memory();
            s.MemPercent = memPct; s.MemTotalGb = memTotal; s.MemUsedGb = memTotal * memPct / 100;

            var engines = pdh.Values(gpuEngines);
            if (engines != null) s.Gpu = GpuPercent(engines);
            var mem = pdh.Values(gpuMem);
            if (mem != null && mem.Count > 0) s.GpuMemUsedGb = mem.Max(m => m.value) / 1073741824.0;

            var idle = pdh.Value(diskIdle);
            if (idle != null) s.DiskBusy = Math.Clamp(100 - idle.Value, 0, 100);
            s.DiskReadMBs = (pdh.Value(diskRead) ?? 0) / 1048576.0;
            s.DiskWriteMBs = (pdh.Value(diskWrite) ?? 0) / 1048576.0;
            try
            {
                s.Drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                    .Select(d => new DriveStat(d.Name.TrimEnd('\\'), d.AvailableFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0)).ToList();
            }
            catch { }

            // network: add up every connected adapter except loopback and tunnels
            long down = 0, up = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPStatistics();
                    down += st.BytesReceived; up += st.BytesSent;
                }
                s.Online = NetworkInterface.GetIsNetworkAvailable();
            }
            catch { }
            if (dt > 0) { s.DownMbps = Mbps(down, lastDown, dt); s.UpMbps = Mbps(up, lastUp, dt); }
            lastDown = down; lastUp = up;

            if (ping && (count % 5 == 1))
            {
                try
                {
                    using var p = new Ping();
                    var r = p.Send(PingHost, 1500);
                    this.ping = r.Status == IPStatus.Success ? (int)r.RoundtripTime : null;
                    pingFailed = r.Status != IPStatus.Success;
                }
                catch { this.ping = null; pingFailed = true; }
            }
            s.PingMs = this.ping; s.PingFailed = pingFailed;

            Apps(s, dt);
            s.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            var ps = SystemInformation.PowerStatus;
            if (!ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) && ps.BatteryChargeStatus != BatteryChargeStatus.Unknown)
            {
                s.Battery = (int)Math.Round(ps.BatteryLifePercent * 100);
                s.Charging = ps.PowerLineStatus == PowerLineStatus.Online;
                if (!s.Charging && ps.BatteryLifeRemaining > 0) s.BatteryLeft = TimeSpan.FromSeconds(ps.BatteryLifeRemaining);
            }
            return s;
        }

        /// <summary>The app using the most CPU (all its processes together) and the one using the most memory.</summary>
        void Apps(HealthStats s, double dt)
        {
            var cpuBy = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var memBy = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();
            var all = Process.GetProcesses();
            s.Processes = all.Length;
            foreach (var p in all)
            {
                using (p)
                {
                    try
                    {
                        var name = p.ProcessName;
                        if (name is "Idle" or "System" or "Memory Compression") continue;
                        seen.Add(p.Id);
                        memBy[name] = memBy.GetValueOrDefault(name) + p.WorkingSet64;
                        var t = p.TotalProcessorTime;
                        if (dt > 0 && procCpu.TryGetValue(p.Id, out var old) && old.name == name)
                            cpuBy[name] = cpuBy.GetValueOrDefault(name) + (t - old.cpu).TotalSeconds / dt / Environment.ProcessorCount * 100;
                        procCpu[p.Id] = (name, t);
                    }
                    catch { }
                }
            }
            foreach (var id in procCpu.Keys.Where(k => !seen.Contains(k)).ToList()) procCpu.Remove(id);
            if (cpuBy.Count > 0) { var top = cpuBy.MaxBy(kv => kv.Value); s.TopCpuApp = top.Key; s.TopCpuPercent = Math.Clamp(top.Value, 0, 100); }
            if (memBy.Count > 0) { var top = memBy.MaxBy(kv => kv.Value); s.TopMemApp = top.Key; s.TopMemGb = top.Value / 1073741824.0; }
        }

        /// <summary>The graphics card with the most memory (skips Windows' basic display driver).</summary>
        static (string name, double gb) BiggestGpu()
        {
            string best = ""; double bestGb = 0;
            try
            {
                using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (cls == null) return ("", 0);
                foreach (var sub in cls.GetSubKeyNames().Where(n => Regex.IsMatch(n, @"^\d{4}$")))
                {
                    try
                    {
                        using var k = cls.OpenSubKey(sub);
                        var desc = k?.GetValue("DriverDesc") as string ?? "";
                        if (desc.Length == 0 || desc.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) continue;
                        double gb = k!.GetValue("HardwareInformation.qwMemorySize") switch
                        {
                            long l => l / 1073741824.0,
                            byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0) / 1073741824.0,
                            byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0) / 1073741824.0,
                            _ => k.GetValue("HardwareInformation.MemorySize") switch
                            {
                                int i => (uint)i / 1073741824.0,
                                byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0) / 1073741824.0,
                                _ => 0,
                            },
                        };
                        if (best.Length == 0 || gb > bestGb) { best = desc; bestGb = gb; }
                    }
                    catch { }
                }
            }
            catch { }
            return (best, bestGb);
        }

        public void Dispose() => pdh.Dispose();
    }

    /// <summary>A small wrapper over the Windows performance counter API (pdh.dll).</summary>
    sealed class Pdh : IDisposable
    {
        IntPtr query;

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out FmtValue value);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);

        const uint Double = 0x200, NoCap = 0x8000;

        [StructLayout(LayoutKind.Explicit)]
        struct FmtValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }

        [StructLayout(LayoutKind.Sequential)]
        struct Item { public IntPtr Name; public FmtValue Value; }

        public Pdh() { if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) query = IntPtr.Zero; }

        public IntPtr Add(string path) => query != IntPtr.Zero && PdhAddEnglishCounter(query, path, IntPtr.Zero, out var c) == 0 ? c : IntPtr.Zero;

        public void Collect() { if (query != IntPtr.Zero) PdhCollectQueryData(query); }

        public double? Value(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return null;
            return PdhGetFormattedCounterValue(counter, Double | NoCap, out _, out var v) == 0 && v.Status <= 1 ? v.Number : null;
        }

        public List<(string name, double value)>? Values(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return null;
            uint size = 0;
            int r = PdhGetFormattedCounterArray(counter, Double | NoCap, ref size, out uint n, IntPtr.Zero);
            if (r != unchecked((int)0x800007D2) || size == 0) return r == 0 ? new() : null; // PDH_MORE_DATA
            var buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(counter, Double | NoCap, ref size, out n, buf) != 0) return null;
                var list = new List<(string, double)>((int)n);
                int step = Marshal.SizeOf<Item>();
                for (int i = 0; i < n; i++)
                {
                    var it = Marshal.PtrToStructure<Item>(buf + i * step);
                    if (it.Value.Status <= 1) list.Add((Marshal.PtrToStringUni(it.Name) ?? "", it.Value.Number));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } }
    }
}
