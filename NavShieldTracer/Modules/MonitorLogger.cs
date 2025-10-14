using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NavShieldTracer.Modules
{
    /// <summary>
    /// Gerencia a escrita de logs para uma sessão de monitoramento específica.
    /// Cria uma pasta de sessão única e organiza os eventos em subpastas.
    /// </summary>
    public class MonitorLogger
    {
        private readonly string _sessionDir;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        // Mapeia tipos de eventos para os nomes das pastas de log
        private static readonly Dictionary<Type, string> EventTypeToFolderName = new()
        {
            { typeof(EventoProcessoCriado), "ProcessosCriados" },
            { typeof(EventoTimestampArquivoAlterado), "TimestampsArquivosAlterados" },
            { typeof(EventoConexaoRede), "ConexoesRede" },
            { typeof(EventoProcessoEncerrado), "ProcessosEncerrados" },
            { typeof(EventoDriverCarregado), "DriversCarregados" },
            { typeof(EventoImagemCarregada), "ImagensCarregadas" },
            { typeof(EventoThreadRemotaCriada), "ThreadsRemotas" },
            { typeof(EventoAcessoRaw), "AcessosRaw" },
            { typeof(EventoAcessoProcesso), "AcessosProcessos" },
            { typeof(EventoArquivoCriado), "ArquivosCriados" },
            { typeof(EventoAcessoRegistro), "AcessosRegistro" },
            { typeof(EventoStreamArquivoCriado), "StreamsArquivos" },
            { typeof(EventoPipeCriado), "PipesCriados" },
            { typeof(EventoPipeConectado), "PipesConectados" },
            { typeof(EventoWmi), "EventosWmi" },
            { typeof(EventoConsultaDns), "ConsultasDns" },
            { typeof(EventoArquivoExcluido), "ArquivosExcluidos" },
            { typeof(EventoClipboard), "EventosClipboard" }
        };

        /// <summary>
        /// Inicializa uma nova instância da classe <see cref="MonitorLogger"/> para uma sessão de monitoramento.
        /// </summary>
        /// <param name="targetProcessName">O nome do processo alvo.</param>
        /// <param name="processId">O ID do processo raiz (pode ser o primeiro detectado).</param>
        public MonitorLogger(string targetProcessName, int processId)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var sessionFolderName = $"{timestamp}_{targetProcessName.Replace(".exe", "")}_{processId}";
            
            // Usar pasta Logs dentro da solução do projeto
            var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)));
            var logBaseDir = Path.Combine(solutionDir ?? Environment.CurrentDirectory, "Logs");
            _sessionDir = Path.Combine(logBaseDir, sessionFolderName);
            Directory.CreateDirectory(_sessionDir);
            
            // Cria arquivo de metadados da sessão
            var metadados = new
            {
                SessaoIniciada = DateTime.Now,
                ProcessoAlvo = targetProcessName,
                ProcessoIdRaiz = processId,
                VersaoNavShieldTracer = "1.0.0",
                ComputadorNome = Environment.MachineName,
                Usuario = Environment.UserName,
                SistemaOperacional = Environment.OSVersion.ToString()
            };
            
            var metadataPath = Path.Combine(_sessionDir, "metadata_sessao.json");
            var metadataJson = JsonSerializer.Serialize(metadados, JsonOptions);
            File.WriteAllText(metadataPath, metadataJson);
            
            Console.WriteLine($" Pasta de logs criada: {_sessionDir}");
            Console.WriteLine($" Metadados da sessão salvos em: metadata_sessao.json");
        }

        /// <summary>
        /// Registra um objeto de dados de evento no arquivo de log apropriado dentro da pasta da sessão.
        /// </summary>
        public void Log<T>(T data)
        {
            if (data == null) return;

            try
            {
                // Usar o tipo real do objeto, não o tipo genérico
                var dataType = data.GetType();
                if (!EventTypeToFolderName.TryGetValue(dataType, out var folderName))
                {
                    folderName = "OutrosEventos"; // Pasta padrão para eventos não mapeados
                    Console.WriteLine($"⚠ Tipo de evento não mapeado: {dataType.Name} (Event ID: {GetEventId(data)})");
                }

                var logDir = Path.Combine(_sessionDir, folderName);
                Directory.CreateDirectory(logDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fffffff");
                var fileName = $"{timestamp}.json";
                var filePath = Path.Combine(logDir, fileName);

                var jsonString = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(filePath, jsonString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Erro ao gravar log para o evento '{typeof(T).Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Salva um objeto de resumo no diretório da sessão.
        /// </summary>
        public void SalvarResumo(object summaryData)
        {
            try
            {
                var filePath = Path.Combine(_sessionDir, "resumo_monitoramento.json");
                var jsonString = JsonSerializer.Serialize(summaryData, JsonOptions);
                File.WriteAllText(filePath, jsonString);
                
                // Salva também estatísticas detalhadas dos tipos de eventos
                var estatisticas = ObterEstatisticasPorTipo();
                var estatisticasPath = Path.Combine(_sessionDir, "estatisticas_eventos.json");
                var estatisticasJson = JsonSerializer.Serialize(estatisticas, JsonOptions);
                File.WriteAllText(estatisticasPath, estatisticasJson);
                
                Console.WriteLine($"\n Monitoramento finalizado. Os logs foram salvos em:");
                Console.WriteLine($" {_sessionDir}");
                Console.WriteLine($" Estatísticas salvas em: estatisticas_eventos.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Falha ao salvar o resumo do log: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtém estatísticas detalhadas por tipo de evento.
        /// </summary>
        private object ObterEstatisticasPorTipo()
        {
            var stats = new Dictionary<string, int>();
            
            foreach (var kvp in EventTypeToFolderName)
            {
                var folderPath = Path.Combine(_sessionDir, kvp.Value);
                if (Directory.Exists(folderPath))
                {
                    var fileCount = Directory.GetFiles(folderPath, "*.json").Length;
                    stats[kvp.Value] = fileCount;
                }
                else
                {
                    stats[kvp.Value] = 0;
                }
            }
            
            return new
            {
                TotalEventos = stats.Values.Sum(),
                EventosPorTipo = stats,
                SessaoEncerrada = DateTime.Now,
                DuracaoSessao = DateTime.Now - Directory.GetCreationTime(_sessionDir)
            };
        }

        /// <summary>
        /// Obtém o caminho do diretório da sessão atual.
        /// </summary>
        public string SessionDirectory => _sessionDir;

        /// <summary>
        /// Extrai o Event ID de um objeto evento para debug.
        /// </summary>
        /// <param name="data">O objeto de evento</param>
        /// <returns>O Event ID ou "Unknown" se não encontrado</returns>
        private static string GetEventId(object data)
        {
            try
            {
                var eventIdProperty = data.GetType().GetProperty("EventId");
                return eventIdProperty?.GetValue(data)?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
