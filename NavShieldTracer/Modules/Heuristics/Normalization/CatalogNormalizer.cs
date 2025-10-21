using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NavShieldTracer.Modules.Storage;

namespace NavShieldTracer.Modules.Heuristics.Normalization
{
    /// <summary>
    /// Orquestra a normalização do catálogo, combinando segregação, extração de features,
    /// recomendações de whitelist e avaliação de qualidade.
    /// </summary>
    internal class CatalogNormalizer
    {
        private readonly EventSegregator _segregator;
        private readonly FeatureVectorFactory _featureFactory;
        private readonly WhitelistAdvisor _whitelistAdvisor;
        private readonly QualityAssessor _qualityAssessor;
        private readonly ThreatSeverityAdvisor _severityAdvisor;

        internal CatalogNormalizer(
            EventSegregator? segregator = null,
            FeatureVectorFactory? featureFactory = null,
            WhitelistAdvisor? whitelistAdvisor = null,
            QualityAssessor? qualityAssessor = null,
            ThreatSeverityAdvisor? severityAdvisor = null)
        {
            _segregator = segregator ?? new EventSegregator();
            _featureFactory = featureFactory ?? new FeatureVectorFactory();
            _whitelistAdvisor = whitelistAdvisor ?? new WhitelistAdvisor();
            _qualityAssessor = qualityAssessor ?? new QualityAssessor();
            _severityAdvisor = severityAdvisor ?? new ThreatSeverityAdvisor();
        }

        public CatalogNormalizationResult Normalize(NormalizationContext context)
        {
            var logs = new List<NormalizationLogEntry>();

            if (context.TotalEventos == 0)
            {
            logs.Add(new NormalizationLogEntry("LOAD", "WARN",
                $"Teste {context.Teste.Numero} ({context.Teste.Nome}) não possui eventos para normalização."));

            var emptySegregation = new EventSegregationResult(
                Array.Empty<CatalogEventSnapshot>(),
                Array.Empty<CatalogEventSnapshot>(),
                Array.Empty<CatalogEventSnapshot>());

            var emptyVector = new NormalizedFeatureVector(
                new Dictionary<int, int>(),
                0,
                0,
                0,
                0,
                0,
                0);

            var emptyQuality = new NormalizationQualityMetrics(
                0,
                0,
                0,
                0,
                0,
                new[] { "Sessão vazia - execute o teste novamente para obter amostra." });

            var emptySeverityDecision = new SeverityDecision(
                ThreatSeverityTarja.Verde,
                "Sem eventos críticos capturados.");

            var emptySignature = new NormalizedTestSignature(
                context.Teste.Id,
                NormalizationStatus.Incomplete,
                emptySeverityDecision.Severity,
                emptySeverityDecision.Reason,
                emptyVector,
                ComputeSignatureHash(context, emptyVector, emptySegregation, emptySeverityDecision.Severity),
                DateTime.Now,
                0,
                "Normalização inconclusiva pela ausência de eventos.");

            return new CatalogNormalizationResult(
                emptySignature,
                emptySegregation,
                Array.Empty<SuggestedWhitelistEntry>(),
                emptyQuality,
                logs);
        }

            logs.Add(new NormalizationLogEntry("LOAD", "INFO",
                $"Carregados {context.TotalEventos} eventos para o teste {context.Teste.Numero}."));

            var segregation = _segregator.Segregate(context, logs);
            var featureVector = _featureFactory.Build(context, segregation, logs);
            var whitelist = _whitelistAdvisor.Suggest(context, segregation, logs);
            var quality = _qualityAssessor.Evaluate(context, segregation, logs);
            var severityDecision = _severityAdvisor.Suggest(context, segregation, quality, logs);

            var status = DetermineStatus(quality);
            if (status == NormalizationStatus.Incomplete)
            {
                logs.Add(new NormalizationLogEntry("STATUS", "WARN",
                    "Normalização marcada como incompleta por baixa cobertura ou ausência de eventos críticos."));
            }

            var signature = new NormalizedTestSignature(
                context.Teste.Id,
                status,
                severityDecision.Severity,
                severityDecision.Reason,
                featureVector,
                ComputeSignatureHash(context, featureVector, segregation, severityDecision.Severity),
                DateTime.Now,
                _qualityAssessor.ComputeQualityScore(quality),
                status == NormalizationStatus.Completed
                    ? "Assinatura pronta para uso no motor heurístico."
                    : "Revisar captura e refinar execução do teste antes de utilizar a assinatura.");

            return new CatalogNormalizationResult(
                signature,
                segregation,
                whitelist,
                quality,
                logs);
        }

