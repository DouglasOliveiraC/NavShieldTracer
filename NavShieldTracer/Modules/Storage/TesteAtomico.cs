using System;

namespace NavShieldTracer.Modules.Storage
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
}