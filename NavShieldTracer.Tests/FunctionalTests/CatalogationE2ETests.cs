using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using NavShieldTracer.Tests.Utils;
using Xunit;

namespace NavShieldTracer.Tests.FunctionalTests;

/// <summary>
/// Exercita o fluxo completo de catalogacao e operacoes principais em cima do SqliteEventStore.
/// </summary>
public sealed class CatalogationE2ETests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteEventStore _store;
    private readonly DatabaseSeeder _seeder;

    public CatalogationE2ETests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_e2e_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_testDbPath);
        _seeder = new DatabaseSeeder(_store, seedBase: 421);
    }

    [Fact]
    public void CompleteCatalogationWorkflow()
    {
        var testeId = _seeder.CriarTesteAtomico("T1055", "Process Injection", "Teste E2E completo", 100);
        var teste = _store.ListarTestesAtomicos().FirstOrDefault(t => t.Id == testeId);

        Assert.NotNull(teste);
        Assert.Equal("T1055", teste!.Numero);
        Assert.Equal(100, teste.TotalEventos);

        ReportFormatter.WriteSection(
            "E2E Catalogacao Completa",
            ("Teste ID", testeId.ToString()),
            ("Numero", teste.Numero),
            ("Nome", teste.Nome),
            ("Eventos persistidos", teste.TotalEventos.ToString("N0")));
    }

    [Fact]
    public void MultipleTestsCatalogation()
    {
        var ttps = new Dictionary<string, (string nome, int eventos)>
        {
            { "T1055", ("Process Injection", 50) },
            { "T1059.001", ("PowerShell Execution", 75) },
            { "T1071.001", ("Web Protocols C2", 30) },
            { "T1105", ("Ingress Tool Transfer", 40) },
            { "T1027", ("Obfuscated Files", 60) }
        };

        _seeder.SimularCatalogacaoMitre(ttps);
        var testesListados = _store.ListarTestesAtomicos();

        foreach (var (numero, (_, eventosEsperados)) in ttps)
        {
            var teste = testesListados.FirstOrDefault(t => t.Numero == numero);
            Assert.NotNull(teste);
            Assert.Equal(eventosEsperados, teste!.TotalEventos);
        }

        ReportFormatter.WriteSection(
            "E2E Multiplos TTPs",
            ("Total de testes", testesListados.Count.ToString("N0")),
            ("Total de eventos", testesListados.Sum(t => t.TotalEventos).ToString("N0")));
    }

    [Fact]
    public void EventCounting()
    {
        var sessionId = _seeder.CriarSessaoComEventos("counting_test.exe", 1000, 6000);
        var eventCount = _store.ContarEventosSessao(sessionId);

        ReportFormatter.WriteSection(
            "E2E Contagem de Eventos",
            ("Sessao", sessionId.ToString()),
            ("Eventos esperados", "1000"),
            ("Eventos contados", eventCount.ToString("N0")));

        Assert.Equal(1000, eventCount);
    }

    [Fact]
    public void ExportFunctionality()
    {
        var testeId = _seeder.CriarTesteAtomico("T1082", "System Info Discovery", "Teste de export", 50);
        var eventos = _store.ExportarEventosTeste(testeId);

        ReportFormatter.WriteSection(
            "E2E Exportacao de Eventos",
            ("Teste ID", testeId.ToString()),
            ("Eventos exportados", eventos.Count.ToString("N0")));

        Assert.Equal(50, eventos.Count);
    }

    [Fact]
    public void ListTestsPerformanceBaseline()
    {
        for (var i = 0; i < 10; i++)
        {
            _seeder.CriarTesteAtomico($"T{1000 + i}", $"Test {i}", $"Descricao {i}", 20);
        }

        var testes = _store.ListarTestesAtomicos();

        ReportFormatter.WriteSection(
            "E2E Lista de Testes",
            ("Testes listados", testes.Count.ToString("N0")),
            ("Eventos totais", testes.Sum(t => t.TotalEventos).ToString("N0")));

        Assert.True(testes.Count >= 10, "Nem todos os testes foram listados.");
    }

    [Fact]
    public void MultipleSessions()
    {
        var sessionIds = _seeder.CriarMultiplasSessoes(5, 100);
        Assert.Equal(5, sessionIds.Count);

        var totalEventos = 0;
        foreach (var sessionId in sessionIds)
        {
            var count = _store.ContarEventosSessao(sessionId);
            Assert.Equal(100, count);
            totalEventos += count;
        }

        ReportFormatter.WriteSection(
            "E2E Multiplas Sessoes",
            ("Sessoes criadas", sessionIds.Count.ToString()),
            ("Total de eventos", totalEventos.ToString("N0")));

        Assert.Equal(500, totalEventos);
    }

    [Fact]
    public void CriticalEventAnalysis()
    {
        var sessionId = _seeder.CriarSessaoComEventos("critical_test.exe", 500, 7000);
        var criticalCounts = _store.GetCriticalEventCounts(sessionId);

        ReportFormatter.WriteSection(
            "E2E Analise Critica",
            ("Sessao", sessionId.ToString()),
            ("Tipos encontrados", criticalCounts.Count.ToString()),
            ("Eventos totais", criticalCounts.Sum(kv => kv.Value).ToString("N0")));

        Assert.True(criticalCounts.Count > 0, "Nenhum evento critico encontrado.");
    }

    [Fact]
    public void SessionLifecycle()
    {
        var startTime = DateTime.UtcNow;
        var sessionId = _seeder.CriarSessaoComEventos("lifecycle_test.exe", 50, 8000);
        var endTime = DateTime.UtcNow;

        var eventCount = _store.ContarEventosSessao(sessionId);

        ReportFormatter.WriteSection(
            "E2E Ciclo de Vida",
            ("Sessao", sessionId.ToString()),
            ("Eventos", eventCount.ToString("N0")),
            ("Duracao (s)", (endTime - startTime).TotalSeconds.ToString("F2")));

        Assert.Equal(50, eventCount);
    }

    [Fact]
    public void DataConsistency()
    {
        var testeId = _seeder.CriarTesteAtomico("T9999", "Consistency Test", "Teste de consistencia", 100);
        var testes = _store.ListarTestesAtomicos();
        var teste = testes.FirstOrDefault(t => t.Id == testeId);
        Assert.NotNull(teste);

        var eventos = _store.ExportarEventosTeste(testeId);
        var eventCount = _store.ContarEventosSessao(teste!.SessionId);

        ReportFormatter.WriteSection(
            "E2E Consistencia",
            ("Eventos declarados", teste.TotalEventos.ToString("N0")),
            ("Eventos exportados", eventos.Count.ToString("N0")),
            ("Eventos na sessao", eventCount.ToString("N0")));

        Assert.Equal(teste.TotalEventos, eventos.Count);
        Assert.Equal(teste.TotalEventos, eventCount);
    }

    [Fact]
    public void LargeDatasetConsistency()
    {
        var result = _seeder.PopularBancoGrande(20, 100, 200);
        var totalContado = 0;

        foreach (var sessionId in result.SessionIds)
        {
            totalContado += _store.ContarEventosSessao(sessionId);
        }

        ReportFormatter.WriteSection(
            "E2E Dataset Grande",
            ("Eventos inseridos", result.TotalEventosInseridos.ToString("N0")),
            ("Eventos contados", totalContado.ToString("N0")),
            ("Duracao (s)", result.Duracao.TotalSeconds.ToString("F2")));

        Assert.Equal(result.TotalEventosInseridos, totalContado);
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
                // ignore temp cleanup failures
            }
        }
    }
}
