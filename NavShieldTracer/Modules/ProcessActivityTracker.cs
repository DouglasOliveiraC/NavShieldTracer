using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.ComponentModel;
using NavShieldTracer.Modules.Storage;

namespace NavShieldTracer.Modules
{
    /// <summary>
    /// Rastreia a atividade de processos relacionados a um executável alvo, incluindo processos filhos.
    /// </summary>
    public class ProcessActivityTracker
    {
        private readonly string _targetExecutableName;
        private readonly ConcurrentDictionary<int, string> _monitoredPids = new ConcurrentDictionary<int, string>();
        private readonly IEventStore _store;
        private readonly int _sessionId;
        private readonly ConcurrentDictionary<int, DateTime> _processStartTimes = new ConcurrentDictionary<int, DateTime>();
        private int _totalProcessesTracked = 0;
        private readonly ConcurrentBag<TimeSpan> _terminatedProcessLifetimes = new ConcurrentBag<TimeSpan>();

        /// <summary>
        /// Obtém a lista de PIDs e nomes de imagem dos processos que estão sendo monitorados.
        /// </summary>
        public IReadOnlyDictionary<int, string> MonitoredProcesses => _monitoredPids;

        /// <summary>
        /// Inicializa uma nova instância da classe <see cref="ProcessActivityTracker"/>.
        /// </summary>
        /// <param name="targetExecutableName">O nome do executável alvo a ser monitorado (ex: "chrome.exe").</param>
        /// <param name="store">Persistência de eventos.</param>
        /// <param name="sessionId">Identificador da sessão ativa.</param>
        public ProcessActivityTracker(string targetExecutableName, IEventStore store, int sessionId)
        {
            _targetExecutableName = targetExecutableName.ToLowerInvariant();
            _store = store;
            _sessionId = sessionId;
        }

        /// <summary>
        /// Inicia o rastreamento verificando processos já existentes que correspondem ao executável alvo.
        /// </summary>
        public void Initialize()
        {
            var existingProcesses = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(_targetExecutableName));
            if (existingProcesses.Length > 0)
            {
                Console.WriteLine($"\n📊 Detectados {existingProcesses.Length} processos existentes de '{_targetExecutableName}'. Iniciando monitoramento.");
                foreach (var process in existingProcesses)
                {
                    try
                    {
                        string imagePath = process.MainModule?.FileName ?? (process.ProcessName + ".exe");
                        if (_monitoredPids.TryAdd(process.Id, imagePath))
                        {
                            _processStartTimes.TryAdd(process.Id, process.StartTime);
                            System.Threading.Interlocked.Increment(ref _totalProcessesTracked);
                            Console.WriteLine($"   -> Monitorando processo existente: '{imagePath}' (PID: {process.Id})");
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        Console.WriteLine($"   -> Aviso: Não foi possível obter informações do processo existente (PID: {process.Id}). Pode ter sido encerrado ou acesso negado. Erro: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   -> Erro inesperado ao processar processo existente (PID: {process.Id}): {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"\n📊 Nenhum processo de '{_targetExecutableName}' encontrado. Aguardando novos processos...");
            }
        }

        /// <summary>
        /// Processa um evento do Sysmon, determinando se ele pertence à árvore de processos monitorada
        /// e registrando-o se for o caso.
        /// </summary>
        /// <param name="data">O objeto de dados do evento Sysmon.</param>
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
                if (data != null && eventPid.HasValue && _monitoredPids.ContainsKey(eventPid.Value))
                {
                    _store.InsertEvent(_sessionId, data);
                }
            }
        }

        /// <summary>
        /// Lida com eventos de criação de processo, adicionando novos processos à lista de monitoramento
        /// se eles corresponderem ao nome do alvo ou forem filhos de um processo já monitorado.
        /// </summary>
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
                        Console.WriteLine($"   -> Novo processo filho detectado: '{procCreate.Imagem}' (PID: {procCreate.ProcessId}), filho de {procCreate.ParentProcessId}.");
                    }
                    else
                    {
                        Console.WriteLine($"\n Novo processo alvo detectado: '{procCreate.Imagem}' (PID: {procCreate.ProcessId}).");
                    }
                }

                _store.InsertEvent(_sessionId, procCreate);
            }
        }

        /// <summary>
        /// Lida com eventos de encerramento de processo, removendo-os da lista de monitoramento.
        /// </summary>
        private void HandleProcessTermination(EventoProcessoEncerrado procEnd)
        {
            if (_monitoredPids.TryRemove(procEnd.ProcessId, out var imagePath))
            {
                if (_processStartTimes.TryRemove(procEnd.ProcessId, out var startTime))
                {
                    var duration = DateTime.Now - startTime;
                    _terminatedProcessLifetimes.Add(duration);
                    Console.WriteLine($"   -> Processo encerrado: '{procEnd.Imagem}' (PID: {procEnd.ProcessId}) - Duração: {duration:hh\\:mm\\:ss}");
                }
                else
                {
                    Console.WriteLine($"   -> Processo encerrado: '{procEnd.Imagem}' (PID: {procEnd.ProcessId})");
                }
                
                _store.InsertEvent(_sessionId, procEnd);
            }
        }

        /// <summary>
        /// Extrai o ID do processo de um objeto de dados de evento.
        /// </summary>
        /// <param name="data">O objeto de dados do evento.</param>
        /// <returns>O ID do processo, ou null se não puder ser extraído.</returns>
        private int? GetPidFromEvent(object? data)
        {
            if (data == null) return null;
            
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
        /// Obtém estatísticas dos processos monitorados.
        /// </summary>
        public object GetProcessStatistics()
        {
            var activeProcesses = _monitoredPids.Count;
            
            TimeSpan averageLifetimeTerminated = TimeSpan.Zero;
            if (!_terminatedProcessLifetimes.IsEmpty)
            {
                double avgTicks = _terminatedProcessLifetimes.Select(ts => ts.Ticks).Average();
                averageLifetimeTerminated = TimeSpan.FromTicks((long)avgTicks);
            }

            return new
            {
                ProcessosAtivos = activeProcesses,
                TotalProcessosRastreados = _totalProcessesTracked,
                ProcessosEncerrados = _terminatedProcessLifetimes.Count,
                TempoMedioDeVidaEncerrados = averageLifetimeTerminated.ToString(@"hh\:mm\:ss"),
                ProcessosAtivosDetalhados = _monitoredPids.Select(kvp => new
                {
                    PID = kvp.Key,
                    Imagem = kvp.Value,
                    Iniciado = _processStartTimes.TryGetValue(kvp.Key, out var startTime) ? startTime.ToString("o") : "N/A",
                    DuracaoAtual = _processStartTimes.TryGetValue(kvp.Key, out var start) ? (DateTime.Now - start).ToString(@"hh\:mm\:ss") : "N/A"
                }).ToList()
            };
        }
    }
}