        private static NormalizationStatus DetermineStatus(NormalizationQualityMetrics quality)
        {
            if (quality.TotalEvents == 0 || quality.CoreEvents == 0)
            {
                return NormalizationStatus.Incomplete;
            }

            if (quality.CoveragePercentual < 10)
            {
                return NormalizationStatus.Incomplete;
            }

            return NormalizationStatus.Completed;
        }

        private static string ComputeSignatureHash(
            NormalizationContext context,
            NormalizedFeatureVector featureVector,
            EventSegregationResult segregation,
            ThreatSeverityTarja severity)
        {
            using var sha = SHA256.Create();

            var histogramPart = string.Join(',',
                featureVector.EventTypeHistogram
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key}:{kv.Value}"));

            var input = string.Join('|', new[]
            {
                context.Teste.Id.ToString(),
                context.Teste.Numero,
                severity.ToString(),
                histogramPart,
                featureVector.ProcessTreeDepth.ToString(),
                featureVector.NetworkConnectionsCount.ToString(),
                featureVector.RegistryOperationsCount.ToString(),
                featureVector.FileOperationsCount.ToString(),
                featureVector.TemporalSpanSeconds.ToString("F2"),
                segregation.CoreEvents.Count.ToString(),
                context.DurationSeconds.ToString("F2")
            });

            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }

    internal class EventSegregator
    {
        private static readonly HashSet<int> CriticalEventIds = new()
        {
            8, 10, 11, 12, 13, 14, 15, 17, 18, 19, 20, 21
        };

        private static readonly HashSet<int> SupportEventIds = new()
        {
            1, 2, 3, 4, 5, 6, 7, 9, 22, 23, 24, 25, 26
        };

        private static readonly string[] SuspiciousCommandTokens =
        {
            "powershell -enc",
            "powershell.exe -enc",
            "invoke-mimikatz",
            "mimikatz",
            "certutil -urlcache",
            "rundll32",
            "regsvr32 /s",
            "wmic process call create",
            "bitsadmin",
            "cmd.exe /c whoami /priv"
        };

        public EventSegregationResult Segregate(NormalizationContext context, IList<NormalizationLogEntry> logs)
        {
            var core = new List<CatalogEventSnapshot>();
            var support = new List<CatalogEventSnapshot>();
            var noise = new List<CatalogEventSnapshot>();

            foreach (var evento in context.Eventos)
            {
                if (evento.EventId <= 0)
                {
                    noise.Add(evento);
                    continue;
                }

                if (CriticalEventIds.Contains(evento.EventId) || IsHighRiskEvent(evento))
                {
                    core.Add(evento);
                    continue;
                }

                if (SupportEventIds.Contains(evento.EventId) || HasImportantContext(evento))
                {
                    support.Add(evento);
                    continue;
                }

                noise.Add(evento);
            }

            logs.Add(new NormalizationLogEntry("SEGREGATION", "INFO",
                $"Segregação concluída: {core.Count} core / {support.Count} suporte / {noise.Count} ruído."));

            if (core.Count == 0)
            {
                logs.Add(new NormalizationLogEntry("SEGREGATION", "WARN",
                    "Nenhum evento crítico identificado - considere revisar o passo a passo do teste."));
            }

            return new EventSegregationResult(core, support, noise);
        }

