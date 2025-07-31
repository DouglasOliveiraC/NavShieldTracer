using System;

namespace NavShieldTracer.Modules
{
    /// <summary>
    /// Classe base para todos os eventos do Sysmon, contendo informações comuns.
    /// </summary>
    public abstract class EventoSysmonBase
    {
        /// <summary>
        /// Timestamp UTC quando o evento foi gerado pelo Sysmon.
        /// </summary>
        public DateTime UtcTime { get; set; }
        
        /// <summary>
        /// Timestamp quando o evento foi capturado pelo NavShieldTracer.
        /// </summary>
        public DateTime CaptureTime { get; set; } = DateTime.Now;
        
        /// <summary>
        /// Número sequencial do evento na sessão de monitoramento.
        /// </summary>
        public long SequenceNumber { get; set; }
        
        /// <summary>
        /// O ID do Event Record no log do Windows.
        /// </summary>
        public long EventRecordId { get; set; }
        
        /// <summary>
        /// O ID do evento Sysmon.
        /// </summary>
        public int EventId { get; set; }
        
        /// <summary>
        /// O nome da máquina onde o evento ocorreu.
        /// </summary>
        public string? ComputerName { get; set; }
    }

    /// <summary>
    /// Contém os modelos de dados para os eventos do Sysmon que são monitorados pelo NavShieldTracer.
    /// </summary>
    public class EventoProcessoCriado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que foi criado.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo criado.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// A linha de comando completa usada para iniciar o processo.
        /// </summary>
        public string? LinhaDeComando { get; set; }
        /// <summary>
        /// O ID do processo pai que criou este processo.
        /// </summary>
        public int ParentProcessId { get; set; }
        /// <summary>
        /// O nome do usuário que executou o processo.
        /// </summary>
        public string? Usuario { get; set; }
        /// <summary>
        /// O GUID do processo (único para cada instância).
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O GUID do processo pai.
        /// </summary>
        public string? ParentProcessGuid { get; set; }
        /// <summary>
        /// O nome da imagem do processo pai.
        /// </summary>
        public string? ParentImage { get; set; }
        /// <summary>
        /// A linha de comando do processo pai.
        /// </summary>
        public string? ParentCommandLine { get; set; }
        /// <summary>
        /// O diretório de trabalho atual do processo.
        /// </summary>
        public string? CurrentDirectory { get; set; }
        /// <summary>
        /// O GUID do logon associado ao processo.
        /// </summary>
        public string? LogonGuid { get; set; }
        /// <summary>
        /// O ID da sessão terminal.
        /// </summary>
        public int TerminalSessionId { get; set; }
        /// <summary>
        /// O nível de integridade do processo.
        /// </summary>
        public string? IntegrityLevel { get; set; }
        /// <summary>
        /// Os hashes do arquivo executável.
        /// </summary>
        public string? Hashes { get; set; }
    }

    /// <summary>
    /// Representa um evento de alteração de timestamp de arquivo do Sysmon (Event ID 2).
    /// </summary>
    public class EventoTimestampArquivoAlterado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que alterou o timestamp.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O arquivo cujo timestamp foi alterado.
        /// </summary>
        public string? ArquivoAlvo { get; set; }
        /// <summary>
        /// A data de criação anterior do arquivo.
        /// </summary>
        public DateTime? CreationUtcTimeAnterior { get; set; }
        /// <summary>
        /// A nova data de criação do arquivo.
        /// </summary>
        public DateTime? CreationUtcTimeNova { get; set; }
    }

    /// <summary>
    /// Representa um evento de conexão de rede do Sysmon (Event ID 3).
    /// </summary>
    public class EventoConexaoRede : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que iniciou a conexão de rede.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo que iniciou a conexão.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O endereço IP de origem da conexão.
        /// </summary>
        public string? IpOrigem { get; set; }
        /// <summary>
        /// A porta de origem da conexão.
        /// </summary>
        public ushort PortaOrigem { get; set; }
        /// <summary>
        /// O endereço IP de destino da conexão.
        /// </summary>
        public string? IpDestino { get; set; }
        /// <summary>
        /// A porta de destino da conexão.
        /// </summary>
        public ushort PortaDestino { get; set; }
        /// <summary>
        /// O protocolo de rede utilizado (ex: TCP, UDP).
        /// </summary>
        public string? Protocolo { get; set; }
        /// <summary>
        /// O nome do usuário que iniciou a conexão.
        /// </summary>
        public string? Usuario { get; set; }
        /// <summary>
        /// Indica se a conexão foi iniciada pelo processo (true) ou recebida (false).
        /// </summary>
        public bool Iniciada { get; set; }
        /// <summary>
        /// O hostname de destino, se disponível.
        /// </summary>
        public string? HostnameDestino { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
    }

    /// <summary>
    /// Representa um evento de encerramento de processo do Sysmon (Event ID 5).
    /// </summary>
    public class EventoProcessoEncerrado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que foi encerrado.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo encerrado.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O GUID do processo encerrado.
        /// </summary>
        public string? ProcessGuid { get; set; }
    }

    /// <summary>
    /// Representa um evento de carregamento de driver do Sysmon (Event ID 6).
    /// </summary>
    public class EventoDriverCarregado : EventoSysmonBase
    {
        /// <summary>
        /// O arquivo do driver que foi carregado.
        /// </summary>
        public string? ImageLoaded { get; set; }
        /// <summary>
        /// Os hashes do driver carregado.
        /// </summary>
        public string? Hashes { get; set; }
        /// <summary>
        /// Indica se o driver é assinado.
        /// </summary>
        public string? Signed { get; set; }
        /// <summary>
        /// Informações sobre a assinatura do driver.
        /// </summary>
        public string? Signature { get; set; }
    }

    /// <summary>
    /// Representa um evento de carregamento de DLL/Imagem do Sysmon (Event ID 7).
    /// </summary>
    public class EventoImagemCarregada : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que carregou a imagem.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo que carregou a DLL.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O caminho completo da DLL/imagem que foi carregada.
        /// </summary>
        public string? ImagemCarregada { get; set; }
        /// <summary>
        /// Os hashes da imagem carregada.
        /// </summary>
        public string? Hashes { get; set; }
        /// <summary>
        /// Indica se a imagem é assinada.
        /// </summary>
        public string? Assinada { get; set; }
        /// <summary>
        /// Informações sobre a assinatura da imagem.
        /// </summary>
        public string? Assinatura { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que carregou a imagem.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de criação de thread remota do Sysmon (Event ID 8).
    /// </summary>
    public class EventoThreadRemotaCriada : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo de origem que criou a thread remota.
        /// </summary>
        public int ProcessIdOrigem { get; set; }
        /// <summary>
        /// O caminho da imagem do processo de origem.
        /// </summary>
        public string? ImagemOrigem { get; set; }
        /// <summary>
        /// O ID do processo de destino onde a thread foi criada.
        /// </summary>
        public int ProcessIdDestino { get; set; }
        /// <summary>
        /// O caminho da imagem do processo de destino.
        /// </summary>
        public string? ImagemDestino { get; set; }
        /// <summary>
        /// O endereço de início da thread criada.
        /// </summary>
        public string? EnderecoInicioThread { get; set; }
        /// <summary>
        /// A função de início da thread.
        /// </summary>
        public string? FuncaoInicio { get; set; }
        /// <summary>
        /// O GUID do processo de origem.
        /// </summary>
        public string? ProcessGuidOrigem { get; set; }
        /// <summary>
        /// O GUID do processo de destino.
        /// </summary>
        public string? ProcessGuidDestino { get; set; }
        /// <summary>
        /// O usuário do processo de origem.
        /// </summary>
        public string? UsuarioOrigem { get; set; }
    }

    /// <summary>
    /// Representa um evento de leitura de acesso raw do Sysmon (Event ID 9).
    /// </summary>
    public class EventoAcessoRaw : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que fez o acesso raw.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O dispositivo que foi acessado.
        /// </summary>
        public string? Dispositivo { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
    }

    /// <summary>
    /// Representa um evento de acesso a processo do Sysmon (Event ID 10).
    /// </summary>
    public class EventoAcessoProcesso : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo de origem que acessou o processo.
        /// </summary>
        public int ProcessIdOrigem { get; set; }
        /// <summary>
        /// O caminho da imagem do processo de origem.
        /// </summary>
        public string? ImagemOrigem { get; set; }
        /// <summary>
        /// O ID do processo de destino que foi acessado.
        /// </summary>
        public int ProcessIdDestino { get; set; }
        /// <summary>
        /// O caminho da imagem do processo de destino.
        /// </summary>
        public string? ImagemDestino { get; set; }
        /// <summary>
        /// Os direitos de acesso solicitados.
        /// </summary>
        public string? DireitosAcesso { get; set; }
        /// <summary>
        /// O tipo de chamada que gerou o acesso.
        /// </summary>
        public string? TipoChamada { get; set; }
        /// <summary>
        /// O GUID do processo de origem.
        /// </summary>
        public string? ProcessGuidOrigem { get; set; }
        /// <summary>
        /// O GUID do processo de destino.
        /// </summary>
        public string? ProcessGuidDestino { get; set; }
        /// <summary>
        /// O usuário do processo de origem.
        /// </summary>
        public string? UsuarioOrigem { get; set; }
    }

    /// <summary>
    /// Representa um evento de criação de arquivo do Sysmon (Event ID 11).
    /// </summary>
    public class EventoArquivoCriado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que criou o arquivo.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo que criou o arquivo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O caminho completo do arquivo que foi criado.
        /// </summary>
        public string? ArquivoAlvo { get; set; }
        /// <summary>
        /// A data e hora de criação do arquivo.
        /// </summary>
        public DateTime? DataCriacao { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que criou o arquivo.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de acesso ao registro do Sysmon (Event IDs 12, 13, 14).
    /// </summary>
    public class EventoAcessoRegistro : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que acessou o registro.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem (executável) do processo que acessou o registro.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O tipo de operação de registro (ex: CreateKey, SetValue, DeleteKey, DeleteValue).
        /// </summary>
        public string? TipoEvento { get; set; }
        /// <summary>
        /// O caminho completo da chave ou valor do registro alvo.
        /// </summary>
        public string? ObjetoAlvo { get; set; }
        /// <summary>
        /// Os detalhes da operação (valor, tipo de dados, etc.).
        /// </summary>
        public string? Detalhes { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que fez o acesso.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de criação de stream de arquivo do Sysmon (Event ID 15).
    /// </summary>
    public class EventoStreamArquivoCriado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que criou o stream.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O arquivo alvo onde o stream foi criado.
        /// </summary>
        public string? ArquivoAlvo { get; set; }
        /// <summary>
        /// O nome do stream criado.
        /// </summary>
        public string? NomeStream { get; set; }
        /// <summary>
        /// O conteúdo do stream.
        /// </summary>
        public string? ConteudoStream { get; set; }
        /// <summary>
        /// O hash do conteúdo do stream.
        /// </summary>
        public string? Hash { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que criou o stream.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de criação de pipe do Sysmon (Event ID 17).
    /// </summary>
    public class EventoPipeCriado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que criou o pipe.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O nome do pipe criado.
        /// </summary>
        public string? NomePipe { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que criou o pipe.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de conexão a pipe do Sysmon (Event ID 18).
    /// </summary>
    public class EventoPipeConectado : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que se conectou ao pipe.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O nome do pipe conectado.
        /// </summary>
        public string? NomePipe { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que se conectou ao pipe.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento WMI do Sysmon (Event IDs 19, 20, 21).
    /// </summary>
    public class EventoWmi : EventoSysmonBase
    {
        /// <summary>
        /// O tipo de operação WMI.
        /// </summary>
        public string? Operacao { get; set; }
        /// <summary>
        /// O usuário que executou a operação WMI.
        /// </summary>
        public string? Usuario { get; set; }
        /// <summary>
        /// O nome da operação WMI.
        /// </summary>
        public string? Nome { get; set; }
        /// <summary>
        /// A consulta WMI executada.
        /// </summary>
        public string? Query { get; set; }
    }

    /// <summary>
    /// Representa um evento de consulta DNS do Sysmon (Event ID 22).
    /// </summary>
    public class EventoConsultaDns : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que fez a consulta DNS.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho completo da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O nome de domínio consultado.
        /// </summary>
        public string? NomeConsultado { get; set; }
        /// <summary>
        /// O tipo de consulta DNS (ex: A, AAAA, CNAME).
        /// </summary>
        public string? TipoConsulta { get; set; }
        /// <summary>
        /// O resultado da consulta DNS.
        /// </summary>
        public string? Resultado { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que fez a consulta.
        /// </summary>
        public string? Usuario { get; set; }
    }

    /// <summary>
    /// Representa um evento de exclusão de arquivo do Sysmon (Event ID 23).
    /// </summary>
    public class EventoArquivoExcluido : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que excluiu o arquivo.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O arquivo que foi excluído.
        /// </summary>
        public string? ArquivoAlvo { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que excluiu o arquivo.
        /// </summary>
        public string? Usuario { get; set; }
        /// <summary>
        /// Os hashes do arquivo excluído.
        /// </summary>
        public string? Hashes { get; set; }
        /// <summary>
        /// Indica se o arquivo foi arquivado.
        /// </summary>
        public bool Arquivado { get; set; }
    }

    /// <summary>
    /// Representa eventos de clipboard do Sysmon (Event IDs 24, 25, 26).
    /// </summary>
    public class EventoClipboard : EventoSysmonBase
    {
        /// <summary>
        /// O ID do processo que acessou o clipboard.
        /// </summary>
        public int ProcessId { get; set; }
        /// <summary>
        /// O caminho da imagem do processo.
        /// </summary>
        public string? Imagem { get; set; }
        /// <summary>
        /// O tipo de operação no clipboard.
        /// </summary>
        public string? TipoOperacao { get; set; }
        /// <summary>
        /// O conteúdo do clipboard (se disponível).
        /// </summary>
        public string? ConteudoClipboard { get; set; }
        /// <summary>
        /// O GUID do processo.
        /// </summary>
        public string? ProcessGuid { get; set; }
        /// <summary>
        /// O usuário que acessou o clipboard.
        /// </summary>
        public string? Usuario { get; set; }
    }
}