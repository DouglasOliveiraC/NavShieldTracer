using System;
using System.Collections.Generic;
using System.Linq;
using NavShieldTracer.Modules.Heuristics.Normalization;

namespace NavShieldTracer.Modules.Heuristics.Engine
{
    /// <summary>
    /// Classifica o nível de ameaça de uma sessão baseado nos matches detectados.
    /// Segue a doutrina MD-31-M-07 (Ministério da Defesa - Níveis de Alerta Cibernético).
    /// </summary>
    internal class SessionThreatClassifier
    {
        private readonly AnalysisConfiguration _config;

        public SessionThreatClassifier(AnalysisConfiguration? config = null)
        {
            _config = config ?? new AnalysisConfiguration();
        }

        /// <summary>
        /// Classifica a sessão baseado nos matches detectados.
        /// Regra 1: Maior ameaça prevalece.
        /// Regra 2: Apenas matches com similaridade >= threshold de confiança mínima alteram classificação.
        /// Regra 3: Nível NUNCA diminui durante sessão ativa.
        /// </summary>
        /// <param name="matches">Lista de matches detectados.</param>
        /// <param name="previousLevel">Nível anterior da sessão (null se primeira classificação).</param>
        /// <returns>Tupla com novo nível, razão da classificação e técnica que causou mudança.</returns>
        public (ThreatSeverityTarja level, string reason, string? triggerTechniqueId, double? triggerSimilarity)
            ClassifySession(
                IReadOnlyList<SimilarityMatch> matches,
                ThreatSeverityTarja? previousLevel)
        {
            // Filtrar apenas matches acima do threshold de média confiança
            var significantMatches = matches
                .Where(m => m.Similarity >= _config.MediumConfidenceThreshold)
                .OrderByDescending(m => GetThreatLevelPriority(m.ThreatLevel))
                .ThenByDescending(m => m.Similarity)
                .ToList();

            // Se não há matches significativos, manter Verde ou nível anterior
            if (significantMatches.Count == 0)
            {
                var currentLevel = previousLevel ?? ThreatSeverityTarja.Verde;
                return (currentLevel, "Nenhuma técnica adversarial detectada com confiança suficiente.", null, null);
            }

            // Pegar o match com maior ameaça
            var highestThreatMatch = significantMatches.First();

            // Regra 3: Nível nunca diminui
            var newLevel = highestThreatMatch.ThreatLevel;
            if (previousLevel.HasValue && GetThreatLevelPriority(previousLevel.Value) > GetThreatLevelPriority(newLevel))
            {
                newLevel = previousLevel.Value;
            }

            // Construir razão
            var reason = BuildReason(highestThreatMatch, significantMatches.Count);

            return (newLevel, reason, highestThreatMatch.TechniqueId, highestThreatMatch.Similarity);
        }

        /// <summary>
        /// Retorna prioridade numérica do nível de ameaça (maior = mais crítico).
        /// </summary>
        private int GetThreatLevelPriority(ThreatSeverityTarja level)
        {
            return level switch
            {
                ThreatSeverityTarja.Vermelho => 5,
                ThreatSeverityTarja.Laranja => 4,
                ThreatSeverityTarja.Amarelo => 3,
                ThreatSeverityTarja.Azul => 2,
                ThreatSeverityTarja.Verde => 1,
                _ => 0
            };
        }

        /// <summary>
        /// Constrói a razão da classificação.
        /// </summary>
        private string BuildReason(SimilarityMatch highestMatch, int totalSignificantMatches)
        {
            var confidence = highestMatch.Similarity >= _config.HighConfidenceThreshold
                ? "alta confiança"
                : "média confiança";

            var reason = $"{highestMatch.TechniqueId} ({highestMatch.TechniqueName}) detectado com {highestMatch.Similarity:P0} de similaridade ({confidence})";

            if (totalSignificantMatches > 1)
            {
                reason += $" - {totalSignificantMatches - 1} outras técnicas detectadas";
            }

            return reason;
        }

        /// <summary>
        /// Retorna emoji correspondente ao nível de ameaça.
        /// </summary>
        public static string GetThreatLevelEmoji(ThreatSeverityTarja level)
        {
            return level switch
            {
                ThreatSeverityTarja.Verde => "[V]",
                ThreatSeverityTarja.Azul => "[B]",
                ThreatSeverityTarja.Amarelo => "[Y]",
                ThreatSeverityTarja.Laranja => "[O]",
                ThreatSeverityTarja.Vermelho => "[R]",
                _ => "?"
            };
        }

