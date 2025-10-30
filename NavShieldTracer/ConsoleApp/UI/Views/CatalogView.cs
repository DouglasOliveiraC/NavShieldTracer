using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Rendering;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Modules.Monitoring;
using NavShieldTracer.Modules.Diagnostics;
using NavShieldTracer.ConsoleApp.Services;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// View para catalogação de testes atômicos MITRE ATT&amp;CK.
/// Inclui fluxo de review pós-catalogação para observações e nível de alerta.
/// </summary>
public sealed class CatalogView : IConsoleView
{
    private readonly ViewContext _context;

    // Campos de catalogação
    private string _catalogNumero = string.Empty;
    private string _catalogNome = string.Empty;
    private string _catalogDescricao = string.Empty;
    private string _catalogTarget = "teste.exe";

    // Campos de review pós-catalogação
    private string _postReviewObservacoes = string.Empty;
    private ThreatSeverityTarja _postReviewTarja = ThreatSeverityTarja.Verde;
    private int? _completedTestId;

    /// <summary>
    /// Cria uma nova instância da view de catalogação.
    /// </summary>
    /// <param name="context">Contexto compartilhado de views.</param>
    public CatalogView(ViewContext context)
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
        var catalogSessionActive = activeSession is not null && activeSession.Kind == MonitoringSessionType.Catalog;

        var grid = new Grid().AddColumn();

        grid.AddRow("[yellow bold]Catalogar Teste Atomico MITRE ATT&CK[/]");
        grid.AddRow("");

        var numeroDisplay = $"[cyan1]{Markup.Escape(_catalogNumero)}[/]";
        grid.AddRow($"[grey]1. Numero (ex: T1055):[/] {numeroDisplay}");

        var nomeDisplay = $"[cyan1]{Markup.Escape(_catalogNome)}[/]";
        grid.AddRow($"[grey]2. Nome:[/] {nomeDisplay}");

        var descricaoDisplay = $"[cyan1]{Markup.Escape(_catalogDescricao)}[/]";
        grid.AddRow($"[grey]3. Descricao:[/] {descricaoDisplay}");

        var targetDisplay = $"[cyan1]{Markup.Escape(_catalogTarget)}[/]";
        grid.AddRow($"[grey]4. Executavel alvo:[/] {targetDisplay}");

        grid.AddRow("");

        var commandText = "[grey]Comandos: [[1-4]] Editar campos  [[I]] Iniciar catalogacao  [[C]] Limpar  [[Esc]] Sair";
        if (catalogSessionActive)
        {
            commandText += "  [[F]] Finalizar";
        }
        grid.AddRow(commandText + "[/]");

        if (catalogSessionActive)
        {
            grid.AddRow("[yellow bold]Pressione [[F]] ao concluir o teste para encerrar e salvar a catalogacao.[/]");
        }
        else
        {
            grid.AddRow("[yellow bold]IMPORTANTE: Certifique-se de que o processo alvo esta em execucao ANTES de pressionar [[I]]![/]");
        }

        if (catalogSessionActive && activeSession is not null)
        {
            var duration = DateTime.Now - activeSession.StartedAt;
            grid.AddRow("");
            grid.AddRow("[green bold]Catalogacao em andamento[/]");
            grid.AddRow($"[grey]Executavel:[/] [green]{Markup.Escape(activeSession.TargetExecutable)}[/]");
            grid.AddRow($"[grey]Eventos capturados:[/] [cyan1]{activeSession.TotalEventos}[/]");
            grid.AddRow($"[grey]Duracao:[/] [cyan1]{duration:hh\\:mm\\:ss}[/]");
        }

