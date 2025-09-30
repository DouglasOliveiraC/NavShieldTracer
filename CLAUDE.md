# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Visão Geral do Projeto

NavShieldTracer é uma ferramenta de monitoramento de segurança para Windows escrita em C# (.NET 9) que utiliza o Sysmon (System Monitor) para rastrear e registrar atividades detalhadas do sistema de processos alvo. É projetada para análise de segurança defensiva e investigação forense do comportamento de software.

**Contexto Acadêmico**: Este é um trabalho escolar/TCC sendo desenvolvido em fases incrementais. O foco atual é a **Fase 1: Catalogação Robusta** de testes do MITRE ATT&CK usando Atomic Red Team. O motor heurístico de análise comportamental será implementado na Fase 2.

## Estado Atual do Projeto

### ✅ Funcionalidades Implementadas
- Captura de eventos Sysmon em tempo real (26 tipos de eventos suportados)
- Rastreamento de árvore de processos (processo alvo + filhos)
- Persistência estruturada em SQLite com índices otimizados
- Catalogação manual de testes atômicos do MITRE ATT&CK
- Integração com Invoke-AtomicRedTeam para execução de TTPs
- Menu interativo para buscar, executar e criar testes YAML customizados
- Export de eventos em JSON para análise manual

### ⚠️ Em Desenvolvimento
- Relatório estruturado de catalogação (geração automática após captura)
- Script de captura de logs nativos do Atomic Red Team
- Validador de cobertura de eventos (comparação esperado vs capturado)

### ❌ Não Implementado (Roadmap Futuro)
- Motor heurístico de análise comportamental
- Comparação automatizada entre execução monitorada e testes catalogados
- Extração de features comportamentais (agregação de eventos)
- API REST para camada web
- Dashboard de visualização

## Comandos de Build e Desenvolvimento

### Compilando a Aplicação
```bash
# Build em modo Debug
dotnet build NavShieldTracer/NavShieldTracer.csproj

# Build em modo Release  
dotnet build NavShieldTracer/NavShieldTracer.csproj -c Release

# Build da solução completa
dotnet build NavShieldTracer.sln
```

### Executando a Aplicação
```bash
# Executar em modo Debug (requer privilégios de Administrador)
dotnet run --project NavShieldTracer/NavShieldTracer.csproj

# Executar build Release
dotnet run --project NavShieldTracer/NavShieldTracer.csproj -c Release
```

### Fluxo de Catalogação de Testes (Processo Manual Controlado)

**IMPORTANTE**: A catalogação é um processo manual e técnico. Certos testes do Atomic Red Team podem prejudicar o sistema, portanto deve ser executado com supervisão técnica em ambiente controlado.

#### Fluxo Recomendado:
```bash
# 1. Terminal 1 (ADMINISTRADOR): NavShieldTracer
dotnet run --project NavShieldTracer/NavShieldTracer.csproj
# → Escolher opção 2: "Catalogar novo teste atômico"
# → Digitar: T1055, "Process Injection", "Descrição do teste"
# → Sistema inicia monitoramento esperando "teste.exe"

# 2. Terminal 2 (Separado): TesteSoftware
dotnet run --project TesteSoftware/TesteSoftware.csproj
# → Menu interativo do Atomic Red Team
# → Executar teste (ex: opção 3, digitar T1055, números de teste)
# → Aguardar conclusão do teste

# 3. Voltar ao Terminal 1
# → Pressionar ENTER para finalizar catalogação
# → Sistema gera automaticamente:
#    • Banco SQLite com eventos estruturados (Logs/navshieldtracer.sqlite)
#    • Export JSON dos eventos (Logs/logs_teste_T1055_timestamp.json)
#    • Relatório visual (quando implementado)

# 4. Validação Manual
# → Comparar eventos capturados pelo NavShield com logs nativos do ART
# → Verificar se todos os comportamentos esperados foram catalogados
# → Usar opção 3 do menu para visualizar testes catalogados
```

#### Por que Manual?
- Testes podem ser destrutivos (modificar registro, criar serviços, infectar sistema)
- Requer supervisão técnica para interpretar resultados
- Permite validação cuidadosa de cada teste antes de catalogação
- Facilita troubleshooting se algo não funcionar como esperado

