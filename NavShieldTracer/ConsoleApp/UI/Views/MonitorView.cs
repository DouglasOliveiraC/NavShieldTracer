using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using NavShieldTracer.ConsoleApp.Services;
using NavShieldTracer.Modules.Heuristics.Engine;
using NavShieldTracer.Modules.Diagnostics;
using HeuristicThreatSeverityTarja = NavShieldTracer.Modules.Heuristics.Normalization.ThreatSeverityTarja;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View completa para monitoramento em tempo real de processos.
/// Gerencia seleção de processos, exibição de sessão ativa e análise de nível de ameaça.
/// </summary>
public sealed class MonitorView : IConsoleView
{
    private const int MaxProcessosVisiveis = 8;
    private const int MaxLogsVisiveis = 6;
    private const int MaxProcessSelectionOptions = 10;

    private readonly ViewContext _context;

    public TimeSpan RefreshInterval => TimeSpan.FromMilliseconds(500); // Atualiza a cada 500ms

    // Estado de monitoramento
    private string _monitorTarget = string.Empty;
    private IReadOnlyList<ProcessSnapshot> _monitorSuggestions = Array.Empty<ProcessSnapshot>();
    private IReadOnlyList<ProcessSnapshot> _topProcesses = Array.Empty<ProcessSnapshot>();
    private IReadOnlyList<ProcessSnapshot> _processSelectionOptions = Array.Empty<ProcessSnapshot>();
    private ProcessSelectionSource _processSelectionSource = ProcessSelectionSource.None;
    private DateTime _lastSuggestionsUpdate = DateTime.MinValue;
    private DateTime _lastTopProcessesUpdate = DateTime.MinValue;
    private int _selectedProcessIndex = -1;

