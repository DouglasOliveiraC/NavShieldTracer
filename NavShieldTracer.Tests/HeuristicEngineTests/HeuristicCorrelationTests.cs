using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NavShieldTracer.ConsoleApp.Services;
using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using NavShieldTracer.Tests.Utils;
using Xunit;

namespace NavShieldTracer.Tests.HeuristicEngineTests;

/// <summary>
/// Exercita o motor heurístico com um volume elevado de técnicas catalogadas.
/// O teste cataloga até 1,5x o número aproximado de técnicas MITRE e, em seguida,
/// monitora os cinco executáveis que mais consomem RAM no momento. Para cada executável
/// todos os PIDs ativos são acompanhados em paralelo e consolidados como se fosse uma
/// única aplicação, permitindo observar o motor heurístico sob carga realista.
/// </summary>
public sealed class HeuristicCorrelationTests : IDisposable
{
    private const int MitreTechniqueCount = 400; // Aproximado
    private const int TargetTestCount = (int)(MitreTechniqueCount * 1.5);
    private const int MonitorDurationSeconds = 45;
    private const int TopExecutablesToMonitor = 5;

    private readonly string _testDbPath;
    private readonly SqliteEventStore _store;
    private readonly DatabaseSeeder _seeder;
    private readonly NavShieldAppService _appService;

