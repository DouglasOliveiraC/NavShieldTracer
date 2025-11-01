using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NavShieldTracer.Modules.Diagnostics;
using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Modules.Monitoring;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;

namespace NavShieldTracer.ConsoleApp.Services;

/// <summary>
/// Serviço principal da aplicação NavShieldTracer que coordena monitoramento de processos,
/// catalogação de testes atômicos do MITRE ATT&amp;CK e gerenciamento de sessões.
/// </summary>
public sealed class NavShieldAppService : IDisposable
{
    private readonly SqliteEventStore _store;
    private readonly object _syncRoot = new();
    private MonitoringSession? _activeSession;
    private string _sysmonLogName = SysmonEventMonitor.DefaultLogName;
    private SysmonStatus? _currentStatus;

    /// <summary>
    /// Cria uma nova instância do serviço de aplicação NavShieldTracer.
    /// </summary>
    /// <param name="store">Repositório SQLite para persistência de eventos e testes.</param>
    public NavShieldAppService(SqliteEventStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Inicializa o serviço de forma assíncrona, verificando o status do Sysmon.
    /// </summary>
    /// <returns>Uma tarefa que representa a operação assíncrona.</returns>
    public async Task InitializeAsync()
    {
        await Task.Run(() => RefreshSysmonStatusInternal()).ConfigureAwait(false);
    }

    /// <summary>
    /// Obtém o status atual do Sysmon. Se não houver cache, atualiza automaticamente.
    /// </summary>
    public SysmonStatus CurrentStatus
    {
        get
        {
            if (_currentStatus is null)
            {
                RefreshSysmonStatusInternal();
            }

            return _currentStatus!;
        }
    }

    /// <summary>
    /// Atualiza o status do Sysmon de forma assíncrona.
    /// </summary>
    /// <returns>Uma tarefa que retorna o status atualizado do Sysmon.</returns>
    public Task<SysmonStatus> RefreshSysmonStatusAsync()
    {
        return Task.Run(() => RefreshSysmonStatusInternal());
    }

    private SysmonStatus RefreshSysmonStatusInternal()
    {
        var status = SysmonDiagnostics.GatherStatus();
        _currentStatus = status;
        _sysmonLogName = status.LogName ?? SysmonEventMonitor.DefaultLogName;
        return status;
    }

    /// <summary>
    /// Obtém um snapshot do estado atual do dashboard com estatísticas gerais.
    /// </summary>
    /// <returns>Snapshot contendo total de testes, eventos, último teste e caminho do banco.</returns>
    public DashboardSnapshot GetDashboardSnapshot()
    {
        var testes = _store.ListarTestesAtomicos();
        var totalEventos = testes.Sum(t => t.TotalEventos);
        var ultimoTeste = testes.OrderByDescending(t => t.DataExecucao).FirstOrDefault();

        return new DashboardSnapshot(
            testes.Count,
            totalEventos,
            ultimoTeste?.DataExecucao,
            _store.DatabasePath,
            _activeSession is not null);
    }

    /// <summary>
    /// Obtém os processos do sistema ordenados por uso de memória.
    /// </summary>
    /// <param name="count">Quantidade máxima de processos a retornar (padrão: 10).</param>
    /// <returns>Lista de snapshots dos processos com maior uso de memória.</returns>
    public IReadOnlyList<ProcessSnapshot> GetTopProcesses(int count = 10)
    {
        return Process.GetProcesses()
            .Where(p =>
            {
                try
                {
                    _ = p.ProcessName;
                    return true;
                }
                catch
                {
                    return false;
                }
            })
            .OrderByDescending(p =>
            {
                try
                {
                    return p.WorkingSet64;
                }
                catch
                {
                    return 0L;
                }
            })
            .Take(count)
            .Select(p =>
            {
                double memoryMb = 0;
                try
                {
                    memoryMb = p.WorkingSet64 / 1024d / 1024d;
                }
                catch
                {
                    // ignore
                }

                return new ProcessSnapshot(
                    $"{p.ProcessName}.exe",
                    p.Id,
                    memoryMb);
            })
            .ToList();
    }

    /// <summary>
    /// Localiza processos em execução cujo nome corresponde parcial ou totalmente ao texto informado.
    /// </summary>
    /// <param name="query">Texto digitado pelo usuário, com ou sem extensão.</param>
    /// <param name="maxResults">Quantidade máxima de resultados a retornar (padrão: 9).</param>
    /// <returns>Lista de processos ordenados por relevância.</returns>
    public IReadOnlyList<ProcessSnapshot> FindProcesses(string query, int maxResults = 9)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ProcessSnapshot>();
        }

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return Array.Empty<ProcessSnapshot>();
        }

        var normalized = NormalizeExecutable(query);
        var desiredName = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrEmpty(desiredName))
        {
            return Array.Empty<ProcessSnapshot>();
        }

        var results = new List<(ProcessSnapshot Snapshot, int Score)>();

        foreach (var process in processes)
        {
            string? name;
            try
            {
                name = process.ProcessName;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var score = ComputeMatchScore(name, desiredName);
            if (score is null)
            {
                continue;
            }

            double memoryMb;
            try
            {
                memoryMb = process.WorkingSet64 / (1024d * 1024d);
            }
            catch
            {
                memoryMb = 0d;
            }

            results.Add((new ProcessSnapshot($"{name}.exe", process.Id, memoryMb), score.Value));
        }

        if (results.Count == 0)
        {
            return Array.Empty<ProcessSnapshot>();
        }

        var max = Math.Max(1, maxResults);
        return results
            .OrderBy(r => r.Score)
            .ThenBy(r => r.Snapshot.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.Snapshot.MemoryMb)
            .Take(max)
            .Select(r => r.Snapshot)
            .ToList();
    }

    /// <summary>
    /// Recupera o resumo de normalizacao persistido para um teste catalogado.
    /// </summary>
    /// <param name="testeId">ID do teste.</param>
    /// <returns>Resumo da normalizacao ou null se inexistente.</returns>
    internal NormalizationSummary? ObterResumoNormalizacao(int testeId)
    {
        return _store.ObterResumoNormalizacao(testeId);
    }

    /// <summary>
    /// Lista todos os testes atômicos catalogados no banco de dados.
    /// </summary>
    /// <returns>Lista de testes atômicos catalogados.</returns>
    public IReadOnlyList<TesteAtomico> ListarTestes() => _store.ListarTestesAtomicos();

    /// <summary>
    /// Lista todas as sessões de monitoramento (ativas e encerradas).
    /// </summary>
    /// <returns>Lista de sessões ordenadas por data de início (mais recentes primeiro).</returns>
    public IReadOnlyList<SessaoMonitoramento> ListarSessoes()
    {
        var sessoes = _store.ListarSessoes();
        if (sessoes.Count == 0)
        {
            return sessoes;
        }

        var filtered = new List<SessaoMonitoramento>(sessoes.Count);
        var removedAny = false;

        foreach (var sessao in sessoes)
        {
            var target = sessao.TargetProcess;
            if (!string.IsNullOrWhiteSpace(target))
            {
                var processName = Path.GetFileName(target) ?? target;
                if (string.Equals(processName, "teste.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(processName, "teste", StringComparison.OrdinalIgnoreCase))
                {
                    removedAny = true;
                    continue;
                }
            }

            filtered.Add(sessao);
        }

        return removedAny ? filtered : sessoes;
    }

    /// <summary>
    /// Obtém o resumo detalhado de um teste atômico específico.
    /// </summary>
    /// <param name="testeId">ID do teste a ser consultado.</param>
    /// <returns>Resumo do teste ou null se não encontrado.</returns>
    public ResumoTesteAtomico? ObterResumoTeste(int testeId) => _store.ObterResumoTeste(testeId);

    /// <summary>
    /// Obtém a contagem de eventos críticos por Event ID para uma sessão específica.
    /// Usado para classificação automática de nível de ameaça sem motor heurístico.
    /// </summary>
    /// <param name="sessionId">ID da sessão a ser analisada.</param>
    /// <returns>Dicionário com Event ID como chave e contagem como valor.</returns>
    public Dictionary<int, int> GetCriticalEventCounts(int sessionId) => _store.GetCriticalEventCounts(sessionId);

    /// <summary>
    /// Obtém um snapshot da sessão de monitoramento ativa, se houver.
    /// </summary>
    /// <returns>Snapshot da sessão ativa ou null se não houver sessão ativa.</returns>
    public MonitoringSessionSnapshot? GetActiveSessionSnapshot()
    {
        MonitoringSession? session;

        lock (_syncRoot)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return null;
        }

        var stats = session.Tracker.GetProcessStatistics();

        int totalEventos;
        try
        {
            totalEventos = _store.ContarEventosSessao(session.SessionId);
        }
        catch
        {
            totalEventos = 0;
        }

        return new MonitoringSessionSnapshot(
            session.SessionId,
            session.Kind,
            session.TargetExecutable,
            session.StartedAt,
            totalEventos,
            stats,
            session.GetLogs());
    }

    /// <summary>
    /// Inicia uma nova sessão de monitoramento de processo sem catalogação.
    /// </summary>
    /// <param name="targetExecutable">Nome do executável a ser monitorado.</param>
    /// <param name="preferredPid">PID preferencial quando existir mais de um processo com o mesmo executável.</param>
    /// <returns>Sessão de monitoramento criada.</returns>
    public MonitoringSession StartMonitoring(string targetExecutable, int? preferredPid = null)
        => StartInternal(MonitoringSessionType.Monitor, targetExecutable, null, preferredPid);

    /// <summary>
    /// Inicia uma nova sessão de catalogação de teste atômico do MITRE ATT&amp;CK.
    /// </summary>
    /// <param name="novoTeste">Metadados do teste atômico a ser catalogado.</param>
    /// <param name="targetExecutable">Nome do executável a ser monitorado.</param>
    /// <param name="preferredPid">PID preferencial quando existir mais de um processo com o mesmo executável.</param>
    /// <returns>Sessão de monitoramento criada para catalogação.</returns>
    public MonitoringSession StartCatalog(NovoTesteAtomico novoTeste, string targetExecutable, int? preferredPid = null)
        => StartInternal(MonitoringSessionType.Catalog, targetExecutable, novoTeste, preferredPid);
    private MonitoringSession StartInternal(
        MonitoringSessionType kind,
        string targetExecutable,
        NovoTesteAtomico? novoTeste,
        int? preferredPid)
    {
        if (string.IsNullOrWhiteSpace(targetExecutable))
        {
            throw new ArgumentException("Nome do executavel nao pode ser vazio.", nameof(targetExecutable));
        }

        if (kind == MonitoringSessionType.Catalog && novoTeste is null)
        {
            throw new ArgumentNullException(nameof(novoTeste), "Dados do teste atomico sao obrigatorios para catalogacao.");
        }

        var normalizedExecutable = NormalizeExecutable(targetExecutable);
        var rootPid = TryResolveProcessId(normalizedExecutable, preferredPid);

        int sessionId = _store.BeginSession(new SessionInfo(
            DateTime.Now,
            normalizedExecutable,
            rootPid,
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.ToString()));

        int? testeId = null;
        if (kind == MonitoringSessionType.Catalog && novoTeste is not null)
        {
            testeId = _store.IniciarTesteAtomico(novoTeste, sessionId);
        }

        var tracker = new ProcessActivityTracker(normalizedExecutable, _store, sessionId);
        var session = new MonitoringSession(kind, sessionId, normalizedExecutable, tracker, novoTeste, testeId);
        tracker.SetLogger(session.AppendLog);
        var monitor = new SysmonEventMonitor(tracker, _sysmonLogName, session.AppendLog);
        session.AttachMonitor(monitor);

        try
        {
            monitor.Start();
        }
        catch (Exception ex)
        {
            session.AppendLog($"Erro ao iniciar monitoramento: {ex.Message}");
            _store.CompleteSession(sessionId, new { error = ex.Message });
            if (testeId.HasValue)
            {
                try
                {
                    _store.ExcluirTesteAtomico(testeId.Value);
                }
                catch
                {
                    // ignore cleanup failure
                }
            }
            throw;
        }

        lock (_syncRoot)
        {
            if (_activeSession is not null)
            {
                monitor.Stop();
                throw new InvalidOperationException("Ja existe uma sessao de monitoramento ativa.");
            }

            _activeSession = session;
        }

        session.AppendLog($"Sessao iniciada para '{normalizedExecutable}'.");
        return session;
    }

    /// <summary>
    /// Encerra a sessão de monitoramento ativa, finalizando a captura de eventos e persistindo os dados.
    /// </summary>
    /// <returns>Resultado do monitoramento com estatísticas e logs da sessão.</returns>
    /// <exception cref="InvalidOperationException">Lançada se não houver sessão ativa.</exception>
    public async Task<MonitoringResult> StopActiveSessionAsync()
    {
        MonitoringSession session;

        lock (_syncRoot)
        {
            if (_activeSession is null)
            {
                throw new InvalidOperationException("Nao ha sessao ativa no momento.");
            }

            session = _activeSession;
            _activeSession = null;
        }

        session.AppendLog("Encerrando monitoramento...");

        try
        {
            session.Monitor.Stop();
        }
        catch (Exception ex)
        {
            session.AppendLog($"Aviso: falha ao parar monitoramento ({ex.Message}).");
        }

        var endedAt = DateTime.Now;
        var stats = session.Tracker.GetProcessStatistics();
        int totalEventos;

        try
        {
            totalEventos = _store.ContarEventosSessao(session.SessionId);
        }
        catch
        {
            totalEventos = 0;
        }

        if (session.Kind == MonitoringSessionType.Catalog && session.TestId.HasValue)
        {
            try
            {
                _store.FinalizarTesteAtomico(session.TestId.Value, totalEventos);
            }
            catch (Exception ex)
            {
                session.AppendLog($"Falha ao finalizar teste: {ex.Message}");
            }
        }

        try
        {
            _store.CompleteSession(session.SessionId, stats);
        }
        catch (Exception ex)
        {
            session.AppendLog($"Falha ao concluir sessao: {ex.Message}");
        }

        if (session.Kind == MonitoringSessionType.Catalog && session.TestId.HasValue)
        {
            try
            {
                var workflow = new CatalogNormalizationWorkflow(_store);
                workflow.Executar(session.TestId.Value);
                session.AppendLog("Normalizacao heuristica concluida com sucesso.");
            }
            catch (Exception ex)
            {
                session.AppendLog($"Normalizacao heuristica falhou: {ex.Message}");
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);

        return new MonitoringResult(
            session.Kind,
            session.TargetExecutable,
            session.StartedAt,
            endedAt,
            totalEventos,
            stats,
            session.TestId,
            session.TestMetadata,
            session.GetLogs());
    }

    /// <summary>
    /// Atualiza os metadados de um teste atômico catalogado.
    /// </summary>
    /// <param name="testeId">ID do teste a ser atualizado.</param>
    /// <param name="numero">Novo número da técnica MITRE (ex: T1055) ou null para manter.</param>
    /// <param name="nome">Novo nome da técnica ou null para manter.</param>
    /// <param name="descricao">Nova descrição ou null para manter.</param>
    /// <returns>True se atualizado com sucesso, false caso contrário.</returns>
    public bool AtualizarTeste(int testeId, string? numero, string? nome, string? descricao)
    {
        try
        {
            _store.AtualizarTesteAtomico(testeId, numero, nome, descricao);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Obtém as informações completas de um teste atômico incluindo metadados de normalização.
    /// </summary>
    /// <param name="testeId">ID do teste a ser consultado.</param>
    /// <returns>Informações completas do teste ou null se não encontrado.</returns>
    public TesteAtomicoCompleto? ObterTesteCompleto(int testeId) => _store.ObterTesteAtomicoCompleto(testeId);

    /// <summary>
    /// Atualiza a tarja (severidade) de um teste atômico.
    /// </summary>
    /// <param name="testeId">ID do teste a ser atualizado.</param>
    /// <param name="tarja">Nova tarja (Verde, Amarelo, Laranja, Vermelho).</param>
    /// <param name="tarjaReason">Justificativa da tarja (opcional).</param>
    /// <returns>True se atualizado com sucesso, false caso contrário.</returns>
    public bool AtualizarTarja(int testeId, string tarja, string? tarjaReason = null)
    {
        try
        {
            _store.AtualizarTarjaTesteAtomico(testeId, tarja, tarjaReason);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Atualiza as notas/observações de uma sessão de monitoramento.
    /// </summary>
    /// <param name="sessionId">ID da sessão a ser atualizada.</param>
    /// <param name="notes">Novas notas (substitui as existentes).</param>
    /// <returns>True se atualizado com sucesso, false caso contrário.</returns>
    public bool AtualizarNotas(int sessionId, string? notes)
    {
        try
        {
            _store.AtualizarNotasSessao(sessionId, notes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Salva metadados de review pós-catalogação: observações e nível de alerta.
    /// </summary>
    /// <param name="testeId">ID do teste atômico catalogado.</param>
    /// <param name="tarja">Nível de alerta (Verde, Amarelo, Laranja, Vermelho).</param>
    /// <param name="observacoes">Observações sobre o teste (opcional).</param>
    /// <returns>True se salvo com sucesso, false caso contrário.</returns>
    public bool SaveTestReview(int testeId, string tarja, string? observacoes)
    {
        try
        {
            _store.SalvarReviewTeste(testeId, observacoes, tarja);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exclui um teste atômico catalogado do banco de dados.
    /// </summary>
    /// <param name="testeId">ID do teste a ser excluído.</param>
    /// <returns>True se excluído com sucesso, false caso contrário.</returns>
    public bool ExcluirTeste(int testeId)
    {
        try
        {
            return _store.ExcluirTesteAtomico(testeId);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Exporta os eventos de um teste atômico catalogado para um arquivo JSON.
    /// </summary>
    /// <param name="testeId">ID do teste a ser exportado.</param>
    /// <returns>Caminho completo do arquivo JSON gerado.</returns>
    public async Task<string> ExportarTesteAsync(int testeId)
    {
        var eventos = _store.ExportarEventosTeste(testeId);
        var fileName = $"logs_teste_{testeId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var directory = Path.GetDirectoryName(_store.DatabasePath) ?? Environment.CurrentDirectory;
        var filePath = Path.Combine(directory, fileName);
        var json = JsonSerializer.Serialize(eventos, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
        return filePath;
    }

    /// <summary>
    /// Exporta todos os eventos de uma sessão de monitoramento para arquivo JSON.
    /// </summary>
    /// <param name="sessionId">ID da sessão a ser exportada.</param>
    /// <returns>Caminho completo do arquivo exportado.</returns>
    public async Task<string> ExportarSessaoAsync(int sessionId)
    {
        // Buscar metadados da sessão
        var sessoes = _store.ListarSessoes();
        var sessao = sessoes.FirstOrDefault(s => s.Id == sessionId);

        // Buscar estatísticas
        var stats = _store.ObterEstatisticasSessao(sessionId);

        // Buscar eventos
        var eventos = _store.ExportarEventosSessao(sessionId);

        // Montar objeto completo para exportação
        var exportData = new
        {
            Sessao = sessao,
            Estatisticas = stats,
            Eventos = eventos
        };

        var fileName = $"logs_sessao_{sessionId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var directory = Path.GetDirectoryName(_store.DatabasePath) ?? Environment.CurrentDirectory;
        var filePath = Path.Combine(directory, fileName);
        var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
        return filePath;
    }

    /// <summary>
    /// Obtém estatísticas básicas de uma sessão para exibição
    /// </summary>
    /// <param name="sessionId">ID da sessão</param>
    /// <returns>Estatísticas da sessão</returns>
    public SessionStats ObterEstatisticasSessao(int sessionId) => _store.ObterEstatisticasSessao(sessionId);

    private static string NormalizeExecutable(string executable)
    {
        var trimmed = executable.Trim();
        if (!trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += ".exe";
        }

        return trimmed;
    }

    private static int? ComputeMatchScore(string candidate, string target)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(target))
        {
            return null;
        }

        if (candidate.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.StartsWith(target, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (candidate.Contains(target, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (target.Contains(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return null;
    }

    private static int TryResolveProcessId(string executableName, int? preferredPid)
    {
        try
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(executableName);
            var processes = Process.GetProcessesByName(nameWithoutExtension);
            if (preferredPid.HasValue)
            {
                var preferred = processes.FirstOrDefault(p => p.Id == preferredPid.Value);
                if (preferred != null)
                {
                    return preferred.Id;
                }
            }

            var match = processes
                .OrderByDescending(p =>
                {
                    try
                    {
                        return p.WorkingSet64;
                    }
                    catch
                    {
                        return 0L;
                    }
                })
                .FirstOrDefault();
            return match?.Id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Libera recursos utilizados pelo serviço, encerrando sessões ativas e fechando o banco de dados.
    /// </summary>
    public void Dispose()
    {
        if (_activeSession is not null)
        {
            try
            {
                _activeSession.Monitor.Stop();
            }
            catch
            {
                // ignore
            }

            _activeSession = null;
        }

        _store.Dispose();
    }
}

/// <summary>
/// Snapshot do estado atual do dashboard com estatísticas agregadas.
/// </summary>
/// <param name="TotalTestes">Quantidade total de testes atômicos catalogados.</param>
/// <param name="TotalEventos">Quantidade total de eventos capturados em todos os testes.</param>
/// <param name="UltimoTesteEm">Data/hora do último teste catalogado ou null se não houver testes.</param>
/// <param name="DatabasePath">Caminho completo do arquivo do banco de dados SQLite.</param>
/// <param name="HasActiveSession">Indica se há uma sessão de monitoramento ativa no momento.</param>
public sealed record DashboardSnapshot(
    int TotalTestes,
    int TotalEventos,
    DateTime? UltimoTesteEm,
    string DatabasePath,
    bool HasActiveSession);

/// <summary>
/// Snapshot de um processo do sistema com informações básicas de uso de recursos.
/// </summary>
/// <param name="ProcessName">Nome do processo (ex: "notepad.exe").</param>
/// <param name="Pid">Process ID (PID).</param>
/// <param name="MemoryMb">Uso de memória em megabytes.</param>
public sealed record ProcessSnapshot(
    string ProcessName,
    int Pid,
    double MemoryMb);

/// <summary>
/// Snapshot de uma sessão de monitoramento ativa em andamento.
/// </summary>
/// <param name="SessionId">ID da sessão no banco de dados.</param>
/// <param name="Kind">Tipo da sessão (Monitor ou Catalog).</param>
/// <param name="TargetExecutable">Nome do executável sendo monitorado.</param>
/// <param name="StartedAt">Data/hora de início da sessão.</param>
/// <param name="TotalEventos">Quantidade de eventos capturados até o momento.</param>
/// <param name="Statistics">Estatísticas de atividade do processo.</param>
/// <param name="Logs">Logs recentes da sessão.</param>
public sealed record MonitoringSessionSnapshot(
    int SessionId,
    MonitoringSessionType Kind,
    string TargetExecutable,
    DateTime StartedAt,
    int TotalEventos,
    ProcessActivityStatistics Statistics,
    IReadOnlyList<string> Logs);

/// <summary>
/// Resultado de uma sessão de monitoramento finalizada.
/// </summary>
/// <param name="Kind">Tipo da sessão (Monitor ou Catalog).</param>
/// <param name="TargetExecutable">Nome do executável monitorado.</param>
/// <param name="StartedAt">Data/hora de início da sessão.</param>
/// <param name="EndedAt">Data/hora de finalização da sessão.</param>
/// <param name="TotalEventos">Quantidade total de eventos capturados.</param>
/// <param name="Statistics">Estatísticas finais de atividade do processo.</param>
/// <param name="TestId">ID do teste atômico catalogado (apenas para sessões Catalog).</param>
/// <param name="TestMetadata">Metadados do teste atômico (apenas para sessões Catalog).</param>
/// <param name="Logs">Logs completos da sessão.</param>
public sealed record MonitoringResult(
    MonitoringSessionType Kind,
    string TargetExecutable,
    DateTime StartedAt,
    DateTime EndedAt,
    int TotalEventos,
    ProcessActivityStatistics Statistics,
    int? TestId,
    NovoTesteAtomico? TestMetadata,
    IReadOnlyList<string> Logs);

/// <summary>
/// Tipo de sessão de monitoramento.
/// </summary>
public enum MonitoringSessionType
{
    /// <summary>
    /// Monitoramento simples sem catalogação de teste.
    /// </summary>
    Monitor,

    /// <summary>
    /// Monitoramento com catalogação de teste atômico do MITRE ATT&amp;CK.
    /// </summary>
    Catalog
}

/// <summary>
/// Representa uma sessão de monitoramento ativa, gerenciando o ciclo de vida do rastreamento de processos.
/// </summary>
public sealed class MonitoringSession
{
    private const int MaxLogs = 50;
    private readonly ConcurrentQueue<string> _logs = new();

    /// <summary>
    /// Cria uma nova sessão de monitoramento.
    /// </summary>
    /// <param name="kind">Tipo da sessão (Monitor ou Catalog).</param>
    /// <param name="sessionId">ID da sessão no banco de dados.</param>
    /// <param name="targetExecutable">Nome do executável monitorado durante a sessão.</param>
    /// <param name="tracker">Rastreador de atividade de processos.</param>
    /// <param name="testMetadata">Metadados do teste atômico (apenas para Catalog).</param>
    /// <param name="testId">ID do teste no banco (apenas para Catalog).</param>
    public MonitoringSession(
        MonitoringSessionType kind,
        int sessionId,
        string targetExecutable,
        ProcessActivityTracker tracker,
        NovoTesteAtomico? testMetadata,
        int? testId)
    {
        Kind = kind;
        SessionId = sessionId;
        TargetExecutable = targetExecutable;
        Tracker = tracker;
        TestMetadata = testMetadata;
        TestId = testId;
        StartedAt = DateTime.Now;
    }

    /// <summary>Tipo da sessão de monitoramento.</summary>
    public MonitoringSessionType Kind { get; }

    /// <summary>ID da sessão no banco de dados.</summary>
    public int SessionId { get; }

    /// <summary>Nome do executável sendo monitorado.</summary>
    public string TargetExecutable { get; }

    /// <summary>Rastreador de atividade de processos.</summary>
    public ProcessActivityTracker Tracker { get; }

    /// <summary>Metadados do teste atômico (apenas para sessões de catalogação).</summary>
    public NovoTesteAtomico? TestMetadata { get; }

    /// <summary>ID do teste atômico no banco (apenas para sessões de catalogação).</summary>
    public int? TestId { get; }

    /// <summary>Data/hora de início da sessão.</summary>
    public DateTime StartedAt { get; }

    /// <summary>Monitor de eventos do Sysmon anexado a esta sessão.</summary>
    public SysmonEventMonitor Monitor { get; private set; } = null!;

    /// <summary>
    /// Anexa um monitor de eventos do Sysmon à sessão.
    /// </summary>
    /// <param name="monitor">Monitor a ser anexado.</param>
    public void AttachMonitor(SysmonEventMonitor monitor) => Monitor = monitor;

    /// <summary>
    /// Adiciona uma mensagem de log à fila de logs da sessão.
    /// </summary>
    /// <param name="message">Mensagem a ser registrada.</param>
    public void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logs.Enqueue(entry);
        while (_logs.Count > MaxLogs && _logs.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Obtém todos os logs da sessão como uma lista somente leitura.
    /// </summary>
    /// <returns>Lista de mensagens de log formatadas com timestamp.</returns>
    public IReadOnlyList<string> GetLogs() => _logs.ToArray();
}



