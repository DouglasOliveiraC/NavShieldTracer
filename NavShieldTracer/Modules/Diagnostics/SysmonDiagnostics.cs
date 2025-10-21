using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;

namespace NavShieldTracer.Modules.Diagnostics;

internal static class SysmonDiagnostics
{
    private const string DefaultLogName = "Microsoft-Windows-Sysmon/Operational";
    private static readonly string[] CandidateServiceNames = { "Sysmon64", "Sysmon" };

    public static SysmonStatus GatherStatus()
    {
        var recommendations = new List<string>();
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        var isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);

        if (!isAdministrator)
        {
            recommendations.Add("Execute o NavShieldTracer em um terminal elevado (Run as Administrator).");
        }

        var (serviceFound, serviceRunning, resolvedServiceName) = DetectService();
        if (!serviceFound)
        {
            recommendations.Add("Instale o Sysmon: sysmon64.exe -accepteula -i");
        }
        else if (!serviceRunning)
        {
            recommendations.Add($"Inicie o serviço {resolvedServiceName ?? "Sysmon"}: sc start {resolvedServiceName ?? "Sysmon64"}");
        }

        var (logExists, resolvedLogName, hasAccess, accessError) = DetectLogChannel();
        if (!logExists)
        {
            recommendations.Add("Habilite o canal 'Microsoft-Windows-Sysmon/Operational' (wevtutil sl Microsoft-Windows-Sysmon/Operational /e:true).");
        }
        else if (!hasAccess)
        {
            if (isAdministrator)
            {
                recommendations.Add("Reinstale ou reconfigure o Sysmon; o canal existe, mas não pôde ser lido.");
            }
            else
            {
                recommendations.Add("Reabra o NavShieldTracer como Administrador para ler o canal do Sysmon.");
            }
        }

        var isReady = isAdministrator && serviceFound && serviceRunning && logExists && hasAccess;

        return new SysmonStatus(
            isReady,
            isAdministrator,
            serviceFound,
            serviceRunning,
            logExists,
            hasAccess,
            resolvedServiceName,
            resolvedLogName,
            accessError,
            recommendations
        );
    }

    private static (bool serviceFound, bool serviceRunning, string? serviceName) DetectService()
    {
        try
        {
            foreach (var candidate in CandidateServiceNames)
            {
                var service = ServiceController.GetServices().FirstOrDefault(s => string.Equals(s.ServiceName, candidate, StringComparison.OrdinalIgnoreCase));
                if (service is null)
                {
                    continue;
                }

                var running = service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.StartPending;
                return (true, running, service.ServiceName);
            }
        }
        catch
        {
            // Em ambientes restritos, apenas ignore a detecção do serviço.
        }

        return (false, false, null);
    }

    private static (bool logExists, string? logName, bool hasAccess, string? accessError) DetectLogChannel()
    {
        string? resolvedName = null;
        try
        {
            using var session = new EventLogSession();
            var logNames = session.GetLogNames();
            resolvedName = logNames.FirstOrDefault(name => string.Equals(name, DefaultLogName, StringComparison.OrdinalIgnoreCase));

            if (resolvedName is null)
            {
                resolvedName = logNames.FirstOrDefault(name => name.IndexOf("Sysmon", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (resolvedName is null)
            {
                return (false, null, false, "Canal do Sysmon não encontrado.");
            }

            var query = new EventLogQuery(resolvedName, PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Sysmon']]]");
            using var reader = new EventLogReader(query);
            reader.ReadEvent();
            return (true, resolvedName, true, null);
        }
        catch (EventLogNotFoundException ex)
        {
            return (false, resolvedName, false, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return (true, resolvedName, false, ex.Message);
        }
        catch (Exception ex)
        {
            return (resolvedName is not null, resolvedName, false, ex.Message);
        }
    }
}

internal sealed record SysmonStatus(
    bool IsReady,
    bool IsAdministrator,
    bool ServiceFound,
    bool ServiceRunning,
    bool LogExists,
    bool HasAccess,
    string? ServiceName,
    string? LogName,
    string? AccessError,
    IReadOnlyList<string> Recommendations);
