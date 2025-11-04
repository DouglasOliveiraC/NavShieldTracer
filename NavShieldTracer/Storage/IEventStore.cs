using System;
using System.Collections.Generic;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Modules.Heuristics.Engine;

namespace NavShieldTracer.Storage
{
    /// <summary>
    /// Informações da sessão de monitoramento
    /// </summary>
    /// <param name="StartedAt">Data e hora de início da sessão</param>
    /// <param name="TargetProcess">Processo alvo sendo monitorado</param>
    /// <param name="RootPid">PID raiz do processo</param>
    /// <param name="Host">Nome da máquina onde ocorre o monitoramento</param>
    /// <param name="User">Usuário que iniciou a sessão</param>
    /// <param name="OsVersion">Versão do sistema operacional</param>
    public record SessionInfo(
        DateTime StartedAt,
        string TargetProcess,
        int RootPid,
        string Host,
        string User,
        string OsVersion
    );

    /// <summary>
    /// Interface para armazenamento persistente de eventos do Sysmon e gerenciamento de testes atomicos catalogados.
    /// </summary>
    /// <remarks>
    /// Define o contrato para:
    /// - Gerenciamento de sessoes de monitoramento (Begin/Complete)
    /// - Persistencia de eventos do Sysmon
    /// - Catalogacao de testes atomicos do MITRE ATT&amp;CK
    /// - Exportacao e consulta de dados capturados
    /// Implementacoes devem ser thread-safe para insercao concorrente de eventos.
    /// </remarks>
    public interface IEventStore : IDisposable
    {
        /// <summary>
        /// Inicia uma nova sessão de monitoramento
        /// </summary>
        /// <param name="info">Informações da sessão</param>
        /// <returns>ID da sessão criada</returns>
        int BeginSession(SessionInfo info);
        
        /// <summary>
        /// Insere um evento na sessão especificada
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <param name="data">Dados do evento</param>
        void InsertEvent(int sessionId, object data);
        
        /// <summary>
        /// Finaliza uma sessão de monitoramento
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <param name="summary">Resumo opcional da sessão</param>
        void CompleteSession(int sessionId, object? summary = null);
        
        /// <summary>
        /// Caminho do banco de dados
        /// </summary>
        string DatabasePath { get; }

        // === MÉTODOS PARA TESTES ATÔMICOS ===

        /// <summary>
        /// Inicia catalogação de um novo teste atômico
        /// </summary>
        /// <param name="novoTeste">Dados do teste a catalogar</param>
        /// <param name="sessionId">ID da sessão associada</param>
        /// <returns>ID do teste criado</returns>
        int IniciarTesteAtomico(NovoTesteAtomico novoTeste, int sessionId);

        /// <summary>
        /// Finaliza catalogação de um teste atômico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <param name="totalEventos">Total de eventos capturados</param>
        void FinalizarTesteAtomico(int testeId, int totalEventos);

        /// <summary>
        /// Lista todos os testes atômicos catalogados
        /// </summary>
        /// <returns>Lista de testes catalogados</returns>
        List<TesteAtomico> ListarTestesAtomicos();

        /// <summary>
        /// Obtém resumo estatístico de um teste específico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <returns>Resumo do teste ou null se não encontrado</returns>
        ResumoTesteAtomico? ObterResumoTeste(int testeId);

        /// <summary>
        /// Exporta eventos de um teste específico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <returns>Lista de eventos do teste</returns>
        List<object> ExportarEventosTeste(int testeId);

        /// <summary>
        /// Atualiza informações de um teste catalogado
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <param name="numero">Novo número da técnica (opcional)</param>
        /// <param name="nome">Novo nome (opcional)</param>
        /// <param name="descricao">Nova descrição (opcional)</param>
        void AtualizarTesteAtomico(int testeId, string? numero = null, string? nome = null, string? descricao = null);

        /// <summary>
        /// Exclui um teste catalogado e seus eventos associados
        /// </summary>
        /// <param name="testeId">ID do teste a excluir</param>
        /// <returns>True se excluído com sucesso</returns>
        bool ExcluirTesteAtomico(int testeId);

        /// <summary>
        /// Obtem o ultimo snapshot heuristico registrado para uma sessao.
        /// </summary>
        /// <param name="sessionId">ID da sessao.</param>
        /// <returns>Snapshot heuristico mais recente ou null se inexistente.</returns>
        SessionSnapshot? ObterUltimoSnapshotDeSimilaridade(int sessionId);

        /// <summary>
        /// Lista todas as sessoes de monitoramento (ativas e encerradas)
        /// </summary>
        /// <returns>Lista de sessoes ordenadas por data de inicio (mais recentes primeiro)</returns>
        List<SessaoMonitoramento> ListarSessoes();

        /// <summary>
        /// Obt�m estat�sticas b�sicas de uma sessao para exibi�ao ao analista
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <returns>Estatísticas básicas incluindo distribuição de eventos, IPs, domínios e processos</returns>
        SessionStats ObterEstatisticasSessao(int sessionId);
    }
}
