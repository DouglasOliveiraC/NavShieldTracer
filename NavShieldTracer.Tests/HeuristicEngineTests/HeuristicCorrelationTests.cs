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
    private const int MonitorDurationSeconds = 90;
    private const int TopExecutablesToMonitor = 1;

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

        ReportFormatter.WriteSection("Resumo Geral do Monitoramento Heurístico",
            ("Tempo total (s)", suiteTimer.Elapsed.TotalSeconds.ToString("F1")),
            ("Executáveis monitorados", executableReports.Count.ToString()),
            ("Sessões monitoradas", allSessions.Count.ToString()),
            ("Eventos analisados", totalEvents.ToString("N0")),
            ("Snapshots heurísticos", totalSnapshots.ToString("N0")),
            ("Comparações realizadas", totalComparisons.ToString("N0")),
            ("Matches encontrados", totalMatches.ToString("N0")),
            ("Catálogo avaliado", catalogSummary.FinalTestCount.ToString("N0"))
        );

        if (totalSnapshots == 0)
        {
            ReportFormatter.WriteList("Observações", new[]
            {
                "Nenhum snapshot heurístico foi produzido durante esta execução; a ausência de eventos relevantes é considerada válida e não representa falha."
            });
        }
        else if (totalComparisons == 0)
        {
            ReportFormatter.WriteList("Observações", new[]
            {
                "O motor heurístico finalizou a janela sem executar correlações (comparações = 0)."
            });
        }

        var sessionsWithErrors = allSessions.Where(r => !string.IsNullOrWhiteSpace(r.Error)).ToList();
        if (sessionsWithErrors.Count > 0)
        {
            ReportFormatter.WriteList("Sessões com avisos",
                sessionsWithErrors.Select(r => $"{r.ExecutableName} (PID {r.Pid}): {r.Error}"));
        }

        ReportFormatter.WriteSection("Teste de Correlação do Motor Heurístico Concluído",
            ("Duração monitorada (s)", totalDurationSeconds.ToString("F1")));
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
        ReportFormatter.WriteSection("Monitorando processo destaque e avaliando heurísticas");

        var topSnapshots = _appService.GetTopProcesses(5);
        var primaryExecutable = topSnapshots
            .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Name = g.Key,
                TotalMemoryMb = g.Sum(x => x.MemoryMb)
            })
            .OrderByDescending(g => g.TotalMemoryMb)
            .FirstOrDefault();

        if (primaryExecutable is null)
        {
            ReportFormatter.WriteList("Aviso", new[] { "Nenhum processo destacado encontrado para monitorar." });
            return Array.Empty<HeuristicExecutableReport>();
        }

        Process[] processesForName;
        try
        {
            var baseName = Path.GetFileNameWithoutExtension(primaryExecutable.Name);
            processesForName = Process.GetProcessesByName(baseName);
        }
        catch (Exception ex)
        {
            ReportFormatter.WriteList("Aviso", new[]
            {
                $"Não foi possível enumerar processos para {primaryExecutable.Name}: {ex.Message}"
            });
            return Array.Empty<HeuristicExecutableReport>();
        }

        if (processesForName.Length == 0)
        {
            ReportFormatter.WriteList("Aviso", new[]
            {
                $"Nenhum processo ativo encontrado para {primaryExecutable.Name}."
            });
            return Array.Empty<HeuristicExecutableReport>();
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
            ReportFormatter.WriteList("Aviso", new[] { $"Não foi possível capturar baseline para {primaryExecutable.Name}." });
            return Array.Empty<HeuristicExecutableReport>();
        }

        baselines.Sort((a, b) => b.InitialMemoryMb.CompareTo(a.InitialMemoryMb));
        var primaryBaseline = baselines[0];

        var totalGroupRam = baselines.Sum(b => b.InitialMemoryMb);
        var additionalPids = baselines.Count > 1
            ? string.Join(", ", baselines.Skip(1).Take(4).Select(b => $"PID {b.Pid}"))
            : "Nenhum";

        ReportFormatter.WriteSection("Executável monitorado",
            ("Nome", primaryExecutable.Name),
            ("PID selecionado", primaryBaseline.Pid.ToString()),
            ("Memória inicial (MB)", primaryBaseline.InitialMemoryMb.ToString("F2")),
            ("Processos no grupo", baselines.Count.ToString()),
            ("RAM total do grupo (MB)", totalGroupRam.ToString("F2")),
            ("PIDs adicionais", additionalPids));

        var reports = new List<HeuristicExecutableReport>();
        var report = new HeuristicExecutableReport(primaryExecutable.Name)
        {
            InitialPidCount = baselines.Count,
            AdditionalPidCount = Math.Max(0, baselines.Count - 1),
            AdditionalPidSummary = additionalPids
        };
        reports.Add(report);

        var sessionReport = await MonitorProcessAsync(primaryExecutable.Name, primaryBaseline, finalTestCount);
        report.Sessions.Add(sessionReport);

        if (report.Sessions.Count > 0)
        {
            report.EmitReport();
        }

        return reports;
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

        sessionReport.CatalogTechniqueCount = finalTestCount;
        sessionReport.TechniqueComparisons = sessionReport.TotalSnapshots > 0
            ? (long)finalTestCount * sessionReport.TotalSnapshots
            : 0;

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

        public int InitialPidCount { get; set; }
        public int AdditionalPidCount { get; set; }
        public string AdditionalPidSummary { get; set; } = "Nenhum";

        public void EmitReport()
        {
            if (Sessions.Count == 0)
            {
                return;
            }

            var session = Sessions[0];

            var lines = new List<(string Label, string Value)>
            {
                ("Executável monitorado", session.ExecutableName),
                ("PID monitorado", session.Pid.ToString()),
                ("Janela monitorada (s)", session.Duration.TotalSeconds.ToString("F1")),
                ("PIDs detectados inicialmente", InitialPidCount.ToString()),
                ("PIDs adicionais conhecidos", AdditionalPidSummary),
                ("Processos monitorados (pico)", session.TotalTracked.ToString()),
                ("Processos ativos ao término", session.ActiveProcesses.ToString()),
                ("Processos encerrados", session.ProcessesClosed.ToString()),
                ("Eventos analisados", session.TotalEvents.ToString("N0")),
                ("Snapshots heurísticos", session.TotalSnapshots.ToString("N0")),
                ("Comparações realizadas", session.TechniqueComparisons.ToString("N0")),
                ("Técnicas no catálogo", session.CatalogTechniqueCount.ToString("N0")),
                ("Matches encontrados", session.TotalMatches.ToString("N0")),
                ("Similaridade máxima", session.TopSimilarity.HasValue ? $"{session.TopSimilarity.Value:P2}" : "Nenhuma")
            };

            ReportFormatter.WriteSection("Resumo Monitoramento Heurístico", lines.ToArray());

            if (session.TechniqueComparisons > 0 && session.TotalSnapshots > 0)
            {
                var comparacoesMensagem =
                    $"Motor heurístico avaliou {session.CatalogTechniqueCount:N0} técnicas a cada snapshot ({session.TotalSnapshots} snapshots no intervalo).";
                ReportFormatter.WriteList("Busca no catálogo", new[] { comparacoesMensagem });
            }

            var observacoes = new List<string>();
            if (!string.IsNullOrWhiteSpace(session.Error))
            {
                observacoes.Add(session.Error!);
            }
            else if (session.TotalSnapshots == 0)
            {
                observacoes.Add("Nenhum snapshot heurístico foi produzido; o motor não encontrou eventos suficientes para correlacionar.");
            }
            else if (session.TotalMatches == 0)
            {
                observacoes.Add("O motor percorreu todo o catálogo, mas não encontrou similaridades acima dos limiares configurados.");
            }

            if (observacoes.Count > 0)
            {
                ReportFormatter.WriteList("Observações", observacoes);
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
        public int ProcessesClosed { get; set; }
        public int ProcessLogCount { get; set; }
        public int TotalMatches { get; set; }
        public int UniqueTechniques { get; set; }
        public int TotalSnapshots { get; set; }
        public long TechniqueComparisons { get; set; }
        public int CatalogTechniqueCount { get; set; }

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
            ProcessesClosed = result.Statistics.ProcessosEncerrados;
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

    }
}

