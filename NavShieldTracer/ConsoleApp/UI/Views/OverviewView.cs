using System;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using NavShieldTracer.Modules.Diagnostics;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View de visão geral do sistema (Status, Sysmon, Banco de dados).
/// </summary>
public sealed class OverviewView : IConsoleView
{
    private readonly ViewContext _context;

    public OverviewView(ViewContext context)
    {
        _context = context;
    }

    public IRenderable BuildContent(DateTime now)
    {
        var dashboard = _context.AppService.GetDashboardSnapshot();
        var sysmon = _context.AppService.CurrentStatus;

        var grid = new Grid()
            .AddColumn()
            .AddRow("[yellow bold]Status do Sistema[/]")
            .AddRow("")
            .AddRow($"[grey]Servico Sysmon:[/] [{(sysmon.ServiceRunning ? "green" : "red")}]{(sysmon.ServiceRunning ? "Em execucao" : "Parado")}[/]")
            .AddRow($"[grey]Acesso ao Log:[/] [{(sysmon.HasAccess ? "green" : "red")}]{(sysmon.HasAccess ? "OK" : "Sem permissao")}[/]")
            .AddRow($"[grey]Status Geral:[/] [{(sysmon.IsReady ? "green" : "yellow")}]{(sysmon.IsReady ? "Pronto" : "Atencao")}[/]")
            .AddRow("")
            .AddRow("[yellow bold]Banco de Dados[/]")
            .AddRow("")
            .AddRow($"[grey]Testes catalogados:[/] [cyan1]{dashboard.TotalTestes}[/]")
            .AddRow($"[grey]Eventos armazenados:[/] [cyan1]{dashboard.TotalEventos}[/]")
            .AddRow($"[grey]Caminho:[/] [grey]{Markup.Escape(dashboard.DatabasePath)}[/]");

        if (sysmon.Recommendations.Count > 0)
        {
            grid.AddRow("").AddRow("[yellow bold]Recomendacoes:[/]").AddRow("");
            foreach (var rec in sysmon.Recommendations.Take(3))
            {
                grid.AddRow($"[yellow]• {Markup.Escape(rec)}[/]");
            }
        }

        return grid;
    }

    public Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        // Overview não tem comandos específicos em modo navegação
        return Task.CompletedTask;
    }

    public Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        // Overview não tem modo de edição
        return Task.CompletedTask;
    }

    public InputMode? GetDefaultEditMode()
    {
        // Overview não entra em modo de edição com Enter
        return null;
    }
}
