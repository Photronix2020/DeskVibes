using System.Diagnostics;
using System.Net.NetworkInformation;

namespace DeskVibes;

/// <summary>What the PC looks like right now. The engine only reads this, so tests can fake it.</summary>
public interface IWorld
{
    DateTime Now { get; }
    double IdleSeconds { get; }
    int? Battery { get; }
    bool? Plugged { get; }
    double Cpu { get; }
    double Memory { get; }
    string App { get; }
    string Title { get; }
    bool Fullscreen { get; }
    IReadOnlyCollection<string> Processes { get; }
    bool Online { get; }
    IReadOnlyCollection<string> RemovableDrives { get; }
    double FreeGb(string drive);
    /// <summary>Seconds each app was in front today (only while you were at the PC).</summary>
    IReadOnlyDictionary<string, double> UsageToday { get; }
    /// <summary>Seconds you've been at the PC since your last 5-minute break.</summary>
    double SittingSeconds { get; }
    string BusiestApp { get; }
    void Refresh();
}

public sealed class RealWorld : IWorld
{
    public DateTime Now => DateTime.Now;
    public double IdleSeconds { get; private set; }
    public int? Battery { get; private set; }
    public bool? Plugged { get; private set; }
    public double Cpu { get; private set; }
    public double Memory { get; private set; }
    public string App { get; private set; } = "";
    public string Title { get; private set; } = "";
    public bool Fullscreen { get; private set; }
    public IReadOnlyCollection<string> Processes => procs;
    public bool Online { get; private set; } = true;
    public IReadOnlyCollection<string> RemovableDrives => drives;
    public IReadOnlyDictionary<string, double> UsageToday => usage;
    public double SittingSeconds { get; private set; }
    public string BusiestApp { get; private set; } = "";

    HashSet<string> procs = new(StringComparer.OrdinalIgnoreCase);
    List<string> drives = new();
    readonly Dictionary<string, double> usage = new(StringComparer.OrdinalIgnoreCase);
    DateTime usageDay = DateTime.Today, lastRefresh = DateTime.MinValue;
    int tick;
    readonly Dictionary<int, TimeSpan> cpuTimes = new();

    public void Refresh()
    {
        var now = DateTime.Now;
        double dt = lastRefresh == DateTime.MinValue ? 0 : Math.Clamp((now - lastRefresh).TotalSeconds, 0, 5);
        lastRefresh = now;
        tick++;

        IdleSeconds = Native.IdleSeconds();
        var ps = SystemInformation.PowerStatus;
        if (ps.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery) || ps.BatteryChargeStatus == BatteryChargeStatus.Unknown)
        { Battery = null; Plugged = null; }
        else
        {
            Battery = (int)Math.Round(ps.BatteryLifePercent * 100);
            Plugged = ps.PowerLineStatus == PowerLineStatus.Online;
        }
        Cpu = Native.CpuPercent();
        Memory = Native.Memory().percent;

        var h = Native.GetForegroundWindow();
        Title = Native.WindowText(h);
        App = "";
        try
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid != 0) { using var p = Process.GetProcessById((int)pid); App = p.ProcessName; }
        }
        catch { }
        Fullscreen = Native.IsFullscreen(h) && App is not ("explorer" or "DeskVibes");

        if (tick % 2 == 1) RefreshProcesses();
        if (tick % 3 == 1)
        {
            Online = NetworkInterface.GetIsNetworkAvailable();
            try
            {
                drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Select(d => d.Name.TrimEnd('\\')).ToList();
            }
            catch { }
        }

        if (usageDay != DateTime.Today) { usage.Clear(); usageDay = DateTime.Today; }
        if (IdleSeconds < 120 && App.Length > 0) usage[App] = usage.GetValueOrDefault(App) + dt;
        SittingSeconds = IdleSeconds >= 300 ? 0 : SittingSeconds + dt;
    }

    void RefreshProcesses()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double best = 0; string busiest = "";
        var seen = new HashSet<int>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                set.Add(p.ProcessName);
                if (Cpu < 60) continue;
                try
                {
                    var t = p.TotalProcessorTime;
                    seen.Add(p.Id);
                    if (cpuTimes.TryGetValue(p.Id, out var old) && (t - old).TotalMilliseconds > best) { best = (t - old).TotalMilliseconds; busiest = p.ProcessName; }
                    cpuTimes[p.Id] = t;
                }
                catch { }
            }
        }
        if (Cpu >= 60) { foreach (var id in cpuTimes.Keys.Where(k => !seen.Contains(k)).ToList()) cpuTimes.Remove(id); BusiestApp = busiest; }
        procs = set;
    }

    public double FreeGb(string drive)
    {
        try
        {
            var d = new DriveInfo(drive.Trim().TrimEnd(':', '\\') + ":\\");
            return d.IsReady ? d.AvailableFreeSpace / 1073741824.0 : -1;
        }
        catch { return -1; }
    }
}
