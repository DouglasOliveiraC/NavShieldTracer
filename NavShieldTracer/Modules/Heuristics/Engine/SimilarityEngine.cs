using System;
using System.Collections.Generic;
using System.Linq;
using NavShieldTracer.Modules.Heuristics.Normalization;

namespace NavShieldTracer.Modules.Heuristics.Engine
{
    /// <summary>
    /// Motor de cálculo de similaridade multi-dimensional entre sessão ativa e testes catalogados.
    /// Implementa 4 dimensões: Histogram, Critical Events, Temporal Pattern, Context.
    /// </summary>
    internal class SimilarityEngine
    {
        private readonly AnalysisConfiguration _config;

        public SimilarityEngine(AnalysisConfiguration? config = null)
        {
            _config = config ?? new AnalysisConfiguration();

            if (!_config.AreWeightsValid())
            {
                throw new InvalidOperationException(
                    "Os pesos das dimensões devem somar 1.0. Verifique a configuração.");
            }
        }

        /// <summary>
        /// Calcula a similaridade entre uma sessão ativa e um teste catalogado.
        /// OTIMIZAÇÃO: Early termination - calcula dimensões por ordem de custo/importância.
        /// </summary>
        /// <param name="sessionStats">Estatísticas da sessão ativa.</param>
        /// <param name="sessionEvents">Eventos da sessão para análise temporal.</param>
        /// <param name="catalogedTechnique">Contexto da técnica catalogada.</param>
        /// <returns>Match de similaridade ou null se abaixo do threshold.</returns>
        public SimilarityMatch? CalculateSimilarity(
            SessionStatistics sessionStats,
            IReadOnlyList<CatalogEventSnapshot> sessionEvents,
            CatalogedTechniqueContext catalogedTechnique)
        {
            // OTIMIZAÇÃO: Calcular D2 primeiro (mais rápido e mais importante - 35% peso)
            // D2: Critical Events Presence Score (35%)
            var d2 = CalculateCriticalEventsScore(sessionStats.EventHistogram, catalogedTechnique.CoreEventIds);

            // EARLY TERMINATION 1: Se < 50% dos eventos críticos presentes, abortar imediatamente
            if (d2 < 0.5)
            {
                return null; // Padrão descartado - economia de 70% do processamento
            }

            // D1: Event Type Histogram Similarity (40%) - mais custoso, calcular depois
            var d1 = CalculateHistogramSimilarity(sessionStats.EventHistogram, catalogedTechnique.FeatureVector.EventTypeHistogram);

            // EARLY TERMINATION 2: Se D1 + D2 combinados já estão muito abaixo do threshold, abortar
            // Threshold ajustado: 75% do mínimo (considerando que D3 e D4 somam apenas 25%)
            var partialSimilarity = (d1 * _config.WeightHistogram) + (d2 * _config.WeightCriticalEvents);
            if (partialSimilarity < (_config.MinimumSimilarityThreshold * 0.75))
            {
                return null; // Economia de 30% do processamento restante
            }

            // D3: Temporal Pattern Matching Score (15%) - médio custo
            var d3 = CalculateTemporalScore(sessionEvents, catalogedTechnique.CoreEventPatterns);

            // D4: Context Similarity Score (10%) - baixo custo
            var d4 = CalculateContextScore(sessionStats, catalogedTechnique.FeatureVector);

            // Cálculo final ponderado
            var totalSimilarity =
                (d1 * _config.WeightHistogram) +
                (d2 * _config.WeightCriticalEvents) +
                (d3 * _config.WeightTemporalPattern) +
                (d4 * _config.WeightContext);

            // Verificar threshold mínimo final
            if (totalSimilarity < _config.MinimumSimilarityThreshold)
            {
                return null;
            }

            // Determinar confiança
            var confidence = totalSimilarity >= _config.HighConfidenceThreshold ? "high"
                : totalSimilarity >= _config.MediumConfidenceThreshold ? "medium"
                : "low";

            // Identificar eventos que formaram o match
            var matchedEventIds = GetMatchedEventIds(sessionEvents, catalogedTechnique.CoreEventIds);

            var dimensions = new SimilarityDimensions(d1, d2, d3, d4);

            return new SimilarityMatch(
                catalogedTechnique.TestId,
                catalogedTechnique.TechniqueId,
                catalogedTechnique.TechniqueName,
                catalogedTechnique.Tactic,
                totalSimilarity,
                catalogedTechnique.ThreatLevel,
                confidence,
                matchedEventIds,
                dimensions
            );
        }

