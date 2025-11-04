using NavShieldTracer.Modules.Heuristics.Engine;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Modules.Monitoring;
using NavShieldTracer.Storage;
using Xunit;

namespace NavShieldTracer.Tests.Monitoring;

public sealed class ProcessActivityTrackerTests
{
    [Fact]
    public void ProcessaCriacaoDoProcessoAlvo_EstadoAtualizado()
    {
        var store = new RecordingEventStore();
        var tracker = new ProcessActivityTracker("target.exe", store, sessionId: 1);

        var criado = new EventoProcessoCriado
        {
            EventId = 1,
            EventRecordId = 1001,
            UtcTime = DateTime.UtcNow,
            ProcessId = 4242,
            ParentProcessId = 100,
            Imagem = @"C:\Tools\target.exe",
            LinhaDeComando = "target.exe -run",
            Usuario = "TEST\\User",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        tracker.ProcessSysmonEvent(criado);

        Assert.Single(store.InsertedEvents);
        Assert.Same(criado, store.InsertedEvents[0]);

        var stats = tracker.GetProcessStatistics();
        Assert.Equal(1, stats.ProcessosAtivos);
        Assert.Equal(1, stats.TotalProcessosRastreados);
    }

    [Fact]
    public void ProcessaEventosDoProcessoMonitored_EstatisticasConsistentes()
    {
        var store = new RecordingEventStore();
        var tracker = new ProcessActivityTracker("target.exe", store, sessionId: 2);

        var criado = new EventoProcessoCriado
        {
            EventId = 1,
            EventRecordId = 2001,
            UtcTime = DateTime.UtcNow,
            ProcessId = 5000,
            ParentProcessId = 0,
            Imagem = @"C:\Tools\target.exe",
            LinhaDeComando = "target.exe -start",
            Usuario = "TEST\\User",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        tracker.ProcessSysmonEvent(criado);

        var conexao = new EventoConexaoRede
        {
            EventId = 3,
            EventRecordId = 2002,
            UtcTime = DateTime.UtcNow,
            ProcessId = 5000,
            Imagem = @"C:\Tools\target.exe",
            IpOrigem = "10.0.0.5",
            PortaOrigem = 50000,
            IpDestino = "8.8.8.8",
            PortaDestino = 443,
            Protocolo = "tcp"
        };

        tracker.ProcessSysmonEvent(conexao);

        var encerrado = new EventoProcessoEncerrado
        {
            EventId = 5,
            EventRecordId = 2003,
            UtcTime = DateTime.UtcNow,
            ProcessId = 5000,
            Imagem = @"C:\Tools\target.exe",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        tracker.ProcessSysmonEvent(encerrado);

        Assert.Equal(3, store.InsertedEvents.Count);
        Assert.Contains(store.InsertedEvents, e => ReferenceEquals(e, conexao));
        Assert.Contains(store.InsertedEvents, e => ReferenceEquals(e, encerrado));

        var stats = tracker.GetProcessStatistics();
        Assert.Equal(0, stats.ProcessosAtivos);
        Assert.Equal(1, stats.ProcessosEncerrados);
        Assert.Equal(1, stats.TotalProcessosRastreados);
    }

    private sealed class RecordingEventStore : IEventStore
    {
        public List<object> InsertedEvents { get; } = new();

        public string DatabasePath => "in-memory";

        public int BeginSession(SessionInfo info) => throw new NotSupportedException();

        public void CompleteSession(int sessionId, object? summary = null) => throw new NotSupportedException();

        public void Dispose()
        {
        }

        public List<object> ExportarEventosTeste(int testeId) => throw new NotSupportedException();

        public void FinalizarTesteAtomico(int testeId, int totalEventos) => throw new NotSupportedException();

        public int IniciarTesteAtomico(NovoTesteAtomico novoTeste, int sessionId) => throw new NotSupportedException();

        public void InsertEvent(int sessionId, object data)
        {
            InsertedEvents.Add(data);
        }

        public List<TesteAtomico> ListarTestesAtomicos() => throw new NotSupportedException();

        public ResumoTesteAtomico? ObterResumoTeste(int testeId) => throw new NotSupportedException();

        public bool ExcluirTesteAtomico(int testeId) => throw new NotSupportedException();

        public void AtualizarTesteAtomico(int testeId, string? numero = null, string? nome = null, string? descricao = null) => throw new NotSupportedException();

        public SessionSnapshot? ObterUltimoSnapshotDeSimilaridade(int sessionId) => null;

        public List<SessaoMonitoramento> ListarSessoes() => new();

        public SessionStats ObterEstatisticasSessao(int sessionId) => new SessionStats
        {
            EventosPorTipo = new Dictionary<int, int>(),
            TopIps = new List<string>(),
            TopDomains = new List<string>(),
            ProcessosCriados = new List<string>(),
            TarjaTesteAssociado = null
        };
    }
}