        private static bool IsHighRiskEvent(CatalogEventSnapshot evento)
        {
            // Evento 1 (Process Create) com comandos suspeitos.
            if (evento.EventId == 1 && !string.IsNullOrWhiteSpace(evento.CommandLine))
            {
                var lower = evento.CommandLine.ToLowerInvariant();
                if (SuspiciousCommandTokens.Any(token => lower.Contains(token)))
                {
                    return true;
                }
            }

            // Evento 10 (ProcessAccess) mirando LSASS.
            if (evento.EventId == 10)
            {
                var target = (evento.TargetFilename ?? string.Empty).ToLowerInvariant();
                var parent = (evento.ParentImage ?? string.Empty).ToLowerInvariant();
                if (target.Contains("lsass.exe") || parent.Contains("lsass.exe"))
                {
                    return true;
                }
            }

            // Evento de rede para IP externo com protocolo claro.
            if (evento.EventId == 3 && !string.IsNullOrWhiteSpace(evento.DstIp))
            {
                return !WhitelistAdvisor.IsPrivateIp(evento.DstIp);
            }

            return false;
        }

        private static bool HasImportantContext(CatalogEventSnapshot evento)
        {
            if (evento.EventId == 3 && !string.IsNullOrWhiteSpace(evento.DstIp))
            {
                return true;
            }

            if (evento.EventId == 22 && !string.IsNullOrWhiteSpace(evento.DnsQuery))
            {
                return true;
            }

            if (evento.EventId == 7 && !string.IsNullOrWhiteSpace(evento.ImageLoaded))
            {
                return true;
            }

            if (evento.EventId == 13 || evento.EventId == 12 || evento.EventId == 14)
            {
                return true;
            }

            return false;
        }
    }

    internal class FeatureVectorFactory
    {
        private static readonly HashSet<int> RegistryEvents = new() { 12, 13, 14 };
        private static readonly HashSet<int> FileEvents = new() { 2, 11, 15, 23 };

        public NormalizedFeatureVector Build(
            NormalizationContext context,
            EventSegregationResult segregation,
            IList<NormalizationLogEntry> logs)
        {
            var histogram = new Dictionary<int, int>();
            var relationships = new Dictionary<int, int?>();
            var networkConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int registryOps = 0;
            int fileOps = 0;

            foreach (var evento in context.Eventos)
            {
                if (evento.EventId > 0)
                {
                    histogram.TryGetValue(evento.EventId, out var count);
                    histogram[evento.EventId] = count + 1;
                }

                if (evento.ProcessId.HasValue && !relationships.ContainsKey(evento.ProcessId.Value))
                {
                    relationships[evento.ProcessId.Value] = evento.ParentProcessId;
                }

                if (!string.IsNullOrWhiteSpace(evento.DstIp))
                {
                    var key = $"{evento.DstIp}:{evento.DstPort ?? 0}";
                    networkConnections.Add(key);
                }

                if (RegistryEvents.Contains(evento.EventId))
                {
                    registryOps++;
                }

                if (FileEvents.Contains(evento.EventId))
                {
                    fileOps++;
                }
            }

            var depth = CalculateProcessTreeDepth(relationships);
            logs.Add(new NormalizationLogEntry("FEATURE_VECTOR", "INFO",
                $"Histograma com {histogram.Count} tipos de eventos. Profundidade de árvore: {depth}."));

            return new NormalizedFeatureVector(
                histogram,
                depth,
                networkConnections.Count,
                registryOps,
                fileOps,
                Math.Max(context.DurationSeconds, 0),
                segregation.CoreEvents.Count);
        }

        private static int CalculateProcessTreeDepth(Dictionary<int, int?> relationships)
        {
            int maxDepth = 0;

            foreach (var pid in relationships.Keys)
            {
                int depth = 0;
                var current = pid;
                var visited = new HashSet<int>();

                while (relationships.TryGetValue(current, out var parent) && parent.HasValue)
                {
                    if (!visited.Add(current))
                    {
                        break; // evita loops.
                    }

                    depth++;
                    current = parent.Value;

                    if (depth > 50)
                    {
                        break;
                    }
                }

                if (depth > maxDepth)
                {
                    maxDepth = depth;
                }
            }

            return maxDepth;
        }
    }

