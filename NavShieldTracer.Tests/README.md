# NavShieldTracer - Suite de Testes

Esta pasta concentra os testes automatizados do NavShieldTracer. O objetivo principal e demonstrar funcionalidade, robustez e capacidade de escala do armazenamento SQLite e dos modulos heuristicos.

> Nota: Os arquivos utilizam apenas caracteres ASCII para garantir portabilidade e evitar problemas de codificacao na coleta de relatorios.

## Visao Geral

| Categoria            | Conteudo principal                                                         | Ativacao padrao |
|---------------------|----------------------------------------------------------------------------|-----------------|
| FunctionalTests     | Cenarios end-to-end cobrindo catalogacao, exportacao e consistencia.       | Sempre          |
| Heuristics          | Normalizacao de assinaturas, monitor de ameacas, rastreamento de processo. | Sempre          |
| Storage             | Casos negativos para o `SqliteEventStore`.                                 | Sempre          |
| DatabaseTests       | Benchmarks de insercao e consultas (marcados como performance).            | Opcional        |
| StressTests         | Ensaios de carga prolongada (marcados como performance).                   | Opcional        |

Testes de performance e estresse sao agrupados pela trait `Category=Performance`. Eles ficam desativados por padrao e sao executados apenas quando a variavel `RUN_PERFORMANCE_TESTS=1` estiver presente.

## Como Executar

### Pre-requisitos
* .NET 9.0 SDK
* Windows 10 build 17763 ou superior
* 4 GB de RAM livre (8 GB recomendados para cenarios de performance)
* 2 GB de espaco livre em disco para bancos temporarios

### Execucao completa (testes funcionais + heuristicas + storage)

```bash
dotnet test NavShieldTracer.Tests/NavShieldTracer.Tests.csproj
```

### Habilitar cenarios de performance e estresse

```bash
set RUN_PERFORMANCE_TESTS=1
dotnet test NavShieldTracer.Tests/NavShieldTracer.Tests.csproj
```

### Filtrar por namespace

```bash
dotnet test --filter "FullyQualifiedName~Heuristics"
dotnet test --filter "FullyQualifiedName~Monitoring"
dotnet test --filter "Category=Performance"
```

## Estrutura da suite

```
NavShieldTracer.Tests/
|-- DatabaseTests/            # Benchmarks de insercao e consulta (PerformanceFact)
|-- FunctionalTests/          # Exercita fluxo completo de catalogacao
|-- Heuristics/               # Normalizacao e monitoramento heuristico
|-- Monitoring/               # Valida rastreamento de processos
|-- Storage/                  # Casos negativos do repositorio SQLite
|-- StressTests/              # Cargas de longo prazo (PerformanceFact)
|-- Utils/                    # Seeder, simulador e formatter de relatorios
```

Ferramentas auxiliares relevantes:

* `EventSimulator` - gera eventos Sysmon deterministicos;
* `DatabaseSeeder` - monta sessoes e testes com seeds fixos;
* `ReportFormatter` - imprime saidas legiveis em ASCII;
* `PerformanceFactAttribute` - habilita traits e skip condicional.

## Relatorios gerados nos testes

Os testes utilizam o `ReportFormatter` para padronizar a saida no console:

```
=== Insercao 10k Eventos ===
Eventos inseridos       : 10 000
Tempo total             : 4.12s
Taxa                    : 2 425.41 eventos/s
Tempo medio             : 0.41 ms por evento
Tamanho arquivo         : 16.38 MB
```

### Recomendacoes para apresentacao
1. Execute os testes funcionais para comprovar integridade.
2. Rode o conjunto `Category=Performance` em maquina dedicada, capturando numeros de throughput.
3. Registre as metricas geradas diretamente do console (as tabelas ASCII ja ficam prontas para slides).
4. Utilize `dotnet test -l "trx;LogFileName=results.trx"` quando precisar anexar logs formais.

## Limpeza

Os testes removem automaticamente os bancos temporarios criados em `%TEMP%`. Caso o processo seja interrompido, basta apagar manualmente os arquivos com prefixo `test_*.sqlite`.
