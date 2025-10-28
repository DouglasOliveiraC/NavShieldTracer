using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Modules.Models;
using Xunit;

namespace NavShieldTracer.Tests.Heuristics;

public sealed class CatalogNormalizerTests
{
    [Fact]
    public void Normalize_SemEventos_RetornaStatusIncompleto()
    {
        var teste = new TesteAtomico(
            Id: 1,
            Numero: "T0000",
            Nome: "Empty Test",
            Descricao: "Sem eventos",
            DataExecucao: DateTime.UtcNow,
            SessionId: 1,
            TotalEventos: 0);

        var context = new NormalizationContext(teste, Array.Empty<CatalogEventSnapshot>());
        var normalizer = new CatalogNormalizer();

        var resultado = normalizer.Normalize(context);

        Assert.Equal(NormalizationStatus.Incomplete, resultado.Signature.Status);
        Assert.Equal(ThreatSeverityTarja.Verde, resultado.Signature.Severity);
        Assert.Equal(0, resultado.Quality.TotalEvents);
        Assert.Contains(resultado.Logs, log => log.Level == "WARN");
    }

    [Fact]
    public void Normalize_ComEventoCritico_RetornaConclusaoVermelha()
    {
        var teste = new TesteAtomico(
            Id: 2,
            Numero: "T1055",
            Nome: "Credential Dump",
            Descricao: "Process injection em lsass",
            DataExecucao: DateTime.UtcNow,
            SessionId: 2,
            TotalEventos: 2);

        var eventos = new[]
        {
            CreateSnapshot(eventRowId: 1, eventId: 8, commandLine: "rundll32.exe lsass"),
            CreateSnapshot(eventRowId: 2, eventId: 1)
        };

        var context = new NormalizationContext(teste, eventos);
        var normalizer = new CatalogNormalizer();

        var resultado = normalizer.Normalize(context);

        Assert.Equal(NormalizationStatus.Completed, resultado.Signature.Status);
        Assert.Equal(ThreatSeverityTarja.Vermelho, resultado.Signature.Severity);
        Assert.True(resultado.Quality.CoveragePercentual >= 50);
        Assert.True(resultado.Segregation.CoreEvents.Any());
        Assert.Contains(resultado.Logs, log => log.Stage == "SEVERITY" && log.Level == "INFO");
    }

    private static CatalogEventSnapshot CreateSnapshot(
        int eventRowId,
        int eventId,
        string? commandLine = null,
        string? image = @"C:\Tools\agent.exe")
    {
        return new CatalogEventSnapshot(
            EventRowId: eventRowId,
            EventId: eventId,
            UtcTime: DateTime.UtcNow,
            CaptureTime: DateTime.UtcNow,
            SequenceNumber: eventRowId,
            Image: image,
            CommandLine: commandLine,
            ParentImage: @"C:\Windows\system32\services.exe",
            ParentCommandLine: "services.exe",
            ProcessId: 4000 + eventRowId,
            ParentProcessId: 3000,
            ProcessGuid: Guid.NewGuid().ToString("B"),
            ParentProcessGuid: Guid.NewGuid().ToString("B"),
            User: "TEST\\User",
            IntegrityLevel: "High",
            Hashes: "SHA256=ABC",
            TargetFilename: null,
            ImageLoaded: null,
            Signed: "true",
            Signature: "Microsoft Corporation",
            SignatureStatus: "Valid",
            PipeName: null,
            WmiOperation: null,
            WmiName: null,
            WmiQuery: null,
            DnsQuery: null,
            DnsResult: null,
            DnsType: null,
            SrcIp: null,
            SrcPort: null,
            DstIp: null,
            DstPort: null,
            Protocol: null,
            RawJson: null
        );
    }
}
