using System.Collections.Concurrent;
using NavShieldTracer.Storage;
using NavShieldTracer.Tests.Utils;
using System.Diagnostics;
using Xunit;

namespace NavShieldTracer.Tests.StressTests;

/// <summary>
/// Exercita cenarios de carga para demonstrar escalabilidade do armazenamento.
/// Esses testes so executam quando RUN_PERFORMANCE_TESTS=1.
/// </summary>
public sealed class EventFloodTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteEventStore _store;
    private readonly DatabaseSeeder _seeder;

    public EventFloodTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_stress_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_testDbPath);
        _seeder = new DatabaseSeeder(_store, seedBase: 557);
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void HighVolumeInsertion_10k_EventsPerMinute()
    {
        var totalEventos = 10_000;

        var sw = Stopwatch.StartNew();
        var sessionId = _seeder.CriarSessaoComEventos("stress_test.exe", totalEventos, 9000, "Teste de estresse 10k eventos/min");
        sw.Stop();

        var insertRate = totalEventos / sw.Elapsed.TotalSeconds;
        var fileInfo = new FileInfo(_testDbPath);

        ReportFormatter.WriteSection(
            "Estresse 10k Eventos/Min",
            ("Sessao", sessionId.ToString()),
            ("Eventos inseridos", totalEventos.ToString("N0")),
            ("Tempo real", $"{sw.Elapsed.TotalSeconds:F2}s"),
            ("Taxa", $"{insertRate:F2} eventos/s"),
            ("Tamanho arquivo", $"{fileInfo.Length / (1024.0 * 1024.0):F2} MB"));

        Assert.True(insertRate > 160, $"Taxa {insertRate:F2} eventos/s abaixo do minimo (160).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void BurstLoad_1000_EventsInBurst()
    {
        const int batchSize = 1000;
        const int numberOfBursts = 10;
        var burstTimes = new List<TimeSpan>();

        for (var burst = 0; burst < numberOfBursts; burst++)
        {
            var sw = Stopwatch.StartNew();
            _seeder.CriarSessaoComEventos($"burst_{burst}.exe", batchSize, 10_000 + burst);
            sw.Stop();
            burstTimes.Add(sw.Elapsed);
        }

        var avgBurstTime = burstTimes.Average(t => t.TotalMilliseconds);
        var maxBurstTime = burstTimes.Max(t => t.TotalMilliseconds);
        var minBurstTime = burstTimes.Min(t => t.TotalMilliseconds);
        var variance = maxBurstTime - minBurstTime;

        ReportFormatter.WriteSection(
            "Estresse Rajadas 1000 Eventos",
            ("Rajadas executadas", numberOfBursts.ToString()),
            ("Media", $"{avgBurstTime:F2} ms"),
            ("Maximo", $"{maxBurstTime:F2} ms"),
            ("Minimo", $"{minBurstTime:F2} ms"),
            ("Variacao", $"{variance:F2} ms"));

        Assert.True(variance < avgBurstTime * 0.5, $"Variacao excessiva entre rajadas ({variance:F2} ms).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void MemoryUsage_UnderLoad()
    {
        var currentProcess = Process.GetCurrentProcess();
        var collector = new MetricsCollector(currentProcess.Id, autoCollect: true, intervalMs: 500);

        var totalEventos = 50_000;
        var sw = Stopwatch.StartNew();

        _seeder.CriarSessaoComEventos("memory_test.exe", totalEventos, 12_000, "Teste de memoria");

        sw.Stop();
        Thread.Sleep(1000);
        collector.Dispose();

        var report = collector.GenerateReport();

        ReportFormatter.WriteSection(
            "Estresse Memoria",
            ("Eventos inseridos", totalEventos.ToString("N0")),
            ("Tempo", $"{sw.Elapsed.TotalSeconds:F2}s"),
            ("Crescimento memoria", $"{report.MemoryGrowthPercent:F2}%"),
            ("Pico memoria", $"{report.PeakMemoryMB:F2} MB"),
            ("Snapshots", report.TotalSnapshots.ToString()));

        Assert.True(report.MemoryGrowthPercent < 200, $"Crescimento de memoria excessivo ({report.MemoryGrowthPercent:F2}%).");
        Assert.True(report.PeakMemoryMB < 1000, $"Pico de memoria muito alto ({report.PeakMemoryMB:F2} MB).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void DatabaseIntegrity_AfterStress()
    {
        var result = _seeder.PopularBancoGrande(50, 500, 1500);

        var totalEventos = 0;
        foreach (var sessionId in result.SessionIds)
        {
            totalEventos += _store.ContarEventosSessao(sessionId);
        }

        ReportFormatter.WriteSection(
            "Integridade Pos Estresse",
            ("Sessoes", result.SessionIds.Count.ToString()),
            ("Eventos contados", totalEventos.ToString("N0")),
            ("Eventos esperados", result.TotalEventosInseridos.ToString("N0")),
            ("Duracao (s)", result.Duracao.TotalSeconds.ToString("F2")));

        Assert.Equal(result.TotalEventosInseridos, totalEventos);
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void ConcurrentStress()
    {
        var tasks = new List<Task>();
        var sessionIds = new ConcurrentBag<int>();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < 10; i++)
        {
            var taskSeed = 700 + i;
            tasks.Add(Task.Run(() =>
            {
                using var threadStore = new SqliteEventStore(_testDbPath);
                var threadSeeder = new DatabaseSeeder(threadStore, seedBase: taskSeed);
                var sessionId = threadSeeder.CriarSessaoComEventos($"concurrent_{taskSeed}.exe", 2000, 15_000 + taskSeed);
                sessionIds.Add(sessionId);
            }));
        }

        Task.WaitAll(tasks.ToArray());
        sw.Stop();

        using var validationStore = new SqliteEventStore(_testDbPath);
        var totalEventos = sessionIds.Sum(validationStore.ContarEventosSessao);
        var throughput = totalEventos / sw.Elapsed.TotalSeconds;

        ReportFormatter.WriteSection(
            "Estresse Concorrente",
            ("Threads", "10"),
            ("Eventos por thread", "2 000"),
            ("Total", totalEventos.ToString("N0")),
            ("Tempo", $"{sw.Elapsed.TotalSeconds:F2}s"),
            ("Throughput", $"{throughput:F2} eventos/s"));

        Assert.True(throughput > 1200, $"Throughput {throughput:F2} abaixo do esperado (> 1200).");
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // ignore cleanup
            }
        }
    }
}