    /// <summary>
    /// Cria uma nova instância da view de monitoramento.
    /// </summary>
    /// <param name="context">Contexto compartilhado de views.</param>
    public MonitorView(ViewContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Constrói o conteúdo visual da view.
    /// </summary>
    /// <param name="now">Timestamp atual.</param>
    public IRenderable BuildContent(DateTime now)
    {
        var activeSession = _context.AppService.GetActiveSessionSnapshot();

        RefreshProcessCaches(_monitorTarget, now);

        var grid = new Grid().AddColumn();

        grid.AddRow("[cyan1 bold]Monitoramento em Tempo Real[/]");
        grid.AddRow("");

        var targetDisplay = $"[cyan1]{Markup.Escape(_monitorTarget)}[/]";
        grid.AddRow($"[grey]Executavel:[/] {targetDisplay}");

        grid.AddRow("[grey]Comandos: [[Enter]] Editar  [[M]] Iniciar  [[S]] Parar  [[C]] Limpar[/]");

        if (activeSession != null)
        {
            grid.AddRow("").AddRow("[green bold]═══ Sessao Ativa ═══[/]").AddRow("");

            var duration = now - activeSession.StartedAt;
            var heuristicSnapshot = activeSession.HeuristicSnapshot;
            var threatLevel = heuristicSnapshot?.SessionThreatLevel ?? HeuristicThreatSeverityTarja.Verde;
            var threatMarkupColor = ResolveThreatMarkupColor(threatLevel);

            grid.AddRow($"[grey]Alvo:[/] [green]{Markup.Escape(activeSession.TargetExecutable)}[/]");
            grid.AddRow($"[grey]Eventos:[/] [cyan1]{activeSession.TotalEventos}[/]");
            grid.AddRow($"[grey]Processos ativos:[/] [yellow]{activeSession.Statistics.ProcessosAtivos}[/]");
            grid.AddRow($"[grey]Processos encerrados:[/] [yellow]{activeSession.Statistics.ProcessosEncerrados}[/]");
            grid.AddRow($"[grey]Duracao:[/] [cyan1]{duration:hh\\:mm\\:ss}[/]");
            grid.AddRow($"[grey]Nivel de Alerta:[/] [{threatMarkupColor} bold]{ResolveThreatText(threatLevel)}[/]");

            grid.AddRow("");
            grid.AddRow("[grey bold]Analise Heuristica[/]");
            grid.AddRow("");

            if (heuristicSnapshot is null)
            {
                grid.AddRow("[grey italic]Monitor heuristico aguardando eventos suficientes para avaliar esta sessao.[/]");
            }
            else
            {
                grid.AddRow(BuildHeuristicSummaryPanel(heuristicSnapshot));

                var matchesTable = BuildTopMatchesTable(heuristicSnapshot);
                if (matchesTable is not null)
                {
                    grid.AddRow(matchesTable);
                }
            }

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

        // Mostrar processos sugeridos ou top processos
        if (_processSelectionOptions.Count > 0)
        {
            var label = _processSelectionSource == ProcessSelectionSource.Suggestions
                ? $"[grey bold]Processos correspondentes em execucao ({_processSelectionOptions.Count} homonimo(s))[/]"
                : "[grey bold]Top processos do sistema[/]";

            grid.AddRow("").AddRow(label);
            grid.AddRow(BuildProcessTable(_processSelectionOptions, _selectedProcessIndex));
        }
        else
        {
            if (_monitorSuggestions.Count > 0)
            {
                grid.AddRow("").AddRow($"[yellow bold]TODOS os {_monitorSuggestions.Count} processo(s) homonimo(s) abaixo serao monitorados:[/]");
                grid.AddRow(BuildProcessTable(_monitorSuggestions));
            }
            else
            {
                grid.AddRow("").AddRow("[grey bold]Top processos por uso de memoria[/]");
                grid.AddRow(_topProcesses.Count > 0
                    ? BuildProcessTable(_topProcesses)
                    : new Markup("[grey italic]Nao foi possivel carregar a lista de processos ou nenhum processo acessivel foi encontrado.[/]"));
            }
        }

        return grid;
    }

    /// <summary>
    /// Processa entrada do usuário em modo navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    public async Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.M)
        {
            await StartMonitoringAsync().ConfigureAwait(false);
        }
        else if (key.Key == ConsoleKey.S)
        {
            await StopMonitoringAsync().ConfigureAwait(false);
        }
        else if (key.Key == ConsoleKey.C)
        {
            ClearMonitorTarget();
        }
    }

    /// <summary>
    /// Processa entrada do usuário em modo edição.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <param name="mode">Modo de edição atual.</param>
    public async Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        if (mode == InputMode.EditingMonitorTarget)
        {
            await HandleEditMonitorTargetAsync(key).ConfigureAwait(false);
        }
        else if (mode == InputMode.SelectingMonitorProcess)
        {
            HandleSelectMonitorProcess(key);
        }
    }

    /// <summary>
    /// Retorna o modo de edição padrão ao pressionar Enter.
    /// </summary>
    public InputMode? GetDefaultEditMode()
    {
        return InputMode.EditingMonitorTarget;
    }

    private async Task StartMonitoringAsync()
    {
        if (string.IsNullOrWhiteSpace(_monitorTarget))
        {
            _context.SetStatusMessage("Erro: Informe o nome do executavel primeiro");
            return;
        }

        var trimmedTarget = _monitorTarget.Trim();
        IReadOnlyList<ProcessSnapshot> matches;
        try
        {
            matches = _context.AppService.FindProcesses(trimmedTarget, MaxProcessSelectionOptions);
        }
        catch
        {
            matches = Array.Empty<ProcessSnapshot>();
        }

        if (matches.Count == 0)
        {
            _context.SetStatusMessage("Nenhum processo ativo encontrado com esse nome.");
            return;
        }

        var resolvedTarget = matches.Any(m => m.ProcessName.Equals(trimmedTarget, StringComparison.OrdinalIgnoreCase))
            ? trimmedTarget
            : matches[0].ProcessName;

        try
        {
            await Task.Run(() => _context.AppService.StartMonitoring(resolvedTarget));
            lock (_context.StateLock)
            {
                _monitorTarget = resolvedTarget;
                _monitorSuggestions = matches;
                _lastSuggestionsUpdate = DateTime.Now;
                ResetProcessSelection();
            }
            _context.SetStatusMessage($"Monitoramento iniciado: {resolvedTarget} - Rastreando {matches.Count} processo(s) homonimo(s) + filhos");
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro: {ex.Message}");
        }
    }