### Geração de Documentação
```bash
# Gerar documentação usando DocFX
docfx docfx.json --serve
```

## Arquitetura Geral

### Componentes Principais

1. **Program.cs**: Ponto de entrada que gerencia interação com usuário, detecção do Sysmon e coordena o pipeline de monitoramento
2. **SysmonEventMonitor.cs**: Monitora o Event Log do Windows para eventos do Sysmon e os converte em objetos estruturados
3. **ProcessActivityTracker.cs**: Gerencia a árvore de processos monitorados, rastreando relações pai-filho e filtrando eventos relevantes
4. **SqliteEventStore.cs**: Gerencia persistência estruturada em base de dados SQLite otimizada
5. **ModelosEventos.cs**: Contém modelos de dados para 18+ tipos diferentes de eventos do Sysmon (criação de processos, conexões de rede, operações de arquivo, acesso ao registro, etc.)
6. **TesteSoftware/**: Projeto modular integrado com Red Canary Atomic Red Team para simulação de TTPs adversariais

### Fluxo de Processamento de Eventos

1. Usuário seleciona executável alvo → Program cria instâncias de store e tracker
2. SysmonEventMonitor se inscreve no Windows Event Log para eventos do Sysmon
3. Eventos são parseados de XML para objetos C# tipados
4. ProcessActivityTracker filtra eventos pertencentes à árvore de processos monitorados
5. SqliteEventStore persiste eventos estruturados em base de dados otimizada

### Estrutura de Dados (SQLite)

Eventos são armazenados em `Logs/navshieldtracer.sqlite` com três tabelas principais:

#### 1. Tabela `sessions`
Armazena metadados de cada sessão de monitoramento:
```sql
- id: Identificador único da sessão
- started_at: Timestamp de início
- ended_at: Timestamp de finalização
- target_process: Nome do executável monitorado
- root_pid: PID do processo raiz
- host: Nome da máquina
- user: Usuário que executou o monitoramento
- os_version: Versão do Windows
- notes: JSON com estatísticas da sessão (opcional)
```

#### 2. Tabela `events`
Armazena eventos do Sysmon de forma normalizada:
```sql
- Campos comuns: session_id, event_id, event_record_id, computer_name, utc_time, capture_time
- Campos de processo: process_id, parent_process_id, image, command_line, parent_image, hashes
- Campos de rede: src_ip, src_port, dst_ip, dst_port, protocol
- Campos de DNS: dns_query, dns_type, dns_result
- Campos de arquivo: target_filename, image_loaded, signed, signature
- Campos de pipe: pipe_name
- Campos de WMI: wmi_operation, wmi_name, wmi_query
- Campos de clipboard: clipboard_operation, clipboard_contents
- raw_json: Evento completo serializado (troubleshooting)
```

**Índices otimizados**: `session_id`, `process_id`, `parent_process_id`, `image`, `dst_ip`, `dst_port`, `dns_query`, `target_filename`, `utc_time`

#### 3. Tabela `atomic_tests`
Armazena testes atômicos catalogados do MITRE ATT&CK:
```sql
- id: Identificador único do teste
- numero: Número da técnica MITRE (ex: "T1055")
- nome: Nome da técnica (ex: "Process Injection")
- descricao: Descrição detalhada do teste
- data_execucao: Timestamp de execução
- session_id: FK para sessions (vincula eventos à catalogação)
- total_eventos: Quantidade de eventos capturados
- finalizado: Flag indicando catalogação completa
```

**Relacionamento**: `atomic_tests.session_id → sessions.id → events.session_id`

Isso permite recuperar todos os eventos capturados durante a catalogação de um teste específico.

## Requisitos Importantes

### Requisitos do Sistema
- **Privilégios de administrador obrigatórios** - O manifest da aplicação solicita `requireAdministrator`
- **Sysmon deve estar instalado** - Aplicação verifica disponibilidade do Sysmon na inicialização
- **Windows 10.0.17763.0 ou posterior** - Especificado no target framework
- **Arquitetura x64** - Platform target configurado para x64

### Dependências
- `Microsoft.Diagnostics.Tracing.TraceEvent` (3.1.6) - Para processamento de eventos ETW
- `System.Diagnostics.EventLog` (9.0.0-preview.5) - Para acesso ao Windows Event Log

## Roadmap de Desenvolvimento (Faseado)

### 🎯 FASE 1: CATALOGAÇÃO ROBUSTA (Estado Atual)

**Objetivo**: Catalogar 10-15 testes do MITRE ATT&CK com alta fidelidade para criar baseline comportamental.

**Tarefas Prioritárias**:
- [ ] Implementar relatório estruturado de catalogação (`GerarRelatorioCatalogacao()` em SqliteEventStore)
- [ ] Criar script PowerShell `CapturarLogsART.ps1` para salvar logs nativos do Atomic Red Team
- [ ] Implementar validador de cobertura de eventos (comparação esperado vs capturado)
- [ ] Catalogar testes prioritários: T1055, T1059.001, T1071.001, T1105, T1027, T1543.003, T1003.001, T1082
- [ ] Documentar checklist de validação para cada TTP catalogado

**Critério de Conclusão**: Banco SQLite com 10+ testes validados + relatórios de cobertura

---

### 🔬 FASE 2: MOTOR HEURÍSTICO BÁSICO (Próxima)

**Objetivo**: Implementar comparação automatizada entre execução monitorada e testes catalogados.

**Componentes a Desenvolver**:

1. **BehavioralAnalyzer.cs** - Motor de extração de features e comparação
   ```csharp
   - ExtractFeatures(sessionId) → FeatureVector
   - Compare(observed, baseline) → SimilarityScore
   - RankTests(sessionId) → List<(testeId, score)>
   ```

2. **Features para Comparação**:
   - Contagem de eventos por tipo (vetor de 26 dimensões - Event IDs 1-26)
   - IPs de destino únicos
   - Domínios DNS consultados
   - Arquivos criados/modificados/deletados (agrupados por extensão)
   - Chaves de registro acessadas
   - Processos filhos criados
   - DLLs carregadas (assinadas vs não-assinadas)

3. **Algoritmo de Similaridade**:
   - Fase inicial: **Cosine Similarity** entre vetores de features
   - Normalização: TF-IDF para features textuais (nomes de arquivo, domínios)
   - Pesos personalizados: Event ID 8 (remote thread) tem peso maior que Event ID 7 (DLL load)

4. **Schema SQLite Estendido**:
   ```sql
   -- Tabela de features agregadas por sessão
   CREATE TABLE session_features (
       session_id INTEGER PRIMARY KEY,
       network_connections_count INTEGER,
       unique_dst_ips INTEGER,
       dns_queries_count INTEGER,
       files_created_count INTEGER,
       registry_accesses_count INTEGER,
       remote_threads_created INTEGER,
       feature_vector JSON  -- vetor de 26 dimensões
   );

   -- Tabela de análises realizadas
   CREATE TABLE analysis_results (
       id INTEGER PRIMARY KEY,
       session_id INTEGER REFERENCES sessions(id),
       analyzed_at TEXT,
       matched_tests JSON,  -- [{"test_id": 1, "score": 0.85}, ...]
       detected_ttps JSON,  -- ["T1055.001", "T1059.001"]
       risk_score REAL
   );
   ```

5. **Novo Menu no NavShieldTracer**:
   ```
   6. [🔍] Analisar sessão monitorada
   → Digitar ID da sessão
   → Sistema extrai features
   → Compara com todos os testes catalogados
   → Retorna top 5 matches com scores de similaridade
   ```

**Critério de Conclusão**: Sistema capaz de identificar TTP conhecido com accuracy > 80%

---

### 🌐 FASE 3: API REST E DASHBOARD (Futuro)

**Objetivo**: Interface web para visualização e análise interativa.

**Stack Tecnológico**:
- Backend: ASP.NET Core Minimal API
- Frontend: Blazor Server (C# full-stack) ou React
- Database: Mesmo SQLite (com otimizações de leitura)

**Endpoints Planejados**:
```
GET  /api/sessions                 - Lista todas as sessões
GET  /api/sessions/{id}            - Detalhes de uma sessão
GET  /api/sessions/{id}/events     - Eventos de uma sessão
POST /api/sessions/{id}/analyze    - Executa análise comportamental
GET  /api/tests                    - Lista testes catalogados
GET  /api/tests/{id}/baseline      - Features do teste catalogado
POST /api/compare                  - Compara sessão com teste específico
```

**Features do Dashboard**:
- Timeline interativa de eventos (Vis.js)
- Gráfico de rede (IPs, domínios, conexões)
- Process tree visualization
- Heatmap de Event IDs
- Export para MITRE ATT&CK Navigator (JSON)

---

## Diretrizes de Desenvolvimento

### Extensões de Modelo de Evento
Ao adicionar suporte para novos tipos de evento do Sysmon:
1. Adicionar nova classe de evento em `ModelosEventos.cs` herdando de `EventoSysmonBase`
2. Adicionar case handler no método `SysmonEventMonitor.ParseEvent()`
3. Atualizar extração de campos em `SqliteEventStore.InsertEvent()` (switch case)
4. Adicionar lógica de extração de PID em `ProcessActivityTracker.GetPidFromEvent()` se necessário

### Boas Práticas para Catalogação
1. **Sempre executar em ambiente controlado** (VM ou sandbox)
2. **Criar snapshot antes** de executar testes destrutivos
3. **Validar Sysmon config** antes de catalogação (usar `sysmon -c` para verificar)
4. **Comparar com documentação MITRE** para confirmar comportamentos esperados
5. **Documentar falsos negativos**: se evento esperado não foi capturado, anotar no campo `descricao`

### Contexto de Segurança Defensiva
Esta ferramenta é projetada exclusivamente para propósitos de segurança defensiva:
- Análise de comportamento de processos e forense
- Análise de malware em sandbox
- Auditoria de atividade de software
- Investigação de incidentes de segurança
- **NÃO** para desenvolvimento de malware ou offensive security

### Contexto da Linguagem Portuguesa
O codebase utiliza português para:
- Nomes de classes e propriedades (ex: `EventoProcessoCriado`, `LinhaDeComando`)
- Saída do console e interação com usuário
- Nomes de pastas de log e documentação
- Comentários e strings de documentação

Isso reflete a base de usuários alvo e deve ser mantido para consistência.

## Software de Teste (TesteSoftware)

### Integração Red Canary Atomic Red Team

**IMPORTANTE**: TesteSoftware compila como `teste.exe` (`<AssemblyName>teste</AssemblyName>` no .csproj). Este nome é hardcoded no NavShieldTracer para detecção automática durante catalogação.

O projeto TesteSoftware é uma interface C# para o Invoke-AtomicRedTeam via PowerShell Runspaces:

#### Funcionalidades Disponíveis:
1. **Buscar técnicas** do MITRE ATT&CK por ID ou nome
2. **Ver detalhes** de uma técnica específica (lista de testes atômicos disponíveis)
3. **Executar teste atômico** com parâmetros customizados e coleta de pré-requisitos
4. **Criar testes customizados** em formato YAML compatível com ART
5. **Atualizar repositório** Atomic Red Team (Update-AtomicRedTeam)

#### Menu Interativo:
```
1) Buscar tecnicas
2) Ver detalhes e testes de uma tecnica
3) Executar teste atomico
4) Criar teste customizado (YAML)
5) Atualizar repositorio Atomic Red Team
6) Sair
```

### TTPs Prioritários para Catalogação (Fase 1)

Testes recomendados para catalogação inicial (comportamentos bem definidos e detectáveis):

- **T1055** - Process Injection (CreateRemoteThread, reflective DLL injection)
- **T1059.001** - PowerShell (scripts maliciosos, download cradles)
- **T1071.001** - Web Protocols (HTTP/HTTPS C2 communication)
- **T1105** - Ingress Tool Transfer (download de ferramentas via certutil, bitsadmin)
- **T1027** - Obfuscated Files (base64, XOR, compression)
- **T1543.003** - Windows Service (criação de serviços persistentes)
- **T1003.001** - LSASS Memory (credential dumping)
- **T1082** - System Information Discovery (whoami, systeminfo)

### Validação de Cobertura

Após catalogar um teste, validar manualmente se foram capturados:

**Para T1055 (Process Injection)**:
- Event ID 1: Processo injetor criado
- Event ID 8: CreateRemoteThread detectado
- Event ID 10: Process Access (PROCESS_VM_WRITE, PROCESS_VM_OPERATION)
- Event ID 7: DLLs carregadas no processo alvo

**Para T1059.001 (PowerShell)**:
- Event ID 1: powershell.exe criado com command line suspeita
- Event ID 3: Conexões de rede (se houver download)
- Event ID 11: Arquivos criados no temp

**Para T1071.001 (Web Protocols)**:
- Event ID 3: Conexões HTTP/HTTPS para IPs suspeitos
- Event ID 22: DNS queries para domínios C2

### Exemplo de Uso (Catalogação Completa)

```bash
# Terminal 1 (Admin): NavShieldTracer
dotnet run --project NavShieldTracer
> Opção 2
> Numero: T1055
> Nome: Process Injection via CreateRemoteThread
> Descricao: Injeta DLL em processo remoto usando API do Windows

# Terminal 2: TesteSoftware
dotnet run --project TesteSoftware
> Opção 3 (Executar teste)
> Tecnica: T1055
> Numeros: 1
> Coletar pre-requisitos: N
> [Aguardar execução e observar output]

# Terminal 1: Pressionar ENTER
# Sistema exibe:
# ✅ Catalogação finalizada! Teste 'T1055' salvo com 47 eventos.

# Terminal 1: Validar cobertura
> Opção 3 (Visualizar testes)
> [Verificar se T1055 aparece na lista com 47 eventos]

# Terminal 1: Exportar logs
> Opção 4 (Acessar logs)
> ID: [número do teste]
> Exportar? S
# [Arquivo JSON gerado em Logs/logs_teste_T1055_timestamp.json]
```

---

## Troubleshooting e Problemas Comuns

### ❌ "Nenhum evento capturado durante catalogação"

**Causas possíveis**:
1. **Sysmon não está capturando o tipo de evento**
   - Solução: Verificar configuração do Sysmon com `sysmon -c`
   - Recomendado: Usar config do SwiftOnSecurity (https://github.com/SwiftOnSecurity/sysmon-config)

2. **TesteSoftware não compilou como teste.exe**
   - Solução: Verificar `<AssemblyName>teste</AssemblyName>` no .csproj
   - Rebuild: `dotnet build TesteSoftware/TesteSoftware.csproj`

3. **Teste do ART falhou silenciosamente**
   - Solução: Executar manualmente no PowerShell: `Invoke-AtomicTest T1055 -ShowDetails`
   - Verificar pré-requisitos: `Invoke-AtomicTest T1055 -CheckPrereqs`

4. **ProcessActivityTracker não detectou teste.exe**
   - Solução: Verificar no console se aparece "Novo processo alvo detectado: teste.exe"
   - Debug: Adicionar logging em `ProcessActivityTracker.HandleProcessCreation()`

### ⚠️ "Event Log do Sysmon não encontrado"

**Solução**:
```powershell
# Verificar se Sysmon está instalado
Get-Service -Name Sysmon64 -ErrorAction SilentlyContinue

# Verificar Event Log
Get-WinEvent -ListLog Microsoft-Windows-Sysmon/Operational

# Se não existir, reinstalar Sysmon:
sysmon64.exe -accepteula -i sysmonconfig.xml
```

### 🐌 "Performance ruim / muitos eventos capturados"

**Otimizações**:
1. **Filtrar processos do sistema** em `ProcessActivityTracker`:
   - Ignorar `svchost.exe`, `backgroundTaskHost.exe`, etc.
   - Adicionar whitelist de processos irrelevantes

2. **Ajustar configuração do Sysmon**:
   - Desabilitar Event ID 7 (Image Load) se não for necessário
   - Filtrar DLLs assinadas pela Microsoft

3. **Usar índices do SQLite**:
   - Já implementado para campos principais
   - Para queries customizadas, criar índices adicionais

### 🔍 "Logs do Atomic Red Team não salvam automaticamente"

**Workaround manual**:
```powershell
# Redirecionar output do Invoke-AtomicTest
Invoke-AtomicTest T1055 -TestNumbers 1 *>&1 | Tee-Object -FilePath "ART_T1055.log"
```

**Solução permanente**: Implementar script `CapturarLogsART.ps1` (Fase 1 do roadmap)

### 📊 "Como saber se catalogação foi bem-sucedida?"

**Checklist de validação**:
1. ✅ Arquivo SQLite tem novo registro em `atomic_tests` com `finalizado = 1`
2. ✅ Quantidade de eventos > 0 (verificar `total_eventos`)
3. ✅ Eventos incluem tipos esperados (verificar opção 3 do menu)
4. ✅ JSON exportado contém eventos relevantes para o TTP
5. ✅ Comparar com documentação MITRE para confirmar comportamentos

**Query SQL para validar**:
```sql
-- Abrir Logs/navshieldtracer.sqlite com sqlite3
SELECT
    at.numero,
    at.nome,
    at.total_eventos,
    COUNT(e.id) as eventos_contados,
    GROUP_CONCAT(DISTINCT e.event_id) as event_ids_capturados
FROM atomic_tests at
LEFT JOIN sessions s ON at.session_id = s.id
LEFT JOIN events e ON s.id = e.session_id
WHERE at.finalizado = 1
GROUP BY at.id;
```

---

## Referências e Recursos

### Documentação Oficial
- **Sysmon**: https://learn.microsoft.com/en-us/sysinternals/downloads/sysmon
- **Atomic Red Team**: https://github.com/redcanaryco/atomic-red-team
- **MITRE ATT&CK**: https://attack.mitre.org/
- **Sysmon Config (SwiftOnSecurity)**: https://github.com/SwiftOnSecurity/sysmon-config

### Event IDs do Sysmon (Referência Rápida)
```
ID 1  - Process Create
ID 2  - File Creation Time Changed
ID 3  - Network Connection
ID 5  - Process Terminated
ID 6  - Driver Loaded
ID 7  - Image/DLL Loaded
ID 8  - CreateRemoteThread (INJECTION)
ID 9  - RawAccessRead (Disk Access)
ID 10 - Process Access (INJECTION)
ID 11 - File Create
ID 12 - Registry Object Create/Delete
ID 13 - Registry Value Set
ID 14 - Registry Object Rename
ID 15 - File Create Stream Hash
ID 17 - Pipe Created
ID 18 - Pipe Connected
ID 19 - WMI Event Filter
ID 20 - WMI Event Consumer
ID 21 - WMI Consumer to Filter Binding
ID 22 - DNS Query
ID 23 - File Delete
ID 24 - Clipboard Change
ID 25 - Process Tampering
ID 26 - File Delete Detected
```

### Comandos Úteis do SQLite

```bash
# Abrir banco de dados
sqlite3 Logs/navshieldtracer.sqlite

# Ver todas as tabelas
.tables

# Ver schema de uma tabela
.schema events

# Contar eventos por tipo
SELECT event_id, COUNT(*) as total FROM events GROUP BY event_id ORDER BY total DESC;

# Listar testes catalogados
SELECT id, numero, nome, total_eventos, data_execucao FROM atomic_tests WHERE finalizado = 1;

# Ver eventos de um teste específico
SELECT e.event_id, e.utc_time, e.image, e.command_line
FROM atomic_tests at
JOIN sessions s ON at.session_id = s.id
JOIN events e ON s.id = e.session_id
WHERE at.id = 1
ORDER BY e.utc_time;

# Exportar para CSV
.mode csv
.output eventos_t1055.csv
SELECT * FROM events WHERE session_id = 1;
.output stdout
```

---

## Contato e Suporte

Para dúvidas ou problemas durante o desenvolvimento:
1. Revisar este CLAUDE.md
2. Consultar documentação oficial do Sysmon e Atomic Red Team
3. Verificar logs do Windows Event Viewer em caso de erros de captura
4. Para issues relacionadas ao Invoke-AtomicRedTeam, consultar: https://github.com/redcanaryco/invoke-atomicredteam

**Lembrete**: Este é um trabalho acadêmico. Sempre executar testes em ambiente isolado (VM ou sandbox) e criar backups antes de catalogação.