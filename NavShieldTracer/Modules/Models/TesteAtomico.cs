using System;
using System.Collections.Generic;

namespace NavShieldTracer.Modules.Models
{
    /// <summary>
    /// Informações de um teste atômico catalogado
    /// </summary>
    /// <param name="Id">ID único do teste no banco</param>
    /// <param name="Numero">Número do teste (ex: T1055)</param>
    /// <param name="Nome">Nome do teste (ex: Process Injection)</param>
    /// <param name="Descricao">Descrição detalhada do teste</param>
    /// <param name="DataExecucao">Data e hora de execução do teste</param>
    /// <param name="SessionId">ID da sessão de monitoramento associada</param>
    /// <param name="TotalEventos">Total de eventos capturados durante o teste</param>
    public record TesteAtomico(
        int Id,
        string Numero,
        string Nome,
        string Descricao,
        DateTime DataExecucao,
        int SessionId,
        int TotalEventos
    );

    /// <summary>
    /// Dados de entrada para catalogação de um novo teste
    /// </summary>
    /// <param name="Numero">Número do teste (ex: T1055)</param>
    /// <param name="Nome">Nome do teste</param>
    /// <param name="Descricao">Descrição do teste</param>
    public record NovoTesteAtomico(
        string Numero,
        string Nome,
        string Descricao
    );

    /// <summary>
    /// Resumo estatístico de um teste catalogado
    /// </summary>
    /// <param name="TesteId">ID do teste</param>
    /// <param name="Numero">Número do teste</param>
    /// <param name="Nome">Nome do teste</param>
    /// <param name="DataExecucao">Data de execução</param>
    /// <param name="DuracaoSegundos">Duração do teste em segundos</param>
    /// <param name="TotalEventos">Total de eventos capturados</param>
    /// <param name="EventosPorTipo">Dicionário com contagem por tipo de evento</param>
    public record ResumoTesteAtomico(
        int TesteId,
        string Numero,
        string Nome,
        DateTime DataExecucao,
        double DuracaoSegundos,
        int TotalEventos,
        Dictionary<int, int> EventosPorTipo
    );

    /// <summary>
    /// Informações completas de um teste atômico incluindo metadados de normalização
    /// </summary>
    /// <param name="Id">ID único do teste no banco</param>
    /// <param name="Numero">Número do teste (ex: T1055)</param>
    /// <param name="Nome">Nome do teste (ex: Process Injection)</param>
    /// <param name="Descricao">Descrição detalhada do teste</param>
    /// <param name="DataExecucao">Data e hora de execução do teste</param>
    /// <param name="SessionId">ID da sessão de monitoramento associada</param>
    /// <param name="TotalEventos">Total de eventos capturados durante o teste</param>
    /// <param name="Tarja">Nível de severidade/alerta (Verde, Amarelo, Laranja, Vermelho)</param>
    /// <param name="TarjaReason">Justificativa da tarja atribuída</param>
    /// <param name="NormalizationStatus">Status da normalização (Pending, Completed, Failed)</param>
    /// <param name="NormalizedAt">Data/hora da normalização</param>
    /// <param name="Notes">Observações/notas sobre o teste</param>
    public record TesteAtomicoCompleto(
        int Id,
        string Numero,
        string Nome,
        string Descricao,
        DateTime DataExecucao,
        int SessionId,
        int TotalEventos,
        string? Tarja,
        string? TarjaReason,
        string? NormalizationStatus,
        DateTime? NormalizedAt,
        string? Notes
    );

    /// <summary>
    /// Informações de uma sessão de monitoramento
    /// </summary>
    /// <param name="Id">ID único da sessão no banco</param>
    /// <param name="StartedAt">Data e hora de início da sessão</param>
    /// <param name="EndedAt">Data e hora de finalização da sessão (null se ainda ativa)</param>
    /// <param name="TargetProcess">Nome do processo alvo monitorado</param>
    /// <param name="RootPid">PID do processo raiz</param>
    /// <param name="Host">Nome da máquina</param>
    /// <param name="User">Usuário que executou o monitoramento</param>
    /// <param name="OsVersion">Versão do sistema operacional</param>
    /// <param name="TotalEventos">Total de eventos capturados na sessão</param>
    /// <param name="Notes">Observações sobre a sessão</param>
    public record SessaoMonitoramento(
        int Id,
        DateTime StartedAt,
        DateTime? EndedAt,
        string TargetProcess,
        int RootPid,
        string Host,
        string User,
        string OsVersion,
        int TotalEventos,
        string? Notes
    );

    /// <summary>
    /// Estatísticas básicas de uma sessão para exibição
    /// </summary>
    public record SessionStats
    {
        /// <summary>Distribuição de eventos por Event ID</summary>
        public required Dictionary<int, int> EventosPorTipo { get; init; }

        /// <summary>IPs de destino únicos (top 10)</summary>
        public required List<string> TopIps { get; init; }

        /// <summary>Domínios DNS únicos (top 10)</summary>
        public required List<string> TopDomains { get; init; }

        /// <summary>Processos criados (nomes únicos)</summary>
        public required List<string> ProcessosCriados { get; init; }

        /// <summary>Tarja do teste associado (se houver)</summary>
        public string? TarjaTesteAssociado { get; init; }
    }
}
