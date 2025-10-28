using System;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using NavShieldTracer.Modules.Models;
using NavShieldTracer.Modules.Heuristics.Normalization;
using NavShieldTracer.Modules.Heuristics.Engine;
using Heuristics = NavShieldTracer.Modules.Heuristics;

namespace NavShieldTracer.Storage
{
    /// <summary>
    /// Implementacao de armazenamento de eventos do Sysmon usando banco de dados SQLite.
    /// Gerencia persistencia estruturada de eventos, sessoes de monitoramento, testes atomicos catalogados,
    /// assinaturas normalizadas e snapshots de similaridade para analise comportamental.
    /// </summary>
    public class SqliteEventStore : IEventStore
    {
        /// <summary>Conexao ativa com o banco de dados SQLite.</summary>
        private readonly SqliteConnection _conn;

        /// <summary>Indica se o objeto foi descartado pelo Dispose.</summary>
        private bool _disposed;

        /// <summary>Opcoes de serializacao JSON para campos estruturados (camelCase, compacto).</summary>
        private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        /// <summary>
        /// Caminho do arquivo de banco de dados SQLite
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Inicializa uma nova instancia do armazenamento SQLite com schema completo.
        /// </summary>
        /// <param name="databasePath">Caminho opcional para o banco de dados. Se nulo, cria em Logs/navshieldtracer.sqlite</param>
        /// <remarks>
        /// Este construtor:
        /// - Cria o diretorio Logs se nao existir
        /// - Abre conexao SQLite com modo ReadWriteCreate e cache compartilhado
        /// - Aplica pragmas de otimizacao (WAL journal, foreign keys, cache de 200MB)
        /// - Cria schema completo incluindo tabelas de eventos, sessoes, testes atomicos, assinaturas normalizadas e alertas
        /// </remarks>
        public SqliteEventStore(string? databasePath = null)
        {
            // Base de logs na pasta da solução, similar ao MonitorLogger
            var solutionDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location)));
            var logBaseDir = Path.Combine(solutionDir ?? Environment.CurrentDirectory, "Logs");
            Directory.CreateDirectory(logBaseDir);

            DatabasePath = databasePath ?? Path.Combine(logBaseDir, "navshieldtracer.sqlite");

            _conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString());

            _conn.Open();
            ApplyPragmas();
            EnsureSchema();
        }

        /// <summary>
        /// Aplica pragmas de otimizacao ao banco de dados SQLite.
        /// </summary>
        /// <remarks>
        /// Configura:
        /// - WAL journal mode para melhor concorrencia
        /// - Synchronous NORMAL para balancear seguranca e performance
        /// - Foreign keys habilitadas para integridade referencial
        /// - Cache de 200MB para queries rapidas
        /// - Timeout de 5s para operacoes com banco bloqueado
        /// </remarks>
        private void ApplyPragmas()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                PRAGMA temp_store=MEMORY;
                PRAGMA cache_size=-200000;
                PRAGMA busy_timeout=5000;
            ";
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Cria o schema completo do banco de dados se nao existir.
        /// </summary>
        /// <remarks>
        /// Cria as seguintes tabelas:
        /// - sessions: Sessoes de monitoramento
        /// - events: Eventos do Sysmon com schema normalizado
        /// - atomic_tests: Testes atomicos catalogados do MITRE ATT&amp;CK
        /// - normalized_test_signatures: Assinaturas comportamentais normalizadas
        /// - normalized_core_events: Eventos criticos de cada assinatura
        /// - normalized_whitelist_entries: Entradas de whitelist para filtragem de ruido
        /// - normalization_log: Log de processamento de normalizacao
        /// - session_similarity_snapshots: Snapshots de analise de similaridade em tempo real
        /// - alert_history: Historico de mudancas de nivel de ameaca
        /// Tambem cria indices otimizados para queries de motor heuristico.
        /// </remarks>
        private void EnsureSchema()
        {
            using var tx = _conn.BeginTransaction();
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                target_process TEXT NOT NULL,
                root_pid INTEGER,
                host TEXT,
                user TEXT,
                os_version TEXT,
                notes TEXT
            );

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                event_id INTEGER,
                event_record_id INTEGER,
                computer_name TEXT,
                utc_time TEXT,
                capture_time TEXT,
                sequence_number INTEGER,

                process_guid TEXT,
                parent_process_guid TEXT,
                process_id INTEGER,
                parent_process_id INTEGER,
                image TEXT,
                command_line TEXT,
                parent_image TEXT,
                parent_command_line TEXT,
                current_directory TEXT,
                logon_guid TEXT,
                integrity_level TEXT,
                hashes TEXT,

                src_ip TEXT,
                src_port INTEGER,
                dst_ip TEXT,
                dst_port INTEGER,
                protocol TEXT,

                dns_query TEXT,
                dns_type TEXT,
                dns_result TEXT,

                target_filename TEXT,
                image_loaded TEXT,
                signed TEXT,
                signature TEXT,
                signature_status TEXT,

                pipe_name TEXT,

                wmi_operation TEXT,
                wmi_name TEXT,
                wmi_query TEXT,

                clipboard_operation TEXT,
                clipboard_contents TEXT,

                user TEXT,
                raw_json TEXT,

                UNIQUE(computer_name, event_record_id),
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);
            CREATE INDEX IF NOT EXISTS idx_events_time ON events(utc_time);
            CREATE INDEX IF NOT EXISTS idx_events_eventid ON events(event_id);
            CREATE INDEX IF NOT EXISTS idx_events_pid ON events(process_id);
            CREATE INDEX IF NOT EXISTS idx_events_ppid ON events(parent_process_id);
            CREATE INDEX IF NOT EXISTS idx_events_image ON events(image);
            CREATE INDEX IF NOT EXISTS idx_events_dst ON events(dst_ip, dst_port);
            CREATE INDEX IF NOT EXISTS idx_events_dns ON events(dns_query);
            CREATE INDEX IF NOT EXISTS idx_events_target ON events(target_filename);

            -- OTIMIZAÇÕES PARA MOTOR HEURÍSTICO
            -- Índice composto para janela temporal (query crítica do motor)
            CREATE INDEX IF NOT EXISTS idx_events_session_time
                ON events(session_id, utc_time, capture_time);

            -- Índice para filtros de rede (Event ID 3)
            CREATE INDEX IF NOT EXISTS idx_events_network
                ON events(session_id, event_id, dst_ip)
                WHERE event_id = 3;

            -- Índice para sequência temporal
            CREATE INDEX IF NOT EXISTS idx_events_sequence
                ON events(session_id, sequence_number, id);

            CREATE TABLE IF NOT EXISTS atomic_tests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                numero TEXT NOT NULL,
                nome TEXT NOT NULL,
                descricao TEXT,
                data_execucao TEXT NOT NULL,
                session_id INTEGER NOT NULL,
                total_eventos INTEGER DEFAULT 0,
                finalizado BOOLEAN DEFAULT 0,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_atomic_tests_numero ON atomic_tests(numero);
            CREATE INDEX IF NOT EXISTS idx_atomic_tests_data ON atomic_tests(data_execucao);
            CREATE INDEX IF NOT EXISTS idx_atomic_tests_session ON atomic_tests(session_id);

            CREATE TABLE IF NOT EXISTS normalized_test_signatures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                test_id INTEGER NOT NULL UNIQUE,
                signature_hash TEXT NOT NULL,
                status TEXT NOT NULL,
                severity_label TEXT NOT NULL,
                severity_reason TEXT,
                feature_vector_json TEXT NOT NULL,
                total_events INTEGER NOT NULL,
                core_event_count INTEGER NOT NULL,
                support_event_count INTEGER NOT NULL,
                noise_event_count INTEGER NOT NULL,
                duration_seconds REAL,
                quality_score REAL,
                notes TEXT,
                quality_warnings_json TEXT,
                processed_at TEXT NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT,
                FOREIGN KEY(test_id) REFERENCES atomic_tests(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_normalized_signatures_test ON normalized_test_signatures(test_id);

            CREATE TABLE IF NOT EXISTS normalized_core_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                signature_id INTEGER NOT NULL,
                event_row_id INTEGER,
                event_id INTEGER NOT NULL,
                event_time TEXT,
                image TEXT,
                command_line TEXT,
                metadata_json TEXT,
                FOREIGN KEY(signature_id) REFERENCES normalized_test_signatures(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_normalized_core_signature ON normalized_core_events(signature_id);

            CREATE TABLE IF NOT EXISTS normalized_whitelist_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                signature_id INTEGER NOT NULL,
                entry_type TEXT NOT NULL,
                value TEXT NOT NULL,
                reason TEXT,
                approved INTEGER DEFAULT 0,
                auto_generated INTEGER DEFAULT 1,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(signature_id) REFERENCES normalized_test_signatures(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_normalized_whitelist_signature ON normalized_whitelist_entries(signature_id);

            CREATE TABLE IF NOT EXISTS normalization_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                test_id INTEGER NOT NULL,
                stage TEXT NOT NULL,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(test_id) REFERENCES atomic_tests(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_normalization_log_test ON normalization_log(test_id);

            CREATE TABLE IF NOT EXISTS session_similarity_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                snapshot_at TEXT NOT NULL,
                matches TEXT NOT NULL,
                highest_match_test_id INTEGER,
                highest_similarity REAL,
                session_threat_level TEXT NOT NULL,
                event_count_at_snapshot INTEGER NOT NULL,
                active_processes_count INTEGER,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_snapshots_session ON session_similarity_snapshots(session_id);
            CREATE INDEX IF NOT EXISTS idx_snapshots_time ON session_similarity_snapshots(snapshot_at);
            CREATE INDEX IF NOT EXISTS idx_snapshots_level ON session_similarity_snapshots(session_threat_level);

            CREATE TABLE IF NOT EXISTS alert_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                previous_level TEXT,
                new_level TEXT NOT NULL,
                reason TEXT NOT NULL,
                trigger_technique_id TEXT,
                trigger_similarity REAL,
                snapshot_id INTEGER,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE,
                FOREIGN KEY(snapshot_id) REFERENCES session_similarity_snapshots(id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_alert_history_session ON alert_history(session_id);
            CREATE INDEX IF NOT EXISTS idx_alert_history_time ON alert_history(timestamp);
            ";
            cmd.ExecuteNonQuery();
            EnsureAtomicNormalizationColumns(tx);
            tx.Commit();
        }

        /// <summary>
        /// Garante que as colunas de normalizacao existam na tabela atomic_tests.
        /// </summary>
        /// <param name="tx">Transacao ativa do SQLite</param>
        /// <remarks>
        /// Adiciona colunas normalized_at, tarja, tarja_reason e normalization_status se nao existirem.
        /// Usado para suportar workflow de normalizacao de testes catalogados.
        /// </remarks>
        private void EnsureAtomicNormalizationColumns(SqliteTransaction tx)
        {
            EnsureColumnExists(tx, "atomic_tests", "normalized_at", "TEXT");
            EnsureColumnExists(tx, "atomic_tests", "tarja", "TEXT");
            EnsureColumnExists(tx, "atomic_tests", "tarja_reason", "TEXT");
            EnsureColumnExists(tx, "atomic_tests", "normalization_status", "TEXT DEFAULT 'Pending'");
        }

        /// <summary>
        /// Verifica se uma coluna existe em uma tabela e a cria se necessario.
        /// </summary>
        /// <param name="tx">Transacao ativa do SQLite</param>
        /// <param name="table">Nome da tabela</param>
        /// <param name="column">Nome da coluna a verificar/criar</param>
        /// <param name="definition">Definicao SQL da coluna (tipo e constraints)</param>
        /// <remarks>
        /// Usa pragma_table_info para verificar existencia sem exceções.
        /// Executa ALTER TABLE ADD COLUMN se a coluna nao existir.
        /// Seguro para migracao incremental de schema.
        /// </remarks>
        private void EnsureColumnExists(SqliteTransaction tx, string table, string column, string definition)
        {
            using var checkCmd = _conn.CreateCommand();
            checkCmd.Transaction = tx;
            checkCmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = '{column}' LIMIT 1;";
            var exists = checkCmd.ExecuteScalar();
            if (exists == null || exists == DBNull.Value)
            {
                using var alterCmd = _conn.CreateCommand();
                alterCmd.Transaction = tx;
                alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                alterCmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Inicia uma nova sessao de monitoramento no banco de dados.
        /// </summary>
        /// <param name="info">Informacoes da sessao incluindo processo alvo, host, usuario e PID raiz</param>
        /// <returns>ID da sessao criada (chave primaria auto-incrementada)</returns>
        /// <remarks>
        /// Insere um registro na tabela sessions com timestamp atual.
        /// Este ID sera usado como chave estrangeira para todos os eventos e testes atomicos relacionados.
        /// </remarks>
        public int BeginSession(SessionInfo info)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO sessions (started_at, target_process, root_pid, host, user, os_version)
                VALUES ($started_at, $target_process, $root_pid, $host, $user, $os_version);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$started_at", info.StartedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$target_process", info.TargetProcess);
            cmd.Parameters.AddWithValue("$root_pid", info.RootPid);
            cmd.Parameters.AddWithValue("$host", info.Host);
            cmd.Parameters.AddWithValue("$user", info.User);
            cmd.Parameters.AddWithValue("$os_version", info.OsVersion);

            var id = Convert.ToInt32(cmd.ExecuteScalar());
            return id;
        }

        /// <summary>
        /// Recupera um teste atômico previamente catalogado.
        /// </summary>
        /// <param name="testeId">Identificador do teste.</param>
        /// <returns>Instância do teste ou null se não encontrado.</returns>
        public TesteAtomico? ObterTesteAtomico(int testeId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, numero, nome, descricao, data_execucao, session_id, total_eventos
                FROM atomic_tests
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", testeId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            var dataExecucaoRaw = reader.IsDBNull(4) ? null : reader.GetString(4);
            DateTime dataExecucao = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(dataExecucaoRaw))
            {
                if (!DateTime.TryParse(dataExecucaoRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dataExecucao))
                {
                    DateTime.TryParse(dataExecucaoRaw, out dataExecucao);
                }
            }

            return new TesteAtomico(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                dataExecucao,
                reader.GetInt32(5),
                reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
            );
        }

        /// <summary>
        /// Finaliza uma sessao de monitoramento registrando timestamp de encerramento.
        /// </summary>
        /// <param name="sessionId">ID da sessao a finalizar</param>
        /// <param name="summary">Objeto opcional com estatisticas da sessao (serializado em JSON no campo notes)</param>
        /// <remarks>
        /// Atualiza o campo ended_at com timestamp atual.
        /// Se summary for fornecido, serializa como JSON e anexa ao campo notes.
        /// Chamado tipicamente ao pressionar ENTER para finalizar catalogacao ou ao encerrar monitoramento.
        /// </remarks>
        public void CompleteSession(int sessionId, object? summary = null)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE sessions
                SET ended_at = $ended_at,
                    notes = COALESCE(notes,'') || $notes
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$ended_at", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$id", sessionId);
            var notes = summary != null ? JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = false }) : string.Empty;
            if (!string.IsNullOrEmpty(notes)) notes = (notes + "\n");
            cmd.Parameters.AddWithValue("$notes", notes);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Retorna os eventos associados a uma sessão, prontos para análise heurística.
        /// </summary>
        /// <param name="sessionId">Identificador da sessão no banco.</param>
        /// <returns>Coleção ordenada dos eventos capturados.</returns>
        internal IReadOnlyList<CatalogEventSnapshot> ObterEventosDaSessao(int sessionId)
        {
            var eventos = new List<CatalogEventSnapshot>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    id,
                    event_id,
                    utc_time,
                    capture_time,
                    sequence_number,
                    image,
                    command_line,
                    parent_image,
                    parent_command_line,
                    process_id,
                    parent_process_id,
                    process_guid,
                    parent_process_guid,
                    user,
                    integrity_level,
                    hashes,
                    target_filename,
                    image_loaded,
                    signed,
                    signature,
                    signature_status,
                    pipe_name,
                    wmi_operation,
                    wmi_name,
                    wmi_query,
                    dns_query,
                    dns_result,
                    dns_type,
                    src_ip,
                    src_port,
                    dst_ip,
                    dst_port,
                    protocol,
                    raw_json
                FROM events
                WHERE session_id = $session_id
                ORDER BY
                    CASE WHEN utc_time IS NOT NULL THEN utc_time ELSE capture_time END,
                    id;
            ";
            cmd.Parameters.AddWithValue("$session_id", sessionId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                eventos.Add(ReadCatalogEventSnapshot(reader));
            }

            return eventos;
        }

        private static CatalogEventSnapshot ReadCatalogEventSnapshot(SqliteDataReader reader)
        {
            var utcString = reader.IsDBNull(2) ? null : reader.GetString(2);
            var captureString = reader.IsDBNull(3) ? null : reader.GetString(3);

            return new CatalogEventSnapshot(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                ParseDateTimeOrNull(utcString),
                ParseDateTimeOrNull(captureString),
                reader.IsDBNull(4) ? (long?)null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9),
                reader.IsDBNull(10) ? (int?)null : reader.GetInt32(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetString(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetString(24),
                reader.IsDBNull(25) ? null : reader.GetString(25),
                reader.IsDBNull(26) ? null : reader.GetString(26),
                reader.IsDBNull(27) ? null : reader.GetString(27),
                reader.IsDBNull(28) ? null : reader.GetString(28),
                reader.IsDBNull(29) ? (int?)null : reader.GetInt32(29),
                reader.IsDBNull(30) ? null : reader.GetString(30),
                reader.IsDBNull(31) ? (int?)null : reader.GetInt32(31),
                reader.IsDBNull(32) ? null : reader.GetString(32),
                reader.IsDBNull(33) ? null : reader.GetString(33)
            );
        }

        private static DateTime? ParseDateTimeOrNull(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt;
            }

            return DateTime.TryParse(value, out dt) ? dt : null;
        }

        private static IDictionary<string, object?> BuildCoreEventMetadata(CatalogEventSnapshot core)
        {
            return new Dictionary<string, object?>
            {
                ["parentImage"] = core.ParentImage,
                ["parentCommandLine"] = core.ParentCommandLine,
                ["parentProcessId"] = core.ParentProcessId,
                ["parentProcessGuid"] = core.ParentProcessGuid,
                ["processGuid"] = core.ProcessGuid,
                ["user"] = core.User,
                ["integrityLevel"] = core.IntegrityLevel,
                ["hashes"] = core.Hashes,
                ["targetFilename"] = core.TargetFilename,
                ["imageLoaded"] = core.ImageLoaded,
                ["signed"] = core.Signed,
                ["signature"] = core.Signature,
                ["signatureStatus"] = core.SignatureStatus,
                ["pipeName"] = core.PipeName,
                ["wmiOperation"] = core.WmiOperation,
                ["wmiName"] = core.WmiName,
                ["wmiQuery"] = core.WmiQuery,
                ["dnsQuery"] = core.DnsQuery,
                ["dnsResult"] = core.DnsResult,
                ["dnsType"] = core.DnsType,
                ["srcIp"] = core.SrcIp,
                ["srcPort"] = core.SrcPort,
                ["dstIp"] = core.DstIp,
                ["dstPort"] = core.DstPort,
                ["protocol"] = core.Protocol,
                ["sequenceNumber"] = core.SequenceNumber,
                ["rawJson"] = core.RawJson
            };
        }

        /// <summary>
        /// Insere um evento do Sysmon no banco de dados associado a uma sessao.
        /// </summary>
        /// <param name="sessionId">ID da sessao de monitoramento</param>
        /// <param name="data">Objeto de evento do Sysmon (deve herdar de EventoSysmonBase)</param>
        /// <remarks>
        /// Este metodo:
        /// - Verifica se o evento herda de EventoSysmonBase
        /// - Extrai campos especificos de acordo com o tipo de evento (process create, network, file, etc)
        /// - Normaliza e serializa dados em schema SQL unificado
        /// - Usa INSERT OR IGNORE para evitar duplicatas (constraint unique em computer_name + event_record_id)
        /// - Serializa evento completo em raw_json para troubleshooting
        /// Suporta todos os 26 tipos de eventos do Sysmon.
        /// </remarks>
        public void InsertEvent(int sessionId, object data)
        {
            if (data is not EventoSysmonBase ebase) return;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO events (
                    session_id, event_id, event_record_id, computer_name, utc_time, capture_time, sequence_number,
                    process_guid, parent_process_guid, process_id, parent_process_id, image, command_line, parent_image, parent_command_line, current_directory, logon_guid, integrity_level, hashes,
                    src_ip, src_port, dst_ip, dst_port, protocol,
                    dns_query, dns_type, dns_result,
                    target_filename, image_loaded, signed, signature, signature_status,
                    pipe_name,
                    wmi_operation, wmi_name, wmi_query,
                    clipboard_operation, clipboard_contents,
                    user, raw_json
                ) VALUES (
                    $session_id, $event_id, $event_record_id, $computer_name, $utc_time, $capture_time, $sequence_number,
                    $process_guid, $parent_process_guid, $process_id, $parent_process_id, $image, $command_line, $parent_image, $parent_command_line, $current_directory, $logon_guid, $integrity_level, $hashes,
                    $src_ip, $src_port, $dst_ip, $dst_port, $protocol,
                    $dns_query, $dns_type, $dns_result,
                    $target_filename, $image_loaded, $signed, $signature, $signature_status,
                    $pipe_name,
                    $wmi_operation, $wmi_name, $wmi_query,
                    $clipboard_operation, $clipboard_contents,
                    $user, $raw_json
                );
            ";

            // Defaults
            string? image = null, commandLine = null, parentImage = null, parentCmd = null, currentDirectory = null,
                logonGuid = null, integrityLevel = null, hashes = null,
                srcIp = null, dstIp = null, protocol = null,
                dnsQuery = null, dnsType = null, dnsResult = null,
                targetFilename = null, imageLoaded = null, signed = null, signature = null, signatureStatus = null,
                pipeName = null, wmiOperation = null, wmiName = null, wmiQuery = null,
                clipboardOperation = null, clipboardContents = null, user = null,
                processGuid = null, parentProcessGuid = null, computerName = ebase.ComputerName;
            int? processId = null, parentProcessId = null; ushort? srcPort = null, dstPort = null;

            switch (data)
            {
                case EventoProcessoCriado pc:
                    processId = pc.ProcessId; image = pc.Imagem; commandLine = pc.LinhaDeComando; parentProcessId = pc.ParentProcessId;
                    user = pc.Usuario; processGuid = pc.ProcessGuid; parentProcessGuid = pc.ParentProcessGuid; parentImage = pc.ParentImage; parentCmd = pc.ParentCommandLine;
                    currentDirectory = pc.CurrentDirectory; logonGuid = pc.LogonGuid; integrityLevel = pc.IntegrityLevel; hashes = pc.Hashes;
                    break;
                case EventoProcessoEncerrado pe:
                    processId = pe.ProcessId; image = pe.Imagem; processGuid = pe.ProcessGuid;
                    break;
                case EventoConexaoRede net:
                    processId = net.ProcessId; image = net.Imagem; srcIp = net.IpOrigem; srcPort = net.PortaOrigem; dstIp = net.IpDestino; dstPort = net.PortaDestino; protocol = net.Protocolo; user = net.Usuario; processGuid = net.ProcessGuid;
                    break;
                case EventoImagemCarregada il:
                    processId = il.ProcessId; image = il.Imagem; imageLoaded = il.ImagemCarregada; hashes = il.Hashes; signed = il.Assinada; signature = il.Assinatura; processGuid = il.ProcessGuid; user = il.Usuario;
                    break;
                case EventoThreadRemotaCriada rt:
                    processId = rt.ProcessIdOrigem; image = rt.ImagemOrigem; parentProcessId = rt.ProcessIdDestino; parentImage = rt.ImagemDestino; // usando campos existentes
                    break;
                case EventoAcessoProcesso pa:
                    processId = pa.ProcessIdOrigem; image = pa.ImagemOrigem; parentProcessId = pa.ProcessIdDestino; parentImage = pa.ImagemDestino; // alvo como "pai"
                    break;
                case EventoArquivoCriado fc:
                    processId = fc.ProcessId; image = fc.Imagem; targetFilename = fc.ArquivoAlvo; user = fc.Usuario; processGuid = fc.ProcessGuid;
                    break;
                case EventoAcessoRegistro reg:
                    processId = reg.ProcessId; image = reg.Imagem; targetFilename = reg.ObjetoAlvo; dnsResult = reg.Detalhes; user = reg.Usuario; processGuid = reg.ProcessGuid; // Detalhes guardado em dns_result temporariamente
                    break;
                case EventoStreamArquivoCriado fs:
                    processId = fs.ProcessId; image = fs.Imagem; targetFilename = fs.ArquivoAlvo; user = fs.Usuario; hashes = fs.Hash; processGuid = fs.ProcessGuid;
                    break;
                case EventoPipeCriado pc2:
                    processId = pc2.ProcessId; image = pc2.Imagem; pipeName = pc2.NomePipe; user = pc2.Usuario; processGuid = pc2.ProcessGuid;
                    break;
                case EventoPipeConectado pcon:
                    processId = pcon.ProcessId; image = pcon.Imagem; pipeName = pcon.NomePipe; user = pcon.Usuario; processGuid = pcon.ProcessGuid;
                    break;
                case EventoWmi wmi:
                    wmiOperation = wmi.Operacao; wmiName = wmi.Nome; wmiQuery = wmi.Query; user = wmi.Usuario;
                    break;
                case EventoConsultaDns dns:
                    processId = dns.ProcessId; image = dns.Imagem; dnsQuery = dns.NomeConsultado; dnsType = dns.TipoConsulta; dnsResult = dns.Resultado; user = dns.Usuario; processGuid = dns.ProcessGuid;
                    break;
                case EventoArquivoExcluido fd:
                    processId = fd.ProcessId; image = fd.Imagem; targetFilename = fd.ArquivoAlvo; user = fd.Usuario; hashes = fd.Hashes; processGuid = fd.ProcessGuid;
                    break;
                case EventoTimestampArquivoAlterado ts:
                    processId = ts.ProcessId; image = ts.Imagem; targetFilename = ts.ArquivoAlvo;
                    break;
                case EventoAcessoRaw raw:
                    processId = raw.ProcessId; image = raw.Imagem; targetFilename = raw.Dispositivo; processGuid = raw.ProcessGuid;
                    break;
                case EventoClipboard cb:
                    processId = cb.ProcessId; image = cb.Imagem; clipboardOperation = cb.TipoOperacao; clipboardContents = cb.ConteudoClipboard; user = cb.Usuario; processGuid = cb.ProcessGuid;
                    break;
            }

            cmd.Parameters.AddWithValue("$session_id", sessionId);
            cmd.Parameters.AddWithValue("$event_id", ebase.EventId);
            cmd.Parameters.AddWithValue("$event_record_id", ebase.EventRecordId);
            cmd.Parameters.AddWithValue("$computer_name", computerName ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$utc_time", ebase.UtcTime == default ? (object)DBNull.Value : ebase.UtcTime.ToString("o"));
            cmd.Parameters.AddWithValue("$capture_time", ebase.CaptureTime == default ? DateTime.Now.ToString("o") : ebase.CaptureTime.ToString("o"));
            cmd.Parameters.AddWithValue("$sequence_number", ebase.SequenceNumber);

            cmd.Parameters.AddWithValue("$process_guid", (object?)processGuid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parent_process_guid", (object?)parentProcessGuid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$process_id", (object?)processId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parent_process_id", (object?)parentProcessId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$image", (object?)image ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$command_line", (object?)commandLine ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parent_image", (object?)parentImage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parent_command_line", (object?)parentCmd ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$current_directory", (object?)currentDirectory ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$logon_guid", (object?)logonGuid ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$integrity_level", (object?)integrityLevel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hashes", (object?)hashes ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$src_ip", (object?)srcIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$src_port", (object?)srcPort ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dst_ip", (object?)dstIp ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dst_port", (object?)dstPort ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$protocol", (object?)protocol ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$dns_query", (object?)dnsQuery ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dns_type", (object?)dnsType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dns_result", (object?)dnsResult ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$target_filename", (object?)targetFilename ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$image_loaded", (object?)imageLoaded ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$signed", (object?)signed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$signature", (object?)signature ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$signature_status", (object?)signatureStatus ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$pipe_name", (object?)pipeName ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$wmi_operation", (object?)wmiOperation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$wmi_name", (object?)wmiName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$wmi_query", (object?)wmiQuery ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$clipboard_operation", (object?)clipboardOperation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$clipboard_contents", (object?)clipboardContents ?? DBNull.Value);

            cmd.Parameters.AddWithValue("$user", (object?)user ?? DBNull.Value);

            // Para facilitar troubleshooting, opcionalmente salva JSON serializado do objeto
            string? rawJson = null;
            try { rawJson = JsonSerializer.Serialize(data); } catch { }
            cmd.Parameters.AddWithValue("$raw_json", (object?)rawJson ?? DBNull.Value);

            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 5)
            {
                // database is locked: tenta novamente rapidamente
                System.Threading.Thread.Sleep(5);
                cmd.ExecuteNonQuery();
            }
        }

        // === IMPLEMENTAÇÃO DOS MÉTODOS PARA TESTES ATÔMICOS ===

        /// <summary>
        /// Inicia catalogação de um novo teste atômico
        /// </summary>
        /// <param name="novoTeste">Dados do teste a catalogar</param>
        /// <param name="sessionId">ID da sessão associada</param>
        /// <returns>ID do teste criado</returns>
        public int IniciarTesteAtomico(NovoTesteAtomico novoTeste, int sessionId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO atomic_tests (numero, nome, descricao, data_execucao, session_id)
                VALUES ($numero, $nome, $descricao, $data_execucao, $session_id);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("$numero", novoTeste.Numero);
            cmd.Parameters.AddWithValue("$nome", novoTeste.Nome);
            cmd.Parameters.AddWithValue("$descricao", novoTeste.Descricao);
            cmd.Parameters.AddWithValue("$data_execucao", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$session_id", sessionId);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Finaliza catalogação de um teste atômico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <param name="totalEventos">Total de eventos capturados</param>
        public void FinalizarTesteAtomico(int testeId, int totalEventos)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE atomic_tests
                SET total_eventos = $total_eventos, finalizado = 1
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$total_eventos", totalEventos);
            cmd.Parameters.AddWithValue("$id", testeId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Lista todos os testes atômicos catalogados
        /// </summary>
        /// <returns>Lista de testes catalogados</returns>
        public List<TesteAtomico> ListarTestesAtomicos()
        {
            var testes = new List<TesteAtomico>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, numero, nome, descricao, data_execucao, session_id, total_eventos
                FROM atomic_tests
                WHERE finalizado = 1
                ORDER BY data_execucao DESC;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                testes.Add(new TesteAtomico(
                    Id: reader.GetInt32("id"),
                    Numero: reader.GetString("numero"),
                    Nome: reader.GetString("nome"),
                    Descricao: reader.IsDBNull("descricao") ? "" : reader.GetString("descricao"),
                    DataExecucao: DateTime.Parse(reader.GetString("data_execucao")),
                    SessionId: reader.GetInt32("session_id"),
                    TotalEventos: reader.GetInt32("total_eventos")
                ));
            }

            return testes;
        }

        /// <summary>
        /// Obtém resumo estatístico de um teste específico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <returns>Resumo do teste ou null se não encontrado</returns>
        public ResumoTesteAtomico? ObterResumoTeste(int testeId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.numero, t.nome, t.data_execucao, t.session_id, t.total_eventos,
                       s.started_at, s.ended_at
                FROM atomic_tests t
                LEFT JOIN sessions s ON t.session_id = s.id
                WHERE t.id = $id AND t.finalizado = 1;
            ";
            cmd.Parameters.AddWithValue("$id", testeId);

            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var dataExecucao = DateTime.Parse(reader.GetString("data_execucao"));
            var iniciado = reader.IsDBNull("started_at") ? dataExecucao : DateTime.Parse(reader.GetString("started_at"));
            var finalizado = reader.IsDBNull("ended_at") ? dataExecucao : DateTime.Parse(reader.GetString("ended_at"));
            var duracao = (finalizado - iniciado).TotalSeconds;

            var eventosPorTipo = new Dictionary<int, int>();

            // Segunda consulta para contar eventos por tipo
            using var cmd2 = _conn.CreateCommand();
            cmd2.CommandText = @"
                SELECT event_id, COUNT(*) as count
                FROM events
                WHERE session_id = $session_id
                GROUP BY event_id;
            ";
            cmd2.Parameters.AddWithValue("$session_id", reader.GetInt32("session_id"));

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var eventId = reader2.IsDBNull("event_id") ? 0 : reader2.GetInt32("event_id");
                var count = reader2.GetInt32("count");
                eventosPorTipo[eventId] = count;
            }

            return new ResumoTesteAtomico(
                TesteId: reader.GetInt32("id"),
                Numero: reader.GetString("numero"),
                Nome: reader.GetString("nome"),
                DataExecucao: dataExecucao,
                DuracaoSegundos: duracao,
                TotalEventos: reader.GetInt32("total_eventos"),
                EventosPorTipo: eventosPorTipo
            );
        }

        /// <summary>
        /// Exporta eventos de um teste específico
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <returns>Lista de eventos do teste</returns>
        public List<object> ExportarEventosTeste(int testeId)
        {
            var eventos = new List<object>();

            // Primeiro busca o session_id do teste
            using var cmd1 = _conn.CreateCommand();
            cmd1.CommandText = "SELECT session_id FROM atomic_tests WHERE id = $id;";
            cmd1.Parameters.AddWithValue("$id", testeId);
            var sessionId = cmd1.ExecuteScalar();

            if (sessionId == null) return eventos;

            // Busca todos os eventos da sessão
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT event_id, utc_time, capture_time, process_id, image, command_line,
                       src_ip, dst_ip, dst_port, target_filename, raw_json
                FROM events
                WHERE session_id = $session_id
                ORDER BY utc_time;
            ";
            cmd.Parameters.AddWithValue("$session_id", sessionId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var evento = new
                {
                    EventId = reader.IsDBNull("event_id") ? 0 : reader.GetInt32("event_id"),
                    UtcTime = reader.IsDBNull("utc_time") ? "" : reader.GetString("utc_time"),
                    CaptureTime = reader.IsDBNull("capture_time") ? "" : reader.GetString("capture_time"),
                    ProcessId = reader.IsDBNull("process_id") ? (int?)null : reader.GetInt32("process_id"),
                    Image = reader.IsDBNull("image") ? "" : reader.GetString("image"),
                    CommandLine = reader.IsDBNull("command_line") ? "" : reader.GetString("command_line"),
                    SrcIp = reader.IsDBNull("src_ip") ? "" : reader.GetString("src_ip"),
                    DstIp = reader.IsDBNull("dst_ip") ? "" : reader.GetString("dst_ip"),
                    DstPort = reader.IsDBNull("dst_port") ? (int?)null : reader.GetInt32("dst_port"),
                    TargetFilename = reader.IsDBNull("target_filename") ? "" : reader.GetString("target_filename"),
                    RawJson = reader.IsDBNull("raw_json") ? "" : reader.GetString("raw_json")
                };
                eventos.Add(evento);
            }

            return eventos;
        }

        /// <summary>
        /// Conta o número total de eventos em uma sessão específica
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <returns>Número total de eventos</returns>
        public int ContarEventosSessao(int sessionId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM events WHERE session_id = $session_id;";
            cmd.Parameters.AddWithValue("$session_id", sessionId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Obtém a contagem de eventos críticos agrupados por Event ID para uma sessão específica.
        /// Usado para classificação automática de nível de ameaça segundo padrão do Ministério da Defesa.
        /// </summary>
        /// <param name="sessionId">ID da sessão a ser analisada.</param>
        /// <returns>Dicionário com Event ID como chave e contagem de ocorrências como valor.</returns>
        /// <remarks>
        /// Eventos críticos monitorados:
        /// - Event ID 1: ProcessCreate (reconhecimento)
        /// - Event ID 2: FileCreateTime (timestomping)
        /// - Event ID 3: NetworkConnect (C2, exfiltração)
        /// - Event ID 8: CreateRemoteThread (injeção de código)
        /// - Event ID 10: ProcessAccess (credential dumping, movimentação lateral)
        /// - Event ID 13: RegistryValueSet (persistência)
        /// - Event ID 17: PipeCreated (comunicação inter-processo maliciosa)
        /// - Event ID 22: DNSQuery (reconhecimento)
        /// - Event ID 23: FileDelete (ransomware, sabotagem)
        /// - Event ID 25: ProcessTampering (adulteração de processos)
        /// </remarks>
        public Dictionary<int, int> GetCriticalEventCounts(int sessionId)
        {
            var result = new Dictionary<int, int>();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT event_id, COUNT(*) as count
                FROM events
                WHERE session_id = $session_id
                  AND event_id IN (1, 2, 3, 8, 10, 13, 17, 22, 23, 25)
                GROUP BY event_id
                ORDER BY event_id;
            ";
            cmd.Parameters.AddWithValue("$session_id", sessionId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                int eventId = reader.GetInt32(0);
                int count = reader.GetInt32(1);
                result[eventId] = count;
            }

            return result;
        }

        /// <summary>
        /// Atualiza informações de um teste catalogado
        /// </summary>
        /// <param name="testeId">ID do teste</param>
        /// <param name="numero">Novo número da técnica (opcional)</param>
        /// <param name="nome">Novo nome (opcional)</param>
        /// <param name="descricao">Nova descrição (opcional)</param>
        public void AtualizarTesteAtomico(int testeId, string? numero = null, string? nome = null, string? descricao = null)
        {
            var updates = new List<string>();
            using var cmd = _conn.CreateCommand();

            if (!string.IsNullOrWhiteSpace(numero))
            {
                updates.Add("numero = $numero");
                cmd.Parameters.AddWithValue("$numero", numero);
            }

            if (!string.IsNullOrWhiteSpace(nome))
            {
                updates.Add("nome = $nome");
                cmd.Parameters.AddWithValue("$nome", nome);
            }

            if (descricao != null) // Permite limpar descrição com string vazia
            {
                updates.Add("descricao = $descricao");
                cmd.Parameters.AddWithValue("$descricao", descricao);
            }

            if (updates.Count == 0)
            {
                return; // Nada para atualizar
            }

            cmd.CommandText = $@"
                UPDATE atomic_tests
                SET {string.Join(", ", updates)}
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", testeId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Persiste o resultado completo da normalização heurística no banco.
        /// </summary>
        /// <param name="resultado">Resultado produzido pelo motor de normalização.</param>
        internal void SalvarResultadoNormalizacao(CatalogNormalizationResult resultado)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                using (var cmdDelSignature = _conn.CreateCommand())
                {
                    cmdDelSignature.Transaction = tx;
                    cmdDelSignature.CommandText = "DELETE FROM normalized_test_signatures WHERE test_id = $test_id;";
                    cmdDelSignature.Parameters.AddWithValue("$test_id", resultado.Signature.TestId);
                    cmdDelSignature.ExecuteNonQuery();
                }

                using (var cmdDelLog = _conn.CreateCommand())
                {
                    cmdDelLog.Transaction = tx;
                    cmdDelLog.CommandText = "DELETE FROM normalization_log WHERE test_id = $test_id;";
                    cmdDelLog.Parameters.AddWithValue("$test_id", resultado.Signature.TestId);
                    cmdDelLog.ExecuteNonQuery();
                }

                var featureVector = resultado.Signature.FeatureVector;
                var featurePayload = new
                {
                    eventTypeHistogram = featureVector.EventTypeHistogram,
                    processTreeDepth = featureVector.ProcessTreeDepth,
                    networkConnectionsCount = featureVector.NetworkConnectionsCount,
                    registryOperationsCount = featureVector.RegistryOperationsCount,
                    fileOperationsCount = featureVector.FileOperationsCount,
                    temporalSpanSeconds = featureVector.TemporalSpanSeconds,
                    criticalEventsCount = featureVector.CriticalEventsCount
                };

                var featureJson = JsonSerializer.Serialize(featurePayload, _serializerOptions);
                var warningsJson = JsonSerializer.Serialize(resultado.Quality.Warnings ?? Array.Empty<string>(), _serializerOptions);
                var nowIso = DateTime.Now.ToString("o");

                int signatureId;
                using (var cmdInsertSignature = _conn.CreateCommand())
                {
                    cmdInsertSignature.Transaction = tx;
                    cmdInsertSignature.CommandText = @"
                        INSERT INTO normalized_test_signatures (
                            test_id,
                            signature_hash,
                            status,
                            severity_label,
                            severity_reason,
                            feature_vector_json,
                            total_events,
                            core_event_count,
                            support_event_count,
                            noise_event_count,
                            duration_seconds,
                            quality_score,
                            notes,
                            quality_warnings_json,
                            processed_at,
                            updated_at
                        )
                        VALUES (
                            $test_id,
                            $signature_hash,
                            $status,
                            $severity_label,
                            $severity_reason,
                            $feature_vector_json,
                            $total_events,
                            $core_event_count,
                            $support_event_count,
                            $noise_event_count,
                            $duration_seconds,
                            $quality_score,
                            $notes,
                            $quality_warnings_json,
                            $processed_at,
                            $updated_at
                        );
                        SELECT last_insert_rowid();
                    ";

                    cmdInsertSignature.Parameters.AddWithValue("$test_id", resultado.Signature.TestId);
                    cmdInsertSignature.Parameters.AddWithValue("$signature_hash", resultado.Signature.SignatureHash);
                    cmdInsertSignature.Parameters.AddWithValue("$status", resultado.Signature.Status.ToString());
                    cmdInsertSignature.Parameters.AddWithValue("$severity_label", resultado.Signature.Severity.ToString());
                    cmdInsertSignature.Parameters.AddWithValue("$severity_reason", string.IsNullOrWhiteSpace(resultado.Signature.SeverityReason) ? (object)DBNull.Value : resultado.Signature.SeverityReason);
                    cmdInsertSignature.Parameters.AddWithValue("$feature_vector_json", featureJson);
                    cmdInsertSignature.Parameters.AddWithValue("$total_events", resultado.Quality.TotalEvents);
                    cmdInsertSignature.Parameters.AddWithValue("$core_event_count", resultado.Quality.CoreEvents);
                    cmdInsertSignature.Parameters.AddWithValue("$support_event_count", resultado.Quality.SupportEvents);
                    cmdInsertSignature.Parameters.AddWithValue("$noise_event_count", resultado.Quality.NoiseEvents);
                    cmdInsertSignature.Parameters.AddWithValue("$duration_seconds", featureVector.TemporalSpanSeconds);
                    cmdInsertSignature.Parameters.AddWithValue("$quality_score", resultado.Signature.QualityScore);
                    cmdInsertSignature.Parameters.AddWithValue("$notes", string.IsNullOrWhiteSpace(resultado.Signature.Notes) ? (object)DBNull.Value : resultado.Signature.Notes);
                    cmdInsertSignature.Parameters.AddWithValue("$quality_warnings_json", warningsJson);
                    cmdInsertSignature.Parameters.AddWithValue("$processed_at", resultado.Signature.ProcessedAt.ToString("o"));
                    cmdInsertSignature.Parameters.AddWithValue("$updated_at", nowIso);

                    signatureId = Convert.ToInt32(cmdInsertSignature.ExecuteScalar());
                }

                foreach (var core in resultado.Segregation.CoreEvents)
                {
                    var metadataJson = JsonSerializer.Serialize(BuildCoreEventMetadata(core), _serializerOptions);
                    using var cmdInsertCore = _conn.CreateCommand();
                    cmdInsertCore.Transaction = tx;
                    cmdInsertCore.CommandText = @"
                        INSERT INTO normalized_core_events (
                            signature_id,
                            event_row_id,
                            event_id,
                            event_time,
                            image,
                            command_line,
                            metadata_json
                        )
                        VALUES (
                            $signature_id,
                            $event_row_id,
                            $event_id,
                            $event_time,
                            $image,
                            $command_line,
                            $metadata_json
                        );
                    ";
                    cmdInsertCore.Parameters.AddWithValue("$signature_id", signatureId);
                    cmdInsertCore.Parameters.AddWithValue("$event_row_id", core.EventRowId);
                    cmdInsertCore.Parameters.AddWithValue("$event_id", core.EventId);
                    var eventTime = core.UtcTime ?? core.CaptureTime;
                    cmdInsertCore.Parameters.AddWithValue("$event_time", eventTime.HasValue ? eventTime.Value.ToString("o") : (object)DBNull.Value);
                    cmdInsertCore.Parameters.AddWithValue("$image", core.Image ?? (object)DBNull.Value);
                    cmdInsertCore.Parameters.AddWithValue("$command_line", core.CommandLine ?? (object)DBNull.Value);
                    cmdInsertCore.Parameters.AddWithValue("$metadata_json", metadataJson);
                    cmdInsertCore.ExecuteNonQuery();
                }

                foreach (var entry in resultado.SuggestedWhitelist)
                {
                    using var cmdInsertWhitelist = _conn.CreateCommand();
                    cmdInsertWhitelist.Transaction = tx;
                    cmdInsertWhitelist.CommandText = @"
                        INSERT INTO normalized_whitelist_entries (
                            signature_id,
                            entry_type,
                            value,
                            reason,
                            approved,
                            auto_generated
                        )
                        VALUES (
                            $signature_id,
                            $entry_type,
                            $value,
                            $reason,
                            $approved,
                            $auto_generated
                        );
                    ";
                    cmdInsertWhitelist.Parameters.AddWithValue("$signature_id", signatureId);
                    cmdInsertWhitelist.Parameters.AddWithValue("$entry_type", entry.EntryType);
                    cmdInsertWhitelist.Parameters.AddWithValue("$value", entry.Value);
                    cmdInsertWhitelist.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(entry.Reason) ? (object)DBNull.Value : entry.Reason);
                    cmdInsertWhitelist.Parameters.AddWithValue("$approved", entry.Approved ? 1 : 0);
                    cmdInsertWhitelist.Parameters.AddWithValue("$auto_generated", entry.AutoApproved ? 1 : 0);
                    cmdInsertWhitelist.ExecuteNonQuery();
                }

                foreach (var log in resultado.Logs)
                {
                    using var cmdInsertLog = _conn.CreateCommand();
                    cmdInsertLog.Transaction = tx;
                    cmdInsertLog.CommandText = @"
                        INSERT INTO normalization_log (test_id, stage, level, message)
                        VALUES ($test_id, $stage, $level, $message);
                    ";
                    cmdInsertLog.Parameters.AddWithValue("$test_id", resultado.Signature.TestId);
                    cmdInsertLog.Parameters.AddWithValue("$stage", log.Stage);
                    cmdInsertLog.Parameters.AddWithValue("$level", log.Level);
                    cmdInsertLog.Parameters.AddWithValue("$message", log.Message);
                    cmdInsertLog.ExecuteNonQuery();
                }

                using (var cmdUpdateTest = _conn.CreateCommand())
                {
                    cmdUpdateTest.Transaction = tx;
                    cmdUpdateTest.CommandText = @"
                        UPDATE atomic_tests
                        SET normalized_at = $normalized_at,
                            tarja = $tarja,
                            tarja_reason = $tarja_reason,
                            normalization_status = $status
                        WHERE id = $id;
                    ";
                    cmdUpdateTest.Parameters.AddWithValue("$normalized_at", resultado.Signature.ProcessedAt.ToString("o"));
                    cmdUpdateTest.Parameters.AddWithValue("$tarja", resultado.Signature.Severity.ToString());
                    cmdUpdateTest.Parameters.AddWithValue("$tarja_reason", string.IsNullOrWhiteSpace(resultado.Signature.SeverityReason) ? (object)DBNull.Value : resultado.Signature.SeverityReason);
                    cmdUpdateTest.Parameters.AddWithValue("$status", resultado.Signature.Status.ToString());
                    cmdUpdateTest.Parameters.AddWithValue("$id", resultado.Signature.TestId);
                    cmdUpdateTest.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Exclui um teste catalogado e seus eventos associados
        /// </summary>
        /// <param name="testeId">ID do teste a excluir</param>
        /// <returns>True se excluído com sucesso</returns>
        public bool ExcluirTesteAtomico(int testeId)
        {
            using var tx = _conn.BeginTransaction();
            try
            {
                // Obter session_id do teste
                using var cmdGetSession = _conn.CreateCommand();
                cmdGetSession.Transaction = tx;
                cmdGetSession.CommandText = "SELECT session_id FROM atomic_tests WHERE id = $id;";
                cmdGetSession.Parameters.AddWithValue("$id", testeId);
                var sessionId = cmdGetSession.ExecuteScalar();

                if (sessionId == null)
                {
                    tx.Rollback();
                    return false; // Teste não encontrado
                }

                // Excluir eventos da sessão (CASCADE deve fazer isso automaticamente, mas garantimos)
                using var cmdDelEvents = _conn.CreateCommand();
                cmdDelEvents.Transaction = tx;
                cmdDelEvents.CommandText = "DELETE FROM events WHERE session_id = $session_id;";
                cmdDelEvents.Parameters.AddWithValue("$session_id", sessionId);
                cmdDelEvents.ExecuteNonQuery();

                // Excluir teste atômico
                using var cmdDelTest = _conn.CreateCommand();
                cmdDelTest.Transaction = tx;
                cmdDelTest.CommandText = "DELETE FROM atomic_tests WHERE id = $id;";
                cmdDelTest.Parameters.AddWithValue("$id", testeId);
                cmdDelTest.ExecuteNonQuery();

                // Excluir sessão
                using var cmdDelSession = _conn.CreateCommand();
                cmdDelSession.Transaction = tx;
                cmdDelSession.CommandText = "DELETE FROM sessions WHERE id = $session_id;";
                cmdDelSession.Parameters.AddWithValue("$session_id", sessionId);
                cmdDelSession.ExecuteNonQuery();

                tx.Commit();
                return true;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        // ==================== MÉTODOS DO MOTOR HEURÍSTICO ====================

        /// <summary>
        /// Busca eventos de uma sessão a partir de um determinado timestamp (janela deslizante).
        /// </summary>
        internal IReadOnlyList<CatalogEventSnapshot> ObterEventosDaSessaoAPartirDe(int sessionId, DateTime fromTimestamp)
        {
            var eventos = new List<CatalogEventSnapshot>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    id, event_id, utc_time, capture_time, sequence_number,
                    image, command_line, parent_image, parent_command_line,
                    process_id, parent_process_id, process_guid, parent_process_guid,
                    user, integrity_level, hashes,
                    target_filename, image_loaded, signed, signature, signature_status,
                    pipe_name, wmi_operation, wmi_name, wmi_query,
                    dns_query, dns_result, dns_type,
                    src_ip, src_port, dst_ip, dst_port, protocol,
                    raw_json
                FROM events
                WHERE session_id = $session_id
                  AND (utc_time >= $from OR capture_time >= $from)
                ORDER BY sequence_number ASC, id ASC;
            ";
            cmd.Parameters.AddWithValue("$session_id", sessionId);
            cmd.Parameters.AddWithValue("$from", fromTimestamp.ToString("o"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                eventos.Add(ReadCatalogEventSnapshot(reader));
            }

            return eventos;
        }

        /// <summary>
        /// Carrega todas as técnicas catalogadas e normalizadas para análise.
        /// </summary>
        internal List<CatalogedTechniqueContext> CarregarTecnicasCatalogadas()
        {
            var techniques = new List<CatalogedTechniqueContext>();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    nts.id, nts.test_id, nts.feature_vector_json,
                    nts.severity_label, nts.status,
                    at.numero, at.nome
                FROM normalized_test_signatures nts
                INNER JOIN atomic_tests at ON nts.test_id = at.id
                WHERE nts.status = 'Completed' AND at.finalizado = 1
                ORDER BY nts.id;
            ";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var testId = reader.GetInt32(1);
                var featureVectorJson = reader.GetString(2);
                var severityLabel = reader.GetString(3);
                var techniqueId = reader.GetString(5);
                var techniqueName = reader.GetString(6);

                // Deserializar feature vector
                var featureVectorDict = JsonSerializer.Deserialize<Dictionary<string, object>>(featureVectorJson);
                if (featureVectorDict == null) continue;

                // Extrair histograma
                var histogram = new Dictionary<int, int>();
                if (featureVectorDict.TryGetValue("eventTypeHistogram", out var histObj) && histObj is JsonElement histElem)
                {
                    foreach (var prop in histElem.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var eventId))
                        {
                            histogram[eventId] = prop.Value.GetInt32();
                        }
                    }
                }

                var processTreeDepth = featureVectorDict.TryGetValue("processTreeDepth", out var ptd) && ptd is JsonElement ptdElem ? ptdElem.GetInt32() : 0;
                var networkConnectionsCount = featureVectorDict.TryGetValue("networkConnectionsCount", out var ncc) && ncc is JsonElement nccElem ? nccElem.GetInt32() : 0;
                var registryOperationsCount = featureVectorDict.TryGetValue("registryOperationsCount", out var roc) && roc is JsonElement rocElem ? rocElem.GetInt32() : 0;
                var fileOperationsCount = featureVectorDict.TryGetValue("fileOperationsCount", out var foc) && foc is JsonElement focElem ? focElem.GetInt32() : 0;
                var temporalSpanSeconds = featureVectorDict.TryGetValue("temporalSpanSeconds", out var tss) && tss is JsonElement tssElem ? tssElem.GetDouble() : 0.0;
                var criticalEventsCount = featureVectorDict.TryGetValue("criticalEventsCount", out var cec) && cec is JsonElement cecElem ? cecElem.GetInt32() : 0;

                var featureVector = new Heuristics.Normalization.NormalizedFeatureVector(
                    histogram,
                    processTreeDepth,
                    networkConnectionsCount,
                    registryOperationsCount,
                    fileOperationsCount,
                    temporalSpanSeconds,
                    criticalEventsCount
                );

                // Carregar core events com ordem e tempo relativo
                var coreEventPatterns = CarregarCoreEventPatterns(testId);
                var coreEvents = coreEventPatterns
                    .Select(p => p.EventId)
                    .Distinct()
                    .ToList();

                // Carregar whitelist
                var (whitelistedIps, whitelistedDomains, whitelistedProcesses) = CarregarWhitelist(testId);

                // Parse severity
                Enum.TryParse<Heuristics.Normalization.ThreatSeverityTarja>(severityLabel, out var severity);

                techniques.Add(new CatalogedTechniqueContext
                {
                    TestId = testId,
                    TechniqueId = techniqueId,
                    TechniqueName = techniqueName,
                    Tactic = InferTacticFromTechnique(techniqueId),
                    ThreatLevel = severity,
                    FeatureVector = featureVector,
                    CoreEventIds = coreEvents,
                    CoreEventPatterns = coreEventPatterns,
                    WhitelistedIps = whitelistedIps,
                    WhitelistedDomains = whitelistedDomains,
                    WhitelistedProcesses = whitelistedProcesses
                });
            }

            return techniques;
        }

        private List<CoreEventPattern> CarregarCoreEventPatterns(int testId)
        {
            var orderedEvents = new List<(int EventId, DateTime? EventTime)>();

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT event_id, event_time, event_row_id, id
                FROM normalized_core_events
                WHERE signature_id = (SELECT id FROM normalized_test_signatures WHERE test_id = $test_id)
                ORDER BY 
                    CASE WHEN event_time IS NULL THEN 1 ELSE 0 END,
                    event_time,
                    event_row_id,
                    id;
            ";
            cmd.Parameters.AddWithValue("$test_id", testId);

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var eventId = reader.GetInt32(0);
                    DateTime? eventTime = null;

                    if (!reader.IsDBNull(1))
                    {
                        var raw = reader.GetString(1);
                        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                        {
                            eventTime = parsed;
                        }
                    }

                    orderedEvents.Add((eventId, eventTime));
                }
            }

            DateTime? reference = orderedEvents
                .Select(e => e.EventTime)
                .FirstOrDefault(t => t.HasValue);

            var patterns = new List<CoreEventPattern>(orderedEvents.Count);
            foreach (var entry in orderedEvents)
            {
                double? relativeSeconds = null;
                if (entry.EventTime.HasValue && reference.HasValue)
                {
                    relativeSeconds = (entry.EventTime.Value - reference.Value).TotalSeconds;
                }

                patterns.Add(new CoreEventPattern(entry.EventId, relativeSeconds));
            }

            return patterns;
        }

        private (HashSet<string>, HashSet<string>, HashSet<string>) CarregarWhitelist(int testId)
        {
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT entry_type, value
                FROM normalized_whitelist_entries
                WHERE signature_id = (SELECT id FROM normalized_test_signatures WHERE test_id = $test_id)
                  AND approved = 1;
            ";
            cmd.Parameters.AddWithValue("$test_id", testId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var value = reader.GetString(1);

                switch (type.ToUpperInvariant())
                {
                    case "IP":
                        ips.Add(value);
                        break;
                    case "DOMAIN":
                        domains.Add(value);
                        break;
                    case "PROCESS":
                        processes.Add(value);
                        break;
                }
            }

            return (ips, domains, processes);
        }

        private string InferTacticFromTechnique(string techniqueId)
        {
            // Mapeamento simplificado de técnicas para táticas
            return techniqueId.Split('.')[0] switch
            {
                "T1003" => "Credential Access",
                "T1055" => "Privilege Escalation",
                "T1059" => "Execution",
                "T1071" => "Command and Control",
                "T1082" => "Discovery",
                "T1105" => "Command and Control",
                "T1543" => "Persistence",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Salva um snapshot de análise de similaridade.
        /// </summary>
        internal int SalvarSnapshot(SessionSnapshot snapshot)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO session_similarity_snapshots (
                    session_id, snapshot_at, matches, highest_match_test_id,
                    highest_similarity, session_threat_level, event_count_at_snapshot,
                    active_processes_count
                )
                VALUES (
                    $session_id, $snapshot_at, $matches, $highest_match_test_id,
                    $highest_similarity, $session_threat_level, $event_count_at_snapshot,
                    $active_processes_count
                )
                RETURNING id;
            ";

            cmd.Parameters.AddWithValue("$session_id", snapshot.SessionId);
            cmd.Parameters.AddWithValue("$snapshot_at", snapshot.SnapshotAt.ToString("o"));
            cmd.Parameters.AddWithValue("$matches", SerializeMatches(snapshot.Matches));
            cmd.Parameters.AddWithValue("$highest_match_test_id", (object?)snapshot.HighestMatch?.TestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$highest_similarity", (object?)snapshot.HighestMatch?.Similarity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$session_threat_level", snapshot.SessionThreatLevel.ToString());
            cmd.Parameters.AddWithValue("$event_count_at_snapshot", snapshot.EventCountAtSnapshot);
            cmd.Parameters.AddWithValue("$active_processes_count", snapshot.ActiveProcessesCount);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private string SerializeMatches(IReadOnlyList<SimilarityMatch> matches)
        {
            var matchesData = matches.Select(m => new
            {
                test_id = m.TestId,
                technique_id = m.TechniqueId,
                technique_name = m.TechniqueName,
                tactic = m.Tactic,
                similarity = m.Similarity,
                threat_level = m.ThreatLevel.ToString(),
                confidence = m.Confidence,
                matched_events = m.MatchedEventIds
            });

            return JsonSerializer.Serialize(matchesData, _serializerOptions);
        }

        /// <summary>
        /// Salva um alerta de mudança de nível de ameaça.
        /// </summary>
        internal int SalvarAlerta(ThreatAlert alert)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO alert_history (
                    session_id, timestamp, previous_level, new_level, reason,
                    trigger_technique_id, trigger_similarity, snapshot_id
                )
                VALUES (
                    $session_id, $timestamp, $previous_level, $new_level, $reason,
                    $trigger_technique_id, $trigger_similarity, $snapshot_id
                )
                RETURNING id;
            ";

            cmd.Parameters.AddWithValue("$session_id", alert.SessionId);
            cmd.Parameters.AddWithValue("$timestamp", alert.Timestamp.ToString("o"));
            cmd.Parameters.AddWithValue("$previous_level", (object?)alert.PreviousLevel?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$new_level", alert.NewLevel.ToString());
            cmd.Parameters.AddWithValue("$reason", alert.Reason);
            cmd.Parameters.AddWithValue("$trigger_technique_id", (object?)alert.TriggerTechniqueId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$trigger_similarity", (object?)alert.TriggerSimilarity ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$snapshot_id", (object?)alert.SnapshotId ?? DBNull.Value);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>
        /// Libera os recursos utilizados pelo armazenamento
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _conn.Dispose();
        }
    }
}
