using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;

namespace NavShieldTracer.Modules.Monitoring
{
    /// <summary>
    /// Rastreia a atividade de processos relacionados a um executavel alvo, incluindo processos filhos.
    /// </summary>
    public class ProcessActivityTracker
    {
        /// <summary>Nome do executavel alvo em lowercase.</summary>
        private readonly string _targetExecutableName;

        /// <summary>Dicionario thread-safe de PIDs monitorados e seus caminhos de imagem.</summary>
        private readonly ConcurrentDictionary<int, string> _monitoredPids = new();

        /// <summary>Store de eventos para persistencia em banco de dados.</summary>
        private readonly IEventStore _store;

        /// <summary>ID da sessao de monitoramento atual.</summary>
        private readonly int _sessionId;

        /// <summary>Dicionario de timestamps de inicio de cada processo monitorado.</summary>
        private readonly ConcurrentDictionary<int, DateTime> _processStartTimes = new();

        /// <summary>Colecao de duracao de vida de processos encerrados para calculo de estatisticas.</summary>
        private readonly ConcurrentBag<TimeSpan> _terminatedProcessLifetimes = new();

        /// <summary>Contador total de processos rastreados (incluindo encerrados).</summary>
        private int _totalProcessesTracked;

        /// <summary>Funcao de logging configuravel.</summary>
        private Action<string> _logger = _ => { };

        /// <summary>
        /// Dicionario somente-leitura dos processos atualmente monitorados (PID -> caminho da imagem).
        /// </summary>
        public IReadOnlyDictionary<int, string> MonitoredProcesses => _monitoredPids;

        /// <summary>
        /// Inicializa uma nova instancia do rastreador de atividades de processos.
        /// </summary>
        /// <param name="targetExecutableName">Nome do executavel alvo a ser monitorado.</param>
        /// <param name="store">Store de eventos para persistencia.</param>
        /// <param name="sessionId">ID da sessao de monitoramento.</param>
        /// <param name="logger">Funcao opcional de logging.</param>
        public ProcessActivityTracker(string targetExecutableName, IEventStore store, int sessionId, Action<string>? logger = null)
        {
            _targetExecutableName = targetExecutableName.ToLowerInvariant();
            _store = store;
            _sessionId = sessionId;
            if (logger is not null)
            {
                _logger = logger;
            }
        }

