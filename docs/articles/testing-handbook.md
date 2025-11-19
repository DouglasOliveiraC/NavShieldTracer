# Manual de Testes

Este manual consolida o fluxo oficial utilizado para validar novas execuções do NavShieldTracer e catalogar técnicas MITRE ATT&CK. Ele reflete exatamente o comportamento presente no código (views de catálogo + módulos heurísticos) e evita discrepâncias entre laboratório e documentação.

## Preparação
1. Garanta que o repositório **Atomic Red Team** ou o módulo `Invoke-AtomicRedTeam` esteja instalado (mesmos caminhos usados pelo projeto `TesteSoftware`).
2. Valide o ambiente com `dotnet build NavShieldTracer.sln` e execute o diagnóstico do `NavShieldTracer` para confirmar privilégios, Sysmon e acesso ao SQLite.
3. Se possível, tire um snapshot da VM antes de rodar técnicas destrutivas.

## Fluxo padrão de catalogação
1. **Terminal 1 (NavShieldTracer)**  
   ```powershell
   dotnet run --project NavShieldTracer/NavShieldTracer.csproj
   ```  
   Escolha *Catalogar novo teste atômico* em `CatalogView` e preencha número, nome e descrição.
2. **Terminal 2 (TesteSoftware)**  
   ```powershell
   dotnet run --project TesteSoftware/TesteSoftware.csproj
   ```  
   Localize o teste (ex.: `T1055`) e execute a variação desejada.
3. Retorne ao **Terminal 1**, acompanhe os contadores no MonitorView e pressione `ENTER` ao final. O resumo mostrará total de eventos, técnicas identificadas e qualquer alerta heurístico emitido.

## Organização dos resultados
- Cada execução cria uma linha em `atomic_tests` (SQLite) e um arquivo JSON específico em `Logs/`.
- Utilize o menu *Tests* do console para revisar execuções anteriores e cruzar os dados com heurísticas.
- Documente manualmente divergências (por exemplo, eventos esperados que não foram capturados) para alimentar futuras correções.

## Considerações de segurança
- Execute testes somente em ambientes controlados.
- Técnicas como `T1055` ou `T1003` podem acionar antivírus/EDR; realize whitelist temporária ou use máquinas isoladas.
- Após bater a meta de testes, restaure o snapshot da VM para evitar resíduos de configuração.

## Automação auxiliar
Scripts presentes na raiz do repositório:
- `executar_teste.bat`: percorre uma lista fixa de técnicas e registra logs.
- `Executar-TesteAtomico.ps1`: permite parametrizar técnica, variação e argumentos extras (útil em pipelines PowerShell).

## Checklist de validade
- [ ] Diagnóstico inicial confirmou Sysmon e permissões elevadas.  
- [ ] `TesteSoftware` executou o cenário sem erros.  
- [ ] Eventos esperados estão presentes na tabela `events`.  
- [ ] Export JSON contém os campos críticos (`Image`, `CommandLine`, `UtcTime`, `ParentImage`).  
- [ ] Resumo exibido pelo console mostra número de eventos e técnica principal.  
- [ ] Caso exista alerta heurístico, o motivo foi registrado no relatório.

Arquive este checklist juntamente com os arquivos exportados. Ele garante rastreabilidade e facilita auditorias sobre o processo de catalogação.
