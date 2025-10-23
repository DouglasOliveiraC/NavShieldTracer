using System;
using System.Diagnostics.Eventing.Reader;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Linq;
using NavShieldTracer.Modules.Models;

namespace NavShieldTracer.Modules.Monitoring
{
    /// <summary>
    /// Monitora eventos do Sysmon no log operacional do Windows e os encaminha para um rastreador de atividade de processo.
    /// Esta classe é responsável por:
    /// - Conectar-se ao Event Log do Windows para eventos do Sysmon
    /// - Processar eventos em tempo real e históricos
    /// - Converter eventos XML do Sysmon para objetos C# tipados
    /// - Filtrar e encaminhar eventos relevantes para o ProcessActivityTracker
    /// - Gerenciar sequenciamento e metadata dos eventos capturados
    /// </summary>
    /// <remarks>
    /// Suporta todos os tipos de eventos do Sysmon:
    /// - Event ID 1: Criação de processo
    /// - Event ID 2: Alteração de timestamp de arquivo
    /// - Event ID 3: Conexão de rede
    /// - Event ID 5: Encerramento de processo
    /// - Event ID 6: Carregamento de driver
    /// - Event ID 7: Carregamento de imagem/DLL
    /// - Event ID 8: Criação de thread remota
    /// - Event ID 9: Acesso raw a disco
    /// - Event ID 10: Acesso a processo
    /// - Event ID 11: Criação de arquivo
    /// - Event IDs 12-14: Operações de registro
    /// - Event ID 15: Criaçao de stream de arquivo
    /// - Event ID 16: Mudanças de estado/configuraçao do Sysmon (ignorados intencionalmente)
    /// - Event IDs 17-18: Operaçoes de pipe
    /// - Event IDs 19-21: Eventos WMI
    /// - Event ID 22: Consulta DNS
    /// - Event ID 23: Exclusão de arquivo
    /// - Event IDs 24-26: Eventos de clipboard
    /// </remarks>
    public class SysmonEventMonitor
    {
        /// <summary>
        /// Nome padrão do canal de eventos operacional do Sysmon.
        /// </summary>
        public const string DefaultLogName = "Microsoft-Windows-Sysmon/Operational";

        /// <summary>Rastreador de atividade de processos que recebe os eventos parseados.</summary>
        private readonly ProcessActivityTracker _tracker;

        /// <summary>Nome do canal do Event Log a ser monitorado.</summary>
        private readonly string _logName;

        /// <summary>Funcao de logging configuravel para mensagens de diagnostico.</summary>
        private readonly Action<string> _logger = _ => { };

        /// <summary>Observador de eventos em tempo real do Windows Event Log.</summary>
        private EventLogWatcher? _watcher;

        /// <summary>Token de cancelamento para interromper o processamento de eventos historicos.</summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>Contador sequencial atomico para ordenacao de eventos capturados.</summary>
        private long _sequenceNumber = 0;

        /// <summary>
        /// Cria uma nova instância da classe <see cref="SysmonEventMonitor"/>.
        /// </summary>
        /// <param name="tracker">Instância de <see cref="ProcessActivityTracker"/> que receberá os eventos processados.</param>
        /// <param name="logName">Nome do canal do Sysmon a ser monitorado. Quando nulo, usa <see cref="DefaultLogName"/>.</param>
        /// <param name="logger">Função opcional de logging para mensagens de diagnóstico. Quando nula, usa implementação padrão que não faz nada.</param>
        public SysmonEventMonitor(ProcessActivityTracker tracker, string? logName = null, Action<string>? logger = null)
        {
            _tracker = tracker;
            _logName = string.IsNullOrWhiteSpace(logName)
                ? DefaultLogName
                : logName;
            _logger = logger ?? _logger;
        }

