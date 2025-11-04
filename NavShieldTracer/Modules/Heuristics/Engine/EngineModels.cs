using System;
using System.Collections.Generic;
using System.Linq;
using NavShieldTracer.Modules.Heuristics.Normalization;

namespace NavShieldTracer.Modules.Heuristics.Engine
{
    /// <summary>
    /// Representa um match de similaridade entre uma sessão e um teste catalogado.
    /// </summary>
    /// <param name="TestId">ID do teste catalogado correspondente.</param>
    /// <param name="TechniqueId">Número da técnica MITRE (ex: T1055).</param>
    /// <param name="TechniqueName">Nome da técnica (ex: Process Injection).</param>
    /// <param name="Tactic">Tática MITRE (ex: Privilege Escalation).</param>
    /// <param name="Similarity">Score de similaridade total (0.0 a 1.0).</param>
    /// <param name="ThreatLevel">Nível de ameaça da técnica.</param>
    /// <param name="Confidence">Nível de confiança (high/medium/low).</param>
    /// <param name="MatchedEventIds">IDs dos eventos que formaram o match.</param>
    /// <param name="Dimensions">Scores detalhados por dimensão.</param>
    public record SimilarityMatch(
        int TestId,
        string TechniqueId,
        string TechniqueName,
        string Tactic,
        double Similarity,
        ThreatSeverityTarja ThreatLevel,
        string Confidence,
        IReadOnlyList<int> MatchedEventIds,
        SimilarityDimensions Dimensions
    );

    /// <summary>
    /// Scores de similaridade detalhados por dimensão.
    /// </summary>
    /// <param name="HistogramSimilarity">D1: Similaridade do histograma de eventos (0.0-1.0).</param>
    /// <param name="CriticalEventsScore">D2: Score de eventos críticos presentes (0.0-1.0).</param>
    /// <param name="TemporalScore">D3: Score de padrão temporal (0.0-1.0).</param>
    /// <param name="ContextScore">D4: Score de contexto operacional (0.0-1.0).</param>
    public record SimilarityDimensions(
        double HistogramSimilarity,
        double CriticalEventsScore,
        double TemporalScore,
        double ContextScore
    );

    /// <summary>
    /// Descreve o padrão esperado de um evento crítico normalizado.
    /// </summary>
    /// <param name="EventId">ID do evento.</param>
    /// <param name="RelativeSeconds">Offset em segundos em relação ao primeiro evento.</param>
    internal record CoreEventPattern(int EventId, double? RelativeSeconds);

    /// <summary>
    /// Snapshot de análise de similaridade em um momento específico.
    /// </summary>
    /// <param name="SessionId">ID da sessão monitorada.</param>
    /// <param name="SnapshotAt">Timestamp do snapshot.</param>
    /// <param name="Matches">Lista de matches detectados.</param>
    /// <param name="SessionThreatLevel">Nível de ameaça da sessão neste momento.</param>
    /// <param name="EventCountAtSnapshot">Quantidade de eventos capturados até este momento.</param>
    /// <param name="ActiveProcessesCount">Quantidade de processos ativos.</param>
    public record SessionSnapshot(
        int SessionId,
        DateTime SnapshotAt,
        IReadOnlyList<SimilarityMatch> Matches,
        ThreatSeverityTarja SessionThreatLevel,
        int EventCountAtSnapshot,
        int ActiveProcessesCount
    )
    {
        /// <summary>
        /// Retorna o match com maior similaridade, se existir.
        /// </summary>
        public SimilarityMatch? HighestMatch => Matches.Count > 0
            ? Matches.OrderByDescending(m => m.Similarity).First()
            : null;

        /// <summary>
        /// Retorna apenas matches acima do threshold especificado.
        /// </summary>
        public IEnumerable<SimilarityMatch> GetMatchesAboveThreshold(double threshold) =>
            Matches.Where(m => m.Similarity >= threshold);
    }

