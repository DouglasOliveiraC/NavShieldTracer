using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NavShieldTracer.Modules.Diagnostics;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using NavShieldTracer.ConsoleApp.Services;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NavShieldTracer.ConsoleApp.UI;

/// <summary>
/// Interface de console otimizada usando Spectre.Console LiveDisplay para atualizações eficientes.
/// </summary>
public sealed class ConsoleUI
{
    private static readonly string[] BannerLines =
    {
        "███╗   ██╗█████╗ ██╗   ██╗███████╗██╗  ██╗██╗███████╗██╗     ██████╗",
        "████╗  ██║██╔══██╗██║   ██║██╔════╝██║  ██║██║██╔════╝██║     ██╔══██╗",
        "██╔██╗ ██║███████║██║   ██║███████╗███████║██║█████╗  ██║     ██║  ██║",
        "██║╚██╗██║██╔══██║╚██╗ ██╔╝╚════██║██╔══██║██║██╔══╝  ██║     ██║  ██║",
        "██║ ╚████║██║  ██║ ╚████╔╝ ███████║██║  ██║██║███████╗███████╗██████╔╝",
        "╚═╝  ╚═══╝╚═╝  ╚═╝  ╚═══╝ ╚══════╝╚═╝  ╚═╝╚═╝╚══════╝╚══════╝╚═════╝ "
    };

    private const int MaxProcessosVisiveis = 8;
    private const int MaxLogsVisiveis = 6;
    private const int MaxProcessSelectionOptions = 10;

    private readonly NavShieldAppService _appService;
    private readonly object _stateLock = new();
    private AppView _currentView = AppView.Overview;
    private InputMode _inputMode = InputMode.Navigation;
    private CancellationTokenSource? _uiLoop;
    private bool _forceRefresh = false;

    // State para cada view
    private string _monitorTarget = string.Empty;
    private string _catalogNumero = string.Empty;
    private string _catalogNome = string.Empty;
    private string _catalogDescricao = string.Empty;
    private string _catalogTarget = "teste.exe";
    private string _testsIdInput = string.Empty;
    private string _manageIdInput = string.Empty;
    private int? _manageSelectedId;
    private string _manageNumero = string.Empty;
    private string _manageNome = string.Empty;
    private string _manageDescricao = string.Empty;
    private ResumoTesteAtomico? _manageResumo;
    private string? _statusMessage;
    private IReadOnlyList<ProcessSnapshot> _monitorSuggestions = Array.Empty<ProcessSnapshot>();
    private IReadOnlyList<ProcessSnapshot> _topProcesses = Array.Empty<ProcessSnapshot>();
    private IReadOnlyList<ProcessSnapshot> _processSelectionOptions = Array.Empty<ProcessSnapshot>();
    private ProcessSelectionSource _processSelectionSource = ProcessSelectionSource.None;
    private DateTime _lastSuggestionsUpdate = DateTime.MinValue;
    private DateTime _lastTopProcessesUpdate = DateTime.MinValue;
    private int _selectedProcessIndex = -1;

    /// <summary>
    /// Inicializa uma nova instância da interface de console.
    /// </summary>
    /// <param name="appService">Serviço da aplicação NavShieldTracer.</param>
    public ConsoleUI(NavShieldAppService appService)
    {
        _appService = appService;
    }

    /// <summary>
    /// Executa a interface de console de forma assíncrona.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento para encerrar a UI.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Console.Clear();
        Console.CursorVisible = false;

        await _appService.InitializeAsync().ConfigureAwait(false);