        return grid;
    }

    /// <summary>
    /// Constrói conteúdo para review pós-catalogação.
    /// </summary>
    public IRenderable BuildPostReviewContent()
    {
        var grid = new Grid().AddColumn();

        grid.AddRow("[yellow bold]Review do Teste Catalogado[/]");
        grid.AddRow("");
        grid.AddRow($"[grey]Teste:[/] [cyan1]{Markup.Escape(_catalogNumero)} - {Markup.Escape(_catalogNome)}[/]");
        grid.AddRow("");

        // Nível de alerta
        grid.AddRow("[cyan1 bold]1. Nivel de Alerta (Tarja)[/]");
        var tarjaOptions = new[]
        {
            ("Verde", ThreatSeverityTarja.Verde, "green"),
            ("Amarelo", ThreatSeverityTarja.Amarelo, "yellow"),
            ("Laranja", ThreatSeverityTarja.Laranja, "orange3"),
            ("Vermelho", ThreatSeverityTarja.Vermelho, "red")
        };

        foreach (var (nome, valor, cor) in tarjaOptions)
        {
            var selected = valor == _postReviewTarja ? "► " : "  ";
            var style = valor == _postReviewTarja ? $"[{cor} bold]" : $"[{cor}]";
            grid.AddRow($"{style}{selected}{nome}[/]");
        }

        grid.AddRow("");

        // Observações
        grid.AddRow("[cyan1 bold]2. Observacoes[/]");
        var obsDisplay = string.IsNullOrWhiteSpace(_postReviewObservacoes)
            ? "[grey italic]<nenhuma>[/]"
            : $"[grey70]{Markup.Escape(_postReviewObservacoes)}[/]";
        grid.AddRow(obsDisplay);
        grid.AddRow("");

        grid.AddRow("[grey]Comandos: [[1]] Mudar tarja (setas ↑/↓)  [[2]] Editar observacoes  [[S]] Salvar e finalizar  [[Esc]] Cancelar[/]");

        return grid;
    }

    /// <summary>
    /// Processa entrada do usuário em modo navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    public async Task HandleNavigationInputAsync(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.F)
        {
            await FinalizeCatalogAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processa entrada do usuário em modo edição.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <param name="mode">Modo de edição atual.</param>
    public async Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode)
    {
        switch (mode)
        {
            case InputMode.CatalogEditing:
                await HandleCatalogEditingModeAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingCatalogNumero:
                HandleEditCatalogNumero(key);
                break;
            case InputMode.EditingCatalogNome:
                HandleEditCatalogNome(key);
                break;
            case InputMode.EditingCatalogDescricao:
                HandleEditCatalogDescricao(key);
                break;
            case InputMode.EditingCatalogTarget:
                HandleEditCatalogTarget(key);
                break;
            case InputMode.PostCatalogReview:
                await HandlePostReviewNavigationAsync(key).ConfigureAwait(false);
                break;
            case InputMode.EditingCatalogObservacoes:
                HandleEditObservacoes(key);
                break;
        }
    }

    /// <summary>
    /// Retorna o modo de edição padrão ao pressionar Enter.
    /// </summary>
    public InputMode? GetDefaultEditMode()
    {
        return InputMode.CatalogEditing;
    }

    private async Task HandleCatalogEditingModeAsync(ConsoleKeyInfo key)
    {
        // 1-4: Editar campos
        if (key.KeyChar >= '1' && key.KeyChar <= '4')
        {
            // Modo será tratado pelo ConsoleUI principal
            return;
        }

        // C: Limpar campos
        if (key.Key == ConsoleKey.C)
        {
            lock (_context.StateLock)
            {
                _catalogNumero = string.Empty;
                _catalogNome = string.Empty;
                _catalogDescricao = string.Empty;
                _catalogTarget = "teste.exe";
                _context.SetStatusMessage("Campos limpos");
                _context.RequestRefresh();
            }
            return;
        }

        // I: Iniciar catalogação
        if (key.Key == ConsoleKey.I || key.Key == ConsoleKey.Enter)
        {
            await IniciarCatalogacaoAsync().ConfigureAwait(false);
            return;
        }

        // F: Finalizar catalogação
        if (key.Key == ConsoleKey.F)
        {
            await FinalizeCatalogAsync().ConfigureAwait(false);
            return;
        }
    }

    private async Task IniciarCatalogacaoAsync()
    {
        // Validação de campos obrigatórios
        if (string.IsNullOrWhiteSpace(_catalogNumero))
        {
            _context.SetStatusMessage("Erro: Numero do teste eh obrigatorio");
            return;
        }

        if (string.IsNullOrWhiteSpace(_catalogNome))
        {
            _context.SetStatusMessage("Erro: Nome do teste eh obrigatorio");
            return;
        }

        // Verificar se o processo alvo está rodando
        var normalizedTarget = _catalogTarget.Trim();
        if (!normalizedTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalizedTarget += ".exe";
        }

        var processName = System.IO.Path.GetFileNameWithoutExtension(normalizedTarget);
        IReadOnlyList<ProcessSnapshot> runningProcesses;
        try
        {
            runningProcesses = _context.AppService.FindProcesses(processName, 10);
        }
        catch
        {
            runningProcesses = Array.Empty<ProcessSnapshot>();
        }

        var hadActiveProcesses = runningProcesses.Count > 0;

        // Inicia processo de catalogacao
        try
        {
            var novoTeste = new NovoTesteAtomico(_catalogNumero.Trim(), _catalogNome.Trim(), _catalogDescricao?.Trim() ?? string.Empty);
            _context.AppService.StartCatalog(novoTeste, normalizedTarget);

            _context.SetStatusMessage(hadActiveProcesses
                ? $"Catalogacao iniciada: {_catalogNumero} - Monitorando {runningProcesses.Count} processo(s) '{normalizedTarget}'"
                : $"Catalogacao iniciada: {_catalogNumero} - Aguardando processo '{normalizedTarget}' iniciar para comecar a registrar eventos.");
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro: {ex.Message}");
        }
    }

    private async Task FinalizeCatalogAsync()
    {
        var activeSession = _context.AppService.GetActiveSessionSnapshot();
        if (activeSession is null || activeSession.Kind != MonitoringSessionType.Catalog)
        {
            _context.SetStatusMessage("Nenhuma catalogacao ativa para finalizar.");
            return;
        }

        try
        {
            var result = await _context.AppService.StopActiveSessionAsync().ConfigureAwait(false);

            // Armazenar ID do teste para o review
            var testes = _context.AppService.ListarTestes();
            var testeRecemCriado = testes.OrderByDescending(t => t.DataExecucao).FirstOrDefault();

            if (testeRecemCriado != null)
            {
                lock (_context.StateLock)
                {
                    _completedTestId = testeRecemCriado.Id;
                    ResetPostReviewFields();
                }
                // Modo será alterado para PostCatalogReview pelo ConsoleUI
                _context.SetStatusMessage($"Catalogacao finalizada: {result.TotalEventos} evento(s). Pressione Enter para review.");
            }
            else
            {
                _context.SetStatusMessage($"Catalogacao finalizada: {result.TotalEventos} evento(s) capturados.");
            }
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro ao finalizar catalogacao: {ex.Message}");
        }
    }

    private async Task HandlePostReviewNavigationAsync(ConsoleKeyInfo key)
    {
        if (key.KeyChar == '1')
        {
            // Não entra em modo de edição, usa setas para navegar
            _context.SetStatusMessage("Use setas ↑/↓ para mudar o nivel de alerta");
            return;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            lock (_context.StateLock)
            {
                int current = (int)_postReviewTarja;
                _postReviewTarja = (ThreatSeverityTarja)Math.Max(0, current - 1);
                _context.RequestRefresh();
            }
            return;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            lock (_context.StateLock)
            {
                int current = (int)_postReviewTarja;
                _postReviewTarja = (ThreatSeverityTarja)Math.Min(3, current + 1);
                _context.RequestRefresh();
            }
            return;
        }

        if (key.KeyChar == 'S' || key.KeyChar == 's')
        {
            await SavePostReviewAsync().ConfigureAwait(false);
            return;
        }

        // '2' e '3' serão tratados pelo ConsoleUI para mudar modo de edição
    }

    private void HandleEditObservacoes(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_postReviewObservacoes.Length > 0)
                {
                    _postReviewObservacoes = _postReviewObservacoes[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                if (_postReviewObservacoes.Length < 1000)
                {
                    _postReviewObservacoes += key.KeyChar;
                    _context.RequestRefresh();
                }
            }
        }
    }

    private async Task SavePostReviewAsync()
    {
        if (!_completedTestId.HasValue)
        {
            _context.SetStatusMessage("Erro: ID do teste nao encontrado");
            return;
        }

        try
        {
            var success = await Task.Run(() => _context.AppService.SaveTestReview(
                _completedTestId.Value,
                _postReviewTarja.ToString(),
                string.IsNullOrWhiteSpace(_postReviewObservacoes) ? null : _postReviewObservacoes
            ));

            if (success)
            {
                _context.SetStatusMessage($"Review salvo com sucesso! Tarja: {_postReviewTarja}");

                // Resetar campos
                ResetPostReviewFields();
                _completedTestId = null;
            }
            else
            {
                _context.SetStatusMessage("Erro ao salvar review no banco de dados");
            }

            await Task.Delay(100); // Pequeno delay para mostrar mensagem
        }
        catch (Exception ex)
        {
            _context.SetStatusMessage($"Erro ao salvar review: {ex.Message}");
        }
    }

    private void ResetPostReviewFields()
    {
        _postReviewObservacoes = string.Empty;
        _postReviewTarja = ThreatSeverityTarja.Verde;
    }

    private void HandleEditCatalogNumero(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_catalogNumero.Length > 0)
                {
                    _catalogNumero = _catalogNumero[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                if (_catalogNumero.Length < 32)
                {
                    _catalogNumero += key.KeyChar;
                    _context.RequestRefresh();
                }
            }
        }
    }

    private void HandleEditCatalogNome(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_catalogNome.Length > 0)
                {
                    _catalogNome = _catalogNome[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                if (_catalogNome.Length < 120)
                {
                    _catalogNome += key.KeyChar;
                    _context.RequestRefresh();
                }
            }
        }
    }

    private void HandleEditCatalogDescricao(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_catalogDescricao.Length > 0)
                {
                    _catalogDescricao = _catalogDescricao[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                if (_catalogDescricao.Length < 600)
                {
                    _catalogDescricao += key.KeyChar;
                    _context.RequestRefresh();
                }
            }
        }
    }

    private void HandleEditCatalogTarget(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            lock (_context.StateLock)
            {
                if (_catalogTarget.Length > 0)
                {
                    _catalogTarget = _catalogTarget[..^1];
                    _context.RequestRefresh();
                }
            }
        }
        else if (!char.IsControl(key.KeyChar))
        {
            lock (_context.StateLock)
            {
                if (_catalogTarget.Length < 120)
                {
                    _catalogTarget += key.KeyChar;
                    _context.RequestRefresh();
                }
            }
        }
    }

    /// <summary>
    /// Verifica se a view está em modo de review pós-catalogação.
    /// </summary>
    public bool IsInPostReviewMode() => _completedTestId.HasValue;
}
