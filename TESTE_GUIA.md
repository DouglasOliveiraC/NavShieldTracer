# Guia de Teste - NavShieldTracer

## Visão Geral

Este documento explica como usar o sistema de testes criado para validar o NavShieldTracer. O teste simula comportamentos suspeitos que podem ser detectados pelas heurísticas de análise dinâmica baseadas no documento do Ministério da Defesa.

## Estrutura do Teste

### Projeto TesteSoftware
- **Localização**: `TesteSoftware/TesteSoftware.csproj`
- **Tipo**: Aplicação console .NET 9 Windows
- **Finalidade**: Simular atividades suspeitas para análise dinâmica

### Comportamento do Teste

#### Fase 1: Período Inerte (30 segundos)
- O software aguarda 30 segundos sem realizar atividades
- Permite tempo para configurar o NavShieldTracer
- Exibe contador regressivo no console

#### Fase 2: Atividades de Teste
O software executa 5 tipos diferentes de atividades suspeitas:

1. **📝 Teste de Arquivo**
   - Cria arquivo `teste_navshield.txt` na área de trabalho
   - Conteúdo inclui informações do processo (PID, usuário, timestamp)
   - **Eventos gerados**: Event ID 11 (FileCreate)

2. **🔐 Teste de Registro**
   - Lê subchaves de `HKCU\Software`
   - Cria chave de teste `HKCU\Software\NavShieldTest`
   - Adiciona valores com timestamp e PID
   - **Eventos gerados**: Event IDs 12-14 (Registry access)

3. **🌐 Teste de Rede**
   - Faz requisição HTTP GET para `https://httpbin.org/ip`
   - Simula comunicação externa (comum em malware)
   - **Eventos gerados**: Event ID 3 (NetworkConnect), Event ID 22 (DnsQuery)

4. **👶 Teste de Processo Filho**
   - Inicia Notepad com o arquivo de teste criado
   - Aguarda 3 segundos
   - Fecha o Notepad automaticamente
   - **Eventos gerados**: Event ID 1 (ProcessCreate), Event ID 5 (ProcessTerminate)

5. **📁 Teste de Operações Suspeitas**
   - Cria diretório temporário `%TEMP%\NavShieldTest`
   - Cria múltiplos arquivos temporários
   - **Modifica timestamps** (comportamento típico de malware)
   - Remove arquivos e diretório
   - **Eventos gerados**: Event ID 11 (FileCreate), Event ID 2 (FileCreateTime), Event ID 23 (FileDelete)

## Como Executar o Teste

### Método 1: Script Automatizado

```bash
# Execute o script de automação
executar_teste.bat
```

O script:
1. Compila ambos os projetos
2. Fornece instruções passo-a-passo
3. Executa o TesteSoftware quando solicitado

### Método 2: Execução Manual

#### Passo 1: Compilar os Projetos
```bash
dotnet build NavShieldTracer.sln
```

#### Passo 2: Executar NavShieldTracer
```bash
# IMPORTANTE: Execute como ADMINISTRADOR
dotnet run --project NavShieldTracer/NavShieldTracer.csproj
```

#### Passo 3: Configurar Monitoramento
- Quando solicitado o nome do executável, digite: `teste`
- O NavShieldTracer vai aguardar o processo aparecer

#### Passo 4: Executar o Teste
```bash
# Em outro terminal (não precisa ser administrador)
dotnet run --project TesteSoftware/TesteSoftware.csproj
```

#### Passo 5: Observar Execução
- TesteSoftware mostra contador de 30 segundos
- NavShieldTracer detecta o processo e inicia monitoramento
- TesteSoftware executa as 5 atividades de teste
- Pressione Enter no NavShieldTracer para finalizar

## Resultados Esperados

