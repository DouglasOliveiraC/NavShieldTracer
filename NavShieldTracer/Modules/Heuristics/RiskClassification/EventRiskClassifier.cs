using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NavShieldTracer.Modules.Heuristics.RiskClassification
{
    /// <summary>
    /// Motor de classificação heurística de eventos Sysmon.
    /// </summary>
    public class EventRiskClassifier
    {
        private readonly RiskClassificationConfig _config;

        public EventRiskClassifier(RiskClassificationConfig? config = null)
        {
            _config = config ?? CreateDefaultConfig();
        }

        public EventRiskClassification ClassifyEvent(EventRiskSnapshot eventSnapshot)
        {
            var matchedRules = new List<string>();
            var severity = EvaluateBaseSeverity(eventSnapshot, matchedRules);
            var score = severity.ToScore();
            var reason = BuildReason(eventSnapshot.EventId, matchedRules);

            return new EventRiskClassification(
                severity,
                score,
                reason,
                matchedRules
            );
        }

        private ThreatSeverityTarja EvaluateBaseSeverity(EventRiskSnapshot evt, List<string> rules)
        {
            return evt.EventId switch
            {
                1 => EvaluateProcessCreate(evt, rules),
                3 => EvaluateNetworkConnection(evt, rules),
                7 => EvaluateImageLoad(evt, rules),
                8 => EvaluateRemoteThread(evt, rules),
                10 => EvaluateProcessAccess(evt, rules),
                11 => EvaluateFileCreate(evt, rules),
                12 or 13 or 14 => EvaluateRegistryEvent(evt, rules),
                17 or 18 => EvaluatePipeEvent(evt, rules),
                22 => EvaluateDnsQuery(evt, rules),
                23 or 26 => EvaluateFileDelete(evt, rules),
                _ => ThreatSeverityTarja.Azul
            };
        }

        #region Event Specific Evaluation

        private ThreatSeverityTarja EvaluateProcessCreate(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.35;

            if (!string.IsNullOrWhiteSpace(evt.CommandLine))
            {
                var cmdLower = evt.CommandLine.ToLowerInvariant();
                foreach (var token in _config.ProcessCreate.SuspiciousCommandTokens)
                {
                    if (cmdLower.Contains(token.ToLowerInvariant()))
                    {
                        score += 0.25;
                        rules.Add($"CMD_SUSPEITO: '{token}'");
                        break;
                    }
                }
            }

            if (_config.ProcessCreate.TrustMicrosoftSigned &&
                evt.Signed == true &&
                evt.Signature?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true)
            {
                score = Math.Max(0.0, score - 0.2);
                rules.Add("MICROSOFT_SIGNED: Processo assinado");
            }

            if (!string.IsNullOrWhiteSpace(evt.Image))
            {
                var trusted = _config.ProcessCreate.TrustedExecutablePaths
                    .Any(path => evt.Image.StartsWith(path, StringComparison.OrdinalIgnoreCase));

                if (!trusted)
                {
                    score += 0.15;
                    rules.Add($"PATH_NAO_TRUSTED: '{evt.Image}'");
                }
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateNetworkConnection(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.4;

            if (string.IsNullOrWhiteSpace(evt.DstIp))
            {
                return ThreatSeverityTarja.Verde;
            }

            if (_config.NetworkConnection.TrustedDestinationIps.Contains(evt.DstIp))
            {
                rules.Add($"IP_TRUSTED: '{evt.DstIp}'");
                return ThreatSeverityTarja.Verde;
            }

            if (evt.DstPort.HasValue && _config.NetworkConnection.TrustedPorts.Contains(evt.DstPort.Value))
            {
                score -= 0.05;
                rules.Add($"PORTA_COMUM: {evt.DstPort.Value}");
            }

            if (evt.DstPort.HasValue && _config.NetworkConnection.HighRiskPorts.Contains(evt.DstPort.Value))
            {
                score += 0.3;
                rules.Add($"PORTA_ALTO_RISCO: {evt.DstPort.Value}");
            }

            var isPrivate = IsPrivateIp(evt.DstIp);
            if (!isPrivate && _config.NetworkConnection.FlagExternalIps)
            {
                score = Math.Max(score, 0.95);
                rules.Add($"EXTERNO_DESCONHECIDO: {evt.DstIp}");
            }
            else if (isPrivate && !_config.NetworkConnection.TrustPrivateIps)
            {
                score += 0.1;
                rules.Add($"PRIVADO_NAO_AUTORIZADO: {evt.DstIp}");
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateDnsQuery(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.3;

            if (string.IsNullOrWhiteSpace(evt.DnsQuery))
            {
                return ThreatSeverityTarja.Verde;
            }

            var query = evt.DnsQuery.ToLowerInvariant();

            if (_config.DnsQuery.TrustedDomains.Any(query.EndsWith))
            {
                rules.Add($"DNS_TRUSTED: {evt.DnsQuery}");
                return ThreatSeverityTarja.Verde;
            }

            if (_config.DnsQuery.SuspiciousPatterns.Any(pattern => query.Contains(pattern)))
            {
                score += 0.3;
                rules.Add($"DNS_SUSPEITO: padrão '{evt.DnsQuery}'");
            }

            if (query.Length > _config.DnsQuery.MaxDomainLengthBeforeSuspicious)
            {
                score += 0.25;
                rules.Add($"DNS_MUITO_LONGO: {query.Length} chars");
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateImageLoad(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.25;

            if (!string.IsNullOrWhiteSpace(evt.TargetFilename))
            {
                if (_config.FileCreate.SuspiciousFilePaths.Any(path =>
                        evt.TargetFilename.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 0.15;
                    rules.Add($"DLL_SUSPEITA_PATH: {evt.TargetFilename}");
                }

                var extension = System.IO.Path.GetExtension(evt.TargetFilename);
                if (!string.IsNullOrEmpty(extension) &&
                    _config.FileCreate.HighRiskExtensions.Contains(extension.ToLowerInvariant()))
                {
                    score += 0.2;
                    rules.Add($"DLL_EXT_ALTO_RISCO: {extension}");
                }
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateRemoteThread(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.75;

            if (!string.IsNullOrWhiteSpace(evt.TargetImage))
            {
                var target = evt.TargetImage.ToLowerInvariant();
                if (_config.RemoteThread.CriticalTargetProcesses.Any(p => target.EndsWith(p)))
                {
                    score = 1.0;
                    rules.Add($"REMOTE_THREAD_CRITICO: {evt.TargetImage}");
                }
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateProcessAccess(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.5;

            if (!string.IsNullOrWhiteSpace(evt.TargetImage))
            {
                var target = evt.TargetImage.ToLowerInvariant();
                if (_config.ProcessAccess.CriticalTargetProcesses.Any(p => target.EndsWith(p)))
                {
                    score += 0.3;
                    rules.Add($"PROC_ACCESS_CRITICO: {evt.TargetImage}");
                }
            }

            if (evt.AdditionalFields != null &&
                evt.AdditionalFields.TryGetValue("AccessMask", out var mask) &&
                _config.ProcessAccess.HighRiskAccessMasks.Contains(mask))
            {
                score += 0.15;
                rules.Add($"PROC_ACCESS_MASK: {mask}");
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateFileCreate(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.35;

            if (string.IsNullOrWhiteSpace(evt.TargetFilename))
            {
                return ThreatSeverityTarja.Verde;
            }

            var extension = System.IO.Path.GetExtension(evt.TargetFilename);
            if (!string.IsNullOrEmpty(extension) &&
                _config.FileCreate.HighRiskExtensions.Contains(extension.ToLowerInvariant()))
            {
                score += 0.25;
                rules.Add($"FILE_EXT_ALTO_RISCO: {extension}");
            }

            if (_config.FileCreate.SuspiciousFilePaths.Any(path =>
                    evt.TargetFilename.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
            {
                score += 0.2;
                rules.Add($"FILE_PATH_SUSPEITO: {evt.TargetFilename}");
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateRegistryEvent(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.35;

            if (evt.AdditionalFields != null &&
                evt.AdditionalFields.TryGetValue("TargetObject", out var target) &&
                !string.IsNullOrWhiteSpace(target))
            {
                if (target.Contains("Run", StringComparison.OrdinalIgnoreCase) ||
                    target.Contains("RunOnce", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.25;
                    rules.Add($"REG_PERSISTENCIA: {target}");
                }
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluatePipeEvent(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.4;

            if (!string.IsNullOrWhiteSpace(evt.PipeName))
            {
                if (evt.PipeName.Contains("msrpc", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.1;
                    rules.Add($"PIPE_RPC: {evt.PipeName}");
                }

                if (evt.PipeName.Contains("lsarpc", StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.2;
                    rules.Add($"PIPE_LSA: {evt.PipeName}");
                }
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        private ThreatSeverityTarja EvaluateFileDelete(EventRiskSnapshot evt, List<string> rules)
        {
            double score = 0.3;

            if (evt.AdditionalFields != null &&
                evt.AdditionalFields.TryGetValue("FilePath", out var path) &&
                !string.IsNullOrWhiteSpace(path) &&
                path.Contains("AppData", StringComparison.OrdinalIgnoreCase))
            {
                score += 0.2;
                rules.Add($"DELETE_APPDATA: {path}");
            }

            return ThreatSeverityTarjaExtensions.FromScore(score);
        }

        #endregion

        #region Helpers

        private static string BuildReason(int eventId, IReadOnlyList<string> rules)
        {
            if (rules.Count == 0)
            {
                return $"Event ID {eventId} sem indicadores específicos";
            }

            return string.Join(" | ", rules);
        }

        private static bool IsPrivateIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip, out var address))
            {
                return false;
            }

            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
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

            return false;
        }

        public static RiskClassificationConfig CreateDefaultConfig()
        {
            return new RiskClassificationConfig
            {
                NetworkConnection = new NetworkConnectionRiskCriteria
                {
                    TrustedDestinationIps = new() { "127.0.0.1", "::1" },
                    HighRiskPorts = new() { 4444, 5555, 6666, 7777, 8888 },
                    TrustedPorts = new() { 80, 443, 8080, 53, 123 },
                    TrustPrivateIps = true,
                    FlagExternalIps = true
                },
                DnsQuery = new DnsQueryRiskCriteria
                {
                    TrustedDomains = new()
                    {
                        ".microsoft.com",
                        ".windowsupdate.com",
                        ".office365.com",
                        ".github.com",
                        ".azureedge.net"
                    },
                    SuspiciousPatterns = new() { ".ru", ".tk", ".bit", "dyndns", "no-ip" },
                    MaxDomainLengthBeforeSuspicious = 60
                },
                ProcessCreate = new ProcessCreateRiskCriteria
                {
                    TrustMicrosoftSigned = true
                },
                FileCreate = new FileCreateRiskCriteria(),
                RemoteThread = new RemoteThreadRiskCriteria(),
                ProcessAccess = new ProcessAccessRiskCriteria()
            };
        }

        #endregion
    }
}
