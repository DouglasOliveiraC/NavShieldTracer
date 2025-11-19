# Documentação do NavShieldTracer

NavShieldTracer é um coletor de telemetria defensiva para Windows, implementado integralmente em .NET 9. O objetivo desta documentação é contextualizar o funcionamento do projeto e servir como guia operacional para equipes que utilizam o console, o módulo de heurísticas e o repositório SQLite.

## Público-alvo
- Analistas SOC/DFIR que precisam reconstruir árvores de processos e justificar alertas.
- Pesquisadores que catalogam técnicas MITRE ATT&CK com base em execuções reais (Atomic Red Team).
- Equipes acadêmicas que acompanham a evolução do projeto como parte do TCC e precisam de referência reproduzível.

## Como começar
1. Siga o passo a passo em [Getting Started](~/articles/getting-started.md) para instalar Sysmon, restaurar as ferramentas e compilar a solução.
2. Execute `dotnet build NavShieldTracer.sln` para garantir que todas as dependências locais foram restauradas.
3. Gere e sirva esta documentação com:
   ```powershell
   dotnet tool restore
   dotnet tool run docfx docs/docfx.json
   dotnet tool run docfx serve docs/_site
   ```
   O site final fica em `docs/_site` e pode ser publicado em qualquer host estático.

## Mapa da documentação
| Seção | Conteúdo |
| --- | --- |
| [Getting Started](~/articles/getting-started.md) | Requisitos, preparação do ambiente e comandos base de execução |
| [Overview](~/articles/overview.md) | Contexto geral do projeto, papéis dos subprojetos e fluxo operacional |
| [Architecture](~/articles/architecture.md) | Camadas reais (UI, Serviços, Armazenamento) e componentes principais |
| [Operations Guide](~/articles/operations.md) | Procedimentos diários de monitoramento, encerramento e exportação |
| [Data Model](~/articles/data-model.md) | Estrutura do SQLite (`sessions`, `events`, `atomic_tests`) e consultas úteis |
| [Testing Handbook](~/articles/testing-handbook.md) | Rotina oficial para testar e catalogar execuções Atomic Red Team |
| [Reference Materials](~/articles/materials/index.md) | Materiais do TCC, relatórios de normalização e apresentações |
| [API Reference](~/api/toc.yml) | Referência DocFX gerada automaticamente a partir dos assemblies |

A documentação evolui junto com o código. Sempre regenere o site após ajustar comentários XML ou arquivos Markdown para garantir consistência com a versão em uso.

