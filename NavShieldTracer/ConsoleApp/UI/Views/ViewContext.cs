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

    public ViewContext(
        NavShieldAppService appService,
        object stateLock,
        Action requestRefresh,
        Action<string?> setStatusMessage)
    {
        AppService = appService;
        StateLock = stateLock;
        RequestRefresh = requestRefresh;
        SetStatusMessage = setStatusMessage;
    }
}
