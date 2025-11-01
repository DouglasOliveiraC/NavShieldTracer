using NavShieldTracer.ConsoleApp.Services;

namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// Contexto compartilhado entre todas as views do console.
/// Contém referências aos serviços e utilitários necessários.
/// </summary>
public sealed class ViewContext
{
    /// <summary>
    /// Serviço principal da aplicação NavShieldTracer.
    /// </summary>
    public NavShieldAppService AppService { get; }

    /// <summary>
    /// Lock para sincronização de estado entre views.
    /// </summary>
    public object StateLock { get; }

    /// <summary>
    /// Callback para forçar atualização da UI.
    /// </summary>
    public Action RequestRefresh { get; }

    /// <summary>
    /// Callback para atualizar mensagem de status.
    /// </summary>
    public Action<string?> SetStatusMessage { get; }

    /// <summary>
    /// Callback para alterar o modo de entrada ativo do console.
    /// </summary>
    public Action<InputMode> SetInputMode { get; }

    /// <summary>
    /// Função para obter o modo de entrada atual.
    /// </summary>
    public Func<InputMode> GetCurrentInputMode { get; }

    /// <summary>
    /// Função para obter o intervalo de atualização preferencial da view atual.
    /// </summary>
    public Func<TimeSpan> GetCurrentViewRefreshInterval { get; }

    /// <summary>
    /// Cria uma nova instância do contexto de view.
    /// </summary>
    /// <param name="appService">Serviço principal da aplicação.</param>
    /// <param name="stateLock">Lock para sincronização.</param>
    /// <param name="requestRefresh">Callback para refresh.</param>
    /// <param name="setStatusMessage">Callback para mensagens de status.</param>
    /// <param name="setInputMode">Callback para definir o modo de entrada.</param>
    /// <param name="getCurrentInputMode">Função para obter o modo de entrada atual.</param>
    /// <param name="getCurrentViewRefreshInterval">Função para obter o intervalo de atualização da view atual.</param>
    public ViewContext(
        NavShieldAppService appService,
        object stateLock,
        Action requestRefresh,
        Action<string?> setStatusMessage,
        Action<InputMode> setInputMode,
        Func<InputMode> getCurrentInputMode,
        Func<TimeSpan> getCurrentViewRefreshInterval)
    {
        AppService = appService;
        StateLock = stateLock;
        RequestRefresh = requestRefresh;
        SetStatusMessage = setStatusMessage;
        SetInputMode = setInputMode;
        GetCurrentInputMode = getCurrentInputMode;
        GetCurrentViewRefreshInterval = getCurrentViewRefreshInterval;
    }
}
