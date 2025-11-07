using System;
using System.Collections.Generic;

namespace NavShieldTracer.Modules.Heuristics.Normalization
{
    /// <summary>
    /// Representa a severidade global (tarja) atribuída ao teste catalogado.
    /// </summary>
    public enum ThreatSeverityTarja
    {
        Verde,
        Azul,
        Amarelo,
        Laranja,
        Vermelho
    }

    /// <summary>
    /// Indica o status resultante da normalização de um teste catalogado.
    /// </summary>
    internal enum NormalizationStatus
    {
        Pending,
        Completed,
        Incomplete
    }

    /// <summary>
    /// Snapshot enxuto de um evento armazenado no SQLite utilizado durante a normalização.
    /// </summary>
    /// <param name="EventRowId">Identificador interno na tabela events.</param>
    /// <param name="EventId">ID do evento Sysmon.</param>
    /// <param name="UtcTime">Timestamp original do evento.</param>
    /// <param name="CaptureTime">Timestamp de captura pelo NavShieldTracer.</param>
    /// <param name="SequenceNumber">Sequência dentro da sessão.</param>
    /// <param name="Image">Imagem (executável) responsável.</param>
    /// <param name="CommandLine">Linha de comando utilizada.</param>
    /// <param name="ParentImage">Imagem do processo pai.</param>
    /// <param name="ParentCommandLine">Linha de comando do processo pai.</param>
    /// <param name="ProcessId">PID.</param>
    /// <param name="ParentProcessId">PPID.</param>
    /// <param name="ProcessGuid">GUID do processo.</param>
    /// <param name="ParentProcessGuid">GUID do processo pai.</param>
    /// <param name="User">Usuário associado.</param>
    /// <param name="IntegrityLevel">Nível de integridade reportado.</param>
    /// <param name="Hashes">Hashes conhecidos para o executável.</param>
    /// <param name="TargetFilename">Arquivo alvo (quando aplicável).</param>
    /// <param name="ImageLoaded">DLL/Imagem carregada (Event ID 7).</param>
    /// <param name="Signed">Indicador de assinatura.</param>
    /// <param name="Signature">Nome da assinatura digital.</param>
    /// <param name="SignatureStatus">Status da verificação da assinatura.</param>
    /// <param name="PipeName">Nome do pipe em eventos 17/18.</param>
    /// <param name="WmiOperation">Operação WMI.</param>
    /// <param name="WmiName">Nome do evento WMI.</param>
    /// <param name="WmiQuery">Consulta WMI.</param>
    /// <param name="DnsQuery">Consulta DNS.</param>
    /// <param name="DnsResult">Resultado da consulta DNS.</param>
    /// <param name="DnsType">Tipo da consulta DNS.</param>
    /// <param name="SrcIp">IP de origem.</param>
    /// <param name="SrcPort">Porta de origem.</param>
    /// <param name="DstIp">IP de destino.</param>
    /// <param name="DstPort">Porta de destino.</param>
    /// <param name="Protocol">Protocolo (TCP/UDP, etc.).</param>
    /// <param name="RawJson">Payload bruto utilizado como fallback.</param>
    internal record CatalogEventSnapshot(
        int EventRowId,
        int EventId,
        DateTime? UtcTime,
        DateTime? CaptureTime,
        long? SequenceNumber,
        string? Image,
        string? CommandLine,
        string? ParentImage,
        string? ParentCommandLine,
        int? ProcessId,
        int? ParentProcessId,
        string? ProcessGuid,
        string? ParentProcessGuid,
        string? User,
        string? IntegrityLevel,
        string? Hashes,
        string? TargetFilename,
        string? ImageLoaded,
        string? Signed,
        string? Signature,
        string? SignatureStatus,
        string? PipeName,
        string? WmiOperation,
        string? WmiName,
        string? WmiQuery,
        string? DnsQuery,
        string? DnsResult,
        string? DnsType,
        string? SrcIp,
        int? SrcPort,
        string? DstIp,
        int? DstPort,
        string? Protocol,
        string? RawJson
    );

    /// <summary>
    /// Resultado da segregação de eventos em categorias heurísticas.
    /// </summary>
    /// <param name="CoreEvents">Eventos críticos que definem a TTP.</param>
    /// <param name="SupportEvents">Eventos de suporte contextual.</param>
    /// <param name="NoiseEvents">Eventos classificados como ruído.</param>
    internal record EventSegregationResult(
        IReadOnlyList<CatalogEventSnapshot> CoreEvents,
        IReadOnlyList<CatalogEventSnapshot> SupportEvents,
        IReadOnlyList<CatalogEventSnapshot> NoiseEvents
    );

