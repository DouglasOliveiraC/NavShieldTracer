using System;
using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NavShieldTracer.Modules;

namespace NavShieldTracer.Modules.Storage
{
    /// <summary>
    /// Implementação de armazenamento de eventos usando SQLite
    /// </summary>
    public class SqliteEventStore : IEventStore
    {
        private readonly SqliteConnection _conn;
        private bool _disposed;
        /// <summary>
        /// Caminho do arquivo de banco de dados SQLite
        /// </summary>
        public string DatabasePath { get; }

        /// <summary>
        /// Inicializa uma nova instância do armazenamento SQLite
        /// </summary>
        /// <param name="databasePath">Caminho opcional para o banco de dados</param>
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
            ";
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        /// <summary>
        /// Inicia uma nova sessão de monitoramento
        /// </summary>
        /// <param name="info">Informações da sessão</param>
        /// <returns>ID da sessão criada</returns>
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
        /// Finaliza uma sessão de monitoramento
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <param name="summary">Resumo opcional da sessão</param>
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
        /// Insere um evento na sessão especificada
        /// </summary>
        /// <param name="sessionId">ID da sessão</param>
        /// <param name="data">Dados do evento</param>
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
