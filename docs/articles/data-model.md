# Modelo de Dados

O schema do NavShieldTracer é criado diretamente pelo `SqliteEventStore` (`NavShieldTracer/Storage/SqliteEventStore.cs`).  
O método `EnsureSchema` executa os `CREATE TABLE` abaixo sempre que o aplicativo sobe, portanto este documento reflete exatamente o banco que será encontrado no arquivo `Logs/navshieldtracer.sqlite`.

## Visão geral das tabelas

| Tabela | Finalidade |
| --- | --- |
| `sessions` | Metadados de cada sessão de monitoramento e anotações do operador |
| `events` | Eventos do Sysmon enriquecidos com campos normalizados pelo coletor |
| `atomic_tests` | Execuções catalogadas do Atomic Red Team associadas às sessões |
| `normalized_test_signatures` | Assinaturas geradas pelo motor de normalização para cada teste |
| `normalized_core_events` | Subconjunto de eventos considerados núcleo de uma assinatura |
| `normalization_log` | Logs de cada estágio da normalização (para troubleshooting) |
| `session_similarity_snapshots` | Snapshots periódicos de similaridade/ameaça da sessão em tempo real |
| `alert_history` | Histórico de mudanças de nível de risco disparadas pelo motor heurístico |

As seções a seguir destrincham colunas e relacionamentos; todos os campos citados estão no script SQL embutido ou adicionados via `EnsureAtomicNormalizationColumns`.

## Tabela `sessions`

Guarda uma linha por sessão acompanhada no console.

```sql
CREATE TABLE IF NOT EXISTS sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at TEXT NOT NULL,
    ended_at TEXT,
    target_process TEXT NOT NULL,
    root_pid INTEGER,
    host TEXT,
    user TEXT,
    os_version TEXT,
    notes TEXT,
    user_notes TEXT
);
```

- `started_at`/`ended_at`: timestamps ISO 8601 em UTC.
- `target_process` e `root_pid`: processo monitorado e PID raiz usado para reconstruir árvores.
- `notes` e `user_notes`: observações automatizadas e comentários do analista.

## Tabela `events`

É a maior tabela e replica todos os campos relevantes do Sysmon, incluindo enriquecimento feito pelo coletor (hashes, DNS, pipe, WMI, clipboard etc.).

- Chave primária `id` autoincrement.
- `session_id` referencia `sessions(id)` e está indexado para consultas rápidas.
- O par (`computer_name`, `event_record_id`) é marcado como `UNIQUE` para impedir duplicidade.
- Conjuntos de colunas:
  - **Identificação**: `event_id`, `event_record_id`, `sequence_number`, `utc_time`, `capture_time`.
  - **Processos**: `process_guid`, `parent_process_guid`, `process_id`, `parent_process_id`, `image`, `command_line`, `parent_image`, `parent_command_line`, `current_directory`, `logon_guid`, `integrity_level`, `hashes`, `user`.
  - **Rede**: `src_ip`, `src_port`, `dst_ip`, `dst_port`, `protocol`.
  - **DNS**: `dns_query`, `dns_type`, `dns_result`.
  - **Arquivo/driver/imagem**: `target_filename`, `image_loaded`, `signed`, `signature`, `signature_status`.
  - **Pipes/WMI/Clipboard**: `pipe_name`, `wmi_operation`, `wmi_name`, `wmi_query`, `clipboard_operation`, `clipboard_contents`.
  - **Raw**: `raw_json` guarda o payload original do Sysmon.

Índices estrategicamente posicionados (`session_id`, `event_id`, `process_id`, `dst_ip/dst_port`, `dns_query`, `target_filename`, `session_id + utc_time`, etc.) alimentam filtros do console e o motor heurístico.

## Tabela `atomic_tests`

Representa cada execução catalogada do Atomic Red Team dentro de uma sessão.

Colunas principais:
- `numero`, `nome`, `descricao`: metadados do teste (por exemplo `T1059.001`).
- `data_execucao`: timestamp em texto.
- `session_id`: FK para `sessions`.
- `total_eventos`: contador acumulado.
- `finalizado`: `BOOLEAN` que indica se a execução foi concluída.
- `created_at`: preenchido com `CURRENT_TIMESTAMP`.

Colunas adicionadas dinamicamente por `EnsureAtomicNormalizationColumns`:
- `normalized_at`: momento em que o teste passou pelo pipeline de normalização.
- `tarja` e `tarja_reason`: correspondem à classificação (severity label) e justificativa aplicada pelo motor.
- `normalization_status`: acompanha o workflow (`Pending`, `Processed`, etc.).

## Tabela `normalized_test_signatures`

Armazena o resultado agregado da normalização de cada teste.  
Colunas importantes:
- `test_id`: FK único para `atomic_tests(id)`.
- `signature_hash`: hash determinístico baseado nas características observadas.
- `status`, `severity_label`, `severity_reason`: classificação da assinatura.
- Métricas de qualidade: `total_events`, `core_event_count`, `support_event_count`, `noise_event_count`, `duration_seconds`, `quality_score`.
- JSONs para inspeção detalhada: `feature_vector_json` e `quality_warnings_json`.
- Auditoria temporal: `processed_at`, `created_at`, `updated_at`.

## Tabela `normalized_core_events`

Contém o subconjunto de eventos considerados núcleo para uma assinatura (`signature_id` → `normalized_test_signatures.id`).

- `event_row_id`: chave (`events.id`) do registro original.
- Campos redundantes (`event_id`, `event_time`, `image`, `command_line`) evitam `JOIN` caros ao renderizar relatórios.
- `metadata_json`: payload serializado com detalhes adicionais.

## Tabela `normalization_log`

Histórico textual de cada etapa do pipeline de normalização.

- `test_id`: FK para `atomic_tests`.
- `stage`, `level` e `message`: descrevem o que aconteceu (por exemplo `FeatureExtraction`, `Warning`, `Processo pai ausente`).
- `created_at`: timestamp com `CURRENT_TIMESTAMP`.

## Tabela `session_similarity_snapshots`

Captura periodicamente o estado da análise em tempo real para cada sessão.

- `matches`: JSON contendo as comparações mais recentes.
- `highest_match_test_id`/`highest_similarity`: melhor match encontrado até o momento.
- `session_threat_level`: nível consolidado exibido na UI (por ex. `Medium`, `Critical`).
- `event_count_at_snapshot` e `active_processes_count`: métricas operacionais utilizadas em dashboards.

## Tabela `alert_history`

Registra toda mudança de nível de ameaça emitida pelo motor heurístico.

- `session_id`: FK para `sessions`.
- `timestamp`: instante da mudança.
- `previous_level`/`new_level`: transição aplicada.
- `reason`, `trigger_technique_id`, `trigger_similarity`: ajudam a explicar o alerta (por exemplo técnica MITRE e similaridade atingida).
- `snapshot_id`: FK opcional para `session_similarity_snapshots` que originou o alerta.

## Como manter sincronizado

- Os comentários XML presentes em `SqliteEventStore` e nos modelos (`NavShieldTracer/Modules/Models`) alimentam o DocFX automaticamente, então qualquer alteração no schema precisa ser refletida nesse arquivo C# antes de regenerar a documentação.
- Sempre regenere a documentação com `dotnet tool run docfx docs/docfx.json` após alterar o schema. O build falhará se houver divergência de FK/colunas, garantindo que o portal público mostre o modelo correto.
- Para inspecionar o banco real, abra `Logs/navshieldtracer.sqlite` no DB Browser for SQLite ou rode `sqlite3 Logs/navshieldtracer.sqlite ".tables"` para conferir a correspondência com as tabelas descritas acima.