    /// <summary>
    /// Representa um alerta de mudança de nível de ameaça.
    /// </summary>
    /// <param name="SessionId">ID da sessão.</param>
    /// <param name="Timestamp">Momento do alerta.</param>
    /// <param name="PreviousLevel">Nível anterior (pode ser null na primeira classificação).</param>
    /// <param name="NewLevel">Novo nível de ameaça.</param>
    /// <param name="Reason">Razão da mudança.</param>
    /// <param name="TriggerTechniqueId">Técnica que causou a mudança.</param>
    /// <param name="TriggerSimilarity">Similaridade da técnica que causou a mudança.</param>
    /// <param name="SnapshotId">ID do snapshot relacionado.</param>
    public record ThreatAlert(
        int SessionId,
        DateTime Timestamp,
        ThreatSeverityTarja? PreviousLevel,
        ThreatSeverityTarja NewLevel,
        string Reason,
        string? TriggerTechniqueId,
        double? TriggerSimilarity,
        int? SnapshotId
    );

    /// <summary>
    /// Configuração do motor de análise.
    /// </summary>
    public class AnalysisConfiguration
    {
        /// <summary>
        /// Threshold mínimo de similaridade para considerar um match (padrão: 0.5).
        /// </summary>
        public double MinimumSimilarityThreshold { get; set; } = 0.5;

        /// <summary>
        /// Threshold para considerar match como "alta confiança" (padrão: 0.85).
        /// </summary>
        public double HighConfidenceThreshold { get; set; } = 0.85;

        /// <summary>
        /// Threshold para considerar match como "média confiança" (padrão: 0.70).
        /// </summary>
        public double MediumConfidenceThreshold { get; set; } = 0.70;

        /// <summary>
        /// Janela temporal padrão em minutos para análise (padrão: 5).
        /// </summary>
        public int DefaultTimeWindowMinutes { get; set; } = 5;

        /// <summary>
        /// Intervalo de análise em segundos (padrão: 10).
        /// </summary>
        public int AnalysisIntervalSeconds { get; set; } = 10;

        /// <summary>
        /// Peso da dimensão D1 - Histogram (padrão: 0.40).
        /// </summary>
        public double WeightHistogram { get; set; } = 0.40;

        /// <summary>
        /// Peso da dimensão D2 - Critical Events (padrão: 0.35).
        /// </summary>
        public double WeightCriticalEvents { get; set; } = 0.35;

        /// <summary>
        /// Peso da dimensão D3 - Temporal Pattern (padrão: 0.15).
        /// </summary>
        public double WeightTemporalPattern { get; set; } = 0.15;

        /// <summary>
        /// Peso da dimensão D4 - Context (padrão: 0.10).
        /// </summary>
        public double WeightContext { get; set; } = 0.10;

        /// <summary>
        /// Valida se os pesos somam 1.0.
        /// </summary>
        public bool AreWeightsValid()
        {
            var sum = WeightHistogram + WeightCriticalEvents + WeightTemporalPattern + WeightContext;
            return Math.Abs(sum - 1.0) < 0.001;
        }
    }

    /// <summary>
    /// Estatísticas de uma sessão de monitoramento.
    /// </summary>
    internal class SessionStatistics
    {
        public int TotalEvents { get; set; }
        public int UniqueEventTypes { get; set; }
        public int NetworkConnections { get; set; }
        public int FileOperations { get; set; }
        public int RegistryOperations { get; set; }
        public int ProcessesCreated { get; set; }
        public int ActiveProcesses { get; set; }
        public int ProcessTreeDepth { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<int, int> EventHistogram { get; set; } = new();
    }

    /// <summary>
    /// Contexto de uma técnica catalogada carregada para comparação.
    /// </summary>
    internal class CatalogedTechniqueContext
    {
        public int TestId { get; set; }
        public string TechniqueId { get; set; } = string.Empty;
        public string TechniqueName { get; set; } = string.Empty;
        public string Tactic { get; set; } = string.Empty;
        public ThreatSeverityTarja ThreatLevel { get; set; }
        public NormalizedFeatureVector FeatureVector { get; set; } = null!;
        public List<int> CoreEventIds { get; set; } = new();
        public List<CoreEventPattern> CoreEventPatterns { get; set; } = new();
    }
}
