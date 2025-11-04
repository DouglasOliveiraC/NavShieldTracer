using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NavShieldTracer.Modules.Heuristics.Engine;
using NavShieldTracer.Modules.Models;
using Spectre.Console;
using Spectre.Console.Rendering;
using NormalizationThreatSeverityTarja = NavShieldTracer.Modules.Heuristics.Normalization.ThreatSeverityTarja;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View para visualização e exportação de testes catalogados.
/// </summary>
public sealed class TestsView : IConsoleView
{
    private readonly ViewContext _context;
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(3); // Atualiza a cada 3 segundos

    private string _testsIdInput = string.Empty;
    private string _sessionIdInput = string.Empty;
    private int? _selectedSessionId;
    private SessaoMonitoramento? _selectedSession;
    private SessionStats? _selectedSessionStats;
    private ViewMode _viewMode = ViewMode.Testes;
    private const int TestsPageSize = 10;
    private const int SessionsPageSize = 15;
    private const int AlertsPageSize = 20;
    private int _testsPageIndex;
    private int _testsTotalPages;
    private int _sessionsPageIndex;
    private int _sessionsTotalPages;
    private int _alertsPageIndex;
    private int _alertsTotalPages;

    private enum ViewMode
    {
        Testes,
        Sessoes,
        Alertas
    }