        /// <summary>
        /// Retorna descrição do nível de ameaça.
        /// </summary>
        public static string GetThreatLevelDescription(ThreatSeverityTarja level)
        {
            return level switch
            {
                ThreatSeverityTarja.Verde => "Baixo - Operacoes normais e probabilidade muito baixa.",
                ThreatSeverityTarja.Azul => "Moderado - Atividades maliciosas sem impacto em infraestrutura critica.",
                ThreatSeverityTarja.Amarelo => "Medio - Acoes hostis com risco a infraestrutura critica, sem comprometimento.",
                ThreatSeverityTarja.Laranja => "Alto - Infraestrutura critica degradada com restabelecimento possivel.",
                ThreatSeverityTarja.Vermelho => "Severo - Impacto critico com restabelecimento fora do aceitavel.",
                _ => "Desconhecido"
            };        };
        }

        /// <summary>
        /// Determina se uma mudança de nível deve gerar alerta.
        /// </summary>
        public bool ShouldAlert(ThreatSeverityTarja? previousLevel, ThreatSeverityTarja newLevel)
        {
            // Sempre alertar se é a primeira classificação e não é Verde
            if (!previousLevel.HasValue && newLevel != ThreatSeverityTarja.Verde)
            {
                return true;
            }

            // Alertar se o nível mudou para um nível mais alto
            if (previousLevel.HasValue && GetThreatLevelPriority(newLevel) > GetThreatLevelPriority(previousLevel.Value))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Retorna recomendações baseadas no nível de ameaça e técnicas detectadas.
        /// </summary>
        public List<string> GetRecommendations(
            ThreatSeverityTarja level,
            IReadOnlyList<SimilarityMatch> significantMatches)
        {
            var recommendations = new List<string>();

            switch (level)
            {
                case ThreatSeverityTarja.Vermelho:
                    recommendations.Add("!! ISOLAR HOST DA REDE IMEDIATAMENTE");
                    recommendations.Add("!! CAPTURAR MEMORIA DOS PROCESSOS ENVOLVIDOS");
                    recommendations.Add("!! INICIAR INVESTIGACAO DE INCIDENTE");
                    recommendations.Add("!! NOTIFICAR EQUIPE DE SEGURANCA");
                    break;

                case ThreatSeverityTarja.Laranja:
                    recommendations.Add("!! MONITORAR CONEXOES DE REDE ATIVAS");
                    recommendations.Add("!! VERIFICAR PERSISTENCIA NO SISTEMA");
                    recommendations.Add("!! REVISAR LOGS DE AUTENTICACAO");
                    break;

                case ThreatSeverityTarja.Amarelo:
                    recommendations.Add("?? AUMENTAR NIVEL DE MONITORAMENTO");
                    recommendations.Add("?? REVISAR PROCESSOS CRIADOS E AGENDAMENTOS");
                    break;

                case ThreatSeverityTarja.Azul:
                    recommendations.Add("?? MONITORAR CONTINUAMENTE E CORRELACIONAR ALERTAS");
                    recommendations.Add("?? VALIDAR CONFIGURACOES DEFENSIVAS E CAPTURAR EVIDENCIAS");
                    break;

                case ThreatSeverityTarja.Verde:
                    recommendations.Add("OK CONTINUAR MONITORAMENTO NORMAL");
                    break;
            }

            // Adicionar recomendações específicas por técnica
            foreach (var match in significantMatches.Take(3))
            {
                var techniqueRec = GetTechniqueSpecificRecommendation(match.TechniqueId);
                if (techniqueRec != null)
                {
                    recommendations.Add(techniqueRec);
                }
            }

            return recommendations;
        }

        /// <summary>
        /// Retorna recomendação específica para uma técnica MITRE ATT&amp;CK.
        /// </summary>
        private string? GetTechniqueSpecificRecommendation(string techniqueId)
        {
            return techniqueId switch
            {
                "T1055" => "Investigar processo que executou injection (Event ID 8/10)",
                "T1003" or "T1003.001" => "Verificar acessos ao processo LSASS",
                "T1059" or "T1059.001" => "Analisar comandos PowerShell executados",
                "T1071" or "T1071.001" => "Monitorar tráfego de rede para C2 beaconing",
                "T1105" => "Verificar arquivos baixados recentemente",
                "T1543.003" => "Auditar serviços criados recentemente",
                _ => null
            };
        }
    }
}








