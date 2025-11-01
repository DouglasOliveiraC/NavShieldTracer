using Spectre.Console.Rendering;
using System;
using System.Threading.Tasks;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// Interface base para views do console interativo.
/// </summary>
public interface IConsoleView
{
    /// <summary>
    /// Intervalo de atualização preferencial para esta view.
    /// </summary>
    TimeSpan RefreshInterval { get; }

    /// <summary>
    /// Constrói o conteúdo renderizável da view.
    /// </summary>
    /// <param name="now">Timestamp atual para exibição.</param>
    /// <returns>Conteúdo renderizável usando Spectre.Console.</returns>
    IRenderable BuildContent(DateTime now);

    /// <summary>
    /// Manipula entrada do usuário quando em modo de navegação.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <returns>Task assíncrona.</returns>
    Task HandleNavigationInputAsync(ConsoleKeyInfo key);

    /// <summary>
    /// Manipula entrada do usuário quando em modo de edição.
    /// </summary>
    /// <param name="key">Tecla pressionada.</param>
    /// <param name="mode">Modo de edição ativo.</param>
    /// <returns>Task assíncrona.</returns>
    Task HandleEditModeInputAsync(ConsoleKeyInfo key, InputMode mode);

    /// <summary>
    /// Retorna o modo de entrada ao pressionar Enter na navegação.
    /// </summary>
    /// <returns>Modo de entrada correspondente ou null se não aplicável.</returns>
    InputMode? GetDefaultEditMode();
}
