using System;
using System.Collections.Generic;
using System.Linq;

namespace NavShieldTracer.Modules.Heuristics.RiskClassification
{
    /// <summary>
    /// Severidades de ameaça (tarjas) adotadas pelo Ministério da Defesa.
    /// </summary>
    public enum ThreatSeverityTarja
    {
        Verde = 0,
        Azul = 1,
        Amarelo = 2,
        Laranja = 3,
        Vermelho = 4
    }

    /// <summary>
    /// Extensões auxiliares para trabalhar com tarjas.
    /// </summary>
    public static class ThreatSeverityTarjaExtensions
    {
        private static readonly IReadOnlyDictionary<ThreatSeverityTarja, double> ScoreMap =
            new Dictionary<ThreatSeverityTarja, double>
            {
                { ThreatSeverityTarja.Verde, 0.0 },
                { ThreatSeverityTarja.Azul, 0.25 },
                { ThreatSeverityTarja.Amarelo, 0.5 },
                { ThreatSeverityTarja.Laranja, 0.75 },
                { ThreatSeverityTarja.Vermelho, 1.0 }
            };

        public static double ToScore(this ThreatSeverityTarja severity) => ScoreMap[severity];

        public static ThreatSeverityTarja FromScore(double score)
        {
            if (score <= 0.1) return ThreatSeverityTarja.Verde;
            if (score <= 0.35) return ThreatSeverityTarja.Azul;
            if (score <= 0.6) return ThreatSeverityTarja.Amarelo;
            if (score <= 0.85) return ThreatSeverityTarja.Laranja;
            return ThreatSeverityTarja.Vermelho;
        }
    }

    /// <summary>
    /// Resultado consolidado da classificação heurística.
    /// </summary>
    /// <param name="Level">Tarja final atribuída.</param>
    /// <param name="Score">Score normalizado (0.0 = Verde, 1.0 = Vermelho).</param>
    /// <param name="Reason">Justificativa textual.</param>
    /// <param name="MatchedRules">Regras heurísticas acionadas.</param>
    public record EventRiskClassification(
        ThreatSeverityTarja Level,
        double Score,
        string Reason,
        IReadOnlyList<string> MatchedRules
    );

    /// <summary>
    /// Critérios de avaliação de risco específicos para Event ID 3 (Network Connection).
    /// </summary>
    public class NetworkConnectionRiskCriteria
    {
        public HashSet<string> TrustedDestinationIps { get; init; } = new();
        public HashSet<int> HighRiskPorts { get; init; } = new() { 4444, 5555, 6666, 7777, 8888 };
        public HashSet<int> TrustedPorts { get; init; } = new() { 80, 443, 8080, 53, 123 };
        public bool TrustPrivateIps { get; init; } = true;
        public bool FlagExternalIps { get; init; } = true;
    }

    /// <summary>
    /// Critérios heurísticos para consultas DNS (Event ID 22).
    /// </summary>
    public class DnsQueryRiskCriteria
    {
        public HashSet<string> TrustedDomains { get; init; } = new();
        public HashSet<string> SuspiciousPatterns { get; init; } = new();
        public int MaxDomainLengthBeforeSuspicious { get; init; } = 60;
    }

    public class ProcessCreateRiskCriteria
    {
        public HashSet<string> SuspiciousCommandTokens { get; init; } = new()
        {
            "powershell -enc",
            "invoke-mimikatz",
            "certutil -urlcache",
            "net user",
            "reg add"
        };

        public HashSet<string> TrustedExecutablePaths { get; init; } = new()
        {
            "C:\\Windows\\System32\\",
            "C:\\Windows\\SysWOW64\\",
            "C:\\Program Files\\",
            "C:\\Program Files (x86)\\"
        };

        public bool TrustMicrosoftSigned { get; init; } = true;
    }

    public class FileCreateRiskCriteria
    {
        public HashSet<string> HighRiskExtensions { get; init; } = new()
        {
            ".exe",
            ".dll",
            ".scr",
            ".bat",
            ".cmd",
            ".ps1",
            ".vbs",
            ".js"
        };

        public HashSet<string> SuspiciousFilePaths { get; init; } = new()
        {
            "C:\\Windows\\Temp\\",
            "C:\\Users\\Public\\",
            "%APPDATA%\\Roaming\\",
            "%TEMP%\\"
        };
    }

    public class RemoteThreadRiskCriteria
    {
        public HashSet<string> CriticalTargetProcesses { get; init; } = new()
        {
            "lsass.exe",
            "winlogon.exe",
            "csrss.exe"
        };
    }

    public class ProcessAccessRiskCriteria
    {
        public HashSet<string> CriticalTargetProcesses { get; init; } = new()
        {
            "lsass.exe",
            "winlogon.exe",
            "csrss.exe"
        };

        public HashSet<string> HighRiskAccessMasks { get; init; } = new()
        {
            "0x1410",
            "0x1438",
            "0x1FFFFF"
        };
    }

    /// <summary>
    /// Configuração global do motor de classificação.
    /// </summary>
    public class RiskClassificationConfig
    {
        public NetworkConnectionRiskCriteria NetworkConnection { get; init; } = new();
        public DnsQueryRiskCriteria DnsQuery { get; init; } = new();
        public ProcessCreateRiskCriteria ProcessCreate { get; init; } = new();
        public FileCreateRiskCriteria FileCreate { get; init; } = new();
        public RemoteThreadRiskCriteria RemoteThread { get; init; } = new();
        public ProcessAccessRiskCriteria ProcessAccess { get; init; } = new();
    }

    /// <summary>
    /// Snapshot simplificado de um evento Sysmon.
    /// </summary>
    public record EventRiskSnapshot(
        int EventId,
        string? Image,
        string? CommandLine,
        string? ParentImage,
        string? DstIp,
        int? DstPort,
        string? Protocol,
        string? DnsQuery,
        string? TargetFilename,
        string? Signature,
        bool? Signed,
        string? SourceImage,
        string? TargetImage,
        string? PipeName,
        Dictionary<string, string>? AdditionalFields
    );
}