    public HeuristicCorrelationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_heuristic_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_testDbPath);
        _seeder = new DatabaseSeeder(_store);
        _appService = new NavShieldAppService(_store);
    }

    [Fact]
    public async Task HeuristicEngineCorrelationAndPerformanceTest()
    {
        var suiteTimer = Stopwatch.StartNew();
        ReportFormatter.WriteSection("Iniciando Teste de Correlação do Motor Heurístico");

        var catalogSummary = PopulateCatalogedTests();
        var executableReports = await MonitorTopExecutablesAndAnalyze(catalogSummary.FinalTestCount);

        suiteTimer.Stop();

        var allSessions = executableReports.SelectMany(r => r.Sessions).ToList();

        var totalEvents = allSessions.Sum(r => r.TotalEvents);
        var totalSnapshots = allSessions.Sum(r => r.TotalSnapshots);
        var totalMatches = allSessions.Sum(r => r.TotalMatches);
        var totalComparisons = allSessions.Sum(r => r.TechniqueComparisons);
        var totalDurationSeconds = allSessions.Sum(r => r.Duration.TotalSeconds);
        var totalCpuSeconds = allSessions.Sum(r => r.ProcessCpuDeltaSeconds ?? 0);
        var totalMemoryDelta = allSessions.Sum(r => r.ProcessMemoryDeltaMb ?? 0);
        var totalMemoryReduction = allSessions.Sum(r => r.ProcessMemoryReductionMb ?? 0);

        ReportFormatter.WriteSection("Resumo Execução Teste Heurístico",
            ("Tempo total (s)", suiteTimer.Elapsed.TotalSeconds.ToString("F1")),
            ("Executáveis monitorados", executableReports.Count.ToString()),
            ("Sessões individuais", allSessions.Count.ToString()),
            ("Eventos analisados", totalEvents.ToString("N0")),
            ("Snapshots gerados", totalSnapshots.ToString("N0")),
            ("Tentativas de correlação", totalComparisons.ToString("N0")),
            ("Matches encontrados", totalMatches.ToString("N0")),
            ("Duração acumulada (s)", totalDurationSeconds.ToString("F1")),
            ("CPU total dos alvos (s)", totalCpuSeconds.ToString("F2")),
            ("Incremento total de RAM (MB)", totalMemoryDelta.ToString("F2")),
            ("Redução total de RAM (MB)", totalMemoryReduction.ToString("F2"))
        );

        var sessionsWithErrors = allSessions.Where(r => !string.IsNullOrWhiteSpace(r.Error)).ToList();
        if (sessionsWithErrors.Count > 0)
        {
            ReportFormatter.WriteList("Sessões com erros",
                sessionsWithErrors.Select(r => $"{r.ExecutableName} (PID {r.Pid}): {r.Error}"));
        }

        ReportFormatter.WriteSection("Teste de Correlação do Motor Heurístico Concluído");
    }

    private CatalogPopulationSummary PopulateCatalogedTests()
    {
        var sw = Stopwatch.StartNew();
        ReportFormatter.WriteSection("População de Testes Catalogados");

        var existingTests = _store.ListarTestesAtomicos();
        var testsToCreate = TargetTestCount - existingTests.Count;
        var created = 0;

        if (testsToCreate > 0)
        {
            ReportFormatter.WriteList("Criando testes adicionais", new[] { $"Necessário: {testsToCreate} testes" });
            for (var i = 0; i < testsToCreate; i++)
            {
                var testId = _seeder.CriarTesteAtomico(
                    $"T{10000 + i}",
                    $"Simulated Test {i}",
                    $"Descrição para teste simulado {i}",
                    _seeder.Random.Next(20, 100));

                created++;

                var testeCompleto = _store.ObterTesteAtomicoCompleto(testId);
                if (testeCompleto is null)
                {
                    continue;
                }

                var teste = new TesteAtomico(
                    testeCompleto.Id,
                    testeCompleto.Numero,
                    testeCompleto.Nome,
                    testeCompleto.Descricao,
                    testeCompleto.DataExecucao,
                    testeCompleto.SessionId,
                    testeCompleto.TotalEventos
                );

                var normalizer = new CatalogNormalizer();
                var eventos = _store.ObterEventosDaSessao(teste.SessionId);
                var contexto = new NormalizationContext(teste, eventos);
                var resultado = normalizer.Normalize(contexto);
                _store.SalvarResultadoNormalizacao(resultado);
            }
        }

        var finalTests = _store.ListarTestesAtomicos();
        var finalTestCount = finalTests.Count;
        var totalEventosCatalogo = finalTests.Sum(t => t.TotalEventos);
        var mediaEventos = finalTests.Count > 0 ? finalTests.Average(t => t.TotalEventos) : 0;
        var minEventos = finalTests.Count > 0 ? finalTests.Min(t => t.TotalEventos) : 0;
        var maxEventos = finalTests.Count > 0 ? finalTests.Max(t => t.TotalEventos) : 0;
        sw.Stop();

        ReportFormatter.WriteSection("Status da População",
            ("Testes iniciais", existingTests.Count.ToString("N0")),
            ("Testes criados", created.ToString("N0")),
            ("Testes finais", finalTestCount.ToString("N0")),
            ("Eventos totais catalogados", totalEventosCatalogo.ToString("N0")),
            ("Eventos por teste (média)", mediaEventos.ToString("N1")),
            ("Eventos por teste (mínimo)", minEventos.ToString("N0")),
            ("Eventos por teste (máximo)", maxEventos.ToString("N0")),
            ("Tempo populando (s)", sw.Elapsed.TotalSeconds.ToString("F2"))
        );

        Assert.True(finalTestCount >= TargetTestCount, "Não foi possível criar o número alvo de testes.");

        return new CatalogPopulationSummary(
            CreatedTests: created,
            FinalTestCount: finalTestCount,
            TotalEvents: totalEventosCatalogo,
            AverageEventsPerTest: mediaEventos,
            PopulationDuration: sw.Elapsed
        );
    }

    private async Task<IReadOnlyList<HeuristicExecutableReport>> MonitorTopExecutablesAndAnalyze(int finalTestCount)
    {
        ReportFormatter.WriteSection("Monitorando Processos Top e Analisando");

        var topSnapshots = _appService.GetTopProcesses(TopExecutablesToMonitor * 5);
        var groupedExecutables = topSnapshots
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Name = g.Key,
                TotalMemoryMb = g.Sum(x => x.MemoryMb)
            })
            .OrderByDescending(g => g.TotalMemoryMb)
            .Take(TopExecutablesToMonitor)
            .ToList();

        if (groupedExecutables.Count == 0)
        {
            ReportFormatter.WriteList("Aviso", new[] { "Nenhum processo top encontrado para monitorar." });
            return Array.Empty<HeuristicExecutableReport>();
        }

        ReportFormatter.WriteList(
            "Executáveis selecionados",
            groupedExecutables.Select(g => $"{g.Name} (RAM total {g.TotalMemoryMb:F0} MB)"));

        var reportsByExecutable = new Dictionary<string, HeuristicExecutableReport>(StringComparer.OrdinalIgnoreCase);
        var monitoringTasks = new List<Task<HeuristicSessionReport>>();

        foreach (var executable in groupedExecutables)
        {
            Process[] processesForName;
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(executable.Name);
                processesForName = Process.GetProcessesByName(baseName);
            }
            catch (Exception ex)
            {
                ReportFormatter.WriteList("Aviso", new[]
                {
                    $"Não foi possível enumerar processos para {executable.Name}: {ex.Message}"
                });
                continue;
            }

            if (processesForName.Length == 0)
            {
                ReportFormatter.WriteList("Aviso", new[]
                {
                    $"Nenhum processo ativo encontrado para {executable.Name}."
                });
                continue;
            }

            var baselines = new List<ProcessBaseline>(processesForName.Length);
            foreach (var process in processesForName)
            {
                var pid = process.Id;
                var initialMemory = SafeWorkingSetMb(process);
                var initialCpu = SafeTotalProcessorSeconds(process);
                baselines.Add(new ProcessBaseline(pid, initialMemory, initialCpu));
                process.Dispose();
            }

            if (baselines.Count == 0)
            {
                continue;
            }

            baselines.Sort((a, b) => b.InitialMemoryMb.CompareTo(a.InitialMemoryMb));

            var totalGroupRam = baselines.Sum(b => b.InitialMemoryMb);
            ReportFormatter.WriteSection($"Executável selecionado - {executable.Name}",
                ("Processos agrupados", baselines.Count.ToString()),
                ("RAM total (MB)", totalGroupRam.ToString("F2")),
                ("RAM do maior PID (MB)", baselines[0].InitialMemoryMb.ToString("F2")));

            ReportFormatter.WriteList(
                "PIDs monitorados",
                baselines.Take(10).Select(b => $"PID {b.Pid} ({b.InitialMemoryMb:F2} MB)"));

            reportsByExecutable[executable.Name] = new HeuristicExecutableReport(executable.Name);

            foreach (var baseline in baselines)
            {
                monitoringTasks.Add(MonitorProcessAsync(executable.Name, baseline, finalTestCount));
            }
        }

        if (monitoringTasks.Count == 0)
        {
            return reportsByExecutable.Values.ToList();
        }

        var sessionReports = await Task.WhenAll(monitoringTasks);

        foreach (var sessionReport in sessionReports)
        {
            if (!reportsByExecutable.TryGetValue(sessionReport.ExecutableName, out var report))
            {
                report = new HeuristicExecutableReport(sessionReport.ExecutableName);
                reportsByExecutable[sessionReport.ExecutableName] = report;
            }

            report.Sessions.Add(sessionReport);
        }

        foreach (var report in reportsByExecutable.Values)
        {
            if (report.Sessions.Count > 0)
            {
                report.EmitReport();
            }
        }

        return reportsByExecutable.Values
            .Where(r => r.Sessions.Count > 0)
            .ToList();
    }

    private async Task<HeuristicSessionReport> MonitorProcessAsync(
        string executableName,
        ProcessBaseline baseline,
        int finalTestCount)
    {
        var sessionReport = HeuristicSessionReport.Create(
            executableName,
            baseline.Pid,
            baseline.InitialMemoryMb,
            baseline.InitialCpuSeconds);

        MonitoringSession? session = null;
        MonitoringResult? result = null;
        Exception? failure = null;

        using var localStore = new SqliteEventStore(_testDbPath);
        using var localService = new NavShieldAppService(localStore);

        try
        {
            session = localService.StartMonitoring(executableName, baseline.Pid);
            await Task.Delay(TimeSpan.FromSeconds(MonitorDurationSeconds));
            result = await localService.StopActiveSessionAsync();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            sessionReport.FinalizeTimings();
        }

        sessionReport.Error = failure?.Message;

        if (result is not null)
        {
            sessionReport.ApplyMonitoringResult(result);
        }

        if (session is not null)
        {
            sessionReport.SessionId = session.SessionId;

            try
            {
                var stats = localService.ObterEstatisticasSessao(session.SessionId);
                sessionReport.ApplySessionStats(stats);
            }
            catch
            {
                // Ignorar falhas de estatística
            }

            try
            {
                var eventos = localStore.ObterEventosDaSessao(session.SessionId);
                if (eventos.Count > 0)
                {
                    sessionReport.TotalEvents = Math.Max(sessionReport.TotalEvents, eventos.Count);

                    if (sessionReport.EventHistogram.Count == 0)
                    {
                        sessionReport.EventHistogram = eventos
                            .GroupBy(e => e.EventId)
                            .ToDictionary(g => g.Key, g => g.Count());

                        sessionReport.TopEventIds = sessionReport.EventHistogram
                            .OrderByDescending(kvp => kvp.Value)
                            .Take(5)
                            .Select(kvp => $"ID {kvp.Key} ({kvp.Value})")
                            .ToList();
                    }
                }
            }
            catch
            {
                // Ignorar coleta de eventos
            }

            try
            {
                var snapshots = localStore.ObterSnapshotsDeSimilaridade(session.SessionId);
                sessionReport.TotalSnapshots = snapshots.Count;

                if (snapshots.Count > 0)
                {
                    var matches = snapshots.SelectMany(s => s.Matches).ToList();
                    sessionReport.TotalMatches = matches.Count;
                    sessionReport.UniqueTechniques = matches
                        .Select(m => m.TechniqueId)
                        .Distinct()
                        .Count();

                    if (matches.Count > 0)
                    {
                        sessionReport.TopSimilarity = matches.Max(m => m.Similarity);
                        sessionReport.AverageSimilarity = matches.Average(m => m.Similarity);
                        sessionReport.MatchesByThreatLevel = matches
                            .GroupBy(m => m.ThreatLevel)
                            .ToDictionary(g => g.Key, g => g.Count());
                    }
                }
            }
            catch
            {
                // Ignorar falhas ao obter snapshots
            }
        }

        sessionReport.CaptureProcessEndState();

        if (sessionReport.TotalSnapshots > 0)
        {
            sessionReport.TechniqueComparisons = (long)finalTestCount * sessionReport.TotalSnapshots;
        }

        return sessionReport;
    }

    public void Dispose()
    {
        _appService.Dispose();
        _store.Dispose();

        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static double SafeWorkingSetMb(Process process)
    {
        try
        {
            return process.WorkingSet64 / 1024d / 1024d;
        }
        catch
        {
            return 0;
        }
    }

    private static double? SafeTotalProcessorSeconds(Process process)
    {
        try
        {
            return process.TotalProcessorTime.TotalSeconds;
        }
        catch
        {
            return null;
        }
    }

    private static Process? TryGetProcess(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch
        {
            return null;
        }
    }

    private sealed record CatalogPopulationSummary(
        int CreatedTests,
        int FinalTestCount,
        int TotalEvents,
        double AverageEventsPerTest,
        TimeSpan PopulationDuration);

    private sealed record ProcessBaseline(
        int Pid,
        double InitialMemoryMb,
        double? InitialCpuSeconds);

    private sealed class HeuristicExecutableReport
    {
        public HeuristicExecutableReport(string executableName)
        {
            ExecutableName = executableName;
        }

        public string ExecutableName { get; }
        public List<HeuristicSessionReport> Sessions { get; } = new();

        public void EmitReport()
        {
            var totalSessions = Sessions.Count;
            var totalEvents = Sessions.Sum(s => s.TotalEvents);
            var totalSnapshots = Sessions.Sum(s => s.TotalSnapshots);
            var totalMatches = Sessions.Sum(s => s.TotalMatches);
            var totalComparisons = Sessions.Sum(s => s.TechniqueComparisons);
            var totalDuration = Sessions.Sum(s => s.Duration.TotalSeconds);
            var avgDuration = totalSessions > 0 ? totalDuration / totalSessions : 0;
            var totalCpu = Sessions.Sum(s => s.ProcessCpuDeltaSeconds ?? 0);
            var totalMemoryIncrease = Sessions.Sum(s => s.ProcessMemoryDeltaMb ?? 0);
            var totalMemoryReduction = Sessions.Sum(s => s.ProcessMemoryReductionMb ?? 0);
            var avgInitialMemory = totalSessions > 0 ? Sessions.Average(s => s.InitialProcessMemoryMb) : 0;
            var peakMemory = Sessions
                .Select(s => s.PeakWorkingSetMb ?? s.InitialProcessMemoryMb)
                .DefaultIfEmpty(0)
                .Max();
            var sessionsWithSnapshots = Sessions.Count(s => s.TotalSnapshots > 0);
            var sessionsWithErrors = Sessions.Count(s => !string.IsNullOrWhiteSpace(s.Error));

            ReportFormatter.WriteSection($"Resumo por Executável - {ExecutableName}",
                ("Sessões monitoradas", totalSessions.ToString()),
                ("Eventos coletados (total)", totalEvents.ToString("N0")),
                ("Snapshots heurísticos", totalSnapshots.ToString("N0")),
                ("Matches encontrados", totalMatches.ToString("N0")),
                ("Tentativas de correlação", totalComparisons.ToString("N0")),
                ("Duração total (s)", totalDuration.ToString("F1")),
                ("Duração média (s)", avgDuration.ToString("F1")),
                ("Memória inicial média (MB)", avgInitialMemory.ToString("F2")),
                ("Pico de memória (MB)", peakMemory.ToString("F2")),
                ("Incremento total de RAM (MB)", totalMemoryIncrease.ToString("F2")),
                ("Redução total de RAM (MB)", totalMemoryReduction.ToString("F2")),
                ("CPU total dos alvos (s)", totalCpu.ToString("F2")),
                ("Sessões com snapshots", sessionsWithSnapshots.ToString()),
                ("Sessões com erro", sessionsWithErrors.ToString())
            );

            var aggregatedEventCounts = new Dictionary<int, int>();
            foreach (var session in Sessions)
            {
                foreach (var kvp in session.EventHistogram)
                {
                    aggregatedEventCounts[kvp.Key] = aggregatedEventCounts.TryGetValue(kvp.Key, out var current)
                        ? current + kvp.Value
                        : kvp.Value;
                }
            }

            if (aggregatedEventCounts.Count > 0)
            {
                var topEvents = aggregatedEventCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => $"ID {kvp.Key} ({kvp.Value})")
                    .ToList();
                ReportFormatter.WriteList("Eventos mais frequentes", topEvents);
            }

            var topIps = Sessions
                .SelectMany(s => s.TopIps)
                .GroupBy(ip => ip, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            if (topIps.Count > 0)
            {
                ReportFormatter.WriteList("IPs observados (top 5)", topIps);
            }

            var topDomains = Sessions
                .SelectMany(s => s.TopDomains)
                .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            if (topDomains.Count > 0)
            {
                ReportFormatter.WriteList("Domínios observados (top 5)", topDomains);
            }

            var topProcesses = Sessions
                .SelectMany(s => s.ProcessesCriados)
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            if (topProcesses.Count > 0)
            {
                ReportFormatter.WriteList("Processos criados (top 5)", topProcesses);
            }

            foreach (var session in Sessions)
            {
                session.EmitReport();
            }
        }
    }

    private sealed class HeuristicSessionReport
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly double initialCpuSeconds;

        private HeuristicSessionReport(string executableName, int pid, double initialMemoryMb, double? initialCpuSeconds)
        {
            ExecutableName = executableName;
            Pid = pid;
            InitialProcessMemoryMb = initialMemoryMb;
            this.initialCpuSeconds = initialCpuSeconds ?? 0;
            InitialCpuAvailable = initialCpuSeconds.HasValue;
        }

        public static HeuristicSessionReport Create(string executableName, int pid, double initialMemoryMb, double? initialCpuSeconds)
            => new(executableName, pid, initialMemoryMb, initialCpuSeconds);

        public string ExecutableName { get; }
        public int Pid { get; }
        public int? SessionId { get; set; }
        public TimeSpan Duration { get; private set; }

        public int TotalEvents { get; set; }
        public int UniqueEventTypes { get; set; }
        public int ActiveProcesses { get; set; }
        public int TotalTracked { get; set; }
        public int ProcessLogCount { get; set; }
        public int TotalMatches { get; set; }
        public int UniqueTechniques { get; set; }
        public int TotalSnapshots { get; set; }
        public long TechniqueComparisons { get; set; }

        public Dictionary<int, int> EventHistogram { get; set; } = new();
        public List<string> TopEventIds { get; set; } = new();
        public List<string> TopIps { get; set; } = new();
        public List<string> TopDomains { get; set; } = new();
        public List<string> ProcessesCriados { get; set; } = new();

        public double InitialProcessMemoryMb { get; }
        public double? FinalProcessMemoryMb { get; private set; }
        public double? ProcessMemoryDeltaMb { get; private set; }
        public double? ProcessMemoryReductionMb { get; private set; }
        public double? PeakWorkingSetMb { get; private set; }
        public double? ProcessCpuDeltaSeconds { get; private set; }
        public bool InitialCpuAvailable { get; }
        public bool ProcessEnded { get; private set; }

        public double? TopSimilarity { get; set; }
        public double? AverageSimilarity { get; set; }
        public Dictionary<ThreatSeverityTarja, int>? MatchesByThreatLevel { get; set; }
        public string? Error { get; set; }

        public void FinalizeTimings()
        {
            _stopwatch.Stop();
            Duration = _stopwatch.Elapsed;
        }

        public void ApplyMonitoringResult(MonitoringResult result)
        {
            TotalEvents = Math.Max(TotalEvents, result.TotalEventos);
            ActiveProcesses = result.Statistics.ProcessosAtivos;
            TotalTracked = result.Statistics.TotalProcessosRastreados;
            ProcessLogCount = result.Logs.Count;
        }

        public void ApplySessionStats(SessionStats stats)
        {
            EventHistogram = stats.EventosPorTipo ?? new Dictionary<int, int>();
            UniqueEventTypes = EventHistogram.Count;
            TopEventIds = EventHistogram
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => $"ID {kvp.Key} ({kvp.Value})")
                .ToList();
            TopIps = stats.TopIps.Take(5).ToList();
            TopDomains = stats.TopDomains.Take(5).ToList();
            ProcessesCriados = stats.ProcessosCriados.Take(5).ToList();
        }

        public void CaptureProcessEndState()
        {
            var process = TryGetProcess(Pid);
            if (process is null)
            {
                ProcessEnded = true;
                FinalProcessMemoryMb = null;
                ProcessMemoryDeltaMb = null;
                ProcessMemoryReductionMb = null;
                PeakWorkingSetMb = InitialProcessMemoryMb;
                ProcessCpuDeltaSeconds = null;
                return;
            }

            try
            {
                process.Refresh();

                var finalMemory = SafeWorkingSetMb(process);
                FinalProcessMemoryMb = finalMemory;
                var memoryDelta = finalMemory - InitialProcessMemoryMb;
                ProcessMemoryDeltaMb = memoryDelta >= 0 ? memoryDelta : 0;
                ProcessMemoryReductionMb = memoryDelta < 0 ? Math.Abs(memoryDelta) : 0;
                PeakWorkingSetMb = Math.Max(InitialProcessMemoryMb, finalMemory);

                var finalCpu = SafeTotalProcessorSeconds(process);
                if (InitialCpuAvailable && finalCpu.HasValue)
                {
                    var delta = finalCpu.Value - initialCpuSeconds;
                    if (delta >= 0)
                    {
                        ProcessCpuDeltaSeconds = delta;
                    }
                }

                ProcessEnded = false;
            }
            finally
            {
                process.Dispose();
            }
        }

        public void EmitReport()
        {
            var lines = new List<(string Label, string Value)>
            {
                ("Executável", ExecutableName),
                ("PID", Pid.ToString()),
                ("Sessão ID", SessionId?.ToString() ?? "n/d"),
                ("Duração (s)", Duration.TotalSeconds.ToString("F1")),
                ("Eventos coletados", TotalEvents.ToString("N0")),
                ("Tipos de evento distintos", UniqueEventTypes.ToString("N0")),
                ("Snapshots heurísticos", TotalSnapshots.ToString("N0")),
                ("Matches encontrados", TotalMatches.ToString("N0")),
                ("Tentativas de correlação", TechniqueComparisons.ToString("N0")),
                ("Processos ativos", ActiveProcesses.ToString()),
                ("Processos rastreados", TotalTracked.ToString()),
                ("Logs coletados", ProcessLogCount.ToString("N0")),
                ("Memória inicial (MB)", InitialProcessMemoryMb.ToString("F2")),
                ("Memória final (MB)", FinalProcessMemoryMb?.ToString("F2") ?? "n/d"),
                ("Incremento RAM (MB)", ProcessMemoryDeltaMb?.ToString("F2") ?? "n/d"),
                ("Redução RAM (MB)", ProcessMemoryReductionMb?.ToString("F2") ?? "n/d"),
                ("CPU alvo (s)", ProcessCpuDeltaSeconds?.ToString("F2") ?? "n/d"),
                ("Processo permaneceu ativo", (!ProcessEnded).ToString())
            };

            if (PeakWorkingSetMb.HasValue)
            {
                lines.Add(("Pico RAM (MB)", PeakWorkingSetMb.Value.ToString("F2")));
            }

            if (TopSimilarity.HasValue)
            {
                lines.Add(("Similaridade máxima", $"{TopSimilarity.Value:P2}"));
            }

            if (AverageSimilarity.HasValue)
            {
                lines.Add(("Similaridade média", $"{AverageSimilarity.Value:P2}"));
            }

            if (!string.IsNullOrWhiteSpace(Error))
            {
                lines.Add(("Erro", Error!));
            }
            else if (TotalSnapshots == 0)
            {
                lines.Add(("Observação", "Nenhum snapshot de similaridade gerado."));
            }

            ReportFormatter.WriteSection("Resumo Sessão Heurística", lines.ToArray());

            if (TopEventIds.Count > 0)
            {
                ReportFormatter.WriteList("Event IDs mais frequentes", TopEventIds);
            }

            if (TopIps.Count > 0)
            {
                ReportFormatter.WriteList("IPs observados", TopIps);
            }

            if (TopDomains.Count > 0)
            {
                ReportFormatter.WriteList("Domínios observados", TopDomains);
            }

            if (ProcessesCriados.Count > 0)
            {
                ReportFormatter.WriteList("Processos criados", ProcessesCriados);
            }

            if (MatchesByThreatLevel is { Count: > 0 })
            {
                var threatLines = MatchesByThreatLevel
                    .OrderByDescending(kvp => kvp.Value)
                    .Select(kvp => $"{kvp.Key}: {kvp.Value}")
                    .ToList();
                ReportFormatter.WriteList("Matches por tarja", threatLines);
            }
        }
    }
}

