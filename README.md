# NavShieldTracer

[![Version](https://img.shields.io/badge/Version-v1.0.0.1-blue?style=flat-square)](https://github.com/DouglasOliveiraC/NavShieldTracer/releases)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

**NavShieldTracer** é uma ferramenta de monitoramento de atividade de processos para Windows, projetada para análise de segurança defensiva e investigação forense do comportamento de software.

> **🎯 Versão Atual: v1.0.0.1**  
> Esta é a primeira versão estável focada em **captura e persistência estruturada** de eventos do sistema. O core de monitoramento está 100% funcional com base de dados SQLite, filtragem inteligente e arquitetura preparada para análise comportamental.

## 📋 Visão Geral

NavShieldTracer utiliza o **Sysmon (System Monitor)** para capturar eventos do sistema e armazená-los em uma **base de dados SQLite estruturada**, fornecendo visibilidade completa sobre:

- 🔄 Criação e encerramento de processos
- 🌐 Conexões de rede e consultas DNS
- 📁 Operações de arquivo (criação, modificação, exclusão)
- 🔐 Acessos ao registro do Windows
- 🧵 Criação de threads remotas
- 📚 Carregamento de DLLs e drivers
- 🔗 Pipes nomeados e streams NTFS

## 🚀 Estado Atual - v1.0.0.1

### ✅ **Funcionalidades Implementadas**

**Core de Monitoramento:**
- ✅ **Captura de 18+ tipos de eventos** do Sysmon (Event IDs 1-26)
- ✅ **Base de dados SQLite** com schema otimizado e índices estratégicos
- ✅ **Rastreamento de árvore de processos** pai-filho com filtragem inteligente
- ✅ **Persistência estruturada** com campos normalizados e JSON raw
- ✅ **Diagnóstico automático** da configuração do Sysmon
- ✅ **Arquitetura modular** preparada para motor heurístico e exibição de dashboard

**Eventos Validados:**
- ✅ **Network Connections** (Event ID 3) - Conexões TCP/UDP com hostnames
- ✅ **DNS Queries** (Event ID 22) - Consultas de resolução de nomes  
- ✅ **Process Creation** (Event ID 1) - Criação de processos com linha de comando
- ✅ **Process Termination** (Event ID 5) - Encerramento de processos

**Infraestrutura:**
- ✅ **TesteSoftware** - Suite modular integrada com Red Canary Atomic Red Team
- ✅ **SQLite WAL Mode** - Performance otimizada com transações seguras
- ✅ **Script de automação** - Execução facilitada de testes
- ✅ **Documentação completa** - Guias técnicos, apresentação TCC e arquitetura

### 🔄 **Em Progresso** 
- **Eventos adicionais** dependem de configuração Sysmon específica:
  - File Operations (Event IDs 2, 11, 23)
  - Registry Access (Event IDs 12-14)  
  - Advanced Process Events (Event IDs 6-10)

### 🎯 **Próximas Versões**
- **Módulo 2 - Motor Heurístico**: Engine de análise comportamental e detecção de anomalias
- **Módulo 3 - Web Dashboard**: Interface gráfica moderna para visualização em tempo real
- **Módulo 4 - Integração Avançada**: Conectores SIEM, APIs REST e threat intelligence

## 🛠️ Requisitos do Sistema

- **Windows 10.0.17763.0 ou posterior**
- **.NET 9 Runtime**
- **Privilégios de Administrador** (obrigatório)
- **Sysmon instalado e configurado**
- **SQLite** (incluído via Microsoft.Data.Sqlite)

## 🚀 Instalação Rápida

### 1. Instalar Sysmon
```bash
# Baixe o Sysmon do Microsoft Sysinternals
# Execute como Administrador:
sysmon -accepteula -i
```

### 2. Configurar Sysmon (Recomendado)
```bash
# Para análise completa, use nossa configuração otimizada:
sysmon -c sysmon-config-completa.xml
```

### 3. Compilar o Projeto
```bash
git clone https://github.com/seu-usuario/NavShieldTracer.git
cd NavShieldTracer
git checkout v1.0.0-Foundation  # Versão estável atual
dotnet build NavShieldTracer.sln
```

## 📖 Como Usar

### Execução Manual
```bash
# Execute como ADMINISTRADOR
dotnet run --project NavShieldTracer/NavShieldTracer.csproj

# Quando solicitado, digite o nome do executável (ex: "notepad")
# Pressione Enter para finalizar o monitoramento
```

### Teste Automatizado
```bash
# Execute o script de teste automatizado
executar_teste.bat

# Ou use o script PowerShell
.\Executar-TesteAtomico.ps1
```

## 📊 Estrutura de Dados

### Base de Dados SQLite
Os eventos são armazenados em `Logs/navshieldtracer.sqlite` com:

```sql
-- Tabela de Sessões
CREATE TABLE sessions (
    id INTEGER PRIMARY KEY,
    started_at TEXT,
    target_process TEXT,
    root_pid INTEGER,
    host TEXT,
    notes TEXT
);

-- Tabela de Eventos (schema normalizado)
CREATE TABLE events (
    id INTEGER PRIMARY KEY,
    session_id INTEGER,
    event_id INTEGER,
    process_id INTEGER,
    image TEXT,
    command_line TEXT,
    src_ip TEXT, dst_ip TEXT,
    dns_query TEXT,
    target_filename TEXT,
    raw_json TEXT  -- JSON completo para troubleshooting
);
```

### Consultas Úteis
```sql
-- Top 10 processos por eventos
SELECT image, COUNT(*) as eventos 
FROM events GROUP BY image ORDER BY eventos DESC LIMIT 10;

-- Conexões de rede por sessão
SELECT dst_ip, dst_port, COUNT(*) as conexoes
FROM events WHERE event_id = 3 GROUP BY dst_ip, dst_port;
```

## 🧪 Software de Teste

O projeto inclui um **TesteSoftware** modular que integra com **Red Canary Atomic Red Team** para simulação de comportamentos adversariais:

### **Características do TesteSoftware**
- **🔄 Execução Modular**: Seleção individual ou sequencial de testes disponíveis
- **🎯 Integração Red Canary**: Utiliza testes padronizados da comunidade de segurança
- **📊 Validação Comportamental**: Simula TTPs (Tactics, Techniques, Procedures) reais
- **⚙️ Configurável**: Permite ajuste de parâmetros e cenários de teste

### **Modo de Operação**
1. **Detecção Automática**: Identifica testes Red Canary instalados no sistema
2. **Seleção Interativa**: Interface para escolha de testes específicos ou execução completa
3. **Execução Controlada**: Ambiente isolado com logging detalhado
4. **Validação de Captura**: Verifica se o NavShieldTracer detectou corretamente os eventos

### **Testes Suportados** (em desenvolvimento)

```

**📝 Nota**: O TesteSoftware está em **desenvolvimento ativo** e será aperfeiçoado continuamente com novos testes e funcionalidades de integração com Red Canary Atomic Red Team.


### Arquitetura do Sistema
O NavShieldTracer possui arquitetura modular em camadas:

**Camada de Captura:**
- SysmonEventMonitor - Captura eventos em tempo real
- ProcessActivityTracker - Filtragem inteligente por árvore de processos
- SqliteEventStore - Persistência estruturada

**Camada de Análise (Futuro):**
- Motor Heurístico - Análise comportamental
- Detecção de Anomalias - Risk assessment
- Alertas em Tempo Real - Threat intelligence

**Camada de Apresentação (Futuro):**
- Web Dashboard - Interface gráfica moderna
- Timeline Interativa - Visualização temporal
- Relatórios Automatizados - Export capabilities

## 📚 Documentação

- [`APRESENTACAO_TCC.md`](APRESENTACAO_TCC.md) - Apresentação completa do projeto
- [`APRESENTACAO_TCC.tex`](APRESENTACAO_TCC.tex) - Versão LaTeX para apresentação

## 🛡️ Uso Responsável

**IMPORTANTE**: Esta ferramenta é projetada exclusivamente para:
- ✅ Análise de segurança defensiva
- ✅ Investigação forense
- ✅ Análise de malware em sandbox
- ✅ Auditoria de atividade de software

---

## 🏗️ **Arquitetura Técnica**

```
┌─────────────────────────────────────────────────────────────┐
│                    Camada de Apresentação                   │
│              🌐 Web Dashboard (Futuro)                     │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────┐
│                    Camada de Análise                        │
│  🧠 Motor Heurístico → 📈 Anomalias → ⚡ Alertas (Futuro)  │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────┐
│                    Camada de Captura                        │
│   🔍 SysmonMonitor → 📊 ProcessTracker → 💾 SqliteStore    │
└─────────────────────┬───────────────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────────────┐
│                    Camada de Dados                          │
│        📋 ModelosEventos ↔ 💾 SQLite Database               │
└─────────────────────────────────────────────────────────────┘
```

**⚠️ Aviso**: Execute sempre como Administrador e em ambiente controlado. A base de dados SQLite cresce conforme a atividade do sistema monitorado.