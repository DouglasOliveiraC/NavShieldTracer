using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;

namespace NavShieldTracer.Modules.Diagnostics;

/// <summary>
/// Classe utilitaria para diagnosticar a disponibilidade e configuracao do Sysmon no sistema.
/// </summary>
/// <remarks>
/// Verifica tres aspectos principais:
/// - Privilegios de administrador do usuario atual
/// - Estado do servico do Sysmon (instalado e em execucao)
/// - Disponibilidade e acessibilidade do canal de log do Windows Event Log
/// </remarks>
internal static class SysmonDiagnostics
{
    /// <summary>Nome padrao do canal de eventos operacional do Sysmon.</summary>
    private const string DefaultLogName = "Microsoft-Windows-Sysmon/Operational";

    /// <summary>Nomes candidatos do servico Sysmon (x64 e x86).</summary>
    private static readonly string[] CandidateServiceNames = { "Sysmon64", "Sysmon" };

    /// <summary>
    /// Coleta status completo do Sysmon incluindo diagnostico e recomendacoes.
    /// </summary>
    /// <returns>Objeto SysmonStatus com estado detalhado e sugestoes de correcao</returns>
    /// <remarks>
    /// Este metodo:
    /// - Verifica se o processo esta rodando como administrador
    /// - Detecta presenca e estado do servico Sysmon
    /// - Verifica acessibilidade do canal de log
    /// - Gera lista de recomendacoes especificas para resolver problemas encontrados
    /// - Determina se o sistema esta pronto para monitoramento (IsReady = true se tudo estiver OK)
    /// </remarks>
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

    /// <summary>
    /// Detecta a presenca e estado de execucao do servico Sysmon.
    /// </summary>
    /// <returns>Tupla com (servico encontrado, servico em execucao, nome do servico)</returns>
    /// <remarks>
    /// Verifica os nomes de servico "Sysmon64" e "Sysmon" (nessa ordem).
    /// Considera servico como rodando se Status for Running ou StartPending.
    /// Retorna (false, false, null) se nenhum servico for encontrado ou se houver excecao.
    /// </remarks>
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

    /// <summary>
    /// Detecta a existencia e acessibilidade do canal de log do Sysmon no Windows Event Log.
    /// </summary>
    /// <returns>Tupla com (log existe, nome do log, tem acesso, erro de acesso)</returns>
    /// <remarks>
    /// Busca primeiro pelo nome padrao "Microsoft-Windows-Sysmon/Operational".
    /// Se nao encontrar, busca por qualquer canal com "Sysmon" no nome.
    /// Tenta ler um evento para verificar permissoes de acesso.
    /// Trata especificamente EventLogNotFoundException e UnauthorizedAccessException.
    /// </remarks>
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

/// <summary>
/// Representa o status completo do Sysmon no sistema e recomendacoes para correcao de problemas.
/// </summary>
/// <param name="IsReady">Indica se o sistema esta pronto para monitoramento (todos os pre-requisitos atendidos)</param>
/// <param name="IsAdministrator">Indica se o processo esta rodando com privilegios de administrador</param>
/// <param name="ServiceFound">Indica se o servico do Sysmon foi encontrado no sistema</param>
/// <param name="ServiceRunning">Indica se o servico do Sysmon esta em execucao</param>
/// <param name="LogExists">Indica se o canal de log do Sysmon existe no Windows Event Log</param>
/// <param name="HasAccess">Indica se o processo consegue ler eventos do canal do Sysmon</param>
/// <param name="ServiceName">Nome do servico Sysmon detectado (ex: "Sysmon64" ou "Sysmon")</param>
/// <param name="LogName">Nome do canal de log detectado (ex: "Microsoft-Windows-Sysmon/Operational")</param>
/// <param name="AccessError">Mensagem de erro de acesso se HasAccess for false</param>
/// <param name="Recommendations">Lista de recomendacoes especificas para corrigir problemas encontrados</param>
public sealed record SysmonStatus(
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