        /// <summary>
        /// D1: Calcula similaridade do histograma de eventos usando Cosine Similarity.
        /// Fórmula: (A · B) / (||A|| × ||B||)
        /// </summary>
        private double CalculateHistogramSimilarity(
            Dictionary<int, int> sessionHistogram,
            IReadOnlyDictionary<int, int> catalogHistogram)
        {
            if (sessionHistogram.Count == 0 || catalogHistogram.Count == 0)
            {
                return 0.0;
            }

            // Criar espaço vetorial comum
            var allEventIds = sessionHistogram.Keys.Union(catalogHistogram.Keys).ToList();

            if (allEventIds.Count == 0)
            {
                return 0.0;
            }

            // Vetores normalizados
            var vectorA = allEventIds.Select(id => sessionHistogram.GetValueOrDefault(id, 0)).ToArray();
            var vectorB = allEventIds.Select(id => catalogHistogram.GetValueOrDefault(id, 0)).ToArray();

            // Produto escalar
            double dotProduct = 0;
            for (int i = 0; i < vectorA.Length; i++)
            {
                dotProduct += vectorA[i] * vectorB[i];
            }

            // Normas (magnitudes)
            var normA = Math.Sqrt(vectorA.Sum(v => v * v));
            var normB = Math.Sqrt(vectorB.Sum(v => v * v));

            if (normA == 0 || normB == 0)
            {
                return 0.0;
            }

            return dotProduct / (normA * normB);
        }

        /// <summary>
        /// D2: Calcula score de presença de eventos críticos.
        /// Score = (Eventos Críticos Presentes) / (Total de Eventos Críticos no Teste)
        /// Aplica penalidade exponencial se menos de 50% estiverem presentes.
        /// </summary>
        private double CalculateCriticalEventsScore(
            Dictionary<int, int> sessionHistogram,
            List<int> requiredCriticalEventIds)
        {
            if (requiredCriticalEventIds.Count == 0)
            {
                return 1.0; // Teste sem eventos críticos definidos
            }

            int presentCount = requiredCriticalEventIds.Count(eventId => sessionHistogram.ContainsKey(eventId));
            double ratio = (double)presentCount / requiredCriticalEventIds.Count;

            // Penalidade exponencial para cobertura < 50%
            if (ratio < 0.5)
            {
                return 0.0; // Padrão descartado
            }

            if (ratio < 0.66)
            {
                return 0.5; // Penalidade
            }

            return ratio; // Score linear para >= 66%
        }

        /// <summary>
        /// D3: Calcula score de padrão temporal.
        /// Verifica se eventos ocorrem na sequência esperada e com intervalos similares.
        /// </summary>
        private double CalculateTemporalScore(
            IReadOnlyList<CatalogEventSnapshot> sessionEvents,
            IReadOnlyList<CoreEventPattern> corePatterns)
        {
            if (corePatterns.Count < 2)
            {
                return 1.0; // Nao ha padrao temporal a validar
            }

            var expectedOrder = corePatterns.Select(p => p.EventId).ToList();
            var expectedIds = new HashSet<int>(expectedOrder);

            var criticalEvents = sessionEvents
                .Where(e => expectedIds.Contains(e.EventId))
                .OrderBy(e => e.UtcTime ?? e.CaptureTime ?? DateTime.MinValue)
                .ToList();

            if (criticalEvents.Count < 2)
            {
                return 0.0; // Nao ha eventos suficientes para validar padrao temporal
            }

            int expectedIndex = 0;
            int correctOrderCount = 0;
            foreach (var eventId in criticalEvents.Select(e => e.EventId))
            {
                if (expectedIndex >= expectedOrder.Count)
                {
                    break;
                }

                if (eventId == expectedOrder[expectedIndex])
                {
                    correctOrderCount++;
                    expectedIndex++;
                }
            }

            double orderScore = expectedOrder.Count > 0
                ? (double)correctOrderCount / expectedOrder.Count
                : 0.0;

            var expectedIntervals = new List<double>();
            for (int i = 1; i < corePatterns.Count; i++)
            {
                var previous = corePatterns[i - 1].RelativeSeconds;
                var current = corePatterns[i].RelativeSeconds;

                if (previous.HasValue && current.HasValue)
                {
                    expectedIntervals.Add(Math.Max(0, current.Value - previous.Value));
                }
            }

            var observedIntervals = new List<double>();
            for (int i = 1; i < criticalEvents.Count; i++)
            {
                var prevTimestamp = criticalEvents[i - 1].UtcTime ?? criticalEvents[i - 1].CaptureTime;
                var currTimestamp = criticalEvents[i].UtcTime ?? criticalEvents[i].CaptureTime;

                if (prevTimestamp.HasValue && currTimestamp.HasValue)
                {
                    observedIntervals.Add(Math.Max(0, (currTimestamp.Value - prevTimestamp.Value).TotalSeconds));
                }
            }

            double intervalScore;
            if (expectedIntervals.Count == 0)
            {
                intervalScore = 1.0;
            }
            else if (observedIntervals.Count == 0)
            {
                intervalScore = 0.0;
            }
            else
            {
                var comparable = Math.Min(expectedIntervals.Count, observedIntervals.Count);
                var matches = 0;
                for (int i = 0; i < comparable; i++)
                {
                    var expected = expectedIntervals[i];
                    var observed = observedIntervals[i];
                    var tolerance = Math.Max(1.0, Math.Abs(expected) * 0.2);

                    if (Math.Abs(observed - expected) <= tolerance)
                    {
                        matches++;
                    }
                }

                intervalScore = comparable > 0 ? (double)matches / comparable : 0.0;
            }

            return (orderScore * 0.7) + (intervalScore * 0.3);
        }

