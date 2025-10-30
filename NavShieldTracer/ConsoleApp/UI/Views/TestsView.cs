using System;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View para visualização e exportação de testes catalogados.
/// </summary>
public sealed class TestsView : IConsoleView
{
    private readonly ViewContext _context;
    private string _testsIdInput = string.Empty;

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
        var testes = _context.AppService.ListarTestes();

        var grid = new Grid().AddColumn();
        grid.AddRow("[blue bold]Testes Catalogados[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Total:[/] [cyan1]{testes.Count}[/]");

        var idDisplay = $"[cyan1]{Markup.Escape(_testsIdInput)}[/]";
        grid.AddRow($"[grey]ID para exportar:[/] {idDisplay}");
        grid.AddRow("");
        grid.AddRow("[grey]Comandos: [[Enter]] Editar ID  [[E]] Exportar[/]");

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

    /// <summary>
    /// Processa entrada do usuário em modo navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    public async Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.E)
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
        if (mode != InputMode.EditingTestId) return Task.CompletedTask;

        if (key.Key == ConsoleKey.Enter)
        {
            // Retorna ao modo navegação - será tratado pelo ConsoleUI
            return Task.CompletedTask;
        }
        else if (key.Key == ConsoleKey.Backspace)
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
        else if (char.IsDigit(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                _testsIdInput += key.KeyChar;
                _context.RequestRefresh();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retorna o modo de edição padrão ao pressionar Enter.
    /// </summary>
    public InputMode? GetDefaultEditMode()
    {
        return InputMode.EditingTestId;
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
