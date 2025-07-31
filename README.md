# NavShieldTracer

[![Version](https://img.shields.io/badge/Version-v1.0.0--Foundation-blue?style=flat-square)](https://github.com/seu-usuario/NavShieldTracer/releases)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

**NavShieldTracer** é uma ferramenta avançada de monitoramento de atividade de processos para Windows, projetada para análise de segurança defensiva e investigação forense do comportamento de software.

> **🎯 Versão Atual: v1.0.0-Foundation**  
> Esta é a primeira versão estável focada em **captura e catalogação precisa** de eventos do sistema. O core de monitoramento está 100% funcional com classificação automática de eventos e logs estruturados.

## 📋 Visão Geral

NavShieldTracer utiliza o **Sysmon (System Monitor)** para capturar e registrar atividades detalhadas do sistema de processos alvo, fornecendo visibilidade completa sobre:

- 🔄 Criação e encerramento de processos
- 🌐 Conexões de rede e consultas DNS
- 📁 Operações de arquivo (criação, modificação, exclusão)
- 🔐 Acessos ao registro do Windows
- 🧵 Criação de threads remotas
- 📚 Carregamento de DLLs e drivers
- 🔗 Pipes nomeados e streams NTFS

## 🚀 Estado Atual - v1.0.0-Foundation

### ✅ **Funcionalidades Implementadas**

**Core de Monitoramento:**
- ✅ **Captura de 18+ tipos de eventos** do Sysmon (Event IDs 1-26)
- ✅ **Classificação automática** de eventos por tipo em pastas organizadas
- ✅ **Rastreamento de árvore de processos** pai-filho com filtragem inteligente
- ✅ **Logs estruturados em JSON** com metadados completos
- ✅ **Diagnóstico automático** da configuração do Sysmon

**Eventos Validados:**
- ✅ **Network Connections** (Event ID 3) - Conexões TCP/UDP com hostnames
- ✅ **DNS Queries** (Event ID 22) - Consultas de resolução de nomes  
- ✅ **Process Creation** (Event ID 1) - Criação de processos com linha de comando
- ✅ **Process Termination** (Event ID 5) - Encerramento de processos

**Infraestrutura:**
- ✅ **TesteSoftware** - Suite de testes que simula 5 comportamentos suspeitos
- ✅ **Configuração Sysmon otimizada** - XML para captura máxima de eventos
- ✅ **Script de automação** - Execução facilitada de testes
- ✅ **Documentação completa** - Guias técnicos e de uso

### 🔄 **Em Progresso** 
- **Eventos adicionais** dependem de configuração Sysmon específica:
  - File Operations (Event IDs 2, 11, 23)
  - Registry Access (Event IDs 12-14)  
  - Advanced Process Events (Event IDs 6-10)

### 🎯 **Próximas Versões**
- **v1.1-Enhanced**: Heurísticas de análise comportamental
- **v1.2-Analytics**: Dashboard e relatórios automatizados
- **v2.0-Intelligence**: Machine learning para detecção de anomalias

## 🛠️ Requisitos do Sistema

- **Windows 10.0.17763.0 ou posterior**
- **.NET 9 Runtime**
- **Privilégios de Administrador** (obrigatório)
- **Sysmon instalado e configurado**

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
```

## 📊 Estrutura de Logs

Os logs são organizados em `Logs/{timestamp}_{processo}_{pid}/`:

```
Logs/20250731_160016_teste_46580/
├── metadata_sessao.json           # Informações da sessão
├── resumo_monitoramento.json      # Resumo dos processos
├── estatisticas_eventos.json      # Contagem de eventos
├── ProcessosCriados/              # Event ID 1
├── ConexoesRede/                  # Event ID 3
├── ArquivosCriados/              # Event ID 11
├── ConsultasDns/                 # Event ID 22
├── AcessosRegistro/              # Event IDs 12-14
└── [outros tipos de evento...]
```

## 🧪 Software de Teste

O projeto inclui um **TesteSoftware** que simula comportamentos típicos para validação:

1. **Fase Inerte**: 30 segundos de espera
2. **Atividades de Teste**:
   - Criação de arquivo na área de trabalho
   - Operações de registro
   - Conexão HTTP externa
   - Criação de processo filho (Notepad)
   - Operações suspeitas (modificação de timestamps)

## 🔧 Configuração Avançada

### Sysmon Personalizado
Edite `sysmon-config-completa.xml` para ajustar quais eventos capturar:

```xml
<!-- Exemplo: Capturar apenas arquivos .exe -->
<FileCreate onmatch="include">
    <TargetFilename condition="end with">.exe</TargetFilename>
</FileCreate>
```

### Diagnóstico Automático
O NavShieldTracer inclui diagnóstico automático que:
- Analisa configuração atual do Sysmon
- Identifica Event IDs disponíveis
- Sugere melhorias na configuração

## 📚 Documentação

- [`TESTE_GUIA.md`](TESTE_GUIA.md) - Guia completo de teste
- [`MELHORIAS_SYSMON.md`](MELHORIAS_SYSMON.md) - Melhorias implementadas
- [`docs/`](docs/) - Documentação técnica detalhada

## 🛡️ Uso Responsável

**IMPORTANTE**: Esta ferramenta é projetada exclusivamente para:
- ✅ Análise de segurança defensiva
- ✅ Investigação forense
- ✅ Análise de malware em sandbox
- ✅ Auditoria de atividade de software

**NÃO use para**:
- ❌ Monitoramento não autorizado
- ❌ Violação de privacidade
- ❌ Atividades maliciosas

## 🤝 Contribuindo

1. Fork o projeto
2. Crie uma branch para sua feature (`git checkout -b feature/nova-funcionalidade`)
3. Commit suas mudanças (`git commit -m 'Adiciona nova funcionalidade'`)
4. Push para a branch (`git push origin feature/nova-funcionalidade`)
5. Abra um Pull Request

## 📝 Licença

Este projeto está licenciado sob a Licença MIT - veja o arquivo [LICENSE](LICENSE) para detalhes.

## 🙏 Agradecimentos

- [Microsoft Sysinternals](https://docs.microsoft.com/sysinternals/) pelo Sysmon
- Comunidade de segurança cibernética por recursos e documentação
- Pesquisadores de segurança por padrões e boas práticas

---

**⚠️ Aviso**: Execute sempre como Administrador e em ambiente controlado. Monitore o espaço em disco, pois a ferramenta pode gerar grandes volumes de logs.