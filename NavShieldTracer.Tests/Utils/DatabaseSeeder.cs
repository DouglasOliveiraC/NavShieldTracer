using System.Threading;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;

namespace NavShieldTracer.Tests.Utils;

/// <summary>
/// Responsavel por popular bancos SQLite com dados de teste deterministicos.
/// </summary>
public class DatabaseSeeder
{
    private readonly SqliteEventStore _store;
    private readonly EventSimulator _simulator;
    private readonly Random _random;
    public Random Random => _random;
    private static int _globalEventRecordCounter = 200000;

    public DatabaseSeeder(SqliteEventStore store, int seedBase = 173)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _simulator = new EventSimulator(seedBase);
        _random = new Random(seedBase);
        _ = seedBase; // retain parameter usage for deterministic Random seeding
    }

    public int CriarSessaoComEventos(
        string targetProcess,
        int quantidadeEventos,
        int rootPid = 1234,
        string? notes = null)
    {
        var sessionInfo = new SessionInfo(
            StartedAt: DateTime.UtcNow,
            TargetProcess: targetProcess,
            RootPid: rootPid,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.VersionString
        );

        var sessionId = _store.BeginSession(sessionInfo);
        var eventos = _simulator.GerarEventosMistos(quantidadeEventos, rootPid);

        foreach (var evento in eventos)
        {
            evento.EventRecordId = NextRecordId();
            _store.InsertEvent(sessionId, evento);
        }

        _store.CompleteSession(sessionId, new { notes, total_eventos = quantidadeEventos });
        return sessionId;
    }

    public List<int> CriarMultiplasSessoes(int quantidadeSessoes, int eventosPorSessao)
    {
        var sessionIds = new List<int>(quantidadeSessoes);

        for (var i = 0; i < quantidadeSessoes; i++)
        {
            var sessionId = CriarSessaoComEventos(
                $"teste_processo_{i}.exe",
                eventosPorSessao,
                10000 + i,
                $"Sessao de teste {i + 1}/{quantidadeSessoes}"
            );

            sessionIds.Add(sessionId);
        }

        return sessionIds;
    }

    public int CriarTesteAtomico(
        string numero,
        string nome,
        string descricao,
        int quantidadeEventos)
    {
        var sessionInfo = new SessionInfo(
            StartedAt: DateTime.UtcNow,
            TargetProcess: "teste.exe",
            RootPid: 5000,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.VersionString
        );

        var sessionId = _store.BeginSession(sessionInfo);
        var novoTeste = new NovoTesteAtomico(numero, nome, descricao);
        var testeId = _store.IniciarTesteAtomico(novoTeste, sessionId);

        var eventos = _simulator.GerarEventosMistos(quantidadeEventos, 5000);
        foreach (var evento in eventos)
        {
            evento.EventRecordId = NextRecordId();
            _store.InsertEvent(sessionId, evento);
        }

        _store.CompleteSession(sessionId);
        _store.FinalizarTesteAtomico(testeId, quantidadeEventos);

        return testeId;
    }

    public SeedResult PopularBancoGrande(
        int quantidadeSessoes,
        int eventosMinPorSessao,
        int eventosMaxPorSessao)
    {
        var resultado = new SeedResult
        {
            Inicio = DateTime.UtcNow
        };

        for (var i = 0; i < quantidadeSessoes; i++)
        {
            var eventos = _random.Next(eventosMinPorSessao, eventosMaxPorSessao + 1);

            var sessionId = CriarSessaoComEventos(
                $"processo_teste_{i}.exe",
                eventos,
                20000 + i,
                $"Sessao {i + 1}/{quantidadeSessoes} - {eventos} eventos"
            );

            resultado.SessionIds.Add(sessionId);
            resultado.TotalEventosInseridos += eventos;
        }

        resultado.Fim = DateTime.UtcNow;
        resultado.Duracao = resultado.Fim - resultado.Inicio;
        return resultado;
    }

    public List<int> SimularCatalogacaoMitre(Dictionary<string, (string nome, int eventos)> ttps)
    {
        var testeIds = new List<int>(ttps.Count);

        foreach (var (numero, (nome, eventos)) in ttps)
        {
            var testeId = CriarTesteAtomico(
                numero,
                nome,
                $"Teste atomico simulado para {numero} - {nome}",
                eventos
            );

            testeIds.Add(testeId);
        }

        return testeIds;
    }

    private static int NextRecordId()
    {
        var next = Interlocked.Increment(ref _globalEventRecordCounter);
        if (next > 1_900_000)
        {
            Interlocked.Exchange(ref _globalEventRecordCounter, 200000);
            next = Interlocked.Increment(ref _globalEventRecordCounter);
        }
        return next;
    }
}

public class SeedResult
{
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public TimeSpan Duracao { get; set; }
    public List<int> SessionIds { get; set; } = new();
    public int TotalEventosInseridos { get; set; }

    public double EventosPorSegundo =>
        Duracao.TotalSeconds > 0 ? TotalEventosInseridos / Duracao.TotalSeconds : 0;

    public override string ToString()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "=== Seed Result ===",
            $"Sessoes criadas: {SessionIds.Count}",
            $"Eventos inseridos: {TotalEventosInseridos:N0}",
            $"Duracao: {Duracao.TotalSeconds:F2}s",
            $"Taxa: {EventosPorSegundo:F2} eventos/s"
        });
    }
}
