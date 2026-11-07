using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MicroserviceManager.Core;

/// <summary>
/// Puts child processes in a Windows job object that is closed when the manager exits — even if it
/// crashes or is killed — so services never keep running hidden in the background.
/// </summary>
public static class ChildProcessGuard
{
    private static readonly Lazy<IntPtr> Job = new(CreateJob);

    public static void Track(Process process)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            IntPtr job = Job.Value;
            if (job != IntPtr.Zero) AssignProcessToJobObject(job, process.Handle);
        }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private static IntPtr CreateJob()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>Parent → children map of all processes, from one cheap system snapshot.</summary>
public static class ProcessTree
{
    public static Dictionary<int, List<int>> SnapshotChildren()
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, parent) in OperatingSystem.IsWindows() ? WindowsPairs() : LinuxPairs())
        {
            if (pid == parent) continue;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<int>();
            list.Add(pid);
        }
        return children;
    }

    public static List<int> WithDescendants(int root, Dictionary<int, List<int>> children)
    {
        var result = new List<int> { root };
        var seen = new HashSet<int> { root };
        for (int i = 0; i < result.Count; i++)
        {
            if (!children.TryGetValue(result[i], out var kids)) continue;
            foreach (int k in kids)
                if (seen.Add(k)) result.Add(k);
        }
        return result;
    }

    private static List<(int Pid, int Parent)> WindowsPairs()
    {
        var pairs = new List<(int, int)>(256);
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return pairs;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return pairs;
            do pairs.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID));
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }
        return pairs;
    }

    private static List<(int Pid, int Parent)> LinuxPairs()
    {
        var pairs = new List<(int, int)>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out int pid)) continue;
                try
                {
                    string stat = File.ReadAllText(Path.Combine(dir, "stat"));
                    int close = stat.LastIndexOf(')');
                    var fields = stat[(close + 2)..].Split(' ');
                    if (fields.Length > 1 && int.TryParse(fields[1], out int ppid)) pairs.Add((pid, ppid));
                }
                catch { }
            }
        }
        catch { }
        return pairs;
    }

    private const uint TH32CS_SNAPPROCESS = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}

/// <summary>
/// Samples CPU and memory for each service's process tree. Uses one snapshot per tick and cached
/// handles — no WMI, no performance counters — so sampling itself costs almost nothing.
/// </summary>
public sealed class MetricsSampler : IDisposable
{
    private readonly Dictionary<int, (Process Process, TimeSpan Cpu)> _known = new();
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly Process _self = Process.GetCurrentProcess();
    private TimeSpan _selfCpu;

    public double SelfCpuPercent { get; private set; }
    public long SelfMemoryBytes { get; private set; }

    public void Sample(IEnumerable<(ServiceInfo Service, int RootPid)> running)
    {
        DateTime now = DateTime.UtcNow;
        double wallMs = Math.Max(1, (now - _lastSample).TotalMilliseconds);
        _lastSample = now;
        int cores = Environment.ProcessorCount;

        var list = running.ToList();
        var children = list.Count > 0 ? ProcessTree.SnapshotChildren() : new Dictionary<int, List<int>>();
        var alive = new HashSet<int>();

        foreach (var (svc, root) in list)
        {
            double cpuMs = 0;
            long memory = 0;
            foreach (int pid in ProcessTree.WithDescendants(root, children))
            {
                if (!TryRead(pid, out var process, out var cpu, out long ws)) continue;
                alive.Add(pid);
                if (_known.TryGetValue(pid, out var prev)) cpuMs += Math.Max(0, (cpu - prev.Cpu).TotalMilliseconds);
                _known[pid] = (process, cpu);
                memory += ws;
            }
            svc.CpuPercent = Math.Clamp(cpuMs / wallMs / cores * 100.0, 0, 100);
            svc.MemoryBytes = memory;
            svc.CpuHistory.Add((float)svc.CpuPercent);
        }

        foreach (int pid in _known.Keys.Where(p => !alive.Contains(p)).ToList())
        {
            _known[pid].Process.Dispose();
            _known.Remove(pid);
        }

        try
        {
            _self.Refresh();
            TimeSpan selfCpu = _self.TotalProcessorTime;
            SelfCpuPercent = Math.Clamp((selfCpu - _selfCpu).TotalMilliseconds / wallMs / cores * 100.0, 0, 100);
            _selfCpu = selfCpu;
            SelfMemoryBytes = _self.WorkingSet64;
        }
        catch { }
    }

    private bool TryRead(int pid, out Process process, out TimeSpan cpu, out long workingSet)
    {
        cpu = default;
        workingSet = 0;
        process = null!;
        bool cached = _known.TryGetValue(pid, out var known);
        Process? candidate = null;
        try
        {
            candidate = cached ? known.Process : Process.GetProcessById(pid);
            candidate.Refresh();
            if (candidate.HasExited) throw new InvalidOperationException();
            cpu = candidate.TotalProcessorTime;
            workingSet = candidate.WorkingSet64;
            process = candidate;
            return true;
        }
        catch
        {
            if (!cached) candidate?.Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var (p, _) in _known.Values) p.Dispose();
        _known.Clear();
        _self.Dispose();
    }
}

public static class Ports
{
    public static List<int> InUse(IEnumerable<int> ports)
    {
        try
        {
            var listening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
            return ports.Where(listening.Contains).ToList();
        }
        catch
        {
            return new List<int>();
        }
    }
}
