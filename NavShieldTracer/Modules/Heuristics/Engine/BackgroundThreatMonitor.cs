using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Storage;

namespace NavShieldTracer.Modules.Heuristics.Engine
{
    /// <summary>
    /// Thread em background que executa análise contínua de ameaças em tempo real.
    /// Roda a cada intervalo configurado (padrão: 10 segundos).
    /// </summary>
    public class BackgroundThreatMonitor : IDisposable
    {
        private readonly SqliteEventStore _store;
        private readonly SimilarityEngine _similarityEngine;
        private readonly SessionThreatClassifier _classifier;
        private readonly AnalysisConfiguration _config;
        private readonly int _sessionId;

        private Task? _monitoringTask;
        private CancellationTokenSource? _cancellationSource;
        private bool _disposed;

        // Cache de técnicas catalogadas
        private List<CatalogedTechniqueContext>? _cachedTechniques;
        private DateTime _cacheLoadedAt;

        // Estado da sessão
        private ThreatSeverityTarja? _currentThreatLevel;
        private int _snapshotCount;
        private int _alertCount;

        /// <summary>
        /// Evento disparado quando um novo snapshot é gerado.
        /// </summary>
        public event EventHandler<SessionSnapshot>? SnapshotGenerated;

        /// <summary>
        /// Evento disparado quando um alerta de mudança de nível é gerado.
        /// </summary>
        public event EventHandler<ThreatAlert>? AlertGenerated;

        /// <summary>
        /// Nível de ameaça atual da sessão.
        /// </summary>
        public ThreatSeverityTarja CurrentThreatLevel => _currentThreatLevel ?? ThreatSeverityTarja.Verde;

        /// <summary>
        /// Quantidade de snapshots gerados.
        /// </summary>
        public int SnapshotCount => _snapshotCount;

        /// <summary>
        /// Quantidade de alertas gerados.
        /// </summary>
        public int AlertCount => _alertCount;

        /// <summary>
        /// Indica se o monitor está rodando.
        /// </summary>
        public bool IsRunning => _monitoringTask != null && !_monitoringTask.IsCompleted;

        /// <summary>
        /// Inicializa o monitoramento em background para uma sessao espec�fica.
        /// </summary>
        /// <param name="store">Armazenamento de eventos que servir� de fonte.</param>
        /// <param name="sessionId">Identificador da sessao sendo analisada.</param>
        /// <param name="config">Configura�oes heur�sticas opcionais.</param>
        public BackgroundThreatMonitor(
            SqliteEventStore store,
            int sessionId,
            AnalysisConfiguration? config = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _sessionId = sessionId;
            _config = config ?? new AnalysisConfiguration();
            _similarityEngine = new SimilarityEngine(_config);
            _classifier = new SessionThreatClassifier(_config);
            _cacheLoadedAt = DateTime.MinValue;
        }

        /// <summary>
        /// Inicia o monitoramento em background.
        /// </summary>
        public void Start()
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Monitor já está rodando.");
            }

