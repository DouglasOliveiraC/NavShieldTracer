# Architecture

O NavShieldTracer opera em uma arquitetura 3-tier enxuta, toda contida na solucao .NET.

## Visao Geral das Camadas
- **Apresentacao (ConsoleApp/UI)**  
  Views Spectre.Console (`CatalogView`, `MonitorView`, `ManageView`, `OverviewView`, `TestsView`) coordenadas por `ViewContext`. A interacao do operador ocorre exclusivamente aqui.
- **Servicos e Negocio (ConsoleApp/Services + Modules)**  
  `NavShieldAppService` expone operacoes de sessao e diagnostico consumindo modulos de monitoramento (`BackgroundThreatMonitor`, `SessionThreatClassifier`, `SimilarityEngine`) e utilitarios (`ProcessActivityTracker`, `MonitoringSession`).
- **Armazenamento (Storage)**  
  `SqliteEventStore` encapsula acesso ao banco SQLite, fornecendo APIs para sessoes, eventos normalizados e catalogacao de testes atomicos.

## Principais Componentes
| Componente | Camada | Responsabilidade |
| --- | --- | --- |
| `NavShieldAppService` | Servicos | Orquestra sessoes, dispara monitoramento heuristico e agrega snapshots para as views. |
| `BackgroundThreatMonitor` | Servicos | Executa correlacao de eventos em background, mantendo nivel de ameaca e alertas. |
| `SimilarityEngine` / `SessionThreatClassifier` | Servicos | Normalizam as metricas das sessoes e atribuem tarja de severidade. |
| `SqliteEventStore` | Armazenamento | Persiste sessoes/alertas/testes e entrega estatisticas para a camada superior. |
| `ViewContext` + Views | Apresentacao | Renderizam paineis, recebem input e sincronizam com o AppService. |

## Fluxo de Dados
1. Usuario inicia sessao via view (ex: MonitorView) ➜ `NavShieldAppService.StartMonitorSession`.
2. `BackgroundThreatMonitor` coleta eventos via `SqliteEventStore.ObterEventosDaSessaoAPartirDe` e calcula similaridade.
3. `SessionThreatClassifier` define o nivel de ameaca e dispara alertas em caso de escalonamento.
4. `NavShieldAppService.GetActiveSessionSnapshot` consolida estatisticas + heuristicas para exibicao.
5. Ao encerrar, `SqliteEventStore.CompleteSession` grava o resultado e os relatorios permanecem em `Logs/`.

## Dependencias Externas (somente codigo gerenciado)
- **Microsoft.Diagnostics.Tracing.TraceEvent** / **System.Diagnostics.EventLog**: leitura do canal `Microsoft-Windows-Sysmon/Operational`.
- **Microsoft.Data.Sqlite**: persistencia local.
- **Spectre.Console**: construcao da interface textual responsiva.

Essa estrutura de tres camadas minimiza acoplamento: as views nunca acessam o banco diretamente e evolucoes no motor heuristico ou na camada de persistencia nao exigem mudancas na interface.
