using System.Collections.Concurrent;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using NavShieldTracer.Tests.Utils;
using System.Diagnostics;
using Xunit;

namespace NavShieldTracer.Tests.DatabaseTests;

/// <summary>
/// Cenários de inserção intensiva para estimar a escalabilidade do armazenamento.
/// Executados apenas quando RUN_PERFORMANCE_TESTS=1.
/// </summary>
public sealed class InsertionPerformanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteEventStore _store;
    private readonly EventSimulator _simulator;

    public InsertionPerformanceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_insertion_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_testDbPath);
        _simulator = new EventSimulator(101);
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void InsertionSpeed_1k_Events()
    {
        var result = RunInsertionScenario(1000, sessionRootPid: 1000);

        ReportFormatter.WriteSection(
            "Insercao 1k Eventos",
            ("Eventos inseridos", result.EventsPersisted.ToString("N0")),
            ("Tempo total", $"{result.Elapsed.TotalSeconds:F2}s"),
            ("Taxa", $"{result.EventsPerSecond:F2} eventos/s"),
            ("Tempo medio", $"{result.ElapsedPerEventMs:F3} ms por evento"),
            ("Tamanho arquivo", $"{result.DatabaseSizeMb:F2} MB"));

        Assert.Equal(result.ExpectedEvents, result.EventsPersisted);
        Assert.True(result.EventsPerSecond > 800, $"Taxa de insercao {result.EventsPerSecond:F2} eventos/s abaixo do esperado (> 800).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void InsertionSpeed_10k_Events()
    {
        var result = RunInsertionScenario(10_000, sessionRootPid: 2000);

        ReportFormatter.WriteSection(
            "Insercao 10k Eventos",
            ("Eventos inseridos", result.EventsPersisted.ToString("N0")),
            ("Tempo total", $"{result.Elapsed.TotalSeconds:F2}s"),
            ("Taxa", $"{result.EventsPerSecond:F2} eventos/s"),
            ("Tempo medio", $"{result.ElapsedPerEventMs:F3} ms por evento"),
            ("Tamanho arquivo", $"{result.DatabaseSizeMb:F2} MB"));

        Assert.Equal(result.ExpectedEvents, result.EventsPersisted);
        Assert.True(result.EventsPerSecond > 1200, $"Taxa de insercao {result.EventsPerSecond:F2} eventos/s abaixo do esperado (> 1200).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void InsertionSpeed_100k_Events()
    {
        var result = RunInsertionScenario(100_000, sessionRootPid: 3000);

        ReportFormatter.WriteSection(
            "Insercao 100k Eventos",
            ("Eventos inseridos", result.EventsPersisted.ToString("N0")),
            ("Tempo total", $"{result.Elapsed.TotalSeconds:F2}s"),
            ("Taxa", $"{result.EventsPerSecond:F2} eventos/s"),
            ("Tempo medio", $"{result.ElapsedPerEventMs:F3} ms por evento"),
            ("Tamanho arquivo", $"{result.DatabaseSizeMb:F2} MB"),
            ("Bytes por evento", $"{result.BytesPerEvent:F0}"));

        Assert.Equal(result.ExpectedEvents, result.EventsPersisted);
        Assert.True(result.EventsPerSecond > 1500, $"Taxa de insercao {result.EventsPerSecond:F2} eventos/s abaixo do esperado (> 1500).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void DatabaseGrowth_LinearScale()
    {
        var measurements = new List<(int Events, long SizeBytes)>();
        var seeder = new DatabaseSeeder(_store, seedBase: 204);

        for (var i = 1; i <= 5; i++)
        {
            var quantity = i * 1000;
            seeder.CriarSessaoComEventos($"teste_{i}.exe", quantity, 4000 + i);
            measurements.Add((quantity, new FileInfo(_testDbPath).Length));
        }

        var growthRates = new List<double>();
        for (var i = 1; i < measurements.Count; i++)
        {
            var deltaEvents = measurements[i].Events - measurements[i - 1].Events;
            var deltaSize = measurements[i].SizeBytes - measurements[i - 1].SizeBytes;
            growthRates.Add(deltaSize / (double)deltaEvents);
        }

        var averageRate = growthRates.Average();
        var stddev = Math.Sqrt(growthRates.Select(rate => Math.Pow(rate - averageRate, 2)).Average());
        var coeficient = (stddev / averageRate) * 100;

        ReportFormatter.WriteSection(
            "Crescimento Linear",
            ("Taxa media", $"{averageRate:F2} bytes/evento"),
            ("Desvio padrao", $"{stddev:F2}"),
            ("Coeficiente variacao", $"{coeficient:F2}%"));

        Assert.True(coeficient < 10, $"Crescimento nao linear detectado (CV={coeficient:F2}% > 10%).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void ConcurrentInsertions()
    {
        var sessionCount = 5;
        var eventsPerSession = 1000;
        var totalExpected = sessionCount * eventsPerSession;

        var sessionIds = new ConcurrentBag<int>();
        var tasks = new List<Task>();
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < sessionCount; i++)
        {
            var sessionSeed = 500 + i;
            tasks.Add(Task.Run(() =>
            {
                using var threadStore = new SqliteEventStore(_testDbPath);
                var threadSeeder = new DatabaseSeeder(threadStore, seedBase: sessionSeed);
                var sessionId = threadSeeder.CriarSessaoComEventos($"processo_{sessionSeed}.exe", eventsPerSession, 5000 + sessionSeed);
                sessionIds.Add(sessionId);
            }));
        }

        Task.WaitAll(tasks.ToArray());
        stopwatch.Stop();

        using var validationStore = new SqliteEventStore(_testDbPath);
        var totalPersisted = sessionIds.Sum(validationStore.ContarEventosSessao);
        var throughput = totalExpected / stopwatch.Elapsed.TotalSeconds;

        ReportFormatter.WriteSection(
            "Insercoes Concorrentes",
            ("Sessoes", sessionCount.ToString()),
            ("Eventos por sessao", eventsPerSession.ToString("N0")),
            ("Total esperado", totalExpected.ToString("N0")),
            ("Tempo total", $"{stopwatch.Elapsed.TotalSeconds:F2}s"),
            ("Throughput", $"{throughput:F2} eventos/s"));

        Assert.True(throughput > 900, $"Throughput concorrente {throughput:F2} abaixo do esperado (> 900).");
        Assert.Equal(totalExpected, totalPersisted);
    }

    private PerformanceResult RunInsertionScenario(int eventCount, int sessionRootPid)
    {
        var sessionInfo = new SessionInfo(
            StartedAt: DateTime.UtcNow,
            TargetProcess: "teste.exe",
            RootPid: sessionRootPid,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.VersionString
        );

        var sessionId = _store.BeginSession(sessionInfo);
        var eventos = _simulator.GerarEventosMistos(eventCount, sessionRootPid);

        var sw = Stopwatch.StartNew();
        foreach (var evento in eventos)
        {
            _store.InsertEvent(sessionId, evento);
        }
        sw.Stop();

        _store.CompleteSession(sessionId);

        var persisted = _store.ContarEventosSessao(sessionId);
        var fileInfo = new FileInfo(_testDbPath);

        return new PerformanceResult(
            eventCount,
            persisted,
            sw.Elapsed,
            persisted / sw.Elapsed.TotalSeconds,
            (sw.Elapsed.TotalMilliseconds / Math.Max(persisted, 1)),
            fileInfo.Length / (1024.0 * 1024.0),
            fileInfo.Length / (double)Math.Max(persisted, 1)
        );
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
                // ignore clean up issues in local temp folder
            }
        }
    }

    private readonly record struct PerformanceResult(
        int ExpectedEvents,
        int EventsPersisted,
        TimeSpan Elapsed,
        double EventsPerSecond,
        double ElapsedPerEventMs,
        double DatabaseSizeMb,
        double BytesPerEvent);
}
