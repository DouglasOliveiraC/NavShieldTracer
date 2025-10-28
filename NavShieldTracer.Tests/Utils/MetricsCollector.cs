using System.Diagnostics;

namespace NavShieldTracer.Tests.Utils;

/// <summary>
/// Coleta métricas de performance do sistema (CPU, RAM, I/O) durante a execução dos testes
/// </summary>
public class MetricsCollector : IDisposable
{
    private readonly Process _process;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly List<MetricSnapshot> _snapshots = new();
    private readonly Timer? _autoCollectTimer;
    private bool _disposed;

    public IReadOnlyList<MetricSnapshot> Snapshots => _snapshots.AsReadOnly();

    public MetricsCollector(int processId, bool autoCollect = false, int intervalMs = 1000)
    {
        try
        {
            _process = Process.GetProcessById(processId);

            // PerformanceCounter pode falhar em alguns ambientes
            try
            {
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _process.ProcessName, true);
                _cpuCounter.NextValue(); // Primeira leitura sempre retorna 0
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AVISO] Não foi possível inicializar PerformanceCounter: {ex.Message}");
            }

            if (autoCollect)
            {
                _autoCollectTimer = new Timer(_ => TakeSnapshot(), null, intervalMs, intervalMs);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao inicializar MetricsCollector para PID {processId}", ex);
        }
    }

    /// <summary>
    /// Coleta snapshot instantâneo das métricas do processo
    /// </summary>
    public MetricSnapshot TakeSnapshot()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MetricsCollector));

        try
        {
            _process.Refresh();

            var snapshot = new MetricSnapshot
            {
                Timestamp = DateTime.UtcNow,
                WorkingSetMB = _process.WorkingSet64 / 1024.0 / 1024.0,
                PrivateBytesMB = _process.PrivateMemorySize64 / 1024.0 / 1024.0,
                VirtualMemoryMB = _process.VirtualMemorySize64 / 1024.0 / 1024.0,
                CpuPercent = _cpuCounter != null ? _cpuCounter.NextValue() / Environment.ProcessorCount : 0,
                ThreadCount = _process.Threads.Count,
                HandleCount = _process.HandleCount,
                GcGen0 = GC.CollectionCount(0),
                GcGen1 = GC.CollectionCount(1),
                GcGen2 = GC.CollectionCount(2),
                TotalGcMemoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0
            };

            _snapshots.Add(snapshot);
            return snapshot;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Erro ao coletar snapshot de métricas", ex);
        }
    }

    /// <summary>
    /// Gera relatório agregado de todas as métricas coletadas
    /// </summary>
    public PerformanceReport GenerateReport()
    {
        if (_snapshots.Count == 0)
            throw new InvalidOperationException("Nenhuma métrica foi coletada ainda. Chame TakeSnapshot() primeiro.");

        var report = new PerformanceReport
        {
            TotalSnapshots = _snapshots.Count,
            Duration = _snapshots.Count > 1 ? _snapshots.Last().Timestamp - _snapshots.First().Timestamp : TimeSpan.Zero,

            // Memória
            AvgMemoryMB = _snapshots.Average(s => s.WorkingSetMB),
            PeakMemoryMB = _snapshots.Max(s => s.WorkingSetMB),
            MinMemoryMB = _snapshots.Min(s => s.WorkingSetMB),

            AvgPrivateBytesMB = _snapshots.Average(s => s.PrivateBytesMB),
            PeakPrivateBytesMB = _snapshots.Max(s => s.PrivateBytesMB),

            AvgVirtualMemoryMB = _snapshots.Average(s => s.VirtualMemoryMB),

            // CPU
            AvgCpuPercent = _snapshots.Average(s => s.CpuPercent),
            PeakCpuPercent = _snapshots.Max(s => s.CpuPercent),

            // Threads e Handles
            AvgThreadCount = _snapshots.Average(s => s.ThreadCount),
            PeakThreadCount = _snapshots.Max(s => s.ThreadCount),

            AvgHandleCount = _snapshots.Average(s => s.HandleCount),
            PeakHandleCount = _snapshots.Max(s => s.HandleCount),

            // Garbage Collection
            TotalGcGen0 = _snapshots.Last().GcGen0 - (_snapshots.First().GcGen0),
            TotalGcGen1 = _snapshots.Last().GcGen1 - (_snapshots.First().GcGen1),
            TotalGcGen2 = _snapshots.Last().GcGen2 - (_snapshots.First().GcGen2),

            AvgGcMemoryMB = _snapshots.Average(s => s.TotalGcMemoryMB),
            PeakGcMemoryMB = _snapshots.Max(s => s.TotalGcMemoryMB),

            // Memory Leak Detection
            MemoryGrowthMB = _snapshots.Count > 1 ? _snapshots.Last().WorkingSetMB - _snapshots.First().WorkingSetMB : 0,
            MemoryGrowthPercent = _snapshots.Count > 1 && _snapshots.First().WorkingSetMB > 0
                ? ((_snapshots.Last().WorkingSetMB - _snapshots.First().WorkingSetMB) / _snapshots.First().WorkingSetMB) * 100
                : 0
        };

        return report;
    }

    /// <summary>
    /// Para coleta automática e libera recursos
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _autoCollectTimer?.Dispose();
        _cpuCounter?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Snapshot instantâneo das métricas do sistema