    private async Task StopMonitoringAsync()
    {
        try
        {
            await _context.AppService.StopActiveSessionAsync().ConfigureAwait(false);
            _context.SetStatusMessage("Monitoramento encerrado com sucesso");
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro: {ex.Message}");
        }
    }

    private void ClearMonitorTarget()
    {
        lock (_context.StateLock)
        {
            _monitorTarget = string.Empty;
            _monitorSuggestions = Array.Empty<ProcessSnapshot>();
            ResetProcessSelection();
            _lastSuggestionsUpdate = DateTime.MinValue;
        }
        _context.SetStatusMessage(null);
        _context.RequestRefresh();
    }

    private Task HandleEditMonitorTargetAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            IReadOnlyList<ProcessSnapshot> suggestions;
            IReadOnlyList<ProcessSnapshot> top;

            try
            {
                suggestions = string.IsNullOrWhiteSpace(_monitorTarget)
                    ? Array.Empty<ProcessSnapshot>()
                    : _context.AppService.FindProcesses(_monitorTarget, MaxProcessSelectionOptions);
            }
            catch
            {
                suggestions = Array.Empty<ProcessSnapshot>();
            }

            try
            {
                top = _context.AppService.GetTopProcesses(MaxProcessSelectionOptions);
            }
            catch
            {
                top = Array.Empty<ProcessSnapshot>();
            }

