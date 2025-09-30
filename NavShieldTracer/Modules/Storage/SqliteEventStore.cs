using System;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
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

