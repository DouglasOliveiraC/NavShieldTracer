# API Reference

Referência completa da API do NavShieldTracer, organizada por camadas arquiteturais.

## UI Layer
Componentes e views da interface de usuário.

- [Components](xref:NavShieldTracer.ConsoleApp.Components) - Componentes da aplicação
- [UI Core](xref:NavShieldTracer.ConsoleApp.UI) - Núcleo da UI
- [Views](xref:NavShieldTracer.ConsoleApp.UI.Views) - Views e apresentação

## Backend Layer
Lógica de negócio e processamento.

- [Services](xref:NavShieldTracer.ConsoleApp.Services) - Serviços da aplicação
- [Diagnostics](xref:NavShieldTracer.Modules.Diagnostics) - Diagnósticos do sistema
- [Heuristics Engine](xref:NavShieldTracer.Modules.Heuristics.Engine) - Motor de heurísticas
- [Normalization](xref:NavShieldTracer.Modules.Heuristics.Normalization) - Normalização de dados
- [Monitoring](xref:NavShieldTracer.Modules.Monitoring) - Monitoramento de processos

## Data Models
Modelos de dados e eventos.

- [Event & Domain Models](xref:NavShieldTracer.Modules.Models) - Modelos de eventos Sysmon e domínio

## Storage Layer
Persistência e armazenamento.

- [Storage & Persistence](xref:NavShieldTracer.Storage) - Camada de armazenamento
