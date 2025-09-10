using System;

namespace NavShieldTracer.Modules.Storage
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
    /// Interface para armazenamento de eventos de monitoramento
    /// </summary>
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
    }
}

