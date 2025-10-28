using NavShieldTracer.Storage;
using NavShieldTracer.Tests.Utils;
using System.Diagnostics;
using Xunit;

namespace NavShieldTracer.Tests.DatabaseTests;

/// <summary>
/// Avalia consultas da API publica do armazenamento com um dataset deterministico.
/// Executado somente com RUN_PERFORMANCE_TESTS=1.
/// </summary>
public sealed class QueryPerformanceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteEventStore _store;
    private readonly DatabaseSeeder _seeder;
    private readonly List<int> _sessionIds = new();

    public QueryPerformanceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_query_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_testDbPath);
        _seeder = new DatabaseSeeder(_store, seedBase: 311);
        _sessionIds.AddRange(_seeder.CriarMultiplasSessoes(10, 1000));
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void ListAllAtomicTests_Performance()
    {
        var ttps = new Dictionary<string, (string nome, int eventos)>
        {
            { "T1055", ("Process Injection", 50) },
            { "T1059.001", ("PowerShell", 75) },
            { "T1071.001", ("Web Protocols", 30) }
        };

        _seeder.SimularCatalogacaoMitre(ttps);

        var sw = Stopwatch.StartNew();
        var testes = _store.ListarTestesAtomicos();
        sw.Stop();

        ReportFormatter.WriteSection(
            "Listagem de Testes Atomicos",
            ("Testes encontrados", testes.Count.ToString()),
            ("Tempo", $"{sw.Elapsed.TotalMilliseconds:F2} ms"));

        Assert.True(sw.ElapsedMilliseconds < 60, $"Query demorou {sw.ElapsedMilliseconds} ms (> 60 ms).");
        Assert.True(testes.Count >= ttps.Count, "Nem todos os testes foram encontrados.");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void CountEvents_PerSession()
    {
        var timings = new List<long>();

        foreach (var sessionId in _sessionIds)
        {
            var sw = Stopwatch.StartNew();
            var count = _store.ContarEventosSessao(sessionId);
            sw.Stop();

            timings.Add(sw.ElapsedMilliseconds);
            Assert.True(count > 0, "Sessao sem eventos retornou contagem zero.");
        }

        var avgTime = timings.Average();
        var maxTime = timings.Max();

        ReportFormatter.WriteSection(
            "Contagem por Sessao",
            ("Sessoes avaliadas", _sessionIds.Count.ToString()),
            ("Tempo medio", $"{avgTime:F2} ms"),
            ("Tempo maximo", $"{maxTime} ms"));

        Assert.True(avgTime < 25, $"Tempo medio {avgTime:F2} ms acima do esperado (< 25 ms).");
        Assert.True(maxTime < 50, $"Tempo maximo {maxTime} ms acima do esperado (< 50 ms).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void ExportTestEvents_Performance()
    {
        var testeId = _seeder.CriarTesteAtomico("T1105", "Ingress Tool Transfer", "Teste de export", 500);

        var sw = Stopwatch.StartNew();
        var eventos = _store.ExportarEventosTeste(testeId);
        sw.Stop();

        ReportFormatter.WriteSection(
            "Exportacao de Eventos",
            ("Eventos exportados", eventos.Count.ToString("N0")),
            ("Tempo", $"{sw.Elapsed.TotalMilliseconds:F2} ms"),
            ("Taxa", $"{eventos.Count / sw.Elapsed.TotalSeconds:F2} eventos/s"));

        Assert.True(sw.ElapsedMilliseconds < 120, $"Export demorou {sw.ElapsedMilliseconds} ms (> 120 ms).");
        Assert.True(eventos.Count == 500, "Quantidade de eventos exportados difere do esperado.");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void CriticalEventCounts_Performance()
    {
        var sessionId = _sessionIds.First();

        var sw = Stopwatch.StartNew();
        var criticalCounts = _store.GetCriticalEventCounts(sessionId);
        sw.Stop();

        ReportFormatter.WriteSection(
            "Contagem de Eventos Criticos",
            ("Tipos distintos", criticalCounts.Count.ToString()),
            ("Tempo", $"{sw.Elapsed.TotalMilliseconds:F2} ms"));

        Assert.True(sw.ElapsedMilliseconds < 60, $"Query demorou {sw.ElapsedMilliseconds} ms (> 60 ms).");
        Assert.True(criticalCounts.Count > 0, "Nenhum evento critico retornado.");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void MultipleQueries_Throughput()
    {
        const int totalQueries = 100;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < totalQueries; i++)
        {
            var sessionId = _sessionIds[i % _sessionIds.Count];
            _ = _store.ContarEventosSessao(sessionId);
        }

        sw.Stop();

        var throughput = totalQueries / sw.Elapsed.TotalSeconds;

        ReportFormatter.WriteSection(
            "Throughput de Queries",
            ("Total de queries", totalQueries.ToString("N0")),
            ("Tempo total", $"{sw.Elapsed.TotalSeconds:F2} s"),
            ("Throughput", $"{throughput:F2} queries/s"));

        Assert.True(throughput > 180, $"Throughput {throughput:F2} abaixo do esperado (> 180).");
    }

    [PerformanceFact]
    [Trait("Category", "Performance")]
    public void ListTests_WithManyEntries()
    {
        for (var i = 0; i < 50; i++)
        {
            _seeder.CriarTesteAtomico($"T{1000 + i}", $"Test {i}", $"Descricao {i}", 10);
        }

        var sw = Stopwatch.StartNew();
        var testes = _store.ListarTestesAtomicos();
        sw.Stop();

        ReportFormatter.WriteSection(
            "Listagem com Muitos Testes",
            ("Testes encontrados", testes.Count.ToString("N0")),
            ("Tempo", $"{sw.Elapsed.TotalMilliseconds:F2} ms"),
            ("Tempo por teste", $"{sw.Elapsed.TotalMilliseconds / Math.Max(testes.Count, 1):F2} ms"));

        Assert.True(sw.ElapsedMilliseconds < 220, $"Query demorou {sw.ElapsedMilliseconds} ms (> 220 ms).");
        Assert.True(testes.Count >= 50, "Nem todos os testes foram listados.");
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
                // ignore cleanup on CI
            }
        }
    }
}
