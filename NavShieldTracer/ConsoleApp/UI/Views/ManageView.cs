using System;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using NavShieldTracer.Modules.Models;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View para gerenciamento de testes catalogados: editar, excluir, exportar.
/// </summary>
public sealed class ManageView : IConsoleView
{
    private readonly ViewContext _context;

    /// <summary>
    /// Intervalo padrao para atualizar informacoes de testes catalogados.
    /// </summary>
    public TimeSpan RefreshInterval => TimeSpan.FromSeconds(3); // Atualiza a cada 3 segundos

    private string _manageIdInput = string.Empty;
    private int? _manageSelectedId;
    private int? _manageSessionId;
    private string _manageNumero = string.Empty;
    private string _manageNome = string.Empty;
    private string _manageDescricao = string.Empty;
    private string _manageTarja = string.Empty;
    private string _manageNotes = string.Empty;
    private string? _manageTarjaReason;
    private string? _manageNormalizationStatus;
    private DateTime? _manageNormalizedAt;
    private ResumoTesteAtomico? _manageResumo;
    private bool _awaitingDeleteConfirmation;
    private DateTime _deleteConfirmationExpiresAt;

    /// <summary>
    /// Cria uma nova instância da view de gerenciamento.
    /// </summary>
    /// <param name="context">Contexto compartilhado de views.</param>
    public ManageView(ViewContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Constrói o conteúdo visual da view.
    /// </summary>
    /// <param name="now">Timestamp atual.</param>
    public IRenderable BuildContent(DateTime now)
    {
        var testes = _context.AppService.ListarTestes();

        var grid = new Grid().AddColumn();
        grid.AddRow("[magenta bold]Gerenciar Testes Catalogados[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total catalogados:[/] [cyan1]{testes.Count}[/]");

        var currentInputMode = _context.GetCurrentInputMode();

        var idLabel = currentInputMode == InputMode.EditingManageId ? "[black on yellow]ID alvo:[/]" : "[grey]ID alvo:[/]";
        var idDisplay = currentInputMode == InputMode.EditingManageId
            ? $"[black on yellow]{Markup.Escape(_manageIdInput)}[/]"
            : $"[cyan1]{Markup.Escape(_manageIdInput)}[/]";
        grid.AddRow($"{idLabel} {idDisplay}");

        if (_manageSelectedId.HasValue)
        {
            grid.AddRow($"[grey]ID carregado:[/] [green]{_manageSelectedId.Value}[/]");
        }

        grid.AddRow("[grey]Comandos: [[I]] ID  [[L]] Carregar  [[N]] Número  [[M]] Nome  [[D]] Descrição  [[T]] Tarja  [[O]] Notas[/]");
        grid.AddRow("[grey]          [[U]] Atualizar  [[E]] Exportar  [[X]] Excluir  [[R]] Limpar[/]");

        if (_manageSelectedId.HasValue)
        {
            grid.AddRow("");
            grid.AddRow("[magenta bold]Metadados do teste[/]");

            var metaTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .HideHeaders()
                .AddColumn(new TableColumn("[grey]Campo[/]").NoWrap().Width(20))
                .AddColumn(new TableColumn("[grey]Valor[/]"));

            // Destacar campos que estão sendo editados com fundo colorido
            var numeroLabel = currentInputMode == InputMode.EditingManageNumero ? "[black on yellow]Número[/]" : "[grey]Número[/]";
            var numeroValue = currentInputMode == InputMode.EditingManageNumero
                ? $"[black on yellow]{Markup.Escape(_manageNumero)}[/]"
                : $"[yellow]{Markup.Escape(_manageNumero)}[/]";
            metaTable.AddRow(numeroLabel, numeroValue);

            var nomeLabel = currentInputMode == InputMode.EditingManageNome ? "[black on yellow]Nome[/]" : "[grey]Nome[/]";
            var nomeValue = currentInputMode == InputMode.EditingManageNome
                ? $"[black on yellow]{Markup.Escape(_manageNome)}[/]"
                : Markup.Escape(_manageNome);
            metaTable.AddRow(nomeLabel, nomeValue);

            var descricaoLabel = currentInputMode == InputMode.EditingManageDescricao ? "[black on yellow]Descrição[/]" : "[grey]Descrição[/]";
            var descricaoValue = currentInputMode == InputMode.EditingManageDescricao
                ? $"[black on yellow]{Markup.Escape(_manageDescricao)}[/]"
                : $"[grey70]{Markup.Escape(_manageDescricao)}[/]";
            metaTable.AddRow(descricaoLabel, descricaoValue);

            // Exibir tarja com cor apropriada e destaque se editando
            var tarjaLabel = currentInputMode == InputMode.EditingManageTarja ? "[black on yellow]Tarja/Severidade[/]" : "[grey]Tarja/Severidade[/]";
            var tarjaDisplay = string.IsNullOrWhiteSpace(_manageTarja) ? "[grey]Não definida[/]" : GetTarjaColoredText(_manageTarja, currentInputMode == InputMode.EditingManageTarja);
            metaTable.AddRow(tarjaLabel, tarjaDisplay);

            if (!string.IsNullOrWhiteSpace(_manageTarjaReason))
            {
                metaTable.AddRow("[grey]Razão da Tarja[/]", $"[grey70]{Markup.Escape(_manageTarjaReason)}[/]");
            }

            // Exibir notas/observações do usuário (formatado e legível)
            var notesLabel = currentInputMode == InputMode.EditingManageNotes ? "[black on yellow]Notas/Observações[/]" : "[grey]Notas/Observações[/]";
            var notesDisplay = "[grey]Nenhuma[/]";
            if (!string.IsNullOrWhiteSpace(_manageNotes))
            {
                // Limitar a 150 caracteres com quebra de linha inteligente
                var displayText = _manageNotes.Length > 150 ? _manageNotes.Substring(0, 150) + "..." : _manageNotes;
                // Substituir quebras de linha por espaços para exibição compacta
                displayText = displayText.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ");
                notesDisplay = currentInputMode == InputMode.EditingManageNotes
                    ? $"[black on yellow]{Markup.Escape(displayText)}[/]"
                    : $"[white]{Markup.Escape(displayText)}[/]";
            }
            metaTable.AddRow(notesLabel, notesDisplay);

            // Exibir status de normalização
            if (!string.IsNullOrWhiteSpace(_manageNormalizationStatus))
            {
                var statusColor = _manageNormalizationStatus == "Completed" ? "green" : _manageNormalizationStatus == "Pending" ? "yellow" : "red";
                metaTable.AddRow("[grey]Status Normalização[/]", $"[{statusColor}]{Markup.Escape(_manageNormalizationStatus)}[/]");
            }

            if (_manageNormalizedAt.HasValue)
            {
                metaTable.AddRow("[grey]Normalizado em[/]", $"[cyan1]{_manageNormalizedAt.Value:dd/MM/yyyy HH:mm:ss}[/]");
            }

            grid.AddRow(metaTable);

            if (_manageResumo is not null)
            {
                grid.AddRow("");
                grid.AddRow("[magenta bold]Resumo da execução[/]");
                grid.AddRow($"[grey]Execução:[/] [cyan1]{_manageResumo.DataExecucao:dd/MM/yyyy HH:mm:ss}[/]");
                grid.AddRow($"[grey]Duração:[/] [cyan1]{_manageResumo.DuracaoSegundos:F2}s[/]");
                grid.AddRow($"[grey]Total eventos:[/] [cyan1]{_manageResumo.TotalEventos}[/]");

                if (_manageResumo.EventosPorTipo.Count > 0)
                {
                    grid.AddRow("");
                    grid.AddRow("[grey bold]Distribuição de eventos:[/]");
                    var topEventos = _manageResumo.EventosPorTipo
                        .OrderByDescending(kv => kv.Value)
                        .Take(10);

                    foreach (var (eventId, count) in topEventos)
                    {
                        grid.AddRow($"  [grey]Event ID {eventId}:[/] [cyan1]{count}x[/]");
                    }
                }
            }
        }

        if (testes.Count > 0)
        {
            grid.AddRow("");
            grid.AddRow("[grey bold]Top 10 testes recentes:[/]");

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .AddColumn("[grey]ID[/]")
                .AddColumn("[grey]Número[/]")
                .AddColumn("[grey]Nome[/]")
                .AddColumn("[grey]Eventos[/]");

            foreach (var teste in testes.OrderByDescending(t => t.DataExecucao).Take(10))
            {
                var highlight = teste.Id == _manageSelectedId;
                var color = highlight ? "green" : "cyan1";
                table.AddRow(
                    $"[{color}]{teste.Id}[/]",
                    $"[{color}]{Markup.Escape(teste.Numero)}[/]",
                    $"[{color}]{Markup.Escape(teste.Nome)}[/]",
                    $"[{color}]{teste.TotalEventos}[/]"
                );
            }

            grid.AddRow(table);
        }

        return grid;
    }

    /// <summary>
    /// Processa entrada do usuário em modo navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    public async Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        // Detectar teclas de edição e preparar destaque ANTES de entrar em modo de edição
        if (key.Key == ConsoleKey.I)
        {
            _context.SetInputMode(InputMode.EditingManageId);
            return;
        }
        else if (key.Key == ConsoleKey.N)
        {
            if (!_manageSelectedId.HasValue)
            {
                _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
                return;
            }
            _context.SetInputMode(InputMode.EditingManageNumero);
            return;
        }
        else if (key.Key == ConsoleKey.M)
        {
            if (!_manageSelectedId.HasValue)
            {
                _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
                return;
            }
            _context.SetInputMode(InputMode.EditingManageNome);
            return;
        }
        else if (key.Key == ConsoleKey.D)
        {
            if (!_manageSelectedId.HasValue)
            {
                _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
                return;
            }
            _context.SetInputMode(InputMode.EditingManageDescricao);
            return;
        }
        else if (key.Key == ConsoleKey.T)
        {
            if (!_manageSelectedId.HasValue)
            {
                _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
                return;
            }
            _context.SetInputMode(InputMode.EditingManageTarja);
            return;
        }
        else if (key.Key == ConsoleKey.O)
        {
            if (!_manageSelectedId.HasValue)
            {
                _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
                return;
            }
            _context.SetInputMode(InputMode.EditingManageNotes);
            return;
        }

        if (key.Key != ConsoleKey.X && _awaitingDeleteConfirmation)
        {
            _awaitingDeleteConfirmation = false;
            _context.SetStatusMessage("Solicitacao de exclusao cancelada.");
        }

        if (key.Key == ConsoleKey.L)
        {
            LoadTest();
        }
        else if (key.Key == ConsoleKey.U)
        {
            await UpdateTestAsync().ConfigureAwait(false);
        }
        else if (key.Key == ConsoleKey.E)
        {
            await ExportTestAsync().ConfigureAwait(false);
        }
        else if (key.Key == ConsoleKey.X)
        {
            DeleteTest();
        }
        else if (key.Key == ConsoleKey.R)
        {
            ResetManageState();
        }
    }

