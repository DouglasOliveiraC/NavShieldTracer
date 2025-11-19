# Modelo de Dados

O NavShieldTracer utiliza SQLite como armazenamento principal. Todas as views e heurísticas consomem esse banco, portanto compreender o schema facilita auditorias, exportações e integrações com ferramentas externas.

## Tabela `sessions`
```sql
CREATE TABLE sessions (
    id INTEGER PRIMARY KEY,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    target_process TEXT NOT NULL,
    root_pid INTEGER NOT NULL,
    host TEXT NOT NULL,
    user TEXT NOT NULL,
    os_version TEXT NOT NULL,
    notes TEXT
);
```
- Uma linha por sessao de monitoramento.
- Campos `started_at` e `ended_at` estao em UTC.

## Tabela `events`
```sql
CREATE TABLE events (
    id INTEGER PRIMARY KEY,
    session_id INTEGER NOT NULL,
    event_id INTEGER NOT NULL,
    event_record_id INTEGER,
    computer_name TEXT,
    utc_time TEXT,
    capture_time TEXT,
    process_id INTEGER,
    parent_process_id INTEGER,
    image TEXT,
    command_line TEXT,
    src_ip TEXT,
    dst_ip TEXT,
    dst_port TEXT,
    dns_query TEXT,
    target_filename TEXT,
    raw_json TEXT,
    FOREIGN KEY(session_id) REFERENCES sessions(id)
);
```
- Event IDs correspondem ao Sysmon (1: ProcessCreate, 3: NetworkConnect, etc.).
- `raw_json` preserva o payload original para troubleshooting.

## Tabela `atomic_tests`
```sql
CREATE TABLE atomic_tests (
    id INTEGER PRIMARY KEY,
    session_id INTEGER NOT NULL,
    technique_number TEXT NOT NULL,
    technique_name TEXT NOT NULL,
    description TEXT,
    total_events INTEGER,
    created_at TEXT NOT NULL,
    FOREIGN KEY(session_id) REFERENCES sessions(id)
);
```
- Armazena catalogacao de execucoes do Atomic Red Team.


## Exportação e integração
- Os arquivos JSON presentes em `Logs/` refletem exatamente os campos da tabela `events`, incluindo o `raw_json` recebido do Sysmon.
- O banco é compatível com ferramentas como Azure Data Studio, DB Browser for SQLite e qualquer driver ODBC/ADO.NET.
- Para pipelines simples, utilize `sqlite3 navshieldtracer.sqlite ".headers on" ".mode csv"` e direcione a saída para relatórios versionados.

Dominar o schema torna mais fácil justificar alertas, responder auditorias internas e criar visualizações específicas sem depender de mudanças no código.
