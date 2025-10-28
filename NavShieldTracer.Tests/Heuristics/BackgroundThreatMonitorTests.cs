using System.Reflection;
using NavShieldTracer.Modules.Heuristics.Engine;
using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using Xunit;

namespace NavShieldTracer.Tests.Heuristics;

public sealed class BackgroundThreatMonitorTests : IAsyncLifetime
{
    private readonly string _dbPath;
    private readonly SqliteEventStore _store;
    private int _sessionId;

    public BackgroundThreatMonitorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"monitor_tests_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_dbPath);
    }

    public async Task InitializeAsync()
    {
        var sessionInfo = new SessionInfo(
            StartedAt: DateTime.UtcNow,
            TargetProcess: "target.exe",
            RootPid: 3210,
            Host: "test-host",
            User: "test-user",
            OsVersion: "Windows");

        _sessionId = _store.BeginSession(sessionInfo);

        var processo = new EventoProcessoCriado
        {
            EventId = 1,
            EventRecordId = 3001,
            UtcTime = DateTime.UtcNow.AddSeconds(-30),
            ProcessId = 3210,
            ParentProcessId = 0,
            Imagem = @"C:\Tools\target.exe",
            LinhaDeComando = "target.exe --capture",
            Usuario = "TEST\\Operator",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        var arquivo = new EventoArquivoCriado
        {
            EventId = 11,
            EventRecordId = 3002,
            UtcTime = DateTime.UtcNow.AddSeconds(-20),
            ProcessId = 3210,
            Imagem = @"C:\Tools\target.exe",
            ArquivoAlvo = @"C:\Dumps\lsass.dmp",
            Usuario = "TEST\\Operator",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        _store.InsertEvent(_sessionId, processo);
        _store.InsertEvent(_sessionId, arquivo);
        _store.CompleteSession(_sessionId);

        var testeId = _store.IniciarTesteAtomico(
            new NovoTesteAtomico("T1003", "Credential Dump Simulation", "Teste de memoria"),
            _sessionId);

        _store.FinalizarTesteAtomico(testeId, totalEventos: 2);

        var teste = _store.ListarTestesAtomicos().First(t => t.Id == testeId);
        var eventos = _store.ObterEventosDaSessao(_sessionId);

        var context = new NormalizationContext(teste, eventos);
        var normalizer = new CatalogNormalizer();
        var resultado = normalizer.Normalize(context);

        _store.SalvarResultadoNormalizacao(resultado);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task PerformAnalysis_GeraSnapshot()
    {
        var config = new AnalysisConfiguration
        {
            AnalysisIntervalSeconds = 0,
            DefaultTimeWindowMinutes = 60
        };

        var monitor = new BackgroundThreatMonitor(_store, _sessionId, config);

        var snapshotCount = 0;
        monitor.SnapshotGenerated += (_, _) => snapshotCount++;

        var performAnalysis = typeof(BackgroundThreatMonitor)
            .GetMethod("PerformAnalysis", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)performAnalysis.Invoke(monitor, Array.Empty<object?>())!;
        await task;

        Assert.Equal(1, monitor.SnapshotCount);
        Assert.Equal(1, snapshotCount);
        Assert.NotEqual(ThreatSeverityTarja.Verde, monitor.CurrentThreatLevel);
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
            }
        }

        return Task.CompletedTask;
    }
}