    /// <summary>
    /// Processa entrada do usuário em modo edição.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <param name="mode">Modo de edição atual.</param>
    public Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        // O destaque agora eh baseado no InputMode global, nao ha necessidade de _activeEditMode local

        switch (mode)
        {
            case InputMode.EditingManageId:
                HandleEditManageId(key);
                break;
            case InputMode.EditingManageNumero:
                HandleEditManageNumero(key);
                break;
            case InputMode.EditingManageNome:
                HandleEditManageNome(key);
                break;
            case InputMode.EditingManageDescricao:
                HandleEditManageDescricao(key);
                break;
            case InputMode.EditingManageTarja:
                HandleEditManageTarja(key);
                break;
            case InputMode.EditingManageNotes:
                HandleEditManageNotes(key);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retorna o modo de edição padrão ao pressionar Enter.
    /// </summary>
    public InputMode? GetDefaultEditMode()
    {
        return InputMode.EditingManageId;
    }

    private void LoadTest()
    {
        if (!int.TryParse(_manageIdInput, out var id))
        {
            _context.SetStatusMessage("Informe um ID válido para carregar");
            return;
        }

        var testeCompleto = _context.AppService.ObterTesteCompleto(id);

        if (testeCompleto is null)
        {
            _context.SetStatusMessage($"Teste com ID {id} não encontrado");
            return;
        }

        lock (_context.StateLock)
        {
            _manageSelectedId = testeCompleto.Id;
            _manageSessionId = testeCompleto.SessionId;
            _manageNumero = testeCompleto.Numero;
            _manageNome = testeCompleto.Nome;
            _manageDescricao = testeCompleto.Descricao ?? string.Empty;
            _manageTarja = testeCompleto.Tarja ?? string.Empty;
            _manageTarjaReason = testeCompleto.TarjaReason;
            _manageNotes = testeCompleto.Notes ?? string.Empty;
            _manageNormalizationStatus = testeCompleto.NormalizationStatus;
            _manageNormalizedAt = testeCompleto.NormalizedAt;
        }

        _manageResumo = _context.AppService.ObterResumoTeste(id);
        _context.SetStatusMessage($"Teste {id} carregado com sucesso");

        _context.RequestRefresh();
    }

    private async Task UpdateTestAsync()
    {
        if (!_manageSelectedId.HasValue)
        {
            _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
            return;
        }

        var success = await Task.Run(() =>
        {
            try
            {
                // Atualizar campos básicos
                _context.AppService.AtualizarTeste(
                    _manageSelectedId.Value,
                    _manageNumero,
                    _manageNome,
                    _manageDescricao
                );

                // Atualizar tarja se especificada
                if (!string.IsNullOrWhiteSpace(_manageTarja))
                {
                    _context.AppService.AtualizarTarja(_manageSelectedId.Value, _manageTarja);
                }

                // Atualizar notas se sessão existe
                if (_manageSessionId.HasValue)
                {
                    _context.AppService.AtualizarNotas(_manageSessionId.Value, _manageNotes);
                }

                return true;
            }
            catch
            {
                return false;
            }
        });

        if (success)
        {
            _context.SetStatusMessage($"Teste {_manageSelectedId.Value} atualizado com sucesso");
        }
        else
        {
            _context.SetStatusMessage("Erro ao atualizar teste");
        }
    }

    private async Task ExportTestAsync()
    {
        if (!_manageSelectedId.HasValue)
        {
            _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
            return;
        }

        try
        {
            var path = await _context.AppService.ExportarTesteAsync(_manageSelectedId.Value).ConfigureAwait(false);
            _context.SetStatusMessage($"Exportado: {path}");
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro ao exportar: {ex.Message}");
        }
    }

    private void DeleteTest()
    {
        if (!_manageSelectedId.HasValue)
        {
            _context.SetStatusMessage("Carregue um teste primeiro (comando L)");
            return;
        }

        if (_awaitingDeleteConfirmation && DateTime.UtcNow > _deleteConfirmationExpiresAt)
        {
            _awaitingDeleteConfirmation = false;
        }

        if (!_awaitingDeleteConfirmation)
        {
            _awaitingDeleteConfirmation = true;
            _deleteConfirmationExpiresAt = DateTime.UtcNow.AddSeconds(10);
            _context.SetStatusMessage(
                $"Confirme a exclusao do teste {_manageSelectedId.Value}: pressione X novamente em ate 10 segundos (qualquer outra tecla cancela).");
            return;
        }

        _awaitingDeleteConfirmation = false;

        var success = _context.AppService.ExcluirTeste(_manageSelectedId.Value);

        if (success)
        {
            var deletedId = _manageSelectedId.Value;
            ResetManageState(announce: false);
            _context.SetStatusMessage($"Teste {deletedId} excluído com sucesso");
        }
        else
        {
            _context.SetStatusMessage("Erro ao excluir teste");
        }
    }

    private void ResetManageState(bool announce = true)
    {
        lock (_context.StateLock)
        {
            _manageSelectedId = null;
            _manageSessionId = null;
            _manageIdInput = string.Empty;
            _manageNumero = string.Empty;
            _manageNome = string.Empty;
            _manageDescricao = string.Empty;
            _manageTarja = string.Empty;
            _manageNotes = string.Empty;
            _manageTarjaReason = null;
            _manageNormalizationStatus = null;
            _manageNormalizedAt = null;
            _manageResumo = null;
            _awaitingDeleteConfirmation = false;
        }
        if (announce)
        {
            _context.SetStatusMessage("Campos limpos");
        }
        _context.RequestRefresh();
    }

    private void HandleEditManageId(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_manageIdInput.Length > 0)
                {
                    _manageIdInput = _manageIdInput[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (char.IsDigit(key.KeyChar) && _manageIdInput.Length < 9)
        {
            lock (_context.StateLock)
            {
                _manageIdInput += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditManageNumero(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_manageNumero.Length > 0)
                {
                    _manageNumero = _manageNumero[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar) && _manageNumero.Length < 32)
        {
            lock (_context.StateLock)
            {
                _manageNumero += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditManageNome(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_manageNome.Length > 0)
                {
                    _manageNome = _manageNome[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar) && _manageNome.Length < 120)
        {
            lock (_context.StateLock)
            {
                _manageNome += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditManageDescricao(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_manageDescricao.Length > 0)
                {
                    _manageDescricao = _manageDescricao[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar) && _manageDescricao.Length < 600)
        {
            lock (_context.StateLock)
            {
                _manageDescricao += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditManageTarja(ConsoleKeyInfo key)
    {
        // Permitir alternar entre as opções usando setas ou números
        if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
        {
            lock (_context.StateLock)
            {
                var tarjas = new[] { "Verde", "Azul", "Amarelo", "Laranja", "Vermelho" };
                var currentIndex = Array.IndexOf(tarjas, _manageTarja);

                if (currentIndex == -1)
                {
                    _manageTarja = tarjas[0];
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    currentIndex = (currentIndex - 1 + tarjas.Length) % tarjas.Length;
                    _manageTarja = tarjas[currentIndex];
                }
                else
                {
                    currentIndex = (currentIndex + 1) % tarjas.Length;
                    _manageTarja = tarjas[currentIndex];
                }

                _context.RequestRefresh();
            }
        }
        else if (key.KeyChar >= '1' && key.KeyChar <= '5')
        {
            lock (_context.StateLock)
            {
                _manageTarja = key.KeyChar switch
                {
                    '1' => "Verde",
                    '2' => "Azul",
                    '3' => "Amarelo",
                    '4' => "Laranja",
                    '5' => "Vermelho",
                    _ => _manageTarja
                };
                _context.RequestRefresh();
            }
        }
    }

    private void HandleEditManageNotes(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_manageNotes.Length > 0)
                {
                    _manageNotes = _manageNotes[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar) && _manageNotes.Length < 1000)
        {
            lock (_context.StateLock)
            {
                _manageNotes += key.KeyChar;
                _context.RequestRefresh();
            }
        }
    }

    private string GetTarjaColoredText(string tarja, bool isEditing = false)
    {
        if (isEditing)
        {
            return tarja.ToLowerInvariant() switch
            {
                "verde" => "[black on yellow]Verde[/]",
                "azul" => "[black on yellow]Azul[/]",
                "amarelo" => "[black on yellow]Amarelo[/]",
                "laranja" => "[black on yellow]Laranja[/]",
                "vermelho" => "[black on yellow]Vermelho[/]",
                _ => $"[black on yellow]{Markup.Escape(tarja)}[/]"
            };
        }

        return tarja.ToLowerInvariant() switch
        {
            "verde" => "[green]Verde[/]",
            "azul" => "[deepskyblue1]Azul[/]",
            "amarelo" => "[yellow]Amarelo[/]",
            "laranja" => "[orange1]Laranja[/]",
            "vermelho" => "[red]Vermelho[/]",
            _ => $"[grey]{Markup.Escape(tarja)}[/]"
        };
    }
}