    internal class WhitelistAdvisor
    {
        private static readonly string[] TrustedDomains =
        {
            ".microsoft.com",
            ".windowsupdate.com",
            ".office365.com",
            ".github.com",
            ".azureedge.net",
            ".google.com"
        };

        public IReadOnlyList<SuggestedWhitelistEntry> Suggest(
            NormalizationContext context,
            EventSegregationResult segregation,
            IList<NormalizationLogEntry> logs)
        {
            var entries = new Dictionary<string, SuggestedWhitelistEntry>(StringComparer.OrdinalIgnoreCase);

            void AddEntry(string type, string value, string reason, bool autoApprove)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                var normalizedValue = value.Trim();
                var key = $"{type}:{normalizedValue}".ToLowerInvariant();
                if (entries.ContainsKey(key))
                {
                    return;
                }

                var entry = new SuggestedWhitelistEntry(
                    type,
                    normalizedValue,
                    reason,
                    autoApprove,
                    autoApprove);

                entries[key] = entry;
                logs.Add(new NormalizationLogEntry("WHITELIST", "INFO",
                    $"Sugerido whitelist [{type}] {value} ({reason}) - auto={autoApprove}."));
            }

            foreach (var evento in context.Eventos)
            {
                if (!string.IsNullOrWhiteSpace(evento.DstIp))
                {
                    if (IsPrivateIp(evento.DstIp))
                    {
                        AddEntry("IP", evento.DstIp, "Tráfego interno durante teste", autoApprove: true);
                    }
                    else if (IPAddress.TryParse(evento.DstIp, out _))
                    {
                        AddEntry("IP", evento.DstIp, "Conexão externa observada", autoApprove: false);
                    }
                }

                if (!string.IsNullOrWhiteSpace(evento.DnsQuery))
                {
                    var query = evento.DnsQuery.ToLowerInvariant();
                    var isTrusted = TrustedDomains.Any(domain => query.EndsWith(domain, StringComparison.OrdinalIgnoreCase));
                    AddEntry("DOMAIN", query, isTrusted ? "Domínio confiável conhecido" : "Domínio resolvido durante o teste", isTrusted);
                }

                if (!string.IsNullOrWhiteSpace(evento.Image) &&
                    !string.IsNullOrWhiteSpace(evento.Signature) &&
                    evento.Signature.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddEntry("PROCESS", evento.Image, "Processo assinado pela Microsoft", autoApprove: true);
                }
            }

            return entries.Values
                .OrderBy(e => e.EntryType)
                .ThenBy(e => e.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsPrivateIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            if (!IPAddress.TryParse(ip, out var address))
            {
                return false;
            }

            var bytes = address.GetAddressBytes();

            return address.AddressFamily switch
            {
                System.Net.Sockets.AddressFamily.InterNetwork => IsPrivateIpv4(bytes),
                System.Net.Sockets.AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.Equals(IPAddress.IPv6Loopback),
                _ => false
            };
        }

        private static bool IsPrivateIpv4(ReadOnlySpan<byte> bytes)
        {
            return bytes[0] switch
            {
                10 => true,
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,
                192 when bytes[1] == 168 => true,
                127 => true,
                _ => false
            };
        }
    }

