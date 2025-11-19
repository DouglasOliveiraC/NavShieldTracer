# Guia de Operações

Esta seção descreve as rotinas mais comuns de quem utiliza o console interativo do NavShieldTracer. As instruções refletem exatamente o fluxo implementado nas views (`OverviewView`, `MonitorView`, `CatalogView`, `TestsView` e `ManageView`).

## Preparar Ambiente
- Limpe a pasta `Logs/` entre sessoes para evitar confusao de datasets.
- Verifique se o serviço Sysmon esta ativo (`Get-Service Sysmon64`).
- Execute o coletor sempre em console elevado.

## Painel e status
- A **página Overview** mostra saúde do Sysmon, estatísticas agregadas e indicadores das últimas sessões.
- O cabeçalho superior é renderizado por `OverviewView` e atualizado automaticamente de acordo com `NavShieldAppService.GetDashboardSnapshot()`.

## Monitorar processo existente
1. No menu principal escolha *Monitorar processo em execução*.
2. Informe o nome do executável (ex.: `notepad.exe`) exatamente como aparece nos eventos Sysmon.
3. Acompanhe o painel em tempo real (coluna direita do MonitorView) e monitore contadores de eventos e uso de memória.

## Aguardar novo processo
1. Escolha *Aguardar novo processo*.
2. Inicie o binário alvo em outro terminal ou estação de testes.
3. Assim que o processo for detectado, confirme para iniciar a sessão; o Tracker limpa o estado anterior automaticamente.

## Finalizar sessão e exportar dados
- Pressione `ENTER` quando concluir o exercício ou utilize o atalho exibido no rodapé do MonitorView.
- Verifique a pasta `Logs/`:
  - `navshieldtracer.sqlite` com tabelas `sessions`, `events`, `atomic_tests`, `atomic_events`.
  - Exportações JSON (`logs_teste_<timestamp>.json`) para análises adicionais.
  - Logs de heurística contendo alertas emitidos pelo `BackgroundThreatMonitor`.

## Troubleshooting
| Sintoma | Causa Provavel | Acao |
| --- | --- | --- |
| Aplicacao informa que Sysmon nao esta disponivel | Log nao habilitado ou console sem privilegios | Executar em modo administrador e validar instalacao do Sysmon |
| Ausencia de eventos na base | Processo alvo terminou antes da confirmacao | Reiniciar sessao certificando-se de confirmar quando o PID aparecer |
| Excessos de eventos genericos | Configuracao do Sysmon muito permissiva | Ajustar `sysmon-config-completa.xml` ou aplicar filtros adicionais |

## Gerenciar catálogos
- Utilize o ManageView para alterar metadados (número, nome, notas) de um teste já catalogado.
- A aba TestsView exibe históricos de sessões, alertas e permite exportar resumos individuais.

## Automatizar execução
Use o script `executar_teste.bat` ou `Executar-TesteAtomico.ps1` quando precisar repetir cenários definidos no Atomic Red Team. Eles apenas invocam o `TesteSoftware`; a captura continua sendo realizada pelo NavShieldTracer.

Manter uma rotina disciplinada ao arquivar os resultados facilita a comparação entre sessões e evita perda de evidências. Utilize o checklist e registre qualquer alerta emitido para manter rastreabilidade.