            lock (_context.StateLock)
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
                    _context.SetStatusMessage($"Selecione o processo correspondente ({suggestions.Count} encontrado(s)).");
                }
                else if (top.Count > 0)
                {
                    _processSelectionOptions = top;
                    _processSelectionSource = ProcessSelectionSource.TopProcesses;
                    _selectedProcessIndex = 0;
                    _context.SetStatusMessage("Nenhum processo correspondente. Utilize o top de processos.");
                }
                else
                {
                    ResetProcessSelection();
                    _context.SetStatusMessage("Nenhum processo em execucao encontrado.");
                }
            }
            _context.RequestRefresh();
            return Task.CompletedTask;
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_monitorTarget.Length > 0)
                {
                    _monitorTarget = _monitorTarget[..^1];
                    _lastSuggestionsUpdate = DateTime.MinValue;
                    ResetProcessSelection();
                    _monitorSuggestions = Array.Empty<ProcessSnapshot>();
                }
            }
            _context.SetStatusMessage(null);
            _context.RequestRefresh();
            return Task.CompletedTask;
        }

        if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                _monitorTarget += key.KeyChar;
                _lastSuggestionsUpdate = DateTime.MinValue;
                ResetProcessSelection();
                _monitorSuggestions = Array.Empty<ProcessSnapshot>();
            }
            _context.SetStatusMessage(null);
            _context.RequestRefresh();
        }

        return Task.CompletedTask;
    }

    private void HandleSelectMonitorProcess(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            lock (_context.StateLock)
            {
                if (_processSelectionOptions.Count == 0 ||
                    _selectedProcessIndex < 0 ||
                    _selectedProcessIndex >= _processSelectionOptions.Count)
                {
                    _context.SetStatusMessage("Selecione um processo valido antes de confirmar.");
                    return;
                }

                var selected = _processSelectionOptions[_selectedProcessIndex];
                _monitorTarget = selected.ProcessName;

                IReadOnlyList<ProcessSnapshot> allMatches;
                try
                {
                    allMatches = _context.AppService.FindProcesses(selected.ProcessName, 100);
                }
                catch
                {
                    allMatches = new[] { selected };
                }

                _monitorSuggestions = allMatches;
                _lastSuggestionsUpdate = DateTime.Now;
                _context.SetStatusMessage($"Alvo configurado: {selected.ProcessName} - {allMatches.Count} processo(s) homonimo(s) serao monitorados.");
                ResetProcessSelection();
            }
            _context.RequestRefresh();
            return;
        }

        if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
        {
            var direction = key.Key == ConsoleKey.UpArrow ? -1 : 1;

            lock (_context.StateLock)
            {
                var count = _processSelectionOptions.Count;
                if (count == 0)
                {
                    _context.SetStatusMessage("Nenhum processo disponivel para selecao.");
                    return;
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
                _context.SetStatusMessage($"Selecionado: {current.ProcessName} (PID {current.Pid}).");
            }
            _context.RequestRefresh();
            return;
        }

        if (char.IsDigit(key.KeyChar) && key.KeyChar >= '1' && key.KeyChar <= '9')
        {
            var desiredIndex = key.KeyChar - '1';

            lock (_context.StateLock)
            {
                if (desiredIndex < _processSelectionOptions.Count)
                {
                    _selectedProcessIndex = desiredIndex;
                    if (_processSelectionOptions.Count > 0)
                    {
                        var current = _processSelectionOptions[_selectedProcessIndex];
                        _context.SetStatusMessage($"Selecionado: {current.ProcessName} (PID {current.Pid}).");
                    }
                }
                else
                {
                    _context.SetStatusMessage("Indice fora da lista de processos.");
                }
            }
            _context.RequestRefresh();
            return;
        }

        if (key.Key == ConsoleKey.Tab)
        {
            lock (_context.StateLock)
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
                        _context.SetStatusMessage($"Fonte alterada: {(_processSelectionSource == ProcessSelectionSource.Suggestions ? "Sugestoes" : "Top processos")} - {current.ProcessName} (PID {current.Pid}).");
                    }
                    else
                    {
                        _selectedProcessIndex = -1;
                        _context.SetStatusMessage("Lista de processos vazia apos alternar fonte.");
                    }
                }
                else
                {
                    _context.SetStatusMessage("Nao ha outra lista de processos para alternar.");
                }
            }
            _context.RequestRefresh();
        }
    }

    private void RefreshProcessCaches(string monitorTarget, DateTime now)
    {
        IReadOnlyList<ProcessSnapshot>? newTop = null;
        IReadOnlyList<ProcessSnapshot>? newSuggestions = null;

        if ((now - _lastTopProcessesUpdate) >= TimeSpan.FromSeconds(5))
        {
            try
            {
                newTop = _context.AppService.GetTopProcesses(MaxProcessSelectionOptions);
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
                newSuggestions = _context.AppService.FindProcesses(monitorTarget, MaxProcessSelectionOptions);
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

        lock (_context.StateLock)
        {
            if (newTop is not null)
            {
                _topProcesses = newTop;
                _lastTopProcessesUpdate = now;

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

                if (_processSelectionSource == ProcessSelectionSource.Suggestions)
                {
                    _processSelectionOptions = newSuggestions;
                    if (_selectedProcessIndex >= newSuggestions.Count)
                    {
                        _selectedProcessIndex = newSuggestions.Count - 1;
                    }
                }
            }
        }
        _context.RequestRefresh();
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

    private void ResetProcessSelection()
    {
        _processSelectionOptions = Array.Empty<ProcessSnapshot>();
        _processSelectionSource = ProcessSelectionSource.None;
        _selectedProcessIndex = -1;
    }

    private static Panel BuildHeuristicSummaryPanel(SessionSnapshot snapshot)
    {
        var infoGrid = new Grid();
        infoGrid.AddColumn(new GridColumn().NoWrap());
        infoGrid.AddColumn(new GridColumn());

        infoGrid.AddRow(
            new Markup("[grey]Atualizado:[/]"),
            new Markup($"[silver]{snapshot.SnapshotAt:HH\\:mm\\:ss}[/]"));
        infoGrid.AddRow(
            new Markup("[grey]Eventos analisados:[/]"),
            new Markup($"[cyan1]{snapshot.EventCountAtSnapshot}[/]"));
        infoGrid.AddRow(
            new Markup("[grey]Processos ativos:[/]"),
            new Markup($"[yellow]{snapshot.ActiveProcessesCount}[/]"));
        infoGrid.AddRow(
            new Markup("[grey]Tarja heuristica:[/]"),
            new Markup($"[{ResolveThreatMarkupColor(snapshot.SessionThreatLevel)} bold]{ResolveThreatText(snapshot.SessionThreatLevel)}[/]"));

        var highestMatch = snapshot.HighestMatch;
        if (highestMatch is not null)
        {
            var matchColor = ResolveThreatMarkupColor(highestMatch.ThreatLevel);
            infoGrid.AddRow(
                new Markup("[grey]Tecnica dominante:[/]"),
                new Markup($"[{matchColor}]{Markup.Escape(highestMatch.TechniqueId)}[/] [grey]-[/] {Markup.Escape(highestMatch.TechniqueName)}"));
            infoGrid.AddRow(
                new Markup("[grey]Similaridade/Confianca:[/]"),
                new Markup($"[cyan1]{highestMatch.Similarity:P1}[/] • {highestMatch.Confidence.ToUpperInvariant()}"));
        }
        else
        {
            infoGrid.AddRow(
                new Markup("[grey]Tecnica dominante:[/]"),
                new Markup("[grey70]Nenhuma correlacao ativa[/]"));
        }

        var panel = new Panel(infoGrid)
        {
            Border = BoxBorder.Rounded
        };
        panel.BorderStyle = new Style(foreground: ResolveThreatSpectreColor(snapshot.SessionThreatLevel));
        panel.Header = new PanelHeader("Monitor Heuristico", Justify.Center);
        return panel;
    }

    private static Table? BuildTopMatchesTable(SessionSnapshot snapshot)
    {
        var topMatches = snapshot.Matches
            .OrderByDescending(m => m.Similarity)
            .Take(3)
            .ToList();

        if (topMatches.Count == 0)
        {
            return null;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("[grey]Tecnica[/]")
            .AddColumn("[grey]Tarja[/]")
            .AddColumn("[grey]Similaridade[/]")
            .AddColumn("[grey]Confianca[/]");

        foreach (var match in topMatches)
        {
            var matchColor = ResolveThreatMarkupColor(match.ThreatLevel);
            table.AddRow(
                $"[white]{Markup.Escape(match.TechniqueId)}[/] [grey]-[/] {Markup.Escape(match.TechniqueName)}",
                $"[{matchColor}]{ResolveThreatText(match.ThreatLevel)}[/]",
                $"[cyan1]{match.Similarity:P1}[/]",
                $"[cyan1]{match.Confidence.ToUpperInvariant()}[/]");
        }

        return table;
    }

    private static string ResolveThreatText(HeuristicThreatSeverityTarja level) => level switch
    {
        HeuristicThreatSeverityTarja.Verde => "VERDE",
        HeuristicThreatSeverityTarja.Amarelo => "AMARELA",
        HeuristicThreatSeverityTarja.Laranja => "LARANJA",
        HeuristicThreatSeverityTarja.Vermelho => "VERMELHA",
        _ => "DESCONHECIDA"
    };

    private static string ResolveThreatMarkupColor(HeuristicThreatSeverityTarja level) => level switch
    {
        HeuristicThreatSeverityTarja.Verde => "green3",
        HeuristicThreatSeverityTarja.Amarelo => "yellow1",
        HeuristicThreatSeverityTarja.Laranja => "orange3",
        HeuristicThreatSeverityTarja.Vermelho => "red1",
        _ => "grey"
    };

    private static Color ResolveThreatSpectreColor(HeuristicThreatSeverityTarja level) => level switch
    {
        HeuristicThreatSeverityTarja.Verde => Color.Green,
        HeuristicThreatSeverityTarja.Amarelo => Color.Yellow,
        HeuristicThreatSeverityTarja.Laranja => Color.Orange3,
        HeuristicThreatSeverityTarja.Vermelho => Color.Red,
        _ => Color.Grey
    };

    /// <summary>
    /// Obtém o nome do processo alvo de monitoramento.
    /// </summary>
    public string GetMonitorTarget() => _monitorTarget;

    /// <summary>
    /// Define o nome do processo alvo de monitoramento.
    /// </summary>
    /// <param name="value">Nome do processo.</param>
    public void SetMonitorTarget(string value) { lock (_context.StateLock) { _monitorTarget = value; } }
}