### Console do NavShieldTracer
```
✅ Sysmon detectado. O monitoramento completo está pronto.

📊 Novo processo alvo detectado: 'teste.exe' (PID: XXXX).
   -> Novo processo filho detectado: 'notepad.exe' (PID: YYYY), filho de XXXX.
   -> Processo encerrado: 'notepad.exe' (PID: YYYY) - Duração: 00:00:03
```

### Logs Gerados
Local: `Desktop/NavShieldTracer_Logs/{timestamp}_teste_{pid}/`

Pastas criadas:
- `ProcessosCriados/` - Criação do TesteSoftware e Notepad
- `ConexoesRede/` - Conexão HTTP para httpbin.org
- `ConsultasDns/` - Resolução DNS do httpbin.org
- `ArquivosCriados/` - Arquivo na área de trabalho e arquivos temporários
- `TimestampsArquivosAlterados/` - Modificação de timestamps
- `ArquivosExcluidos/` - Remoção dos arquivos temporários
- `AcessosRegistro/` - Operações no registro do Windows
- `ProcessosEncerrados/` - Encerramento do Notepad

### Arquivos de Resultado
- `metadata_sessao.json` - Informações da sessão de monitoramento
- `resumo_monitoramento.json` - Resumo dos processos monitorados
- `estatisticas_eventos.json` - Contagem de eventos por tipo

## Validação do Teste

### Checklist de Eventos Capturados
- [ ] **Process Create**: teste.exe e Notepad detectados
- [ ] **Network Connection**: Conexão HTTPS capturada
- [ ] **DNS Query**: Consulta httpbin.org registrada
- [ ] **File Create**: Arquivo na área de trabalho e arquivos temporários
- [ ] **File Timestamp**: Modificação de timestamp detectada
- [ ] **File Delete**: Remoção de arquivos temporários
- [ ] **Registry Access**: Acesso e modificação de chaves
- [ ] **Process Terminate**: Encerramento do Notepad

### Análise dos Logs
1. **Árvore de Processos**: Verificar relação pai-filho teste.exe → Notepad
2. **Sequência Temporal**: Eventos devem seguir ordem lógica
3. **Detalhes Completos**: Cada evento deve ter timestamp, PID, usuário, etc.
4. **Filtragem**: Apenas eventos do teste.exe e filhos devem estar nos logs

## Próximos Passos

### Para Desenvolvimento de Heurísticas
Com os logs gerados, você pode:

1. **Analisar Padrões**: Identificar sequências típicas de atividades suspeitas
2. **Implementar Classificadores**: Criar regras baseadas no documento do Ministério da Defesa
3. **Definir Thresholds**: Estabelecer limites para detecção de comportamentos anômalos
4. **Validar Detecção**: Usar este teste como baseline para validar heurísticas

### Exemplo de Heurísticas Possíveis
- **Modificação de Timestamps**: Detectar alterações suspeitas em metadados de arquivo
- **Múltiplas Conexões Externas**: Identificar comunicação com servidores remotos
- **Criação Rápida de Processos**: Detectar spawning acelerado de processos filhos
- **Operações de Registro Sensíveis**: Monitorar chaves críticas do sistema

## Troubleshooting

### Problemas Comuns

**Erro: "Sysmon não está instalado"**
- Instale o Sysmon: `sysmon -i -accepteula`
- Execute como administrador

**NavShieldTracer não detecta o teste**
- Verifique se digitou "teste" exatamente
- Aguarde alguns segundos após iniciar o teste

**Poucos eventos capturados**
- Verifique se o Sysmon está configurado corretamente
- Execute ambos os programas como administrador

**Erro de compilação**
- Verifique se tem .NET 9 SDK instalado
- Execute `dotnet --version` para confirmar

### Logs de Debug
Para debug detalhado, modifique temporariamente o código para habilitar logs de erro nos blocos try-catch dos módulos do NavShieldTracer.

---

**Nota**: Este teste é projetado exclusivamente para fins de segurança defensiva e desenvolvimento de ferramentas de análise. Todos os comportamentos são controlados e reversíveis.