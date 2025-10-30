using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NavShieldTracer.ConsoleApp.Services;
using NavShieldTracer.ConsoleApp.UI.Views;
using NavShieldTracer.Modules.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NavShieldTracer.ConsoleApp.UI;

/// <summary>
/// Interface de console refatorada usando arquitetura modular baseada em views.
/// Orquestra navegação entre telas e delega renderização para views especializadas.
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

    private readonly NavShieldAppService _appService;
    private readonly object _stateLock = new();
    private AppView _currentView = AppView.Overview;
    private InputMode _inputMode = InputMode.Navigation;
    private CancellationTokenSource? _uiLoop;
    private bool _forceRefresh = false;
    private string? _statusMessage;

    // Views especializadas
    private readonly OverviewView _overviewView;
    private readonly MonitorView _monitorView;
    private readonly CatalogView _catalogView;
    private readonly TestsView _testsView;
    private readonly ManageView _manageView;
    private readonly ViewContext _viewContext;

    /// <summary>
    /// Cria uma nova instância da interface de console.
    /// </summary>
    /// <param name="appService">Serviço principal da aplicação.</param>
    public ConsoleUI(NavShieldAppService appService)
    {
        _appService = appService;

        // Criar contexto compartilhado
        _viewContext = new ViewContext(
            appService,
            _stateLock,
            () => { lock (_stateLock) { _forceRefresh = true; } },
            (msg) => { lock (_stateLock) { _statusMessage = msg; _forceRefresh = true; } }
        );

        // Inicializar views
        _overviewView = new OverviewView(_viewContext);
        _monitorView = new MonitorView(_viewContext);
        _catalogView = new CatalogView(_viewContext);
        _testsView = new TestsView(_viewContext);
        _manageView = new ManageView(_viewContext);
    }

    /// <summary>
    /// Inicia o loop interativo da interface de console.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
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
                    // OTIMIZAÇÃO: Reduzir refresh periódico de 250ms para 1000ms (1 segundo)
                    var periodicInterval = TimeSpan.FromSeconds(1);
                    var statusInterval = TimeSpan.FromSeconds(3);

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

                            // OTIMIZAÇÃO: Aumentar delay para reduzir CPU e piscadas
                            var delay = forcedRefresh ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromMilliseconds(200);
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

        // Banner
        sections.Add((compactMode
            ? new Panel(BuildCompactBanner())
            : new Panel(BuildBanner()))
            .Border(BoxBorder.None)
            .Expand());

        // Header
        sections.Add(new Panel(BuildHeader(dashboard, sysmonStatus, now))
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand());

        // Navigation
        sections.Add(new Panel(BuildNavigation())
            .Border(BoxBorder.None)
            .Padding(0, 0, 0, 1)
            .Expand());

        // Content (delegado para views)
        sections.Add(new Panel(BuildContent(now))
            .Border(BoxBorder.Rounded)
            .Expand());

        // Footer
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

    private IRenderable BuildContent(DateTime now)
    {
        // Verificar se estamos em modo de review pós-catalogação
        if (_catalogView.IsInPostReviewMode() && _inputMode == InputMode.PostCatalogReview)
        {
            return _catalogView.BuildPostReviewContent();
        }

        return _currentView switch
        {
            AppView.Overview => _overviewView.BuildContent(now),
            AppView.Monitor => _monitorView.BuildContent(now),
            AppView.Catalog => _catalogView.BuildContent(now),
            AppView.Tests => _testsView.BuildContent(now),
            AppView.Manage => _manageView.BuildContent(now),
            _ => new Markup("[red]View invalida[/]")
        };
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
            InputMode.EditingManageTarja => "[yellow]Modo: EDITANDO TARJA - [[↑/↓]] ou [[1-4]] Selecionar │ [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingManageNotes => "[yellow]Modo: EDITANDO NOTAS - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.CatalogEditing => "[yellow]Modo: CATALOGACAO - [[1-4]] Editar │ [[I]] Iniciar │ [[C]] Limpar │ [[F]] Finalizar │ [[Esc]] Sair[/]",
            InputMode.EditingCatalogNumero => "[yellow]Modo: EDITANDO NUMERO TESTE - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingCatalogNome => "[yellow]Modo: EDITANDO NOME TESTE - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingCatalogDescricao => "[yellow]Modo: EDITANDO DESCRICAO TESTE - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.EditingCatalogTarget => "[yellow]Modo: EDITANDO EXECUTAVEL ALVO - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            InputMode.PostCatalogReview => "[yellow]Modo: REVIEW TESTE - [[1]] Tarja (↑/↓) [[2]] Observacoes [[S]] Salvar [[Esc]] Cancelar[/]",
            InputMode.EditingCatalogObservacoes => "[yellow]Modo: EDITANDO OBSERVACOES - [[Enter]] Confirmar │ [[Esc]] Cancelar[/]",
            _ => "[grey]Modo desconhecido[/]"
        };

        if (_statusMessage != null)
        {
            return new Rows(new Markup(modeText), new Markup($"[yellow]{Markup.Escape(_statusMessage)}[/]"));
        }

        return new Markup(modeText);
    }

    private async Task HandleInputAsync(ConsoleKeyInfo key)
    {
        InputMode currentMode;
        lock (_stateLock)
        {
            currentMode = _inputMode;
        }

        // Modo de edição
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

        // Enter para entrar em modo de edição
        if (key.Key == ConsoleKey.Enter)
        {
            var defaultMode = GetCurrentView()?.GetDefaultEditMode();
            if (defaultMode.HasValue)
            {
                lock (_stateLock)
                {
                    _inputMode = defaultMode.Value;
                    _forceRefresh = true;
                }
            }
            return;
        }

        // Comandos específicos da view ativa
        await GetCurrentView()?.HandleNavigationInputAsync(key)!;
    }

    private async Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        // Escape sempre cancela edição
        if (key.Key == ConsoleKey.Escape)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _statusMessage = "Edicao cancelada";
                _forceRefresh = true;
            }
            return;
        }

        // Enter finaliza edição (exceto em modos especiais)
        if (key.Key == ConsoleKey.Enter && mode != InputMode.CatalogEditing && mode != InputMode.PostCatalogReview)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.Navigation;
                _forceRefresh = true;
            }
            return;
        }

        // Delegar para a view ativa
        await GetCurrentView()?.HandleEditModeInputAsync(key, mode)!;

        // Tratamento especial para CatalogEditing
        if (mode == InputMode.CatalogEditing)
        {
            if (key.KeyChar >= '1' && key.KeyChar <= '4')
            {
                lock (_stateLock)
                {
                    _inputMode = key.KeyChar switch
                    {
                        '1' => InputMode.EditingCatalogNumero,
                        '2' => InputMode.EditingCatalogNome,
                        '3' => InputMode.EditingCatalogDescricao,
                        '4' => InputMode.EditingCatalogTarget,
                        _ => InputMode.CatalogEditing
                    };
                    _statusMessage = null;
                    _forceRefresh = true;
                }
            }
        }

        // Tratamento especial para PostCatalogReview
        if (mode == InputMode.PostCatalogReview)
        {
            if (key.KeyChar == '2')
            {
                lock (_stateLock)
                {
                    _inputMode = InputMode.EditingCatalogObservacoes;
                    _forceRefresh = true;
                }
            }
        }

        // Voltar ao review após editar observações
        if (mode == InputMode.EditingCatalogObservacoes && key.Key == ConsoleKey.Enter)
        {
            lock (_stateLock)
            {
                _inputMode = InputMode.PostCatalogReview;
                _forceRefresh = true;
            }
        }
    }

    private IConsoleView? GetCurrentView()
    {
        // Se estamos em review pós-catalogação, sempre retornar CatalogView
        if (_catalogView.IsInPostReviewMode() && _inputMode == InputMode.PostCatalogReview)
        {
            return _catalogView;
        }

        return _currentView switch
        {
            AppView.Overview => _overviewView,
            AppView.Monitor => _monitorView,
            AppView.Catalog => _catalogView,
            AppView.Tests => _testsView,
            AppView.Manage => _manageView,
            _ => null
        };
    }
}