/// </summary>
public class MetricSnapshot
{
    public DateTime Timestamp { get; init; }
    public double WorkingSetMB { get; init; }
    public double PrivateBytesMB { get; init; }
    public double VirtualMemoryMB { get; init; }
    public double CpuPercent { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public int GcGen0 { get; init; }
    public int GcGen1 { get; init; }
    public int GcGen2 { get; init; }
    public double TotalGcMemoryMB { get; init; }
}

/// <summary>
/// Relatório agregado de performance
/// </summary>
public class PerformanceReport
{
    public int TotalSnapshots { get; init; }
    public TimeSpan Duration { get; init; }

    // Memória
    public double AvgMemoryMB { get; init; }
    public double PeakMemoryMB { get; init; }
    public double MinMemoryMB { get; init; }
    public double AvgPrivateBytesMB { get; init; }
    public double PeakPrivateBytesMB { get; init; }
    public double AvgVirtualMemoryMB { get; init; }

    // CPU
    public double AvgCpuPercent { get; init; }
    public double PeakCpuPercent { get; init; }

    // Threads/Handles
    public double AvgThreadCount { get; init; }
    public int PeakThreadCount { get; init; }
    public double AvgHandleCount { get; init; }
    public int PeakHandleCount { get; init; }

    // GC
    public int TotalGcGen0 { get; init; }
    public int TotalGcGen1 { get; init; }
    public int TotalGcGen2 { get; init; }
    public double AvgGcMemoryMB { get; init; }
    public double PeakGcMemoryMB { get; init; }

    // Memory Leak Detection
    public double MemoryGrowthMB { get; init; }
    public double MemoryGrowthPercent { get; init; }

    public override string ToString()
    {
        return $@"
=== Performance Report ===
Duração: {Duration.TotalSeconds:F2}s ({TotalSnapshots} snapshots)

Memória:
  - Working Set: Avg={AvgMemoryMB:F2} MB, Peak={PeakMemoryMB:F2} MB, Min={MinMemoryMB:F2} MB
  - Private Bytes: Avg={AvgPrivateBytesMB:F2} MB, Peak={PeakPrivateBytesMB:F2} MB
  - Virtual Memory: Avg={AvgVirtualMemoryMB:F2} MB
  - GC Memory: Avg={AvgGcMemoryMB:F2} MB, Peak={PeakGcMemoryMB:F2} MB
  - Crescimento: {MemoryGrowthMB:F2} MB ({MemoryGrowthPercent:F2}%)

CPU:
  - Avg={AvgCpuPercent:F2}%, Peak={PeakCpuPercent:F2}%

Threads/Handles:
  - Threads: Avg={AvgThreadCount:F1}, Peak={PeakThreadCount}
  - Handles: Avg={AvgHandleCount:F1}, Peak={PeakHandleCount}

Garbage Collection:
  - Gen 0: {TotalGcGen0} coletas
  - Gen 1: {TotalGcGen1} coletas
  - Gen 2: {TotalGcGen2} coletas
";
    }
}