        /// <summary>
        /// Inicializa o rastreamento buscando processos ja existentes do executavel alvo.
        /// </summary>
        public void Initialize()
        {
            var normalizedName = System.IO.Path.GetFileNameWithoutExtension(_targetExecutableName);
            var existingProcesses = Process.GetProcessesByName(normalizedName);
            if (existingProcesses.Length > 0)
            {
                _logger($"Detectados {existingProcesses.Length} processos existentes de '{_targetExecutableName}'. Iniciando monitoramento.");
                foreach (var process in existingProcesses)
                {
                    string fallbackImage = $"{process.ProcessName}.exe";
                    DateTime? startTime = null;

                    try
                    {
                        string imagePath;
                        try
                        {
                            imagePath = process.MainModule?.FileName ?? fallbackImage;
                        }
                        catch (Win32Exception ex)
                        {
                            imagePath = fallbackImage;
                            _logger($"Aviso: acesso negado ao obter caminho completo do processo existente (PID: {process.Id}). Usando nome basico '{imagePath}'. Detalhes: {ex.Message}");
                        }

                        try
                        {
                            startTime = process.StartTime;
                        }
                        catch (Win32Exception ex)
                        {
                            startTime = DateTime.Now;
                            _logger($"Aviso: acesso negado ao obter horario de inicio do processo existente (PID: {process.Id}). Usando instante atual como referencia. Detalhes: {ex.Message}");
                        }

                        if (_monitoredPids.TryAdd(process.Id, imagePath))
                        {
                            if (startTime.HasValue)
                            {
                                _processStartTimes.TryAdd(process.Id, startTime.Value);
                            }
                            System.Threading.Interlocked.Increment(ref _totalProcessesTracked);
                            _logger($"Monitorando processo existente: '{imagePath}' (PID: {process.Id})");
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        _logger($"Aviso: nao foi possivel obter informacoes do processo existente (PID: {process.Id}). Pode ter sido encerrado ou acesso negado. Erro: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger($"Erro inesperado ao processar processo existente (PID: {process.Id}): {ex.Message}");
                    }
                }
            }
            else
            {
                _logger($"Nenhum processo de '{_targetExecutableName}' foi encontrado. Aguardando novos processos...");
            }
        }

        /// <summary>
        /// Processa um evento do Sysmon e determina se pertence a arvore de processos monitorados.
        /// </summary>
        /// <param name="data">Objeto de evento do Sysmon a ser processado.</param>
        public void ProcessSysmonEvent(object? data)
        {
            if (data is EventoProcessoCriado procCreate)
            {
                HandleProcessCreation(procCreate);
            }
            else if (data is EventoProcessoEncerrado procEnd)
            {
                HandleProcessTermination(procEnd);
            }
            else
            {
                int? eventPid = GetPidFromEvent(data);
                if (data is not null && eventPid.HasValue && _monitoredPids.ContainsKey(eventPid.Value))
                {
                    _store.InsertEvent(_sessionId, data);
                }
            }
        }

        /// <summary>
        /// Gerencia o evento de criacao de processo, adicionando-o a arvore de monitoramento se for relevante.
        /// </summary>
        /// <param name="procCreate">Evento de criacao de processo do Sysmon.</param>
        private void HandleProcessCreation(EventoProcessoCriado procCreate)
        {
            var processName = System.IO.Path.GetFileName(procCreate.Imagem?.ToLowerInvariant() ?? string.Empty);

            if (processName == _targetExecutableName || _monitoredPids.ContainsKey(procCreate.ParentProcessId))
            {
                if (_monitoredPids.TryAdd(procCreate.ProcessId, procCreate.Imagem ?? "unknown"))
                {
                    _processStartTimes.TryAdd(procCreate.ProcessId, DateTime.Now);
                    System.Threading.Interlocked.Increment(ref _totalProcessesTracked);

                    if (_monitoredPids.ContainsKey(procCreate.ParentProcessId))
                    {
                        _logger($"Novo processo filho detectado: '{procCreate.Imagem}' (PID: {procCreate.ProcessId}), filho de {procCreate.ParentProcessId}.");
                    }
                    else
                    {
                        _logger($"Novo processo alvo detectado: '{procCreate.Imagem}' (PID: {procCreate.ProcessId}).");
                    }
                }

                _store.InsertEvent(_sessionId, procCreate);
            }
        }

        /// <summary>
        /// Gerencia o evento de encerramento de processo, removendo-o da arvore de monitoramento.
        /// </summary>
        /// <param name="procEnd">Evento de encerramento de processo do Sysmon.</param>
        private void HandleProcessTermination(EventoProcessoEncerrado procEnd)
        {
            if (_monitoredPids.TryRemove(procEnd.ProcessId, out var imagePath))
            {
                if (_processStartTimes.TryRemove(procEnd.ProcessId, out var startTime))
                {
                    var duration = DateTime.Now - startTime;
                    _terminatedProcessLifetimes.Add(duration);
                    _logger($"Processo encerrado: '{procEnd.Imagem}' (PID: {procEnd.ProcessId}) - Duracao: {duration:hh\\:mm\\:ss}");
                }
                else
                {
                    _logger($"Processo encerrado: '{procEnd.Imagem}' (PID: {procEnd.ProcessId})");
                }

                _store.InsertEvent(_sessionId, procEnd);
            }
        }

        /// <summary>
        /// Extrai o PID do processo de um evento do Sysmon, independentemente do tipo de evento.
        /// </summary>
        /// <param name="data">Objeto de evento do Sysmon.</param>
        /// <returns>PID do processo ou null se nao for possivel extrair.</returns>
        private int? GetPidFromEvent(object? data)
        {
            if (data is null)
            {
                return null;
            }

            return data switch
            {
                EventoProcessoCriado pc => pc.ProcessId,
                EventoProcessoEncerrado pe => pe.ProcessId,
                EventoConexaoRede cr => cr.ProcessId,
                EventoImagemCarregada ic => ic.ProcessId,
                EventoThreadRemotaCriada trc => trc.ProcessIdOrigem,
                EventoAcessoProcesso ap => ap.ProcessIdOrigem,
                EventoArquivoCriado ac => ac.ProcessId,
                EventoAcessoRegistro ar => ar.ProcessId,
                EventoStreamArquivoCriado sac => sac.ProcessId,
                EventoPipeCriado ppc => ppc.ProcessId,
                EventoPipeConectado pco => pco.ProcessId,
                EventoConsultaDns cd => cd.ProcessId,
                _ => null
            };
        }

        /// <summary>
        /// Obtem estatisticas detalhadas sobre os processos monitorados.
        /// </summary>
        /// <returns>Objeto contendo estatisticas de processos ativos, encerrados e detalhes individuais.</returns>
        public ProcessActivityStatistics GetProcessStatistics()
        {
            var activeProcesses = _monitoredPids.Count;

            TimeSpan? averageLifetimeTerminated = null;
            if (!_terminatedProcessLifetimes.IsEmpty)
            {
                double avgTicks = _terminatedProcessLifetimes.Select(ts => ts.Ticks).Average();
                averageLifetimeTerminated = TimeSpan.FromTicks((long)avgTicks);
            }

            var detalhes = _monitoredPids.Select(kvp =>
            {
                _processStartTimes.TryGetValue(kvp.Key, out var startTime);
                TimeSpan? duracaoAtual = startTime == default ? null : DateTime.Now - startTime;
                return new ProcessDetail(
                    kvp.Key,
                    kvp.Value,
                    startTime == default ? (DateTime?)null : startTime,
                    duracaoAtual);
            }).ToList();

            return new ProcessActivityStatistics(
                activeProcesses,
                _totalProcessesTracked,
                _terminatedProcessLifetimes.Count,
                averageLifetimeTerminated,
                detalhes);
        }

        /// <summary>
        /// Define a funcao de logging a ser utilizada para mensagens de monitoramento.
        /// </summary>
        /// <param name="logger">Funcao que recebe mensagens de log como string.</param>
        public void SetLogger(Action<string> logger)
        {
            _logger = logger ?? (_ => { });
        }
    }

    /// <summary>
    /// Estatisticas agregadas sobre a atividade de processos monitorados.
    /// </summary>
    /// <param name="ProcessosAtivos">Numero de processos atualmente ativos.</param>
    /// <param name="TotalProcessosRastreados">Numero total de processos rastreados desde o inicio.</param>
    /// <param name="ProcessosEncerrados">Numero de processos que foram encerrados.</param>
    /// <param name="TempoMedioDeVidaEncerrados">Tempo medio de vida dos processos encerrados.</param>
    /// <param name="ProcessosAtivosDetalhados">Lista detalhada de todos os processos ativos.</param>
    public sealed record ProcessActivityStatistics(
        int ProcessosAtivos,
        int TotalProcessosRastreados,
        int ProcessosEncerrados,
        TimeSpan? TempoMedioDeVidaEncerrados,
        IReadOnlyList<ProcessDetail> ProcessosAtivosDetalhados);

    /// <summary>
    /// Detalhes de um processo individual monitorado.
    /// </summary>
    /// <param name="PID">ID do processo.</param>
    /// <param name="Imagem">Caminho completo da imagem do executavel.</param>
    /// <param name="IniciadoEm">Timestamp de inicio do processo.</param>
    /// <param name="DuracaoAtual">Tempo decorrido desde o inicio do processo.</param>
    public sealed record ProcessDetail(
        int PID,
        string Imagem,
        DateTime? IniciadoEm,
        TimeSpan? DuracaoAtual);
}
