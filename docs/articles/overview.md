# Visão Geral do Projeto

NavShieldTracer é composto por dois executáveis:
- `NavShieldTracer` (ConsoleApp): interface Spectre.Console que conduz sessões de monitoramento, aplica heurísticas e mantém o painel em tempo real.
- `TesteSoftware`: shell auxiliar focado em acionar cenários do Atomic Red Team para popular o catálogo e validar regras.

Ambos compartilham a mesma base de código e o armazenamento SQLite em `Logs/navshieldtracer.sqlite`.

## Como o fluxo opera

1. **Diagnóstico automático** – Ao iniciar o console principal, `NavShieldAppService` verifica privilégios elevados, disponibilidade do Sysmon e acesso ao banco.
2. **Sessões de monitoramento** – O operador escolhe monitorar um processo existente ou aguardar um novo binário. `SysmonEventMonitor` e `ProcessActivityTracker` filtram apenas a árvore relevante.
3. **Persistência e heurísticas** – `SqliteEventStore` grava eventos normais e, em paralelo, `BackgroundThreatMonitor` calcula similaridade com o catálogo para emitir alertas (`SessionThreatClassifier`).
4. **Encerramento e exportação** – Ao finalizar a sessão, os dados ficam disponíveis no SQLite e em arquivos JSON/CSV dentro de `Logs/`.

## Papel de cada camada

- **Apresentação (ConsoleApp/UI/Views)**: `OverviewView`, `MonitorView`, `CatalogView`, `ManageView` e `TestsView` renderizam o painel interativo, consumindo snapshots fornecidos pelo serviço.
- **Serviços/Negócio (ConsoleApp/Services + Modules)**: `NavShieldAppService`, `BackgroundThreatMonitor`, `SimilarityEngine`, `ProcessActivityTracker` e os modelos de monitoramento encapsulam todas as regras de negócio.
- **Armazenamento (Storage)**: `SqliteEventStore` centraliza o acesso ao banco e expõe métodos de leitura para a UI e para o motor heurístico.

## Quando utilizar cada projeto

- Use **NavShieldTracer** sempre que precisar acompanhar um processo em tempo real ou classificar uma sessão.
- Use **TesteSoftware** para orquestrar execuções Atomic Red Team e alimentar o catálogo. Ele não escreve diretamente no banco; toda a persistência continua sob responsabilidade do serviço principal.

Essa separação em três camadas evita dependência cruzada entre interface e armazenamento e reflete exatamente a estrutura existente no repositório.
