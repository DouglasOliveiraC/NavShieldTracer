# NavShieldTracer

[![Version](https://img.shields.io/badge/Version-v1.0.0.1-blue?style=flat-square)](https://github.com/DouglasOliveiraC/NavShieldTracer/releases)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/9.0)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)

![NavShield Interface](https://raw.githubusercontent.com/DouglasOliveiraC/NavShieldTracer/master/.github/assets/navshield-interface.png)

NavShieldTracer e uma ferramenta de monitoramento de processos para Windows que registra eventos do Sysmon em uma base SQLite para apoiar investigacao forense e operacoes de defesa. A versao 1.0 consolidou todas as entregas planejadas, incluindo coleta confiavel, persistencia estruturada e suite de testes. O modulo de analise comportamental com tecnicas de IA permanece planejado para a proxima iteracao.

## Visao geral

- Captura eventos do Sysmon (IDs 1-26) com rastreamento de arvore de processos e enriquecimento de conexoes de rede e DNS.
- Persiste os eventos em `Logs/navshieldtracer.sqlite`, mantendo dados normalizados e o JSON bruto para auditoria.
- Inclui shell dedicado para automatizar execucoes do Atomic Red Team e validar deteccoes.
- Disponibiliza diagnostico inicial que verifica permissao administrativa, instalacao do Sysmon e estado da base.

## Estado do projeto

- Entregas concluidas: captura de eventos, normalizacao e armazenamento, automacao de testes com Atomic Red Team, testes de unidade/integracao/estresse e documentacao tecnica.
- Entrega futura: modulo heuristico com analise de IA e painel web permanecem fora da versao 1.0 e serao tratados em releases posteriores.

## Componentes principais

- `NavShieldTracer`: servico de monitoramento que consome eventos do Sysmon e grava no SQLite.
- `TesteSoftware`: shell interativo que encapsula Invoke-AtomicRedTeam para facilitar testes adversariais.
- Pastas `NavShieldTracer.Tests*`: suites de testes automatizados (funcionais, performance, confiabilidade e relatorios).

## Requisitos de sistema

- Windows 10 build ou superior.
- .NET 9 SDK para compilar e executar.
- Permissao administrativa para instalar Sysmon e capturar eventos.
- Sysmon instalado e configurado com o arquivo `sysmon-config-completa.xml`.
- PowerShell 5.1 ou 7.x com politica de execucao capaz de importar modulos locais.
- SQLite ja incluso via Microsoft.Data.Sqlite (nao requer instalacao externa).

## Preparacao do ambiente

1. **Instalar Sysmon**
   ```powershell
   sysmon64.exe -accepteula -i sysmon-config-completa.xml
   ```
   Caso ja tenha Sysmon instalado, atualize a configuracao com `sysmon64.exe -c sysmon-config-completa.xml`.

2. **Instalar o Atomic Red Team e o modulo Invoke-AtomicRedTeam**
   - Crie a estrutura base:
     ```powershell
     New-Item -ItemType Directory -Force -Path C:\AtomicRedTeam | Out-Null
     git clone https://github.com/redcanaryco/atomic-red-team.git C:\AtomicRedTeam\atomic-red-team
     git clone https://github.com/redcanaryco/invoke-atomicredteam.git C:\AtomicRedTeam\invoke-atomicredteam
     ```
   - Alternativa PowerShell Gallery:
     ```powershell
     Install-Module -Name InvokeAtomicRedTeam -Scope CurrentUser
     ```
   O `TesteSoftware` procura automaticamente por `Invoke-AtomicRedTeam.psd1` nas pastas acima ou em qualquer diretorio listado no `PSModulePath`.

3. **Instalar o .NET 9 SDK**
   - Download em https://dotnet.microsoft.com/download/dotnet/9.0.
   - Confirme com `dotnet --version`.

4. **Clonar o NavShieldTracer**
   ```powershell
   git clone https://github.com/DouglasOliveiraC/NavShieldTracer.git
   ```

## Instalacao e compilacao

```powershell
cd NavShieldTracer
dotnet restore NavShieldTracer.sln
dotnet build NavShieldTracer.sln -c Release
```

A build gera os binarios em `NavShieldTracer\bin\Release\net9.0`.

## Execucao

### Monitoramento interativo

```powershell
dotnet run --project NavShieldTracer/NavShieldTracer.csproj
```

1. Execute o comando em um prompt elevado (Run as Administrator).
2. Informe o nome do executavel a ser acompanhado quando solicitado (exemplo `powershell.exe` ou `notepad.exe`).
3. Reproduza o comportamento que deseja observar.
4. Pressione Enter para encerrar a sessao; os eventos ficam registrados no banco SQLite e nos logs em `Logs\`.

### Shell de testes com Atomic Red Team

```powershell
dotnet run --project TesteSoftware/TesteSoftware.csproj
```

- O shell carrega o modulo Invoke-AtomicRedTeam detectado no ambiente.
- Utilize comandos padrao como `Get-AtomicTechnique`, `Invoke-AtomicTest T1055 -TestNumbers 1` e `Update-AtomicRedTeam`.
- Para monitorar uma execucao atomica, mantenha o NavShieldTracer acompanhando `powershell.exe` ou o processo alvo e acione o teste a partir do shell.

## Estrutura de saida

- **Banco de dados**: `Logs/navshieldtracer.sqlite` com tabelas `sessions` e `events`.
- **Logs auxiliares**: arquivos `*.log` em `Logs/` com diagnosticos e mensagens de execucao.
- **Relatorios de testes**: pastas `NavShieldTracer.TestsReports` e `NavShieldTracer.TestsResourceMonitoringTests` armazenam resultados e metricas.

## Suite de testes

1. **Testes padrao**
   ```powershell
   dotnet test NavShieldTracer.sln
   ```

2. **Testes de performance**
   - Habilite com a variavel de ambiente `RUN_PERFORMANCE_TESTS=1`.
     ```powershell
     # Apenas para a sessao atual
     $env:RUN_PERFORMANCE_TESTS = "1"

     # Persistente para o usuario
     setx RUN_PERFORMANCE_TESTS 1
     ```
   - Execute apenas a categoria de performance, se desejar:
     ```powershell
     dotnet test NavShieldTracer.sln --filter Category=Performance
     ```
   - Para desativar, defina `RUN_PERFORMANCE_TESTS=0` ou remova a variavel.

3. **Dependencias dos testes**
   - Sysmon deve estar em execucao.
   - Os caminhos configurados em `appsettings.Test*.json` apontam para o banco em `Logs/`.

## Documentacao navegavel

Os comentarios XML do codigo alimentam um site estatico DocFX dentro da pasta `docs`.

1. **Restaurar a ferramenta DocFX (apenas uma vez por maquina)**
   ```powershell
   dotnet tool restore
   ```

2. **Gerar o site**
   ```powershell
   dotnet tool run docfx docs/docfx.json
   ```
   O resultado navegavel e publicado em `docs/_site`. Para revisar localmente execute `dotnet tool run docfx serve docs/_site` e abra `http://localhost:8080`.

> Observacao: alguns artigos em `docs/articles` referenciam arquivos externos que ainda nao estao presentes no repositorio. O DocFX mostrara avisos enquanto esses includes nao forem adicionados.

## Publicar no GitHub Pages

1. No reposit�rio do GitHub, acesse *Settings ▸ Pages* e escolha *GitHub Actions* como fonte.
2. Certifique-se de que o workflow `Publish Docs` esteja habilitado (arquivo `.github/workflows/docs.yml`).
3. A cada push na branch `master` que altere `docs/` ou o c�digo, o workflow vai:
   - Restaurar as ferramentas (`dotnet tool restore`)
   - Rodar o DocFX (`dotnet tool run docfx docs/docfx.json`)
   - Publicar o conte�do de `docs/_site` na branch `gh-pages`.
4. O link final do site aparece na aba *Actions ▸ Publish Docs ▸ Deploy to GitHub Pages* ou em *Settings ▸ Pages*.

## Roadmap pos-versao 1.0

- Motor heuristico com correlacao de eventos e pontuacao de risco (IA/ML).

## Licenca

Distribuido sob a licenca [MIT](LICENSE).