        /// <summary>
        /// D4: Calcula score de contexto operacional.
        /// Compara features contextuais como profundidade da árvore, volume de operações, etc.
        /// </summary>
        private double CalculateContextScore(
            SessionStatistics sessionStats,
            NormalizedFeatureVector catalogFeatures)
        {
            var matches = new List<bool>();

            // Comparar profundidade da árvore de processos (± 1 nível)
            var treeDepthMatch = Math.Abs(sessionStats.ProcessTreeDepth - catalogFeatures.ProcessTreeDepth) <= 1;
            matches.Add(treeDepthMatch);

            // Comparar volume de conexões de rede (categorizado)
            var sessionNetworkCategory = CategorizeVolume(sessionStats.NetworkConnections);
            var catalogNetworkCategory = CategorizeVolume(catalogFeatures.NetworkConnectionsCount);
            matches.Add(sessionNetworkCategory == catalogNetworkCategory);

            // Comparar operações de arquivo
            var sessionFileCategory = CategorizeVolume(sessionStats.FileOperations);
            var catalogFileCategory = CategorizeVolume(catalogFeatures.FileOperationsCount);
            matches.Add(sessionFileCategory == catalogFileCategory);

            // Comparar operações de registro
            var sessionRegistryCategory = CategorizeVolume(sessionStats.RegistryOperations);
            var catalogRegistryCategory = CategorizeVolume(catalogFeatures.RegistryOperationsCount);
            matches.Add(sessionRegistryCategory == catalogRegistryCategory);

            // Score = proporção de features que fazem match
            return (double)matches.Count(m => m) / matches.Count;
        }

        /// <summary>
        /// Categoriza volume em baixo/médio/alto.
        /// </summary>
        private string CategorizeVolume(int count)
        {
            return count switch
            {
                0 => "none",
                <= 5 => "low",
                <= 20 => "medium",
                _ => "high"
            };
        }

        private Dictionary<int, int> BuildHistogram(IReadOnlyList<CatalogEventSnapshot> events)
        {
            var histogram = new Dictionary<int, int>();
            foreach (var e in events)
            {
                if (e.EventId > 0)
                {
                    histogram.TryGetValue(e.EventId, out var count);
                    histogram[e.EventId] = count + 1;
                }
            }
            return histogram;
        }

        /// <summary>
        /// Identifica IDs dos eventos que formaram o match.
        /// </summary>
        private List<int> GetMatchedEventIds(
            IReadOnlyList<CatalogEventSnapshot> events,
            List<int> coreEventIds)
        {
            return events
                .Where(e => coreEventIds.Contains(e.EventId))
                .Select(e => e.EventRowId)
                .Distinct()
                .ToList();
        }
    }
}