            _cancellationSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoop(_cancellationSource.Token), _cancellationSource.Token);
        }

        /// <summary>
        /// Para o monitoramento em background.
        /// </summary>
        public void Stop()
        {
            if (_cancellationSource != null && !_cancellationSource.IsCancellationRequested)
            {
                _cancellationSource.Cancel();
            }

            _monitoringTask?.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Loop principal de monitoramento.
        /// </summary>
        private async Task MonitoringLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await PerformAnalysis();
                    }
                    catch (Exception ex)
                    {
                        // Log erro mas continua monitoramento
                        Console.WriteLine($"[BackgroundMonitor] Erro na análise: {ex.Message}");
                    }

                    // Aguardar intervalo configurado
                    await Task.Delay(TimeSpan.FromSeconds(_config.AnalysisIntervalSeconds), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelamento normal
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BackgroundMonitor] Erro fatal: {ex.Message}");
            }
        }

        /// <summary>
        /// Executa um ciclo de análise.
        /// </summary>
        private async Task PerformAnalysis()
        {
            // 1. Carregar/atualizar cache de técnicas catalogadas
            await LoadCatalogedTechniques();

            if (_cachedTechniques == null || _cachedTechniques.Count == 0)
            {
                // Não há técnicas catalogadas ainda
                return;
            }

            // 2. Buscar eventos da sessão (janela deslizante)
            var timeWindow = DateTime.Now.AddMinutes(-_config.DefaultTimeWindowMinutes);
            var sessionEvents = _store.ObterEventosDaSessaoAPartirDe(_sessionId, timeWindow);

            if (sessionEvents.Count == 0)
            {
                // Ainda não há eventos para analisar
                return;
            }

            // 3. Calcular estatísticas da sessão
            var sessionStats = CalculateSessionStatistics(sessionEvents);

            // 4. Calcular similaridades com cada técnica catalogada (PARALELIZADO)
            // OTIMIZAÇÃO: Usar PLINQ para processar técnicas em paralelo em CPUs multi-core
            var matches = _cachedTechniques
                .AsParallel()
                .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount / 2)) // Usar metade dos cores
                .Select(technique => _similarityEngine.CalculateSimilarity(sessionStats, sessionEvents, technique))
                .Where(match => match != null)
                .Cast<SimilarityMatch>() // Safe cast pois Where já filtrou nulls
                .ToList();

            // 5. Classificar sessão
            var (newLevel, reason, triggerTechniqueId, triggerSimilarity) =
                _classifier.ClassifySession(matches, _currentThreatLevel);

            ThreatAlert? pendingAlert = null;
            if (_classifier.ShouldAlert(_currentThreatLevel, newLevel))
            {
                pendingAlert = new ThreatAlert(
                    _sessionId,
                    DateTime.Now,
                    _currentThreatLevel,
                    newLevel,
                    reason,
                    triggerTechniqueId,
                    triggerSimilarity,
                    null);
            }

            _currentThreatLevel = newLevel;

            var snapshot = new SessionSnapshot(
                _sessionId,
                DateTime.Now,
                matches,
                newLevel,
                sessionEvents.Count,
                sessionStats.ActiveProcesses
            );

            var snapshotId = await Task.Run(() => _store.SalvarSnapshot(snapshot));
            // Atualizar contador
            Interlocked.Increment(ref _snapshotCount);

            // Disparar evento
            SnapshotGenerated?.Invoke(this, snapshot);

            if (pendingAlert is not null)
            {
                var alertWithSnapshot = pendingAlert with { SnapshotId = snapshotId };
                await Task.Run(() => _store.SalvarAlerta(alertWithSnapshot));
                Interlocked.Increment(ref _alertCount);
                AlertGenerated?.Invoke(this, alertWithSnapshot);
            }
        }

        /// <summary>
        /// Carrega técnicas catalogadas do banco (com cache).
        /// </summary>
        private async Task LoadCatalogedTechniques()
        {
            // Recarregar cache a cada 5 minutos ou se ainda não foi carregado
            if (_cachedTechniques != null && (DateTime.Now - _cacheLoadedAt).TotalMinutes < 5)
            {
                return;
            }

            _cachedTechniques = await Task.Run(() => _store.CarregarTecnicasCatalogadas());
            _cacheLoadedAt = DateTime.Now;
        }

        /// <summary>
        /// Calcula estatísticas da sessão.
        /// </summary>
        private SessionStatistics CalculateSessionStatistics(IReadOnlyList<CatalogEventSnapshot> events)
        {
            var stats = new SessionStatistics
            {
                TotalEvents = events.Count,
                UniqueEventTypes = events.Select(e => e.EventId).Distinct().Count(),
                NetworkConnections = events.Count(e => e.EventId == 3),
                FileOperations = events.Count(e => e.EventId == 11 || e.EventId == 23 || e.EventId == 2),
                RegistryOperations = events.Count(e => e.EventId == 12 || e.EventId == 13 || e.EventId == 14),
                ProcessesCreated = events.Count(e => e.EventId == 1),
                ActiveProcesses = events.Select(e => e.ProcessId).Where(p => p.HasValue).Distinct().Count()
            };

            // Construir histograma
            foreach (var e in events)
            {
                if (e.EventId > 0)
                {
                    stats.EventHistogram.TryGetValue(e.EventId, out var count);
                    stats.EventHistogram[e.EventId] = count + 1;
                }
            }
            // Calcular profundidade da arvore de processos
            var parentMap = new Dictionary<int, int?>();
            foreach (var e in events)
            {
                if (!e.ProcessId.HasValue)
                {
                    continue;
                }

                var pid = e.ProcessId.Value;
                if (!parentMap.ContainsKey(pid))
                {
                    parentMap[pid] = e.ParentProcessId;
                }
            }

            var depthCache = new Dictionary<int, int>();
            int ResolveDepth(int pid, HashSet<int> trail)
            {
                if (depthCache.TryGetValue(pid, out var cached))
                {
                    return cached;
                }

                if (trail.Contains(pid))
                {
                    return 1;
                }

                trail.Add(pid);

                if (!parentMap.TryGetValue(pid, out var parent) || !parent.HasValue || parent.Value == pid)
                {
                    depthCache[pid] = 1;
                    trail.Remove(pid);
                    return 1;
                }

                if (!parentMap.ContainsKey(parent.Value))
                {
                    depthCache[pid] = 2;
                    trail.Remove(pid);
                    return 2;
                }

                var depth = 1 + ResolveDepth(parent.Value, trail);
                depthCache[pid] = depth;
                trail.Remove(pid);
                return depth;
            }

            var maxDepth = 0;
            foreach (var pid in parentMap.Keys)
            {
                var depth = ResolveDepth(pid, new HashSet<int>());
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                }
            }
            stats.ProcessTreeDepth = maxDepth;

            // Calcular duração
            var timestamps = events
                .Select(e => e.UtcTime ?? e.CaptureTime)
                .Where(t => t.HasValue)
                .Select(t => t!.Value)
                .OrderBy(t => t)
                .ToList();

            if (timestamps.Count >= 2)
            {
                stats.Duration = timestamps[^1] - timestamps[0];
            }

            return stats;
        }

        /// <summary>
        /// Libera recursos usados pelo monitor e interrompe o loop em execu�ao.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _cancellationSource?.Dispose();
            _disposed = true;
        }
    }
}

