namespace NavShieldTracer.ConsoleApp.UI.Views;

/// <summary>
/// Views disponíveis na aplicação.
/// </summary>
public enum AppView
{
    /// <summary>Dashboard principal.</summary>
    Overview = 0,
    /// <summary>Monitoramento ativo de processos.</summary>
    Monitor = 1,
    /// <summary>Catalogação de testes atômicos.</summary>
    Catalog = 2,
    /// <summary>Visualização de testes catalogados.</summary>
    Tests = 3,
    /// <summary>Gerenciamento e edição de testes.</summary>
    Manage = 4
}

/// <summary>
/// Modos de entrada para interação com o usuário.
/// </summary>
public enum InputMode
{
    /// <summary>Navegação entre views.</summary>
    Navigation,
    /// <summary>Edição do alvo de monitoramento.</summary>
    EditingMonitorTarget,
    /// <summary>Seleção de processo a monitorar.</summary>
    SelectingMonitorProcess,
    /// <summary>Edição do ID do teste.</summary>
    EditingTestId,
    /// <summary>Edição do ID na view de gerenciamento.</summary>
    EditingManageId,
    /// <summary>Edição do número da técnica MITRE.</summary>
    EditingManageNumero,
    /// <summary>Edição do nome do teste.</summary>
    EditingManageNome,
    /// <summary>Edição da descrição do teste.</summary>
    EditingManageDescricao,
    /// <summary>Edição da tarja de severidade.</summary>
    EditingManageTarja,
    /// <summary>Edição de notas adicionais.</summary>
    EditingManageNotes,
    /// <summary>Modo de edição da catalogação.</summary>
    CatalogEditing,
    /// <summary>Edição do número da técnica na catalogação.</summary>
    EditingCatalogNumero,
    /// <summary>Edição do nome na catalogação.</summary>
    EditingCatalogNome,
    /// <summary>Edição da descrição na catalogação.</summary>
    EditingCatalogDescricao,
    /// <summary>Edição do processo alvo na catalogação.</summary>
    EditingCatalogTarget,
    /// <summary>Revisão pós-catalogação.</summary>
    PostCatalogReview,
    /// <summary>Edição de observações da catalogação.</summary>
    EditingCatalogObservacoes
}

/// <summary>
/// Nível de ameaça detectado durante monitoramento.
/// </summary>
public enum ThreatLevel
{
    /// <summary>Ameaça baixa.</summary>
    Baixo,
    /// <summary>Ameaça moderada.</summary>
    Moderado,
    /// <summary>Ameaça média.</summary>
    Medio,
    /// <summary>Ameaça alta.</summary>
    Alto,
    /// <summary>Ameaça severa.</summary>
    Severo
}

/// <summary>
/// Fonte de seleção de processos.
/// </summary>
public enum ProcessSelectionSource
{
    /// <summary>Nenhuma fonte selecionada.</summary>
    None,
    /// <summary>Sugestões de processos.</summary>
    Suggestions,
    /// <summary>Processos com maior consumo de recursos.</summary>
    TopProcesses
}

/// <summary>
/// Níveis de alerta (tarja) para testes catalogados.
/// Sincronizado com ThreatSeverityTarja do módulo de normalização.
/// </summary>
public enum ThreatSeverityTarja
{
    /// <summary>Sem risco detectado.</summary>
    Verde = 0,
    /// <summary>Atenção requerida.</summary>
    Amarelo = 1,
    /// <summary>Alto risco.</summary>
    Laranja = 2,
    /// <summary>Ameaça crítica.</summary>
    Vermelho = 3
}