        /// <summary>
        /// Inicia o monitoramento de eventos do Sysmon.
        /// </summary>
        /// <remarks>
        /// Este método:
        /// 1. Inicializa o ProcessActivityTracker para detectar processos existentes
        /// 2. Configura um EventLogWatcher para monitorar eventos em tempo real
        /// 3. Inicia uma task assíncrona para processar eventos históricos recentes
        /// 4. Registra um handler para processar cada evento recebido
        /// </remarks>
        /// <exception cref="System.ComponentModel.Win32Exception">
        /// Lançada quando não há permissões suficientes para acessar o Event Log
        /// </exception>
        /// <exception cref="System.ArgumentException">
        /// Lançada quando o log do Sysmon não está disponível no sistema
        /// </exception>
        public void Start()
        {
            // Diagnosticar configuração do Sysmon antes de iniciar
            DiagnosticarConfiguracaoSysmon();
            
            _tracker.Initialize(); // Verifica processos existentes no início
            _cancellationTokenSource = new CancellationTokenSource();
            var query = new EventLogQuery(_logName, PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Sysmon']]]");
            
            _watcher = new EventLogWatcher(query);
            _watcher.EventRecordWritten += (sender, args) =>
            {
                if (args.EventRecord != null)
                {
                    ProcessEvent(args.EventRecord);
                }
            };

            _watcher.Enabled = true;
            Task.Run(() => PreloadExistingEvents(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// Diagnostica a configuração atual do Sysmon e exibe avisos sobre possíveis limitações na captura de eventos.
        /// </summary>
        /// <remarks>
        /// Este método analisa os tipos de eventos recentes para identificar:
        /// - Quais Event IDs estão sendo gerados pelo Sysmon
        /// - Possíveis limitações na configuração que podem afetar a análise
        /// - Sugestões para melhorar a cobertura de eventos
        /// </remarks>
        private void DiagnosticarConfiguracaoSysmon()
        {
            try
            {
                _logger("🔍 Analisando configuração do Sysmon...");
                
                // Analisa os últimos 100 eventos para ver que tipos estão sendo gerados
                var query = new EventLogQuery(_logName, PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Sysmon']]]");
                query.ReverseDirection = true;
                
                var eventCounts = new Dictionary<int, int>();
                var totalEvents = 0;
                
                using (var reader = new EventLogReader(query))
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var record = reader.ReadEvent();
                        if (record == null) break;
                        
                        eventCounts.TryGetValue(record.Id, out var count);
                        eventCounts[record.Id] = count + 1;
                        totalEvents++;
                    }
                }
                
                if (totalEvents == 0)
                {
                    _logger("⚠️ Nenhum evento Sysmon encontrado nos logs recentes.");
                    return;
                }
                
                _logger($"📊 Análise dos últimos {totalEvents} eventos Sysmon:");
                foreach (var kvp in eventCounts.OrderByDescending(x => x.Value))
                {
                    var eventName = GetEventName(kvp.Key);
                    _logger($"   Event ID {kvp.Key} ({eventName}): {kvp.Value} eventos");
                }
                
                // Verificar eventos importantes que podem estar faltando
                var importantEvents = new Dictionary<int, string>
                {
                    { 3, "NetworkConnect" },
                    { 11, "FileCreate" },
                    { 12, "RegistryEvent (Object create/delete)" },
                    { 13, "RegistryEvent (Value Set)" },
                    { 22, "DNSEvent" },
                    { 23, "FileDelete" }
                };
                
                var missingEvents = importantEvents.Where(e => !eventCounts.ContainsKey(e.Key)).ToList();
                if (missingEvents.Any())
                {
                    _logger("\n⚠️ Eventos importantes não detectados recentemente:");
                    foreach (var missing in missingEvents)
                    {
                        _logger($"   Event ID {missing.Key} ({missing.Value})");
                    }
                    _logger("\n💡 Para capturar mais eventos, considere configurar o Sysmon com:");
                    _logger("   - Configuração mais permissiva para FileCreate (Event ID 11)");
                    _logger("   - Habilitação de eventos de Registry (Event IDs 12-14)");
                    _logger("   - Habilitação de eventos DNS (Event ID 22)");
                    _logger("   - Habilitação de eventos FileDelete (Event ID 23)");
                }
                else
                {
                    _logger("\n✅ Configuração do Sysmon parece abrangente para análise completa.");
                }
            }
            catch (Exception ex)
            {
                _logger($"⚠️ Não foi possível analisar configuração do Sysmon: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtém o nome descritivo de um Event ID do Sysmon.
        /// </summary>
        /// <param name="eventId">O ID do evento Sysmon</param>
        /// <returns>Nome descritivo do evento ou "Unknown" se não reconhecido</returns>
        private static string GetEventName(int eventId)
        {
            return eventId switch
            {
                1 => "ProcessCreate",
                2 => "FileCreateTime",
                3 => "NetworkConnect",
                5 => "ProcessTerminate",
                6 => "DriverLoad",
                7 => "ImageLoad",
                8 => "CreateRemoteThread",
                9 => "RawAccessRead",
                10 => "ProcessAccess",
                11 => "FileCreate",
                12 => "RegistryEvent (Object create/delete)",
                13 => "RegistryEvent (Value Set)",
                14 => "RegistryEvent (Key and Value Rename)",
                15 => "FileCreateStreamHash",
                17 => "PipeEvent (Pipe Created)",
                18 => "PipeEvent (Pipe Connected)",
                19 => "WmiEvent (WmiEventFilter activity detected)",
                20 => "WmiEvent (WmiEventConsumer activity detected)",
                21 => "WmiEvent (WmiEventConsumerToFilter activity detected)",
                22 => "DNSEvent",
                23 => "FileDelete",
                24 => "ClipboardChange",
                25 => "ProcessTampering",
                26 => "FileDeleteDetected",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Para o monitoramento de eventos do Sysmon de forma limpa.
        /// </summary>
        /// <remarks>
        /// Este método:
        /// 1. Desabilita o EventLogWatcher para parar o monitoramento em tempo real
        /// 2. Cancela a task de processamento de eventos históricos
        /// 3. Libera recursos de forma segura
        /// </remarks>
        public void Stop()
        {
            if (_watcher != null) _watcher.Enabled = false;
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Pré-carrega eventos Sysmon existentes para garantir que nenhum evento recente seja perdido.
        /// </summary>
        /// <param name="token">Um token de cancelamento para interromper a operação.</param>
        private void PreloadExistingEvents(CancellationToken token)
        {
            try
            {
                // Opcional: Processa eventos recentes para garantir que não perdemos o início do processo
                // se ele começar imediatamente após o NavShieldTracer.
                var query = new EventLogQuery(_logName, PathType.LogName, "*[System[Provider[@Name='Microsoft-Windows-Sysmon']]]");
                query.ReverseDirection = true; // Começa dos mais recentes

                using (var reader = new EventLogReader(query))
                {
                    for (int i = 0; i < 100; i++) // Limita a um número razoável de eventos passados
                    {
                        if (token.IsCancellationRequested) break;
                        var record = reader.ReadEvent();
                        if (record == null) break;
                        ProcessEvent(record);
                    }
                }
            }
            catch (Exception ex)
            {
                // Erros no preload são registrados mas não impedem o monitoramento
                _logger($"⚠️ Aviso: Erro ao pré-carregar eventos históricos: {ex.Message}");
            }
        }

        /// <summary>
        /// Processa um único EventRecord do Sysmon, convertendo-o em um modelo de evento e encaminhando-o para o rastreador.
        /// </summary>
        /// <param name="eventRecord">O EventRecord a ser processado.</param>
        private void ProcessEvent(EventRecord eventRecord)
        {
            try
            {
                object? parsedEvent = ParseEvent(eventRecord);
                if (parsedEvent != null)
                {
                    _tracker.ProcessSysmonEvent(parsedEvent);
                }
            }
            catch (Exception ex)
            {
                // Erros de parsing são registrados para debug mas não interrompem o monitoramento
                _logger($"⚠️ Aviso: Erro ao processar evento {eventRecord.Id} (Record ID: {eventRecord.RecordId}): {ex.Message}");
                
                // Em modo debug, mostra o XML do evento problemático
                #if DEBUG
                try
                {
                    _logger($"XML do evento: {eventRecord.ToXml()}");
                }
                catch
                {
                    _logger("Não foi possível obter XML do evento.");
                }
                #endif
            }
        }

        /// <summary>
        /// Preenche os campos base comuns a todos os eventos Sysmon.
        /// </summary>
        /// <param name="evento">O objeto evento a ser preenchido</param>
        /// <param name="eventRecord">O EventRecord original do Windows Event Log</param>
        /// <param name="eventData">O elemento XML EventData parseado do evento</param>
        /// <remarks>
        /// Este método extrai e define:
        /// - EventId: Tipo do evento Sysmon (1-26)
        /// - EventRecordId: ID único do record no Event Log
        /// - SequenceNumber: Número sequencial interno para ordenação
        /// - ComputerName: Nome da máquina onde o evento ocorreu
        /// - CaptureTime: Timestamp de quando o evento foi capturado pelo NavShieldTracer
        /// - UtcTime: Timestamp UTC original do evento Sysmon
        /// </remarks>
        private void PopulateBaseFields(EventoSysmonBase evento, EventRecord eventRecord, XElement eventData)
        {
            Func<string, string?> getData = (name) => eventData.Elements(XName.Get("Data", "http://schemas.microsoft.com/win/2004/08/events/event"))
                                                        .FirstOrDefault(e => e.Attribute("Name")?.Value == name)?.Value;

            evento.EventId = eventRecord.Id;
            evento.EventRecordId = eventRecord.RecordId ?? 0;
            evento.SequenceNumber = Interlocked.Increment(ref _sequenceNumber);
            evento.ComputerName = eventRecord.MachineName;
            evento.CaptureTime = DateTime.Now;
            
            if (DateTime.TryParse(getData("UtcTime"), out var utcTime))
            {
                evento.UtcTime = utcTime;
            }
        }

        /// <summary>
        /// Converte uma string para int de forma segura, retornando 0 se a conversão falhar.
        /// </summary>
        /// <param name="value">A string a ser convertida</param>
        /// <returns>O valor inteiro convertido ou 0 se a conversão falhar</returns>
        /// <remarks>
        /// Usado para campos de PID e outros valores numéricos dos eventos Sysmon.
        /// </remarks>
        private static int SafeParseInt(string? value)
        {
            return int.TryParse(value, out var result) ? result : 0;
        }

        /// <summary>
        /// Converte uma string para ushort de forma segura, retornando 0 se a conversão falhar.
        /// </summary>
        /// <param name="value">A string a ser convertida</param>
        /// <returns>O valor ushort convertido ou 0 se a conversão falhar</returns>
        /// <remarks>
        /// Usado principalmente para portas de rede nos eventos de conexão.
        /// </remarks>
        private static ushort SafeParseUShort(string? value)
        {
            return ushort.TryParse(value, out var result) ? result : (ushort)0;
        }

        /// <summary>
        /// Converte uma string para bool de forma segura, retornando false se a conversão falhar.
        /// </summary>
        /// <param name="value">A string a ser convertida</param>
        /// <returns>O valor booleano convertido ou false se a conversão falhar</returns>
        /// <remarks>
        /// Usado para campos como 'Initiated' em eventos de rede e 'IsExecutable' em eventos de arquivo.
        /// </remarks>
        private static bool SafeParseBool(string? value)
        {
            return bool.TryParse(value, out var result) && result;
        }

        /// <summary>
        /// Analisa um EventRecord do Sysmon e o converte em um objeto de modelo de evento específico.
        /// </summary>
        /// <param name="eventRecord">O EventRecord a ser analisado</param>
        /// <returns>Um objeto de modelo de evento tipado (ex: EventoProcessoCriado), ou null se o evento não for reconhecido</returns>
        /// <remarks>
        /// Este método:
        /// 1. Faz parse do XML do evento usando XDocument
        /// 2. Extrai o elemento EventData com os campos específicos
        /// 3. Mapeia o Event ID para o tipo de objeto apropriado
        /// 4. Preenche os campos específicos usando os dados XML
        /// 5. Chama PopulateBaseFields para campos comuns
        /// 6. Retorna o objeto tipado ou null para eventos não suportados
        /// 
        /// Suporta todos os Event IDs documentados do Sysmon (1-26).
        /// </remarks>
        /// <exception cref="System.Xml.XmlException">
        /// Lançada quando o XML do evento está malformado
        /// </exception>
        private object? ParseEvent(EventRecord eventRecord)
        {
            var xml = XDocument.Parse(eventRecord.ToXml());
            var eventData = xml.Root?.Element(XName.Get("EventData", "http://schemas.microsoft.com/win/2004/08/events/event"));
            if (eventData == null) return null;

            Func<string, string?> getData = (name) => eventData.Elements(XName.Get("Data", "http://schemas.microsoft.com/win/2004/08/events/event"))
                                                        .FirstOrDefault(e => e.Attribute("Name")?.Value == name)?.Value;

            switch (eventRecord.Id)
            {
                case 1: // ProcessCreate
                    var processCreate = new EventoProcessoCriado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        LinhaDeComando = getData("CommandLine"),
                        ParentProcessId = SafeParseInt(getData("ParentProcessId")),
                        Usuario = getData("User"),
                        ProcessGuid = getData("ProcessGuid"),
                        ParentProcessGuid = getData("ParentProcessGuid"),
                        ParentImage = getData("ParentImage"),
                        ParentCommandLine = getData("ParentCommandLine"),
                        CurrentDirectory = getData("CurrentDirectory"),
                        LogonGuid = getData("LogonGuid"),
                        TerminalSessionId = SafeParseInt(getData("TerminalSessionId")),
                        IntegrityLevel = getData("IntegrityLevel"),
                        Hashes = getData("Hashes")
                    };
                    PopulateBaseFields(processCreate, eventRecord, eventData);
                    return processCreate;

                case 2: // FileCreateTime
                    var fileCreateTime = new EventoTimestampArquivoAlterado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ArquivoAlvo = getData("TargetFilename")
                    };
                    if (DateTime.TryParse(getData("PreviousCreationUtcTime"), out var prevTime))
                        fileCreateTime.CreationUtcTimeAnterior = prevTime;
                    if (DateTime.TryParse(getData("CreationUtcTime"), out var newTime))
                        fileCreateTime.CreationUtcTimeNova = newTime;
                    PopulateBaseFields(fileCreateTime, eventRecord, eventData);
                    return fileCreateTime;

                case 3: // NetworkConnect
                    var networkConnect = new EventoConexaoRede
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        IpOrigem = getData("SourceIp"),
                        PortaOrigem = SafeParseUShort(getData("SourcePort")),
                        IpDestino = getData("DestinationIp"),
                        PortaDestino = SafeParseUShort(getData("DestinationPort")),
                        Protocolo = getData("Protocol"),
                        Usuario = getData("User"),
                        ProcessGuid = getData("ProcessGuid"),
                        HostnameDestino = getData("DestinationHostname"),
                        Iniciada = SafeParseBool(getData("Initiated"))
                    };
                    PopulateBaseFields(networkConnect, eventRecord, eventData);
                    return networkConnect;

                case 5: // ProcessTerminate
                    var processTerminate = new EventoProcessoEncerrado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ProcessGuid = getData("ProcessGuid")
                    };
                    PopulateBaseFields(processTerminate, eventRecord, eventData);
                    return processTerminate;

                case 6: // DriverLoad
                    var driverLoad = new EventoDriverCarregado
                    {
                        ImageLoaded = getData("ImageLoaded"),
                        Hashes = getData("Hashes"),
                        Signed = getData("Signed"),
                        Signature = getData("Signature")
                    };
                    PopulateBaseFields(driverLoad, eventRecord, eventData);
                    return driverLoad;

                case 7: // ImageLoaded
                    var imageLoaded = new EventoImagemCarregada
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ImagemCarregada = getData("ImageLoaded"),
                        Hashes = getData("Hashes"),
                        Assinada = getData("Signed"),
                        Assinatura = getData("Signature"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(imageLoaded, eventRecord, eventData);
                    return imageLoaded;

                case 8: // CreateRemoteThread
                    var createRemoteThread = new EventoThreadRemotaCriada
                    {
                        ProcessIdOrigem = SafeParseInt(getData("SourceProcessId")),
                        ImagemOrigem = getData("SourceImage"),
                        ProcessIdDestino = SafeParseInt(getData("TargetProcessId")),
                        ImagemDestino = getData("TargetImage"),
                        EnderecoInicioThread = getData("StartAddress"),
                        FuncaoInicio = getData("StartFunction"),
                        ProcessGuidOrigem = getData("SourceProcessGuid"),
                        ProcessGuidDestino = getData("TargetProcessGuid"),
                        UsuarioOrigem = getData("SourceUser")
                    };
                    PopulateBaseFields(createRemoteThread, eventRecord, eventData);
                    return createRemoteThread;

                case 9: // RawAccessRead
                    var rawAccessRead = new EventoAcessoRaw
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        Dispositivo = getData("Device"),
                        ProcessGuid = getData("ProcessGuid")
                    };
                    PopulateBaseFields(rawAccessRead, eventRecord, eventData);
                    return rawAccessRead;

                case 10: // ProcessAccess
                    var processAccess = new EventoAcessoProcesso
                    {
                        ProcessIdOrigem = SafeParseInt(getData("SourceProcessId")),
                        ImagemOrigem = getData("SourceImage"),
                        ProcessIdDestino = SafeParseInt(getData("TargetProcessId")),
                        ImagemDestino = getData("TargetImage"),
                        DireitosAcesso = getData("GrantedAccess"),
                        TipoChamada = getData("CallTrace"),
                        ProcessGuidOrigem = getData("SourceProcessGuid"),
                        ProcessGuidDestino = getData("TargetProcessGuid"),
                        UsuarioOrigem = getData("SourceUser")
                    };
                    PopulateBaseFields(processAccess, eventRecord, eventData);
                    return processAccess;

                case 11: // FileCreate
                    var fileCreate = new EventoArquivoCriado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ArquivoAlvo = getData("TargetFilename"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    if (DateTime.TryParse(getData("CreationUtcTime"), out var creationTime))
                        fileCreate.DataCriacao = creationTime;
                    PopulateBaseFields(fileCreate, eventRecord, eventData);
                    return fileCreate;

                case 12: case 13: case 14: // RegistryKey (Create/Delete, SetValue, Rename)
                    var registryAccess = new EventoAcessoRegistro
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        TipoEvento = getData("EventType"),
                        ObjetoAlvo = getData("TargetObject"),
                        Detalhes = getData("Details"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(registryAccess, eventRecord, eventData);
                    return registryAccess;

                case 15: // FileCreateStreamHash
                    var fileCreateStream = new EventoStreamArquivoCriado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ArquivoAlvo = getData("TargetFilename"),
                        NomeStream = getData("StreamName"),
                        ConteudoStream = getData("Contents"),
                        Hash = getData("Hash"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(fileCreateStream, eventRecord, eventData);
                    return fileCreateStream;

                case 17: // PipeCreated
                    var pipeCreated = new EventoPipeCriado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        NomePipe = getData("PipeName"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(pipeCreated, eventRecord, eventData);
                    return pipeCreated;

                case 18: // PipeConnected
                    var pipeConnected = new EventoPipeConectado
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        NomePipe = getData("PipeName"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(pipeConnected, eventRecord, eventData);
                    return pipeConnected;

                case 19: case 20: case 21: // WMI events
                    var wmiEvent = new EventoWmi
                    {
                        Operacao = getData("Operation"),
                        Usuario = getData("User"),
                        Nome = getData("Name"),
                        Query = getData("Query")
                    };
                    PopulateBaseFields(wmiEvent, eventRecord, eventData);
                    return wmiEvent;

                case 22: // DnsQuery
                    var dnsQuery = new EventoConsultaDns
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        NomeConsultado = getData("QueryName"),
                        TipoConsulta = getData("QueryType"),
                        Resultado = getData("QueryResults"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(dnsQuery, eventRecord, eventData);
                    return dnsQuery;

                case 23: // FileDelete
                    var fileDelete = new EventoArquivoExcluido
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        ArquivoAlvo = getData("TargetFilename"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User"),
                        Hashes = getData("Hashes"),
                        Arquivado = SafeParseBool(getData("IsExecutable"))
                    };
                    PopulateBaseFields(fileDelete, eventRecord, eventData);
                    return fileDelete;

                case 24: case 25: case 26: // ClipboardChange
                    var clipboardEvent = new EventoClipboard
                    {
                        ProcessId = SafeParseInt(getData("ProcessId")),
                        Imagem = getData("Image"),
                        TipoOperacao = getData("Operation"),
                        ConteudoClipboard = getData("Contents"),
                        ProcessGuid = getData("ProcessGuid"),
                        Usuario = getData("User")
                    };
                    PopulateBaseFields(clipboardEvent, eventRecord, eventData);
                    return clipboardEvent;

                default:
                    return null;
            }
        }
    }
}






