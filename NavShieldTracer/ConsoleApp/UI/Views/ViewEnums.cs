namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// Views disponíveis na aplicação.
/// </summary>
public enum AppView
{
    Overview = 0,
    Monitor = 1,
    Catalog = 2,
    Tests = 3,
    Manage = 4
}

/// <summary>
/// Modos de entrada para interação com o usuário.
/// </summary>
public enum InputMode
{
    Navigation,
    EditingMonitorTarget,
    SelectingMonitorProcess,
    EditingTestId,
    EditingManageId,
    EditingManageNumero,
    EditingManageNome,
    EditingManageDescricao,
    EditingManageTarja,
    EditingManageNotes,
    CatalogEditing,
    EditingCatalogNumero,
    EditingCatalogNome,
    EditingCatalogDescricao,
    EditingCatalogTarget,
    PostCatalogReview,
    EditingCatalogObservacoes,
    EditingCatalogWhitelist
}

/// <summary>
/// Nível de ameaça detectado durante monitoramento.
/// </summary>
public enum ThreatLevel
{
    Baixo,
    Moderado,
    Medio,
    Alto,
    Severo
}

/// <summary>
/// Fonte de seleção de processos.
/// </summary>
public enum ProcessSelectionSource
{
    None,
    Suggestions,
    TopProcesses
}

/// <summary>
/// Níveis de alerta (tarja) para testes catalogados.
/// Sincronizado com ThreatSeverityTarja do módulo de normalização.
/// </summary>
public enum ThreatSeverityTarja
{
    Verde = 0,
    Amarelo = 1,
    Laranja = 2,
    Vermelho = 3
}