    internal class QualityAssessor
    {
        public NormalizationQualityMetrics Evaluate(
            NormalizationContext context,
            EventSegregationResult segregation,
            IList<NormalizationLogEntry> logs)
        {
            var total = context.TotalEventos;
            var core = segregation.CoreEvents.Count;
            var support = segregation.SupportEvents.Count;
            var noise = segregation.NoiseEvents.Count;

            double coverage = total > 0 ? (double)core / total * 100 : 0;
            var warnings = new List<string>();

            if (total == 0)
            {
                warnings.Add("Nenhum evento processado.");
            }

            if (core == 0)
            {
                warnings.Add("Nenhum evento crítico identificado.");
            }

            if (coverage < 15 && total > 0)
            {
                warnings.Add("Cobertura de eventos críticos inferior a 15%.");
            }

            if (context.DurationSeconds < 2 && total > 0)
            {
                warnings.Add("Janela temporal muito curta - verifique se a coleta iniciou antes do teste.");
            }

            if (warnings.Count > 0)
            {
                foreach (var warning in warnings)
                {
                    logs.Add(new NormalizationLogEntry("QUALITY", "WARN", warning));
                }
            }
            else
            {
                logs.Add(new NormalizationLogEntry("QUALITY", "INFO", $"Cobertura de {coverage:F1}% com {core} eventos críticos."));
            }

            return new NormalizationQualityMetrics(
                total,
                core,
                support,
                noise,
                coverage,
                warnings);
        }

        public double ComputeQualityScore(NormalizationQualityMetrics metrics)
        {
            if (metrics.TotalEvents == 0)
            {
                return 0;
            }

            var score = metrics.CoveragePercentual / 100.0;

            if (metrics.CoreEvents > 0)
            {
                score += 0.2;
            }

            score -= Math.Min(metrics.Warnings.Count * 0.05, 0.3);
            return Math.Clamp(score, 0, 1);
        }
    }

    internal class ThreatSeverityAdvisor
    {
        private static readonly int[] CredentialAccessEvents = { 8, 10, 11 };
        private static readonly int[] LateralMovementEvents = { 3, 17, 18, 19, 20, 21 };

        public SeverityDecision Suggest(
            NormalizationContext context,
            EventSegregationResult segregation,
            NormalizationQualityMetrics quality,
            IList<NormalizationLogEntry> logs)
        {
            foreach (var evento in segregation.CoreEvents)
            {
                if (CredentialAccessEvents.Contains(evento.EventId) && IndicatesCredentialDump(evento))
                {
                    logs.Add(new NormalizationLogEntry("SEVERITY", "INFO",
                        "Detecção de padrão de credential dumping - tarja Vermelha aplicada."));
                    return new SeverityDecision(ThreatSeverityTarja.Vermelho,
                        "Padrão de credential dumping (LSASS/Injection).");
                }
            }

            if (segregation.CoreEvents.Any(ev => LateralMovementEvents.Contains(ev.EventId)))
            {
                logs.Add(new NormalizationLogEntry("SEVERITY", "INFO",
                    "Eventos críticos de movimento lateral detectados - tarja Laranja sugerida."));
                return new SeverityDecision(ThreatSeverityTarja.Laranja,
                    "Indicadores de movimento lateral/exfiltração.");
            }

            if (segregation.CoreEvents.Count > 0)
            {
                logs.Add(new NormalizationLogEntry("SEVERITY", "INFO",
                    "Eventos críticos presentes sem sinais de credencial ou lateral movement - tarja Amarela."));
                return new SeverityDecision(ThreatSeverityTarja.Amarelo,
                    "Execução de técnica adversarial sem evidências de escalonamento crítico.");
            }

            logs.Add(new NormalizationLogEntry("SEVERITY", "INFO",
                "Nenhum evento crítico - tarja Verde atribuída."));
            return new SeverityDecision(ThreatSeverityTarja.Verde,
                "Amostra sem indicadores de ameaça relevantes.");
        }

        private static bool IndicatesCredentialDump(CatalogEventSnapshot evento)
        {
            var cmd = (evento.CommandLine ?? string.Empty).ToLowerInvariant();
            var parent = (evento.ParentImage ?? string.Empty).ToLowerInvariant();
            var target = (evento.TargetFilename ?? string.Empty).ToLowerInvariant();

            if (cmd.Contains("lsass") || parent.Contains("lsass") || target.Contains("lsass"))
            {
                return true;
            }

            if (cmd.Contains("sekurlsa") || cmd.Contains("mimikatz"))
            {
                return true;
            }

            return false;
        }
    }

    internal record SeverityDecision(ThreatSeverityTarja Severity, string Reason);
}