        try
        {
            await RunInteractiveUIAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.CursorVisible = true;
            Console.Clear();
        }
    }

    private async Task RunInteractiveUIAsync(CancellationToken cancellationToken)
    {
        _uiLoop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var uiToken = _uiLoop.Token;

        await AnsiConsole.Live(BuildMainLayout())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Visible)
            .Cropping(VerticalOverflowCropping.Bottom)
            .StartAsync(async ctx =>
            {
                var refreshTask = Task.Run(async () =>
                {
                    var lastRender = DateTime.MinValue;
                    var lastStatusRefresh = DateTime.MinValue;
                    var periodicInterval = TimeSpan.FromMilliseconds(250);
                    var statusInterval = TimeSpan.FromSeconds(1);

                    while (!uiToken.IsCancellationRequested)
                    {
                        try
                        {
                            bool forcedRefresh;
                            lock (_stateLock)
                            {
                                forcedRefresh = _forceRefresh;
                                if (_forceRefresh)
                                {
                                    _forceRefresh = false;
                                }
                            }

                            var now = DateTime.Now;
                            var shouldRefresh = forcedRefresh || (now - lastRender) >= periodicInterval;

                            if (shouldRefresh)
                            {
                                if (forcedRefresh || (now - lastStatusRefresh) >= statusInterval)
                                {
                                    await _appService.RefreshSysmonStatusAsync().ConfigureAwait(false);
                                    lastStatusRefresh = DateTime.Now;
                                }

                                ctx.UpdateTarget(BuildMainLayout());
                                lastRender = DateTime.Now;
                            }

                            var delay = forcedRefresh ? TimeSpan.FromMilliseconds(25) : TimeSpan.FromMilliseconds(80);
                            await Task.Delay(delay, uiToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, uiToken);

                var inputTask = Task.Run(async () =>
                {
                    while (!uiToken.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            await HandleInputAsync(key).ConfigureAwait(false);
                        }
                        await Task.Delay(50, uiToken).ConfigureAwait(false);
                    }
                }, uiToken);

                await Task.WhenAny(refreshTask, inputTask).ConfigureAwait(false);
            });
    }

    private IRenderable BuildMainLayout()
    {
        var dashboard = _appService.GetDashboardSnapshot();
        var sysmonStatus = _appService.CurrentStatus;
        var now = DateTime.Now;

        var profile = AnsiConsole.Profile;
        var compactMode = profile.Height is > 0 and <= 25;

        var sections = new List<IRenderable>();

        sections.Add((compactMode
            ? new Panel(BuildCompactBanner())
            : new Panel(BuildBanner()))
            .Border(BoxBorder.None)
            .Expand());

        sections.Add(new Panel(BuildHeader(dashboard, sysmonStatus, now))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand());

        sections.Add(new Panel(BuildNavigation())
            .Border(BoxBorder.None)
            .Padding(0, 0, 0, 1)
            .Expand());

        sections.Add(new Panel(BuildContent(dashboard, sysmonStatus, now))
            .Border(BoxBorder.Rounded)
            .Expand());

        sections.Add(new Panel(BuildFooter())
            .Border(BoxBorder.None)
            .Expand());

        return new Rows(sections);
    }

    private IRenderable BuildBanner()
    {
        var grid = new Grid().AddColumn();
        grid.AddRow(""); // Espaçamento superior
        foreach (var line in BannerLines)
        {
            grid.AddRow($"[cyan1 bold]{line}[/]");
        }
        grid.AddRow("[white]Console SecOps Toolkit v1.0[/]");
        grid.AddRow("[grey]Sistema de Classificacao de Ameacas - Baseado em Protocolos do Ministerio da Defesa[/]");
        return grid;
    }

    private IRenderable BuildCompactBanner()
    {
        return new Markup("[cyan1 bold]NavShieldTracer[/] [grey70]|[/] [grey]Console SecOps Toolkit v1.0 - Sistema de Defesa Cibernetica[/]");
    }

    private IRenderable BuildHeader(DashboardSnapshot dashboard, SysmonStatus sysmon, DateTime now)
    {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").Width(15))
            .AddColumn(new TableColumn("").Width(20))
            .AddColumn(new TableColumn("").Width(15))
            .AddColumn(new TableColumn("").Width(20))
            .AddColumn(new TableColumn("").Width(15))
            .AddColumn(new TableColumn("").Width(20));

        var sysmonColor = sysmon.ServiceRunning ? "green" : "red";
        var sysmonText = sysmon.ServiceRunning ? "Online" : "Offline";
        var sessionColor = dashboard.HasActiveSession ? "green" : "grey";
        var sessionText = dashboard.HasActiveSession ? "Ativa" : "Inativa";

        table.AddRow(
            "[grey]Horario:[/]",
            $"[cyan1]{now:HH:mm:ss}[/]",
            "[grey]Testes:[/]",
            $"[cyan1]{dashboard.TotalTestes}[/]",
            "[grey]Sysmon:[/]",
            $"[{sysmonColor}]{sysmonText}[/]"
        );

        table.AddRow(
            "[grey]Sessao:[/]",
            $"[{sessionColor}]{sessionText}[/]",
            "[grey]Eventos:[/]",
            $"[cyan1]{dashboard.TotalEventos}[/]",
            "[grey]Acesso:[/]",
            $"[{(sysmon.HasAccess ? "green" : "red")}]{(sysmon.HasAccess ? "OK" : "Negado")}[/]"
        );

        return table;
    }

    private IRenderable BuildNavigation()
    {
        var views = new[] { "1:Overview", "2:Monitorar", "3:Catalogar", "4:Testes", "5:Gerenciar", "Q:Sair" };
        var items = views.Select((v, i) =>
        {
            var idx = (AppView)i;
            var color = idx == _currentView ? "dodgerblue1" : "grey";
            return $"[{color}]{v}[/]";
        });

        return new Markup(string.Join("  │  ", items));
    }

    private IRenderable BuildContent(DashboardSnapshot dashboard, SysmonStatus sysmon, DateTime now)
    {
        return _currentView switch
        {
            AppView.Overview => BuildOverviewView(dashboard, sysmon),
            AppView.Monitor => BuildMonitorView(now),
            AppView.Catalog => BuildCatalogView(),
            AppView.Tests => BuildTestsView(),
            AppView.Manage => BuildManageView(),
            _ => new Markup("[red]View invalida[/]")
        };
    }

    private IRenderable BuildOverviewView(DashboardSnapshot dashboard, SysmonStatus sysmon)
    {
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

    private IRenderable BuildMonitorView(DateTime now)
    {
        var activeSession = _appService.GetActiveSessionSnapshot();

        string monitorTarget;
        string? statusMsg;

        InputMode currentMode;
        lock (_stateLock)
        {
            monitorTarget = _monitorTarget;
            statusMsg = _statusMessage;
            currentMode = _inputMode;
        }

        RefreshProcessCaches(monitorTarget, now);

        IReadOnlyList<ProcessSnapshot> suggestions;
        IReadOnlyList<ProcessSnapshot> topProcesses;
        IReadOnlyList<ProcessSnapshot> selectionOptions;
        ProcessSelectionSource selectionSource;
        int selectedIndex;

        lock (_stateLock)
        {
            suggestions = _monitorSuggestions;
            topProcesses = _topProcesses;
            selectionOptions = _processSelectionOptions;
            selectionSource = _processSelectionSource;
            selectedIndex = _selectedProcessIndex;
        }

        var grid = new Grid().AddColumn();

        grid.AddRow("[cyan1 bold]Monitoramento em Tempo Real[/]");
        grid.AddRow("");

        var targetDisplay = currentMode == InputMode.EditingMonitorTarget
            ? $"[yellow]> {Markup.Escape(monitorTarget)}█[/]"
            : $"[cyan1]{Markup.Escape(monitorTarget)}[/]";

        grid.AddRow($"[grey]Executavel:[/] {targetDisplay}");

        if (currentMode == InputMode.Navigation)
        {
            grid.AddRow("[grey]Comandos: [[Enter]] Editar  [[M]] Iniciar  [[S]] Parar  [[C]] Limpar[/]");
        }
        else if (currentMode == InputMode.SelectingMonitorProcess)
        {
            grid.AddRow("[grey]Selecao: [[↑/↓]] Navegar  [[1-9]] Atalho  [[Tab]] Alternar lista  [[Enter]] Confirmar  [[Esc]] Cancelar[/]");
        }

        if (statusMsg != null)
        {
            grid.AddRow("").AddRow($"[yellow]{Markup.Escape(statusMsg)}[/]");
        }

        if (activeSession != null)
        {
            grid.AddRow("").AddRow("[green bold]═══ Sessao Ativa ═══[/]").AddRow("");

            var duration = now - activeSession.StartedAt;
            var threatLevel = GetThreatLevel(activeSession);
            var threatColor = ResolveThreatColor(threatLevel);

            grid.AddRow($"[grey]Alvo:[/] [green]{Markup.Escape(activeSession.TargetExecutable)}[/]");
            grid.AddRow($"[grey]Eventos:[/] [cyan1]{activeSession.TotalEventos}[/]");
            grid.AddRow($"[grey]Processos ativos:[/] [yellow]{activeSession.Statistics.ProcessosAtivos}[/]");
            grid.AddRow($"[grey]Processos encerrados:[/] [yellow]{activeSession.Statistics.ProcessosEncerrados}[/]");
            grid.AddRow($"[grey]Duracao:[/] [cyan1]{duration:hh\\:mm\\:ss}[/]");
            grid.AddRow($"[grey]Nivel de Alerta:[/] [{threatColor} bold]{ResolveThreatText(threatLevel)}[/]");

            if (activeSession.Statistics.ProcessosAtivosDetalhados.Count > 0)
            {
                var total = activeSession.Statistics.ProcessosAtivosDetalhados.Count;
                var visible = Math.Min(MaxProcessosVisiveis, total);

                grid.AddRow("").AddRow($"[grey bold]Top {visible} Processos (Total: {total})[/]").AddRow("");

                var procTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn("[grey]PID[/]")
                    .AddColumn("[grey]Imagem[/]")
                    .AddColumn("[grey]Inicio[/]")
                    .AddColumn("[grey]Duracao[/]");

                foreach (var proc in activeSession.Statistics.ProcessosAtivosDetalhados
                    .OrderByDescending(p => p.IniciadoEm ?? DateTime.MinValue)
                    .Take(MaxProcessosVisiveis))
                {
                    var inicio = proc.IniciadoEm?.ToString("HH:mm:ss") ?? "--";
                    var duracao = proc.IniciadoEm is not null
                        ? (now - proc.IniciadoEm.Value).ToString("hh\\:mm\\:ss")
                        : "--";

                    procTable.AddRow(
                        $"[yellow]{proc.PID}[/]",
                        Markup.Escape(proc.Imagem),
                        inicio,
                        duracao
                    );
                }

                grid.AddRow(procTable);
            }

            if (activeSession.Logs.Count > 0)
            {
                var logs = activeSession.Logs.TakeLast(MaxLogsVisiveis).ToList();
                grid.AddRow("").AddRow($"[grey bold]Logs Recentes ({logs.Count}/{activeSession.Logs.Count})[/]");
                foreach (var log in logs)
                {
                    grid.AddRow($"[grey70]{Markup.Escape(log)}[/]");
                }
            }
        }
        else
        {
            grid.AddRow("").AddRow("[grey]Nenhuma sessao ativa. Pressione [[M]] para iniciar.[/]");
        }

        if (currentMode == InputMode.SelectingMonitorProcess && selectionOptions.Count > 0)
        {
            var label = selectionSource == ProcessSelectionSource.Suggestions
                ? $"[grey bold]Processos correspondentes em execucao ({selectionOptions.Count} homonimo(s))[/]"
                : "[grey bold]Top processos do sistema[/]";

            grid.AddRow("").AddRow(label);
            grid.AddRow(BuildProcessTable(selectionOptions, selectedIndex));
        }
        else
        {
            if (suggestions.Count > 0)
            {
                grid.AddRow("").AddRow($"[yellow bold]TODOS os {suggestions.Count} processo(s) homonimo(s) abaixo serao monitorados:[/]");
                grid.AddRow(BuildProcessTable(suggestions));
            }
            else
            {
                // So mostra o top 10 quando NAO ha sugestoes de busca
                grid.AddRow("").AddRow("[grey bold]Top processos por uso de memoria[/]");
                grid.AddRow(topProcesses.Count > 0
                    ? BuildProcessTable(topProcesses)
                    : new Markup("[grey italic]Nao foi possivel carregar a lista de processos ou nenhum processo acessivel foi encontrado.[/]"));
            }
        }

        return grid;
    }

    private IRenderable BuildCatalogView()
    {
        string numero, nome, descricao, target;
        string? statusMsg;

        lock (_stateLock)
        {
            numero = _catalogNumero;
            nome = _catalogNome;
            descricao = _catalogDescricao;
            target = _catalogTarget;
            statusMsg = _statusMessage;
        }

        var grid = new Grid().AddColumn();

        grid.AddRow("[yellow bold]Catalogar Teste Atomico MITRE ATT&CK[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Numero (ex: T1055):[/] [cyan1]{Markup.Escape(numero)}[/]");
        grid.AddRow($"[grey]Nome:[/] [cyan1]{Markup.Escape(nome)}[/]");
        grid.AddRow($"[grey]Descricao:[/] [cyan1]{Markup.Escape(descricao)}[/]");
        grid.AddRow($"[grey]Executavel alvo:[/] [cyan1]{Markup.Escape(target)}[/]");
        grid.AddRow("");
        grid.AddRow("[grey]Comandos: [[1-4]] Editar campos  [[Enter]] Iniciar  [[C]] Limpar[/]");

        if (statusMsg != null)
        {
            grid.AddRow("").AddRow($"[yellow]{Markup.Escape(statusMsg)}[/]");
        }

        return grid;
    }

    private IRenderable BuildTestsView()
    {
        var testes = _appService.ListarTestes();

        string testsId;
        string? statusMsg;
        InputMode currentMode;

        lock (_stateLock)
        {
            testsId = _testsIdInput;
            statusMsg = _statusMessage;
            currentMode = _inputMode;
        }

        var grid = new Grid().AddColumn();
        grid.AddRow("[blue bold]Testes Catalogados[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total:[/] [cyan1]{testes.Count}[/]");

        var idDisplay = currentMode == InputMode.EditingTestId
            ? $"[yellow]> {Markup.Escape(testsId)}█[/]"
            : $"[cyan1]{Markup.Escape(testsId)}[/]";

        grid.AddRow($"[grey]ID para exportar:[/] {idDisplay}");
        grid.AddRow("");

        if (currentMode == InputMode.Navigation)
        {
            grid.AddRow("[grey]Comandos: [[Enter]] Editar ID  [[E]] Exportar[/]");
        }

        if (statusMsg != null)
        {
            grid.AddRow("").AddRow($"[yellow]{Markup.Escape(statusMsg)}[/]");
        }

        if (testes.Count > 0)
        {
            grid.AddRow("");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[grey]ID[/]")
                .AddColumn("[grey]Numero[/]")
                .AddColumn("[grey]Nome[/]")
                .AddColumn("[grey]Execucao[/]")
                .AddColumn("[grey]Eventos[/]");

            foreach (var teste in testes.Take(10))
            {
                table.AddRow(
                    $"[cyan1]{teste.Id}[/]",
                    $"[yellow]{Markup.Escape(teste.Numero)}[/]",
                    Markup.Escape(teste.Nome),
                    teste.DataExecucao.ToString("dd/MM HH:mm"),
                    $"[cyan1]{teste.TotalEventos}[/]"
                );
            }

            grid.AddRow(table);
        }
        else
        {
            grid.AddRow("[grey]Nenhum teste catalogado ainda.[/]");
        }

        return grid;
    }

    private static string FormatEditableValue(string? value, bool editing, string colorWhenStatic = "cyan1", string emptyPlaceholder = "[grey italic]<vazio>[/]")
    {
        value ??= string.Empty;
        var escaped = Markup.Escape(value);

        if (editing)
        {
            return $"[yellow]> {escaped}█[/]";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return emptyPlaceholder;
        }

        return $"[{colorWhenStatic}]{escaped}[/]";
    }

    private Task HandleEditManageIdAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_manageIdInput.Length > 0)
                {
                    _manageIdInput = _manageIdInput[..^1];
                    _forceRefresh = true;
                }
            }
        }
        else if (char.IsDigit(key.KeyChar))
        {
            lock (_stateLock)
            {
                if (_manageIdInput.Length < 9)
                {
                    _manageIdInput += key.KeyChar;
                    _forceRefresh = true;
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleEditManageNumeroAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_manageNumero.Length > 0)
                {
                    _manageNumero = _manageNumero[..^1];
                    _forceRefresh = true;
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_stateLock)
            {
                if (_manageNumero.Length < 32)
                {
                    _manageNumero += key.KeyChar;
                    _forceRefresh = true;
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleEditManageNomeAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_manageNome.Length > 0)
                {
                    _manageNome = _manageNome[..^1];
                    _forceRefresh = true;
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_stateLock)
            {
                if (_manageNome.Length < 120)
                {
                    _manageNome += key.KeyChar;
                    _forceRefresh = true;
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleEditManageDescricaoAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_manageDescricao.Length > 0)
                {
                    _manageDescricao = _manageDescricao[..^1];
                    _forceRefresh = true;
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_stateLock)
            {
                if (_manageDescricao.Length < 600)
                {
                    _manageDescricao += key.KeyChar;
                    _forceRefresh = true;
                }
            }
        }

        return Task.CompletedTask;
    }

    private IRenderable BuildManageView()
    {
        var testes = _appService.ListarTestes();

        string idInput;
        string numero;
        string nome;
        string descricao;
        int? selectedId;
        ResumoTesteAtomico? resumo;
        InputMode mode;
        string? statusMsg;

        lock (_stateLock)
        {
            idInput = _manageIdInput;
            numero = _manageNumero;
            nome = _manageNome;
            descricao = _manageDescricao;
            selectedId = _manageSelectedId;
            resumo = _manageResumo;
            mode = _inputMode;
            statusMsg = _statusMessage;
        }

        var grid = new Grid().AddColumn();
        grid.AddRow("[magenta bold]Gerenciar Testes Catalogados[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total catalogados:[/] [cyan1]{testes.Count}[/]");

        var idDisplay = FormatEditableValue(idInput, mode == InputMode.EditingManageId, "cyan1", "[grey]--[/]");
        grid.AddRow($"[grey]ID alvo:[/] {idDisplay}");

        if (selectedId.HasValue)
        {
            grid.AddRow($"[grey]ID carregado:[/] [green]{selectedId.Value}[/]");
        }

        if (mode == InputMode.Navigation)
        {
            grid.AddRow("[grey]Comandos: [[I]] ID  [[L]] Carregar  [[N]] Número  [[M]] Nome  [[D]] Descrição  [[U]] Atualizar  [[E]] Exportar  [[X]] Excluir  [[R]] Limpar[/]");
        }

        if (statusMsg is not null)
        {
            grid.AddRow("").AddRow($"[yellow]{Markup.Escape(statusMsg)}[/]");
        }

        if (selectedId.HasValue)
        {
            grid.AddRow("");
            grid.AddRow("[magenta bold]Metadados do teste[/]");

            var metaTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .HideHeaders()
                .AddColumn(new TableColumn("[grey]Campo[/]").NoWrap())
                .AddColumn(new TableColumn("[grey]Valor[/]"));

            metaTable.AddRow("[grey]Número[/]", FormatEditableValue(numero, mode == InputMode.EditingManageNumero, "yellow"));
            metaTable.AddRow("[grey]Nome[/]", FormatEditableValue(nome, mode == InputMode.EditingManageNome));
            metaTable.AddRow("[grey]Descrição[/]", FormatEditableValue(descricao, mode == InputMode.EditingManageDescricao, "grey70"));

            grid.AddRow(metaTable);

            if (resumo is not null)
            {
                grid.AddRow("");
                grid.AddRow("[magenta bold]Resumo da execução[/]");
                grid.AddRow($"[grey]Execução:[/] [cyan1]{resumo.DataExecucao:dd/MM/yyyy HH:mm:ss}[/]");
                var duracao = TimeSpan.FromSeconds(resumo.DuracaoSegundos);
                grid.AddRow($"[grey]Duração:[/] [cyan1]{duracao:hh\\:mm\\:ss}[/]");
                grid.AddRow($"[grey]Total de eventos:[/] [cyan1]{resumo.TotalEventos}[/]");

                if (resumo.EventosPorTipo.Count > 0)
                {
                    var eventosTabela = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Grey)
                        .AddColumn("[grey]Event ID[/]")
                        .AddColumn("[grey]Ocorrências[/]");

                    foreach (var kv in resumo.EventosPorTipo.OrderByDescending(kvp => kvp.Value).Take(8))
                    {
                        eventosTabela.AddRow(
                            $"[cyan1]{kv.Key}[/]",
                            $"[yellow]{kv.Value}[/]"
                        );
                    }

                    grid.AddRow(eventosTabela);
                }
                else
                {
                    grid.AddRow("[grey italic]Nenhum evento associado.[/]");
                }
            }
            else
            {
                grid.AddRow("");
                grid.AddRow("[grey italic]Resumo não disponível para este teste.[/]");
            }
        }

        if (testes.Count > 0)
        {
            grid.AddRow("");
            grid.AddRow("[grey bold]Últimos testes catalogados[/]");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("[grey]ID[/]")
                .AddColumn("[grey]Número[/]")
                .AddColumn("[grey]Nome[/]")
                .AddColumn("[grey]Execução[/]")
                .AddColumn("[grey]Eventos[/]");

            foreach (var teste in testes.OrderByDescending(t => t.DataExecucao).Take(5))
            {
                var highlight = selectedId.HasValue && teste.Id == selectedId.Value;
                var idColor = highlight ? "green" : "cyan1";
                var numeroColor = highlight ? "green" : "yellow";

                table.AddRow(
                    $"[{idColor}]{teste.Id}[/]",
                    $"[{numeroColor}]{Markup.Escape(teste.Numero)}[/]",
                    Markup.Escape(teste.Nome),
                    teste.DataExecucao.ToString("dd/MM HH:mm"),
                    $"[cyan1]{teste.TotalEventos}[/]"
                );
            }

            grid.AddRow(table);
        }
        else
        {
            grid.AddRow("");
            grid.AddRow("[grey]Nenhum teste catalogado ainda.[/]");
        }

        return grid;
    }

    private void RefreshProcessCaches(string monitorTarget, DateTime now)
    {
        IReadOnlyList<ProcessSnapshot>? newTop = null;
        IReadOnlyList<ProcessSnapshot>? newSuggestions = null;
        var dataChanged = false;

        if ((now - _lastTopProcessesUpdate) >= TimeSpan.FromSeconds(5))
        {
            try
            {
                newTop = _appService.GetTopProcesses(MaxProcessSelectionOptions);
            }
            catch
            {
                newTop = Array.Empty<ProcessSnapshot>();
            }
        }

        if (!string.IsNullOrWhiteSpace(monitorTarget) &&
            (now - _lastSuggestionsUpdate) >= TimeSpan.FromMilliseconds(500))
        {
            try
            {
                newSuggestions = _appService.FindProcesses(monitorTarget, MaxProcessSelectionOptions);
            }
            catch
            {
                newSuggestions = Array.Empty<ProcessSnapshot>();
            }
        }

        if (newTop is null && newSuggestions is null)
        {
            return;
        }

        lock (_stateLock)
        {
            if (newTop is not null)
            {
                _topProcesses = newTop;
                _lastTopProcessesUpdate = now;
                dataChanged = true;

                if (_processSelectionSource == ProcessSelectionSource.TopProcesses)
                {
                    _processSelectionOptions = newTop;
                    if (_selectedProcessIndex >= newTop.Count)
                    {
                        _selectedProcessIndex = newTop.Count - 1;
                    }
                }
            }

            if (newSuggestions is not null)
            {
                _monitorSuggestions = newSuggestions;
                _lastSuggestionsUpdate = now;
                dataChanged = true;

                if (_processSelectionSource == ProcessSelectionSource.Suggestions)
                {
                    _processSelectionOptions = newSuggestions;
                    if (_selectedProcessIndex >= newSuggestions.Count)
                    {
                        _selectedProcessIndex = newSuggestions.Count - 1;
                    }
                }
            }

            if (dataChanged)
            {
                _forceRefresh = true;
            }
        }
    }

    private static Table BuildProcessTable(IReadOnlyList<ProcessSnapshot> processes, int? highlightIndex = null)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand()
            .AddColumn("[grey]#[/]")
            .AddColumn("[grey]Imagem[/]")
            .AddColumn("[grey]PID[/]")
            .AddColumn("[grey]Mem (MB)[/]");

        for (var i = 0; i < processes.Count; i++)
        {
            var isHighlighted = highlightIndex.HasValue && highlightIndex.Value == i;
            var color = isHighlighted ? "yellow" : "cyan1";
            var indexText = isHighlighted ? $"[{color} bold]> {i + 1}[/]" : $"[{color}]{i + 1}[/]";
            var proc = processes[i];
            var nameText = isHighlighted ? $"[{color} bold]{Markup.Escape(proc.ProcessName)}[/]" : $"[{color}]{Markup.Escape(proc.ProcessName)}[/]";
            var pidText = isHighlighted ? $"[{color} bold]{proc.Pid}[/]" : $"[{color}]{proc.Pid}[/]";
            var memText = isHighlighted ? $"[{color} bold]{proc.MemoryMb:F1}[/]" : $"[{color}]{proc.MemoryMb:F1}[/]";
            table.AddRow(indexText, nameText, pidText, memText);
        }

        if (processes.Count == 0)
        {
            table.AddRow("[grey]-[/]", "[grey]Nenhum processo[/]", "[grey]-[/]", "[grey]-[/]");
        }

        return table;
    }

    private bool TryLoadManageTest(int testeId, string? successMessage = null)
    {
        IReadOnlyList<TesteAtomico> testes;
        try
        {
            testes = _appService.ListarTestes();
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _statusMessage = $"Erro ao listar testes: {ex.Message}";
                _forceRefresh = true;
            }

            return false;
        }

        var teste = testes.FirstOrDefault(t => t.Id == testeId);
        if (teste is null)
        {
            lock (_stateLock)
            {
                _statusMessage = $"Teste {testeId} não encontrado";
                _forceRefresh = true;
            }

            return false;
        }

        ResumoTesteAtomico? resumo;
        try
        {
            resumo = _appService.ObterResumoTeste(testeId);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _statusMessage = $"Erro ao carregar resumo: {ex.Message}";
                _forceRefresh = true;
            }

            return false;
        }

        lock (_stateLock)
        {
            _manageSelectedId = testeId;
            _manageIdInput = testeId.ToString();
            _manageNumero = teste.Numero;
            _manageNome = teste.Nome;
            _manageDescricao = teste.Descricao;
            _manageResumo = resumo;

            var message = successMessage ?? $"Teste {teste.Numero} carregado";
            if (resumo is null)
            {
                message += " (sem resumo)";
            }

            _statusMessage = message;
            _forceRefresh = true;
        }

        return true;
    }

    private void ResetManageStateUnsafe()
    {
        _manageSelectedId = null;
        _manageIdInput = string.Empty;
        _manageNumero = string.Empty;
        _manageNome = string.Empty;
        _manageDescricao = string.Empty;
        _manageResumo = null;
    }

    private void ResetProcessSelectionUnsafe()
    {
        _processSelectionOptions = Array.Empty<ProcessSnapshot>();
        _processSelectionSource = ProcessSelectionSource.None;
        _selectedProcessIndex = -1;
    }

    private IRenderable BuildFooter()
    {
        InputMode mode;
        lock (_stateLock)
        {
            mode = _inputMode;
        }

        var modeText = mode switch
        {
            InputMode.Navigation => "[grey]Modo: NAVEGACAO - Use [[1-5]] │ [[Q]] Sair │ [[Enter]] Editar campo[/]",
            InputMode.EditingMonitorTarget => "[yellow]Modo: EDITANDO EXECUTAVEL - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.SelectingMonitorProcess => "[yellow]Modo: SELECIONANDO PROCESSO - [[↑/↓]] Navegar │ [[Tab]] Alternar lista │ [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingTestId => "[yellow]Modo: EDITANDO ID - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingManageId => "[yellow]Modo: EDITANDO ID GERENCIAMENTO - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingManageNumero => "[yellow]Modo: EDITANDO NUMERO - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingManageNome => "[yellow]Modo: EDITANDO NOME - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingManageDescricao => "[yellow]Modo: EDITANDO DESCRICAO - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            _ => "[grey]Modo desconhecido[/]"
        };

        return new Markup(modeText);
    }

    private ThreatLevel GetThreatLevel(MonitoringSessionSnapshot session)
    {
        var counts = _appService.GetCriticalEventCounts(session.SessionId);

        if (counts.GetValueOrDefault(8) > 0 || counts.GetValueOrDefault(25) > 0)
            return ThreatLevel.Severo;

        if (counts.GetValueOrDefault(23) > 20)
            return ThreatLevel.Severo;

        if (counts.GetValueOrDefault(3) > 50 || counts.GetValueOrDefault(10) > 10)
            return ThreatLevel.Alto;

        if (counts.GetValueOrDefault(23) is > 5 and <= 20)
            return ThreatLevel.Alto;

        if (counts.GetValueOrDefault(2) > 0 || (counts.GetValueOrDefault(13) > 10 && counts.GetValueOrDefault(17) > 0))
            return ThreatLevel.Medio;

        if (counts.GetValueOrDefault(22) > 20 || counts.GetValueOrDefault(1) > 15)
            return ThreatLevel.Moderado;

        if (counts.GetValueOrDefault(13) > 5 || counts.GetValueOrDefault(3) > 10)
            return ThreatLevel.Moderado;

        return ThreatLevel.Baixo;
    }

    private static string ResolveThreatText(ThreatLevel level) => level switch
    {
        ThreatLevel.Severo => "SEVERO",
        ThreatLevel.Alto => "ALTO",
        ThreatLevel.Medio => "MEDIO",
        ThreatLevel.Moderado => "MODERADO",
        _ => "BAIXO"
    };

    private static string ResolveThreatColor(ThreatLevel level) => level switch
    {
        ThreatLevel.Severo => "red",
        ThreatLevel.Alto => "orange3",
        ThreatLevel.Medio => "yellow",
        ThreatLevel.Moderado => "blue",
        _ => "green"
    };

    private async Task HandleInputAsync(ConsoleKeyInfo key)
    {
        InputMode currentMode;
        lock (_stateLock)
        {
            currentMode = _inputMode;
        }

        // Se está em modo de edição, só aceita texto, Enter ou Escape
        if (currentMode != InputMode.Navigation)
        {
            await HandleEditModeInputAsync(key, currentMode).ConfigureAwait(false);
            return;
        }

        // MODO NAVEGAÇÃO - Comandos globais

        // Setas para navegar entre views
        if (key.Key == ConsoleKey.UpArrow)
        {
            lock (_stateLock)
            {
                int current = (int)_currentView;
                _currentView = (AppView)((current - 1 + 5) % 5);
                _statusMessage = null;
                _forceRefresh = true;
            }
            return;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            lock (_stateLock)
            {
                int current = (int)_currentView;
                _currentView = (AppView)((current + 1) % 5);
                _statusMessage = null;
                _forceRefresh = true;
            }
            return;
        }

        // Navegação por números
        if (key.KeyChar >= '1' && key.KeyChar <= '5')
        {
            lock (_stateLock)
            {
                _currentView = (AppView)(key.KeyChar - '1');
                _statusMessage = null;
                _forceRefresh = true;
            }
            return;
        }

        // Sair
        if (key.Key == ConsoleKey.Q)
        {
            _uiLoop?.Cancel();
            return;
        }

        // Entrar em modo de edição com Enter (dependendo da view)
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                if (_currentView == AppView.Monitor)
                {
                    _inputMode = InputMode.EditingMonitorTarget;
                    _forceRefresh = true;
                }
                else if (_currentView == AppView.Tests)
                {
                    _inputMode = InputMode.EditingTestId;
                    _forceRefresh = true;
                }
                else if (_currentView == AppView.Manage)
                {
                    _inputMode = InputMode.EditingManageId;
                    _forceRefresh = true;
                }
            }
            return;
        }

        // Comandos específicos por view (apenas em modo navegação)
        switch (_currentView)
        {
            case AppView.Monitor:
                await HandleMonitorCommandsAsync(key).ConfigureAwait(false);
                break;
            case AppView.Tests:
                await HandleTestsCommandsAsync(key).ConfigureAwait(false);
                break;
            case AppView.Manage:
                await HandleManageCommandsAsync(key).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        // Escape sempre cancela edição
        if (key.Key == ConsoleKey.Escape)
        {
            lock (_stateLock)
            {
                if (mode == InputMode.SelectingMonitorProcess)
                {
                    ResetProcessSelectionUnsafe();
                    _statusMessage = "Selecao cancelada.";
                }

                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
            return;
        }

        switch (mode)
        {
            case InputMode.EditingMonitorTarget:
                await HandleEditMonitorTargetAsync(key).ConfigureAwait(false);
                break;
            case InputMode.SelectingMonitorProcess:
                await HandleSelectMonitorProcessAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingTestId:
                await HandleEditTestIdAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingManageId:
                await HandleEditManageIdAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingManageNumero:
                await HandleEditManageNumeroAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingManageNome:
                await HandleEditManageNomeAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingManageDescricao:
                await HandleEditManageDescricaoAsync(key).ConfigureAwait(false);
                break;
        }
    }

    private Task HandleEditMonitorTargetAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            string currentTarget;
            lock (_stateLock)
            {
                currentTarget = _monitorTarget;
            }

            IReadOnlyList<ProcessSnapshot> suggestions;
            IReadOnlyList<ProcessSnapshot> top;

            try
            {
                suggestions = string.IsNullOrWhiteSpace(currentTarget)
                    ? Array.Empty<ProcessSnapshot>()
                    : _appService.FindProcesses(currentTarget, MaxProcessSelectionOptions);
            }
            catch
            {
                suggestions = Array.Empty<ProcessSnapshot>();
            }

            try
            {
                top = _appService.GetTopProcesses(MaxProcessSelectionOptions);
            }
            catch
            {
                top = Array.Empty<ProcessSnapshot>();
            }

            lock (_stateLock)
            {
                _monitorSuggestions = suggestions;
                _lastSuggestionsUpdate = DateTime.Now;
                _topProcesses = top;
                _lastTopProcessesUpdate = DateTime.Now;

                if (suggestions.Count > 0)
                {
                    _processSelectionOptions = suggestions;
                    _processSelectionSource = ProcessSelectionSource.Suggestions;
                    _selectedProcessIndex = 0;
                    _inputMode = InputMode.SelectingMonitorProcess;
                    _statusMessage = $"Selecione o processo correspondente ({suggestions.Count} encontrado(s)).";
                }
                else if (top.Count > 0)
                {
                    _processSelectionOptions = top;
                    _processSelectionSource = ProcessSelectionSource.TopProcesses;
                    _selectedProcessIndex = 0;
                    _inputMode = InputMode.SelectingMonitorProcess;
                    _statusMessage = "Nenhum processo correspondente. Utilize o top de processos.";
                }
                else
                {
                    ResetProcessSelectionUnsafe();
                    _inputMode = InputMode.Navigation;
                    _statusMessage = "Nenhum processo em execucao encontrado.";
                }

                _forceRefresh = true;
            }

            return Task.CompletedTask;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_monitorTarget.Length > 0)
                {
                    _monitorTarget = _monitorTarget[..^1];
                    _lastSuggestionsUpdate = DateTime.MinValue;
                    ResetProcessSelectionUnsafe();
                    _monitorSuggestions = Array.Empty<ProcessSnapshot>();
                    _statusMessage = null;
                    _forceRefresh = true;
                }
            }

            return Task.CompletedTask;
        }

        if (!char.IsControl(key.KeyChar))
        {
            lock (_stateLock)
            {
                _monitorTarget += key.KeyChar;
                _lastSuggestionsUpdate = DateTime.MinValue;
                ResetProcessSelectionUnsafe();
                _monitorSuggestions = Array.Empty<ProcessSnapshot>();
                _statusMessage = null;
                _forceRefresh = true;
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleSelectMonitorProcessAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                if (_processSelectionOptions.Count == 0 ||
                    _selectedProcessIndex < 0 ||
                    _selectedProcessIndex >= _processSelectionOptions.Count)
                {
                    _statusMessage = "Selecione um processo valido antes de confirmar.";
                    _forceRefresh = true;
                    return Task.CompletedTask;
                }

                var selected = _processSelectionOptions[_selectedProcessIndex];
                _monitorTarget = selected.ProcessName;

                // Atualiza sugestoes para mostrar todos os processos homonimos
                IReadOnlyList<ProcessSnapshot> allMatches;
                try
                {
                    allMatches = _appService.FindProcesses(selected.ProcessName, 100);
                }
                catch
                {
                    allMatches = new[] { selected };
                }

                _monitorSuggestions = allMatches;
                _lastSuggestionsUpdate = DateTime.Now;
                _statusMessage = $"Alvo configurado: {selected.ProcessName} - {allMatches.Count} processo(s) homonimo(s) serao monitorados.";
                ResetProcessSelectionUnsafe();
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }

            return Task.CompletedTask;
        }

        if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
        {
            var direction = key.Key == ConsoleKey.UpArrow ? -1 : 1;

            lock (_stateLock)
            {
                var count = _processSelectionOptions.Count;
                if (count == 0)
                {
                    _statusMessage = "Nenhum processo disponivel para selecao.";
                    _forceRefresh = true;
                    return Task.CompletedTask;
                }

                if (_selectedProcessIndex < 0)
                {
                    _selectedProcessIndex = direction > 0 ? 0 : count - 1;
                }
                else
                {
                    _selectedProcessIndex = (_selectedProcessIndex + direction + count) % count;
                }

                var current = _processSelectionOptions[_selectedProcessIndex];
                _statusMessage = $"Selecionado: {current.ProcessName} (PID {current.Pid}).";
                _forceRefresh = true;
            }

            return Task.CompletedTask;
        }

        if (char.IsDigit(key.KeyChar) && key.KeyChar >= '1' && key.KeyChar <= '9')
        {
            var desiredIndex = key.KeyChar - '1';

            lock (_stateLock)
            {
                if (desiredIndex < _processSelectionOptions.Count)
                {
                    _selectedProcessIndex = desiredIndex;
                    if (_processSelectionOptions.Count > 0)
                    {
                        var current = _processSelectionOptions[_selectedProcessIndex];
                        _statusMessage = $"Selecionado: {current.ProcessName} (PID {current.Pid}).";
                    }
                }
                else
                {
                    _statusMessage = "Indice fora da lista de processos.";
                }

                _forceRefresh = true;
            }

            return Task.CompletedTask;
        }

        if (key.Key == ConsoleKey.Tab)
        {
            lock (_stateLock)
            {
                if (_monitorSuggestions.Count > 0 && _topProcesses.Count > 0)
                {
                    if (_processSelectionSource == ProcessSelectionSource.Suggestions)
                    {
                        _processSelectionSource = ProcessSelectionSource.TopProcesses;
                        _processSelectionOptions = _topProcesses;
                    }
                    else
                    {
                        _processSelectionSource = ProcessSelectionSource.Suggestions;
                        _processSelectionOptions = _monitorSuggestions;
                    }

                    if (_processSelectionOptions.Count > 0)
                    {
                        _selectedProcessIndex = Math.Min(
                            _selectedProcessIndex < 0 ? 0 : _selectedProcessIndex,
                            _processSelectionOptions.Count - 1);
                        var current = _processSelectionOptions[_selectedProcessIndex];
                        _statusMessage = $"Fonte alterada: {(_processSelectionSource == ProcessSelectionSource.Suggestions ? "Sugestoes" : "Top processos")} - {current.ProcessName} (PID {current.Pid}).";
                    }
                    else
                    {
                        _selectedProcessIndex = -1;
                        _statusMessage = "Lista de processos vazia apos alternar fonte.";
                    }
                }
                else
                {
                    _statusMessage = "Nao ha outra lista de processos para alternar.";
                }

                _forceRefresh = true;
            }

            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private Task HandleEditTestIdAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            // Confirma e volta para navegação
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            lock (_stateLock)
            {
                if (_testsIdInput.Length > 0)
                {
                    _testsIdInput = _testsIdInput[..^1];
                    _forceRefresh = true;
                }
            }
        }
        else if (char.IsDigit(key.KeyChar))
        {
            lock (_stateLock)
            {
                _testsIdInput += key.KeyChar;
                _forceRefresh = true;
            }
        }

        return Task.CompletedTask;
    }

    private async Task HandleMonitorCommandsAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.M)
        {
            string target;
            lock (_stateLock)
            {
                target = _monitorTarget;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                lock (_stateLock)
                {
                    _statusMessage = "Erro: Informe o nome do executavel primeiro";
                    _forceRefresh = true;
                }
                return;
            }

            var trimmedTarget = target.Trim();
            IReadOnlyList<ProcessSnapshot> matches;
            try
            {
                matches = _appService.FindProcesses(trimmedTarget, MaxProcessSelectionOptions);
            }
            catch
            {
                matches = Array.Empty<ProcessSnapshot>();
            }

            if (matches.Count == 0)
            {
                lock (_stateLock)
                {
                    _statusMessage = "Nenhum processo ativo encontrado com esse nome.";
                    _forceRefresh = true;
                }
                return;
            }

            var resolvedTarget = matches.Any(m => m.ProcessName.Equals(trimmedTarget, StringComparison.OrdinalIgnoreCase))
                ? trimmedTarget
                : matches[0].ProcessName;

            try
            {
                _appService.StartMonitoring(resolvedTarget);
                lock (_stateLock)
                {
                    _monitorTarget = resolvedTarget;
                    _monitorSuggestions = matches;
                    _lastSuggestionsUpdate = DateTime.Now;
                    ResetProcessSelectionUnsafe();
                    _inputMode = InputMode.Navigation;
                    _statusMessage = $"Monitoramento iniciado: {resolvedTarget} - Rastreando {matches.Count} processo(s) homonimo(s) + filhos";
                    _forceRefresh = true;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _statusMessage = $"Erro: {ex.Message}";
                    _forceRefresh = true;
                }
            }
        }
        else if (key.Key == ConsoleKey.S)
        {
            try
            {
                await _appService.StopActiveSessionAsync().ConfigureAwait(false);
                lock (_stateLock)
                {
                    _statusMessage = "Monitoramento encerrado com sucesso";
                    _forceRefresh = true;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _statusMessage = $"Erro: {ex.Message}";
                    _forceRefresh = true;
                }
            }
        }
        else if (key.Key == ConsoleKey.C)
        {
            lock (_stateLock)
            {
                _monitorTarget = string.Empty;
                _monitorSuggestions = Array.Empty<ProcessSnapshot>();
                ResetProcessSelectionUnsafe();
                _statusMessage = null;
                _lastSuggestionsUpdate = DateTime.MinValue;
                _forceRefresh = true;
            }
        }
    }

    private async Task HandleTestsCommandsAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.E)
        {
            string idInput;
            lock (_stateLock)
            {
                idInput = _testsIdInput;
            }

            if (!int.TryParse(idInput, out var id))
            {
                lock (_stateLock)
                {
                    _statusMessage = "Erro: ID invalido";
                    _forceRefresh = true;
                }
                return;
            }

            try
            {
                var path = await _appService.ExportarTesteAsync(id).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _statusMessage = $"Exportado: {path}";
                    _forceRefresh = true;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _statusMessage = $"Erro: {ex.Message}";
                    _forceRefresh = true;
                }
            }
        }
    }

    private async Task HandleManageCommandsAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.I)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.EditingManageId;
                _forceRefresh = true;
            }
            return;
        }

        if (key.Key == ConsoleKey.R)
        {
            lock (_stateLock)
            {
                ResetManageStateUnsafe();
                _statusMessage = "Campos limpos";
                _forceRefresh = true;
            }
            return;
        }

        if (key.Key == ConsoleKey.L)
        {
            string idText;
            lock (_stateLock)
            {
                idText = _manageIdInput;
            }

            if (!int.TryParse(idText, out var id))
            {
                lock (_stateLock)
                {
                    _statusMessage = "Informe um ID válido para carregar";
                    _forceRefresh = true;
                }
            }
            else
            {
                TryLoadManageTest(id);
            }

            return;
        }

        if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.M || key.Key == ConsoleKey.D)
        {
            lock (_stateLock)
            {
                if (_manageSelectedId.HasValue)
                {
                    _inputMode = key.Key switch
                    {
                        ConsoleKey.N => InputMode.EditingManageNumero,
                        ConsoleKey.M => InputMode.EditingManageNome,
                        ConsoleKey.D => InputMode.EditingManageDescricao,
                        _ => InputMode.Navigation
                    };
                }
                else
                {
                    _statusMessage = "Carregue um teste antes de editar";
                }

                _forceRefresh = true;
            }

            return;
        }

        if (key.Key == ConsoleKey.U)
        {
            int? selectedId;
            string numero;
            string nome;
            string descricao;

            lock (_stateLock)
            {
                selectedId = _manageSelectedId;
                numero = _manageNumero;
                nome = _manageNome;
                descricao = _manageDescricao;
            }

            if (!selectedId.HasValue)
            {
                lock (_stateLock)
                {
                    _statusMessage = "Nenhum teste carregado para atualizar";
                    _forceRefresh = true;
                }
                return;
            }

            var numeroValue = string.IsNullOrWhiteSpace(numero) ? null : numero.Trim();
            var nomeValue = string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
            var descricaoValue = descricao?.Trim();

            if (_appService.AtualizarTeste(selectedId.Value, numeroValue, nomeValue, descricaoValue))
            {
                TryLoadManageTest(selectedId.Value, "Teste atualizado com sucesso");
            }
            else
            {
                lock (_stateLock)
                {
                    _statusMessage = "Falha ao atualizar teste";
                    _forceRefresh = true;
                }
            }

            return;
        }

        if (key.Key == ConsoleKey.E)
        {
            int? selectedId;
            string idInput;

            lock (_stateLock)
            {
                selectedId = _manageSelectedId;
                idInput = _manageIdInput;
            }

            int targetId;
            if (selectedId.HasValue)
            {
                targetId = selectedId.Value;
            }
            else if (int.TryParse(idInput, out var parsed))
            {
                targetId = parsed;
            }
            else
            {
                lock (_stateLock)
                {
                    _statusMessage = "Informe um ID válido para exportar";
                    _forceRefresh = true;
                }
                return;
            }

            try
            {
                var path = await _appService.ExportarTesteAsync(targetId).ConfigureAwait(false);
                lock (_stateLock)
                {
                    _statusMessage = $"Exportado: {path}";
                    _forceRefresh = true;
                }
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    _statusMessage = $"Erro: {ex.Message}";
                    _forceRefresh = true;
                }
            }

            return;
        }

        if (key.Key == ConsoleKey.X)
        {
            int? selectedId;
            lock (_stateLock)
            {
                selectedId = _manageSelectedId;
            }

            if (!selectedId.HasValue)
            {
                lock (_stateLock)
                {
                    _statusMessage = "Nenhum teste carregado para excluir";
                    _forceRefresh = true;
                }
                return;
            }

            if (_appService.ExcluirTeste(selectedId.Value))
            {
                lock (_stateLock)
                {
                    ResetManageStateUnsafe();
                    _statusMessage = $"Teste {selectedId.Value} excluído";
                    _forceRefresh = true;
                }
            }
            else
            {
                lock (_stateLock)
                {
                    _statusMessage = "Falha ao excluir teste";
                    _forceRefresh = true;
                }
            }

            return;
        }
    }

    private enum InputMode
    {
        Navigation,
        EditingMonitorTarget,
        SelectingMonitorProcess,
        EditingTestId,
        EditingManageId,
        EditingManageNumero,
        EditingManageNome,
        EditingManageDescricao
    }

    private enum AppView
    {
        Overview = 0,
        Monitor = 1,
        Catalog = 2,
        Tests = 3,
        Manage = 4
    }

    private enum ThreatLevel
    {
        Baixo,
        Moderado,
        Medio,
        Alto,
        Severo
    }

    private enum ProcessSelectionSource
    {
        None,
        Suggestions,
        TopProcesses
    }
}