    /// <summary>
    /// Cria uma nova instância da view de testes.
    /// </summary>
    /// <param name="context">Contexto compartilhado de views.</param>
    public TestsView(ViewContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Constrói o conteúdo visual da view.
    /// </summary>
    /// <param name="now">Timestamp atual.</param>
    public IRenderable BuildContent(DateTime now)
    {
        var grid = new Grid().AddColumn();

        // Tabs para alternar entre Testes, Sessoes e Alertas
        var testsTab = _viewMode == ViewMode.Testes
            ? "[dodgerblue1 bold][[T]] Testes[/]"
            : "[grey][[T]] Testes[/]";
        var sessionsTab = _viewMode == ViewMode.Sessoes
            ? "[dodgerblue1 bold][[S]] Sessoes[/]"
            : "[grey][[S]] Sessoes[/]";
        var alertsTab = _viewMode == ViewMode.Alertas
            ? "[dodgerblue1 bold][[A]] Alertas[/]"
            : "[grey][[A]] Alertas[/]";
        grid.AddRow($"{testsTab}  {sessionsTab}  {alertsTab}");
        grid.AddRow("");

        switch (_viewMode)
        {
            case ViewMode.Testes:
                BuildTestesContent(grid);
                break;
            case ViewMode.Sessoes:
                BuildSessoesContent(grid);
                break;
            case ViewMode.Alertas:
            default:
                BuildAlertasContent(grid);
                break;
        }

        return grid;
    }

    private void BuildTestesContent(Grid grid)
    {
        var testes = _context.AppService.ListarTestes();

        var totalTestes = testes.Count;
        var maxPageIndex = totalTestes <= 0 ? 0 : (int)Math.Ceiling(totalTestes / (double)TestsPageSize) - 1;
        if (maxPageIndex < 0)
        {
            maxPageIndex = 0;
        }

        int pageIndex;
        lock (_context.StateLock)
        {
            if (_testsPageIndex > maxPageIndex)
            {
                _testsPageIndex = maxPageIndex;
            }
            else if (_testsPageIndex < 0)
            {
                _testsPageIndex = 0;
            }

            _testsTotalPages = maxPageIndex;
            pageIndex = _testsPageIndex;
        }

        var totalPaginas = Math.Max(1, maxPageIndex + 1);
        var paginaAtual = totalTestes == 0 ? 0 : pageIndex + 1;

        grid.AddRow("[blue bold]Testes Catalogados[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total:[/] [cyan1]{totalTestes}[/]  [grey]Pagina:[/] [cyan1]{paginaAtual}/{totalPaginas}[/]");

        var idDisplay = $"[cyan1]{Markup.Escape(_testsIdInput)}[/]";
        grid.AddRow($"[grey]ID para exportar:[/] {idDisplay}");
        grid.AddRow("");
        grid.AddRow("[grey]Comandos: [[Enter]] Editar ID  [[E]] Exportar  [[< / PgUp]] Pagina anterior  [[> / PgDn]] Proxima  [[Home]] Inicio  [[End]] Final[/]");

        if (totalTestes == 0)
        {
            grid.AddRow("[grey]Nenhum teste catalogado ainda.[/]");
            return;
        }

        grid.AddRow("");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("[grey]ID[/]")
            .AddColumn("[grey]Numero[/]")
            .AddColumn("[grey]Nome[/]")
            .AddColumn("[grey]Execucao[/]")
            .AddColumn("[grey]Eventos[/]")
            .AddColumn("[grey]Sessao[/]");

        var offset = pageIndex * TestsPageSize;
        var pagina = testes
            .OrderByDescending(t => t.DataExecucao)
            .ThenBy(t => t.Id)
            .Skip(offset)
            .Take(TestsPageSize);

        foreach (var teste in pagina)
        {
            table.AddRow(
                $"[cyan1]{teste.Id}[/]",
                $"[yellow]{Markup.Escape(teste.Numero)}[/]",
                Markup.Escape(teste.Nome),
                teste.DataExecucao.ToString("dd/MM HH:mm"),
                $"[cyan1]{teste.TotalEventos}[/]",
                $"[grey]#{teste.SessionId}[/]"
            );
        }

        grid.AddRow(table);

        var exibidos = Math.Min(TestsPageSize, Math.Max(0, totalTestes - offset));
        grid.AddRow($"[grey]Mostrando {exibidos} de {totalTestes} testes[/]");
    }

    private void BuildSessoesContent(Grid grid)
    {
        var sessoes = _context.AppService.ListarSessoes();

        var totalSessoes = sessoes.Count;
        var maxPageIndex = totalSessoes <= 0 ? 0 : (int)Math.Ceiling(totalSessoes / (double)SessionsPageSize) - 1;
        if (maxPageIndex < 0)
        {
            maxPageIndex = 0;
        }

        int pageIndex;
        lock (_context.StateLock)
        {
            if (_sessionsPageIndex > maxPageIndex)
            {
                _sessionsPageIndex = maxPageIndex;
            }
            else if (_sessionsPageIndex < 0)
            {
                _sessionsPageIndex = 0;
            }

            _sessionsTotalPages = maxPageIndex;
            pageIndex = _sessionsPageIndex;
        }

        var totalPaginas = Math.Max(1, maxPageIndex + 1);
        var paginaAtual = totalSessoes == 0 ? 0 : pageIndex + 1;

        grid.AddRow("[blue bold]Sessoes de Monitoramento[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total:[/] [cyan1]{totalSessoes}[/]  [grey]Pagina:[/] [cyan1]{paginaAtual}/{totalPaginas}[/]");

        var currentInputMode = _context.GetCurrentInputMode();
        var sessionIdLabel = currentInputMode == InputMode.EditingSessionId ? "[black on yellow]ID da sessao:[/]" : "[grey]ID da sessao:[/]";
        var sessionIdDisplay = currentInputMode == InputMode.EditingSessionId
            ? $"[black on yellow]{Markup.Escape(_sessionIdInput)}[/]"
            : $"[cyan1]{Markup.Escape(_sessionIdInput)}[/]";
        grid.AddRow($"{sessionIdLabel} {sessionIdDisplay}");

        if (_selectedSessionId.HasValue && _selectedSession != null)
        {
            grid.AddRow($"[grey]ID carregado:[/] [green]{_selectedSessionId.Value}[/]");
        }

        grid.AddRow("");
        grid.AddRow("[grey]Comandos: [[Enter]] Editar ID  [[L]] Carregar sessao  [[E]] Exportar  [[< / PgUp]] Pagina anterior  [[> / PgDn]] Proxima  [[Home]] Inicio  [[End]] Final[/]");
        grid.AddRow("");

        if (totalSessoes == 0)
        {
            grid.AddRow("[grey]Nenhuma sessao encontrada.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("[grey]ID[/]")
            .AddColumn("[grey]Processo[/]")
            .AddColumn("[grey]Inicio[/]")
            .AddColumn("[grey]Fim[/]")
            .AddColumn("[grey]Duracao[/]")
            .AddColumn("[grey]Eventos[/]")
            .AddColumn("[grey]Status[/]");

        var offset = pageIndex * SessionsPageSize;
        var pagina = sessoes
            .OrderByDescending(s => s.StartedAt)
            .ThenByDescending(s => s.Id)
            .Skip(offset)
            .Take(SessionsPageSize);

        foreach (var sessao in pagina)
        {
            var duracao = sessao.EndedAt.HasValue
                ? (sessao.EndedAt.Value - sessao.StartedAt).TotalMinutes.ToString("F1") + "min"
                : "-";

            var status = sessao.EndedAt.HasValue
                ? "[green]Encerrada[/]"
                : "[yellow]Ativa[/]";

            var fim = sessao.EndedAt.HasValue
                ? sessao.EndedAt.Value.ToString("dd/MM HH:mm")
                : "-";

            table.AddRow(
                $"[cyan1]{sessao.Id}[/]",
                Markup.Escape(sessao.TargetProcess),
                sessao.StartedAt.ToString("dd/MM HH:mm"),
                fim,
                duracao,
                $"[cyan1]{sessao.TotalEventos}[/]",
                status
            );
        }

        grid.AddRow(table);

        var exibidos = Math.Min(SessionsPageSize, Math.Max(0, totalSessoes - offset));
        grid.AddRow($"[grey]Mostrando {exibidos} de {totalSessoes} sessoes[/]");

        // Exibir detalhes da sessao selecionada
        if (_selectedSession != null)
        {
            grid.AddRow("");
            grid.AddRow("[blue bold]Detalhes da Sessao[/]");

            var detailsTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .HideHeaders()
                .AddColumn(new TableColumn("[grey]Campo[/]").NoWrap().Width(20))
                .AddColumn(new TableColumn("[grey]Valor[/]"));

            detailsTable.AddRow("[grey]ID[/]", $"[cyan1]{_selectedSession.Id}[/]");
            detailsTable.AddRow("[grey]Processo Alvo[/]", $"[yellow]{Markup.Escape(_selectedSession.TargetProcess)}[/]");
            detailsTable.AddRow("[grey]PID Raiz[/]", $"[cyan1]{_selectedSession.RootPid}[/]");
            detailsTable.AddRow("[grey]Host[/]", Markup.Escape(_selectedSession.Host));
            detailsTable.AddRow("[grey]Usuario[/]", Markup.Escape(_selectedSession.User));
            detailsTable.AddRow("[grey]OS Version[/]", $"[grey70]{Markup.Escape(_selectedSession.OsVersion)}[/]");
            detailsTable.AddRow("[grey]Inicio[/]", $"[cyan1]{_selectedSession.StartedAt:dd/MM/yyyy HH:mm:ss}[/]");

            if (_selectedSession.EndedAt.HasValue)
            {
                detailsTable.AddRow("[grey]Fim[/]", $"[cyan1]{_selectedSession.EndedAt.Value:dd/MM/yyyy HH:mm:ss}[/]");
                var duracao = (_selectedSession.EndedAt.Value - _selectedSession.StartedAt).TotalMinutes;
                detailsTable.AddRow("[grey]Duracao[/]", $"[cyan1]{duracao:F2} minutos[/]");
                detailsTable.AddRow("[grey]Status[/]", "[green]Encerrada[/]");
            }
            else
            {
                detailsTable.AddRow("[grey]Status[/]", "[yellow]Ativa[/]");
            }

            detailsTable.AddRow("[grey]Total de Eventos[/]", $"[cyan1]{_selectedSession.TotalEventos}[/]");

            grid.AddRow(detailsTable);

            // Exibir observações formatadas (se houver)
            if (!string.IsNullOrWhiteSpace(_selectedSession.Notes))
            {
                grid.AddRow("");
                grid.AddRow("[blue bold]Resumo da Sessao[/]");
                FormatarObservacoes(_selectedSession.Notes, grid);
            }

            // Exibir estatísticas da sessao
            if (_selectedSessionStats != null)
            {
                grid.AddRow("");
                grid.AddRow("[blue bold]Estatisticas da Sessao[/]");

                // Tarja do teste associado (se houver)
                if (!string.IsNullOrWhiteSpace(_selectedSessionStats.TarjaTesteAssociado))
                {
                    var tarjaColor = _selectedSessionStats.TarjaTesteAssociado.ToLowerInvariant() switch
                    {
                        "verde" => "green",
                        "amarelo" => "yellow",
                        "laranja" => "orange1",
                        "vermelho" => "red",
                        _ => "grey"
                    };
                    grid.AddRow($"[grey]Tarja (Teste Associado):[/] [{tarjaColor}]{_selectedSessionStats.TarjaTesteAssociado}[/]");
                    grid.AddRow("");
                }

                // Distribuição de eventos
                if (_selectedSessionStats.EventosPorTipo.Count > 0)
                {
                    grid.AddRow("[grey bold]Top Event IDs:[/]");
                    var eventTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Blue)
                        .AddColumn("[grey]Event ID[/]")
                        .AddColumn("[grey]Descricao[/]")
                        .AddColumn("[grey]Quantidade[/]");

                    foreach (var (eventId, count) in _selectedSessionStats.EventosPorTipo.OrderByDescending(kv => kv.Value).Take(10))
                    {
                        var descricao = GetEventDescription(eventId);
                        eventTable.AddRow(
                            $"[cyan1]{eventId}[/]",
                            descricao,
                            $"[yellow]{count}[/]"
                        );
                    }

                    grid.AddRow(eventTable);
                }

                // IPs de destino
                if (_selectedSessionStats.TopIps.Count > 0)
                {
                    grid.AddRow("");
                    grid.AddRow("[grey bold]IPs de Destino (Top 10):[/]");
                    foreach (var ip in _selectedSessionStats.TopIps.Take(10))
                    {
                        grid.AddRow($"  [cyan1]{Markup.Escape(ip)}[/]");
                    }
                }

                // Domínios DNS
                if (_selectedSessionStats.TopDomains.Count > 0)
                {
                    grid.AddRow("");
                    grid.AddRow("[grey bold]Dominios DNS (Top 10):[/]");
                    foreach (var domain in _selectedSessionStats.TopDomains.Take(10))
                    {
                        grid.AddRow($"  [cyan1]{Markup.Escape(domain)}[/]");
                    }
                }

                // Processos criados
                if (_selectedSessionStats.ProcessosCriados.Count > 0)
                {
                    grid.AddRow("");
                    grid.AddRow($"[grey bold]Processos Criados ({_selectedSessionStats.ProcessosCriados.Count}):[/]");
                    foreach (var proc in _selectedSessionStats.ProcessosCriados.Take(15))
                    {
                        grid.AddRow($"  [white]{Markup.Escape(proc)}[/]");
                    }
                }
            }
        }
    }

    private void BuildAlertasContent(Grid grid)
    {
        var totalAlertas = _context.AppService.ContarAlertas();

        var maxPageIndex = totalAlertas <= 0
            ? 0
            : (int)Math.Ceiling(totalAlertas / (double)AlertsPageSize) - 1;
        if (maxPageIndex < 0)
        {
            maxPageIndex = 0;
        }

        int pageIndex;
        lock (_context.StateLock)
        {
            if (_alertsPageIndex > maxPageIndex)
            {
                _alertsPageIndex = maxPageIndex;
            }
            else if (_alertsPageIndex < 0)
            {
                _alertsPageIndex = 0;
            }

            _alertsTotalPages = maxPageIndex;
            pageIndex = _alertsPageIndex;
        }

        grid.AddRow("[blue bold]Historico de Alertas[/]");
        grid.AddRow("");
        var totalPaginas = Math.Max(1, maxPageIndex + 1);
        var paginaAtual = totalAlertas == 0 ? 0 : pageIndex + 1;
        grid.AddRow($"[grey]Total:[/] [cyan1]{totalAlertas}[/]  [grey]Pagina:[/] [cyan1]{paginaAtual}/{totalPaginas}[/]");
        grid.AddRow("[grey]Comandos: [[A]] Alertas  [[< / PgUp]] Pagina anterior  [[> / PgDn]] Proxima  [[Home]] Inicio  [[End]] Final  [[R]] Atualizar[/]");
        grid.AddRow("");

        if (totalAlertas == 0)
        {
            grid.AddRow("[grey]Nenhum alerta registrado ate o momento.[/]");
            return;
        }

        var offset = pageIndex * AlertsPageSize;
        var alertas = _context.AppService.ListarAlertas(offset, AlertsPageSize);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("[grey]Data/Hora[/]")
            .AddColumn("[grey]Sessao[/]")
            .AddColumn("[grey]Anterior[/]")
            .AddColumn("[grey]Novo[/]")
            .AddColumn("[grey]Tecnica[/]")
            .AddColumn("[grey]Similaridade[/]")
            .AddColumn("[grey]Motivo[/]");

        foreach (var alerta in alertas)
        {
            var timestamp = alerta.Timestamp.ToString("dd/MM HH:mm:ss");
            var tecnica = string.IsNullOrWhiteSpace(alerta.TriggerTechniqueId)
                ? "[grey]-[/]"
                : $"[cyan1]{Markup.Escape(alerta.TriggerTechniqueId!)}[/]";
            var similarity = alerta.TriggerSimilarity.HasValue
                ? $"[yellow]{alerta.TriggerSimilarity.Value:P0}[/]"
                : "[grey]-[/]";
            var motivo = string.IsNullOrWhiteSpace(alerta.Reason)
                ? "[grey]-[/]"
                : Markup.Escape(Truncate(alerta.Reason, 80));

            table.AddRow(
                $"[cyan1]{timestamp}[/]",
                $"[grey]#{alerta.SessionId}[/]",
                FormatTarja(alerta.PreviousLevel),
                FormatTarja(alerta.NewLevel),
                tecnica,
                similarity,
                motivo);
        }

        grid.AddRow(table);
        var exibidos = Math.Min(AlertsPageSize, Math.Max(0, totalAlertas - offset));
        grid.AddRow($"[grey]Mostrando {exibidos} de {totalAlertas} alertas.[/]");
    }
    private static string FormatTarja(NormalizationThreatSeverityTarja? tarja) => tarja.HasValue ? FormatTarja(tarja.Value) : "[grey]-[/]";

    private static string FormatTarja(NormalizationThreatSeverityTarja tarja)
    {
        return tarja switch
        {
            NormalizationThreatSeverityTarja.Verde => "[green]Verde[/]",
            NormalizationThreatSeverityTarja.Amarelo => "[yellow]Amarelo[/]",
            NormalizationThreatSeverityTarja.Laranja => "[orange1]Laranja[/]",
            NormalizationThreatSeverityTarja.Vermelho => "[red]Vermelho[/]",
            _ => $"[grey]{tarja}[/]"
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= 3)
        {
            return value.Substring(0, maxLength);
        }

        return value.Substring(0, maxLength - 3) + "...";
    }
    private static string GetEventDescription(int eventId)
    {
        return eventId switch
        {
            1 => "Process Create",
            2 => "File Creation Time",
            3 => "Network Connection",
            5 => "Process Terminated",
            6 => "Driver Loaded",
            7 => "Image/DLL Loaded",
            8 => "CreateRemoteThread",
            9 => "RawAccessRead",
            10 => "Process Access",
            11 => "File Create",
            12 => "Registry Create/Delete",
            13 => "Registry Value Set",
            14 => "Registry Rename",
            15 => "File Stream Create",
            17 => "Pipe Created",
            18 => "Pipe Connected",
            19 => "WMI Event Filter",
            20 => "WMI Event Consumer",
            21 => "WMI Binding",
            22 => "DNS Query",
            23 => "File Delete",
            24 => "Clipboard Change",
            25 => "Process Tampering",
            26 => "File Delete Detected",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Processa entrada do usuário em modo navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    public async Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        // Alternar entre abas
        if (key.Key == ConsoleKey.T)
        {
            lock (_context.StateLock)
            {
                _viewMode = ViewMode.Testes;
                _context.RequestRefresh();
            }
            return;
        }

        if (key.Key == ConsoleKey.S)
        {
            lock (_context.StateLock)
            {
                _viewMode = ViewMode.Sessoes;
                _context.RequestRefresh();
            }
            return;
        }

        if (key.Key == ConsoleKey.A)
        {
            lock (_context.StateLock)
            {
                _viewMode = ViewMode.Alertas;
                _context.RequestRefresh();
            }
            return;
        }

        if (_viewMode == ViewMode.Testes)
        {
            var handledPaging = false;
            lock (_context.StateLock)
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.PageUp:
                        if (_testsPageIndex > 0)
                        {
                            _testsPageIndex--;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.PageDown:
                        if (_testsPageIndex < _testsTotalPages)
                        {
                            _testsPageIndex++;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.Home:
                        if (_testsPageIndex != 0)
                        {
                            _testsPageIndex = 0;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.End:
                        if (_testsPageIndex != _testsTotalPages)
                        {
                            _testsPageIndex = _testsTotalPages;
                            handledPaging = true;
                        }
                        break;
                }
            }

            if (handledPaging)
            {
                _context.RequestRefresh();
                return;
            }
        }
        if (_viewMode == ViewMode.Alertas)
        {
            var changed = false;
            lock (_context.StateLock)
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.PageUp:
                        if (_alertsPageIndex > 0)
                        {
                            _alertsPageIndex--;
                            changed = true;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.PageDown:
                        if (_alertsPageIndex < _alertsTotalPages)
                        {
                            _alertsPageIndex++;
                            changed = true;
                        }
                        break;
                    case ConsoleKey.Home:
                        if (_alertsPageIndex != 0)
                        {
                            _alertsPageIndex = 0;
                            changed = true;
                        }
                        break;
                    case ConsoleKey.End:
                        if (_alertsPageIndex != _alertsTotalPages)
                        {
                            _alertsPageIndex = _alertsTotalPages;
                            changed = true;
                        }
                        break;
                    case ConsoleKey.R:
                        changed = true;
                        break;
                }
            }

            if (changed)
            {
                _context.RequestRefresh();
            }
            return;
        }

        // Comandos na aba de sessoes
        if (_viewMode == ViewMode.Sessoes)
        {
            var handledPaging = false;
            lock (_context.StateLock)
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.PageUp:
                        if (_sessionsPageIndex > 0)
                        {
                            _sessionsPageIndex--;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.PageDown:
                        if (_sessionsPageIndex < _sessionsTotalPages)
                        {
                            _sessionsPageIndex++;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.Home:
                        if (_sessionsPageIndex != 0)
                        {
                            _sessionsPageIndex = 0;
                            handledPaging = true;
                        }
                        break;
                    case ConsoleKey.End:
                        if (_sessionsPageIndex != _sessionsTotalPages)
                        {
                            _sessionsPageIndex = _sessionsTotalPages;
                            handledPaging = true;
                        }
                        break;
                }
            }

            if (handledPaging)
            {
                _context.RequestRefresh();
                return;
            }
            if (key.Key == ConsoleKey.L)
            {
                LoadSession();
                return;
            }

            if (key.Key == ConsoleKey.E)
            {
                await ExportSessionAsync().ConfigureAwait(false);
                return;
            }
        }

        // Exportar teste (apenas na aba de testes)
        if (key.Key == ConsoleKey.E && _viewMode == ViewMode.Testes)
        {
            if (!int.TryParse(_testsIdInput, out var id))
            {
                _context.SetStatusMessage("Erro: ID invalido");
                return;
            }

            try
            {
                var path = await _context.AppService.ExportarTesteAsync(id).ConfigureAwait(false);
                _context.SetStatusMessage($"Exportado: {path}");
            }
            catch (Exception ex)
            {
                _context.SetStatusMessage($"Erro: {ex.Message}");
            }
        }
    }
    /// <summary>
    /// Processa entrada do usuário em modo edição.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <param name="mode">Modo de edição atual.</param>
    public Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        if (mode == InputMode.EditingTestId)
        {
            HandleEditTestId(key);
        }
        else if (mode == InputMode.EditingSessionId)
        {
            HandleEditSessionId(key);
        }

        return Task.CompletedTask;
    }

    private void HandleEditTestId(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_testsIdInput.Length > 0)
                {
                    _testsIdInput = _testsIdInput[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (char.IsDigit(key.KeyChar) && _testsIdInput.Length < 9)
        {
            lock (_context.StateLock)
            {
                _testsIdInput += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditSessionId(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_sessionIdInput.Length > 0)
                {
                    _sessionIdInput = _sessionIdInput[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (char.IsDigit(key.KeyChar) && _sessionIdInput.Length < 9)
        {
            lock (_context.StateLock)
            {
                _sessionIdInput += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    /// <summary>
    /// Retorna o modo de edição padrão ao pressionar Enter.
    /// </summary>
    public InputMode? GetDefaultEditMode()
    {
        return _viewMode == ViewMode.Testes ? InputMode.EditingTestId : InputMode.EditingSessionId;
    }

    /// <summary>
    /// Define o valor do input de ID.
    /// </summary>
    /// <param name="value">Novo valor.</param>
    public void SetIdInput(string value)
    {
        lock (_context.StateLock)
        {
            _testsIdInput = value;
        }
    }

    private void LoadSession()
    {
        if (!int.TryParse(_sessionIdInput, out var id))
        {
            _context.SetStatusMessage("Informe um ID válido para carregar");
            return;
        }

        var sessoes = _context.AppService.ListarSessoes();
        var sessao = sessoes.FirstOrDefault(s => s.Id == id);

        if (sessao == null)
        {
            _context.SetStatusMessage($"Sessão com ID {id} não encontrada");
            return;
        }

        // Carregar estatísticas
        var stats = _context.AppService.ObterEstatisticasSessao(id);

        lock (_context.StateLock)
        {
            _selectedSessionId = id;
            _selectedSession = sessao;
            _selectedSessionStats = stats;
        }

        _context.SetStatusMessage($"Sessão {id} carregada com sucesso");
        _context.RequestRefresh();
    }

    private async Task ExportSessionAsync()
    {
        if (!_selectedSessionId.HasValue)
        {
            _context.SetStatusMessage("Carregue uma sessão primeiro (comando L)");
            return;
        }

        try
        {
            var path = await _context.AppService.ExportarSessaoAsync(_selectedSessionId.Value).ConfigureAwait(false);
            _context.SetStatusMessage($"Exportado: {path}");
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro ao exportar: {ex.Message}");
        }
    }

    /// <summary>
    /// Formata as observações da sessão (JSON ou texto) para exibição legível
    /// </summary>
    private static void FormatarObservacoes(string notes, Grid grid)
    {
        try
        {
            // Tentar fazer parse do JSON
            using var doc = JsonDocument.Parse(notes);
            var root = doc.RootElement;

            // Criar tabela para exibir campos principais
            var notesTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .HideHeaders()
                .AddColumn(new TableColumn("[grey]Campo[/]").NoWrap().Width(30))
                .AddColumn(new TableColumn("[grey]Valor[/]"));

            // Extrair campos principais do JSON
            if (root.TryGetProperty("ProcessosAtivos", out var processosAtivos))
                notesTable.AddRow("[grey]Processos Ativos:[/]", $"[yellow]{processosAtivos.GetInt32()}[/]");

            if (root.TryGetProperty("TotalProcessosRastreados", out var totalProcessos))
                notesTable.AddRow("[grey]Total Processos Rastreados:[/]", $"[cyan1]{totalProcessos.GetInt32()}[/]");

            if (root.TryGetProperty("ProcessosEncerrados", out var processosEncerrados))
                notesTable.AddRow("[grey]Processos Encerrados:[/]", $"[cyan1]{processosEncerrados.GetInt32()}[/]");

            if (root.TryGetProperty("TempoMedioDeVidaEncerrados", out var tempoMedio))
            {
                var tempo = tempoMedio.GetString() ?? "";
                if (TimeSpan.TryParse(tempo, out var ts))
                    notesTable.AddRow("[grey]Tempo Medio de Vida:[/]", $"[cyan1]{ts:hh\\:mm\\:ss}[/]");
            }

            if (root.TryGetProperty("ProcessosAtivosDetalhados", out var processosDetalhados)
                && processosDetalhados.ValueKind == JsonValueKind.Array)
            {
                var count = processosDetalhados.GetArrayLength();
                if (count > 0)
                {
                    notesTable.AddRow("[grey]Processos Detalhados:[/]", $"[grey]{count} processos (ver JSON exportado)[/]");
                }
            }

            grid.AddRow(notesTable);
        }
        catch (JsonException)
        {
            // Se não for JSON válido, exibir como texto simples com quebra de linha
            var lines = notes.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var trimmed = line.Trim();
                    var display = trimmed.Length > 100 ? trimmed.Substring(0, 100) + "..." : trimmed;
                    grid.AddRow($"[grey]{Markup.Escape(display)}[/]");
                }
            }
        }
    }

    /// <summary>
    /// Obtém o valor atual do input de ID.
    /// </summary>
    public string GetIdInput()
    {
        lock (_context.StateLock)
        {
            return _testsIdInput;
        }
    }
}