    /// <summary>
    /// Vetor de características sintetizado a partir dos eventos catalogados.
    /// </summary>
    /// <param name="EventTypeHistogram">Histograma de Event IDs.</param>
    /// <param name="ProcessTreeDepth">Profundidade da árvore de processos.</param>
    /// <param name="NetworkConnectionsCount">Total de conexões de rede únicas.</param>
    /// <param name="RegistryOperationsCount">Operações de registro.</param>
    /// <param name="FileOperationsCount">Operações de arquivo.</param>
    /// <param name="TemporalSpanSeconds">Duração total observada.</param>
    /// <param name="CriticalEventsCount">Quantidade de eventos críticos.</param>
    internal record NormalizedFeatureVector(
        IReadOnlyDictionary<int, int> EventTypeHistogram,
        int ProcessTreeDepth,
        int NetworkConnectionsCount,
        int RegistryOperationsCount,
        int FileOperationsCount,
        double TemporalSpanSeconds,
        int CriticalEventsCount
    );

    /// <summary>
    /// Métricas de qualidade da normalização para auditoria e revisão.
    /// </summary>
    /// <param name="TotalEvents">Total de eventos processados.</param>
    /// <param name="CoreEvents">Eventos classificados como críticos.</param>
    /// <param name="SupportEvents">Eventos de suporte.</param>
    /// <param name="NoiseEvents">Eventos descartados.</param>
    /// <param name="CoveragePercentual">Cobertura percentual de eventos considerados essenciais.</param>
    /// <param name="Warnings">Alertas e anotações para revisão.</param>
    internal record NormalizationQualityMetrics(
        int TotalEvents,
        int CoreEvents,
        int SupportEvents,
        int NoiseEvents,
        double CoveragePercentual,
        IReadOnlyList<string> Warnings
    );

    /// <summary>
    /// Metadados derivados que definem a assinatura normalizada do teste.
    /// </summary>
    /// <param name="TestId">ID do teste original.</param>
    /// <param name="Status">Status da normalização.</param>
    /// <param name="Severity">Tarja sugerida.</param>
    /// <param name="SeverityReason">Justificativa para a tarja atribuída.</param>
    /// <param name="FeatureVector">Vetor de características calculado.</param>
    /// <param name="SignatureHash">Hash determinístico da assinatura.</param>
    /// <param name="ProcessedAt">Momento em que a normalização ocorreu.</param>
    /// <param name="QualityScore">Pontuação global de qualidade heurística.</param>
    /// <param name="Notes">Anotações complementares.</param>
    internal record NormalizedTestSignature(
        int TestId,
        NormalizationStatus Status,
        ThreatSeverityTarja Severity,
        string SeverityReason,
        NormalizedFeatureVector FeatureVector,
        string SignatureHash,
        DateTime ProcessedAt,
        double QualityScore,
        string Notes
    );

    /// <summary>
    /// Entrada de log detalhando etapas da normalização.
    /// </summary>
    /// <param name="Stage">Etapa (LOAD, SEGREGATION, FEATURE_VECTOR, etc.).</param>
    /// <param name="Level">Nível (INFO, WARN, ERROR).</param>
    /// <param name="Message">Mensagem descritiva.</param>
    internal record NormalizationLogEntry(
        string Stage,
        string Level,
        string Message
    );

    /// <summary>
    /// Resultado completo produzido pelo normalizador.
    /// </summary>
    /// <param name="Signature">Assinatura resultante.</param>
    /// <param name="Segregation">Classificação de eventos.</param>
    /// <param name="Quality">Métricas de qualidade.</param>
    /// <param name="Logs">Eventos de log gerados.</param>
    internal record CatalogNormalizationResult(
        NormalizedTestSignature Signature,
        EventSegregationResult Segregation,
        NormalizationQualityMetrics Quality,
        IReadOnlyList<NormalizationLogEntry> Logs
    );

    /// <summary>
    /// Resumo da normalizacao persistido para consulta e exibicao no CLI.
    /// </summary>
    /// <param name="TestId">ID do teste catalogado.</param>
    /// <param name="Status">Status da normalizacao.</param>
    /// <param name="Severity">Tarja heuristica registrada.</param>
    /// <param name="SeverityReason">Justificativa associada a tarja.</param>
    /// <param name="QualityScore">Pontuacao consolidada de qualidade (0.0-1.0).</param>
    /// <param name="CoveragePercent">Cobertura percentual de eventos core.</param>
    /// <param name="TotalEvents">Total de eventos processados.</param>
    /// <param name="CoreEvents">Eventos classificados como core.</param>
    /// <param name="SupportEvents">Eventos de suporte.</param>
    /// <param name="NoiseEvents">Eventos identificados como ruido.</param>
    /// <param name="ProcessedAt">Timestamp em que a normalizacao foi concluida.</param>
    /// <param name="FeatureVector">Snapshot das features geradas.</param>
    /// <param name="Warnings">Avisos emitidos durante a avaliacao de qualidade.</param>
    /// <param name="Notes">Notas adicionais salvas na assinatura.</param>
    internal record NormalizationSummary(
        int TestId,
        NormalizationStatus Status,
        ThreatSeverityTarja Severity,
        string? SeverityReason,
        double QualityScore,
        double CoveragePercent,
        int TotalEvents,
        int CoreEvents,
        int SupportEvents,
        int NoiseEvents,
        DateTime ProcessedAt,
        NormalizedFeatureVector FeatureVector,
        IReadOnlyList<string> Warnings,
        string? Notes
    );
}
