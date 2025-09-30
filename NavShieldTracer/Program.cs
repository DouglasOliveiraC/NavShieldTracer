using NavShieldTracer.Modules;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using NavShieldTracer.Modules.Storage;
using System.Security.Principal;

/// <summary>
/// Ponto de entrada principal para o aplicativo NavShieldTracer.
/// Este programa monitora a atividade de software usando eventos do Sysmon.
/// </summary>
class Program
{
    /// <summary>
    /// O método principal do aplicativo.
    /// </summary>
    static async Task Main()
    {
        Console.WriteLine("NavShieldTracer - Monitoramento de Atividade de Software");
        Console.WriteLine("=======================================================\n");

        // --- ETAPA 1: INICIALIZAR O BANCO DE DADOS PRIMEIRO ---
        // Garante que o arquivo de log exista, mesmo que o resto falhe.
        SqliteEventStore store;
        try
        {
            store = new SqliteEventStore();
            Console.WriteLine($"✅ Banco de dados inicializado em: {store.DatabasePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ ERRO CRÍTICO: Não foi possível criar ou abrir o banco de dados.");
            Console.WriteLine($"   Motivo: {ex.Message}");
            Console.WriteLine("\nPressione Enter para sair.");
            Console.ReadLine();
            return;
        }

        // --- ETAPA 2: VERIFICAR PERMISSÕES E ACESSO AO SYSMON ---
        bool temAcessoAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        bool sysmonAcessivel = false;

        if (!temAcessoAdmin)
        {
            Console.WriteLine("⚠️ AVISO: O programa não está sendo executado como Administrador.");
            Console.WriteLine("   A captura de eventos do Sysmon requer privilégios elevados.");
        }
        else
        {
            try
            {
                // Tenta uma operação que exige privilégios para verificar o acesso.
                using (var reader = new EventLogReader(new EventLogQuery("Microsoft-Windows-Sysmon/Operational", PathType.LogName)))
                {
                    sysmonAcessivel = true;
                }
                Console.WriteLine("✅ Privilégios de Administrador detectados e log do Sysmon acessível.");
            }
            catch (EventLogNotFoundException)
            {
                Console.WriteLine("❌ ERRO: O log 'Microsoft-Windows-Sysmon/Operational' não foi encontrado.");
                Console.WriteLine("   Verifique se o Sysmon está instalado corretamente.");
            }
            catch (Exception ex) // Outras exceções, provavelmente de permissão mesmo com admin
            {
                Console.WriteLine($"❌ ERRO: Falha ao acessar o log do Sysmon, mesmo como Administrador.");
                Console.WriteLine($"   Motivo: {ex.Message}");
            }
        }

        if (!sysmonAcessivel)
        {
            Console.WriteLine("\nPressione Enter para sair.");
            Console.ReadLine();
            store.Dispose(); // Libera o recurso do banco de dados antes de sair
            return;
        }

        // --- ETAPA 3: MENU PRINCIPAL ---
        using (store)
        {
            bool continuar = true;
            while (continuar)
            {
                MostrarMenuPrincipal();
                ConsoleKeyInfo opcao;
                try
                {
                    opcao = Console.ReadKey(true);
                }
                catch (InvalidOperationException)
                {
                    // Console não está disponível, usar opção padrão para sair
                    opcao = new ConsoleKeyInfo('5', ConsoleKey.D5, false, false, false);
                }
                Console.WriteLine();

                switch (opcao.Key)
                {
                    case ConsoleKey.D1:
                    case ConsoleKey.NumPad1:
                        ExecutarMonitoramentoProcesso(store);
                        break;

                    case ConsoleKey.D2:
                    case ConsoleKey.NumPad2:
                        ExecutarCatalogacaoTeste(store);
                        break;

                    case ConsoleKey.D3:
                    case ConsoleKey.NumPad3:
                        MostrarTestesAtomicos(store);
                        break;

                    case ConsoleKey.D4:
                    case ConsoleKey.NumPad4:
                        await AcessarLogsTeste(store);
                        break;

                    case ConsoleKey.D5:
                    case ConsoleKey.NumPad5:
                        GerenciarTestes(store);
                        break;

                    case ConsoleKey.D6:
                    case ConsoleKey.NumPad6:
                    case ConsoleKey.Escape:
                        continuar = false;
                        Console.WriteLine("Encerrando NavShieldTracer...");
                        break;

                    default:
                        Console.WriteLine("⚠️ Opção inválida. Tente novamente.");
                        Console.WriteLine("Pressione qualquer tecla para continuar...");
                        Console.ReadKey();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Mostra o menu principal do aplicativo
    /// </summary>
    static void MostrarMenuPrincipal()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            // Ignora erro se console não suporta Clear()
        }
        Console.WriteLine("NavShieldTracer - Monitoramento de Atividade de Software");
        Console.WriteLine("=======================================================\n");
        Console.WriteLine("Escolha uma opção:");
        Console.WriteLine();
        Console.WriteLine("1. [*] Monitorar processo existente");
        Console.WriteLine("2. [+] Catalogar novo teste atômico");
        Console.WriteLine("3. [-] Visualizar testes catalogados");
        Console.WriteLine("4. [>] Acessar logs de teste específico");
        Console.WriteLine("5. [✎] Gerenciar testes catalogados (editar/excluir)");
        Console.WriteLine("6. [X] Sair");
        Console.WriteLine();
        Console.Write("Pressione o número da opção desejada: ");
    }

    /// <summary>
    /// Executa monitoramento de processo tradicional
    /// </summary>
    /// <param name="store">Store de eventos</param>
    static void ExecutarMonitoramentoProcesso(SqliteEventStore store)
    {
        Console.Clear();
        Console.WriteLine("=== MONITORAMENTO DE PROCESSO ===\n");

        Console.WriteLine("Top 10 processos por consumo de memória:");
        var topProcesses = Process.GetProcesses()
            .OrderByDescending(p => p.WorkingSet64)
            .Take(10)
            .ToList();

        foreach (var p in topProcesses)
        {
            Console.WriteLine($"- {p.ProcessName} (PID: {p.Id}) - {p.WorkingSet64 / 1024 / 1024} MB");
        }

        Console.Write("\nDigite o nome do executável para monitorar (ex: chrome, setup): ");
        string? userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput))
        {
            Console.WriteLine("Nome do executável inválido.");
            Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
            return;
        }

        string targetExecutable;
        var matchingProcesses = Process.GetProcesses()
            .Where(p => !string.IsNullOrWhiteSpace(p.ProcessName) &&
                        p.ProcessName.Contains(userInput, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingProcesses.Count == 0)
        {
            Console.WriteLine($"\nNenhum processo em execução corresponde a '{userInput}'.");
            Console.Write("Deseja monitorar este nome de executável mesmo assim (ex: para aguardar um novo processo)? (S/N): ");
            if (Console.ReadKey(true).Key == ConsoleKey.S)
            {
                targetExecutable = userInput.EndsWith(".exe") ? userInput : userInput + ".exe";
                Console.WriteLine($"\nOk, monitorando por '{targetExecutable}'.");
            }
            else
            {
                Console.WriteLine("\nOperação cancelada.");
                Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
                Console.ReadKey();
                return;
            }
        }
        else
        {
            string processName = matchingProcesses.First().ProcessName + ".exe";
            int processCount = matchingProcesses.Count;

            Console.WriteLine($"\nForam encontradas {processCount} instâncias de processos relacionados a '{userInput}'.");
            Console.Write($"Deseja monitorar todas as atividades do executável '{processName}'? (S/N): ");

            if (Console.ReadKey(true).Key == ConsoleKey.S)
            {
                targetExecutable = processName;
                Console.WriteLine($"\nOk, monitorando '{targetExecutable}'.");
            }
            else
            {
                Console.WriteLine("\nOperação cancelada.");
                Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
                Console.ReadKey();
                return;
            }
        }

        // Iniciar monitoramento
        int rootPid = matchingProcesses.FirstOrDefault()?.Id ?? 0;
        var sessionId = store.BeginSession(new SessionInfo(
            StartedAt: DateTime.Now,
            TargetProcess: targetExecutable,
            RootPid: rootPid,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.ToString()
        ));

        var tracker = new ProcessActivityTracker(targetExecutable, store, sessionId);
        var monitor = new SysmonEventMonitor(tracker);

        Console.WriteLine($"\n⏳ Aguardando atividade de '{targetExecutable}'...");

        monitor.Start();

        Console.WriteLine("\n✅ O monitoramento está ativo.");
        Console.WriteLine("   Pressione Enter a qualquer momento para parar o monitoramento e salvar os logs.");
        Console.ReadLine();

        monitor.Stop();

        var stats = tracker.GetProcessStatistics();
        store.CompleteSession(sessionId, stats);

        Console.WriteLine("\n✅ Monitoramento finalizado. Resumo salvo no banco de dados.");
        Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
        Console.ReadKey();
    }

    /// <summary>
    /// Executa catalogação de um novo teste atômico
    /// </summary>
    /// <param name="store">Store de eventos</param>
    static void ExecutarCatalogacaoTeste(SqliteEventStore store)
    {
        Console.Clear();
        Console.WriteLine("=== CATALOGAÇÃO DE TESTE ATÔMICO ===\n");

        Console.Write("Número do teste (ex: T1055): ");
        string? numero = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(numero))
        {
            Console.WriteLine("Número do teste é obrigatório.");
            Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
            return;
        }

        Console.Write("Nome do teste (ex: Process Injection): ");
        string? nome = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(nome))
        {
            Console.WriteLine("Nome do teste é obrigatório.");
            Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
            return;
        }

        Console.Write("Descrição do teste (opcional): ");
        string? descricao = Console.ReadLine() ?? "";

        var novoTeste = new NovoTesteAtomico(numero, nome, descricao);

        // Iniciar sessão de monitoramento para o teste
        var sessionId = store.BeginSession(new SessionInfo(
            StartedAt: DateTime.Now,
            TargetProcess: "teste.exe", // TesteSoftware será executado
            RootPid: 0,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.ToString()
        ));

        // Registrar teste atômico
        var testeId = store.IniciarTesteAtomico(novoTeste, sessionId);

        Console.WriteLine($"\n✅ Teste '{numero} - {nome}' registrado (ID: {testeId})");
        Console.WriteLine("\n🔄 Iniciando monitoramento para catalogação...");

        var tracker = new ProcessActivityTracker("teste.exe", store, sessionId);
        var monitor = new SysmonEventMonitor(tracker);

        monitor.Start();

        Console.WriteLine("\n✅ Monitoramento ativo. Execute o TesteSoftware em outro PowerShell.");
        Console.WriteLine("   Pressione ENTER quando terminar para finalizar a catalogação.");
        Console.ReadLine();

        monitor.Stop();

        // Contar eventos capturados na sessão
        var totalEventos = ContarEventosSessao(store, sessionId);
        var stats = tracker.GetProcessStatistics();

        // Finalizar teste e sessão
        store.FinalizarTesteAtomico(testeId, totalEventos);
        store.CompleteSession(sessionId, stats);

        Console.WriteLine($"\n✅ Catalogação finalizada! Teste '{numero}' salvo com {totalEventos} eventos.");
        Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
        Console.ReadKey();
    }

    /// <summary>
    /// Mostra lista de testes atômicos catalogados
    /// </summary>
    /// <param name="store">Store de eventos</param>
    static void MostrarTestesAtomicos(SqliteEventStore store)
    {
        Console.Clear();
        Console.WriteLine("=== TESTES ATÔMICOS CATALOGADOS ===\n");

        var testes = store.ListarTestesAtomicos();

        if (!testes.Any())
        {
            Console.WriteLine("Nenhum teste atômico catalogado ainda.");
            Console.WriteLine("Use a opção 2 para catalogar um novo teste.");
        }
        else
        {
            Console.WriteLine($"Total de testes catalogados: {testes.Count}\n");

            foreach (var teste in testes)
            {
                Console.WriteLine($"ID: {teste.Id} | {teste.Numero} - {teste.Nome}");
                Console.WriteLine($"    Data: {teste.DataExecucao:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"    Eventos: {teste.TotalEventos}");
                if (!string.IsNullOrEmpty(teste.Descricao))
                    Console.WriteLine($"    Desc: {teste.Descricao}");
                Console.WriteLine();
            }
        }

        Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
        Console.ReadKey();
    }

    /// <summary>
    /// Permite acesso aos logs de um teste específico
    /// </summary>
    /// <param name="store">Store de eventos</param>
    static async Task AcessarLogsTeste(SqliteEventStore store)
    {
        Console.Clear();
        Console.WriteLine("=== ACESSO A LOGS DE TESTE ===\n");

        var testes = store.ListarTestesAtomicos();

        if (!testes.Any())
        {
            Console.WriteLine("Nenhum teste catalogado disponível.");
            Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("Testes disponíveis:");
        foreach (var teste in testes.Take(10)) // Mostra apenas os 10 mais recentes
        {
            Console.WriteLine($"{teste.Id}. {teste.Numero} - {teste.Nome} ({teste.DataExecucao:yyyy-MM-dd HH:mm})");
        }

        Console.Write("\nDigite o ID do teste para acessar logs: ");
        if (int.TryParse(Console.ReadLine(), out int testeId))
        {
            var resumo = store.ObterResumoTeste(testeId);
            if (resumo == null)
            {
                Console.WriteLine("Teste não encontrado.");
            }
            else
            {
                Console.WriteLine($"\n=== RESUMO DO TESTE {resumo.Numero} ===");
                Console.WriteLine($"Nome: {resumo.Nome}");
                Console.WriteLine($"Data: {resumo.DataExecucao:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"Duração: {resumo.DuracaoSegundos:F1} segundos");
                Console.WriteLine($"Total de eventos: {resumo.TotalEventos}");

                Console.WriteLine("\nEventos por tipo:");
                foreach (var kvp in resumo.EventosPorTipo)
                {
                    Console.WriteLine($"  EventID {kvp.Key}: {kvp.Value} eventos");
                }

                Console.Write("\nDeseja exportar os logs completos? (S/N): ");
                if (Console.ReadKey(true).Key == ConsoleKey.S)
                {
                    var eventos = store.ExportarEventosTeste(testeId);
                    var fileName = $"logs_teste_{resumo.Numero}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    var filePath = Path.Combine(Path.GetDirectoryName(store.DatabasePath) ?? ".", fileName);

                    await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(eventos, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"\n Logs exportados para: {filePath}");
                }
            }
        }
        else
        {
            Console.WriteLine("ID inválido.");
        }

        Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
        Console.ReadKey();
    }

    /// <summary>
    /// Conta o número total de eventos capturados em uma sessão
    /// </summary>
    /// <param name="store">Store de eventos</param>
    /// <param name="sessionId">ID da sessão</param>
    /// <returns>Número total de eventos</returns>
    static int ContarEventosSessao(SqliteEventStore store, int sessionId)
    {
        return store.ContarEventosSessao(sessionId);
    }

    /// <summary>
    /// Gerencia testes catalogados (editar/excluir)
    /// </summary>
    /// <param name="store">Store de eventos</param>
    static void GerenciarTestes(SqliteEventStore store)
    {
        Console.Clear();
        Console.WriteLine("=== GERENCIAR TESTES CATALOGADOS ===\n");

        var testes = store.ListarTestesAtomicos();

        if (!testes.Any())
        {
            Console.WriteLine("Nenhum teste catalogado disponível.");
            Console.WriteLine("Pressione qualquer tecla para voltar ao menu...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("Testes disponíveis:");
        foreach (var teste in testes)
        {
            Console.WriteLine($"{teste.Id}. {teste.Numero} - {teste.Nome}");
            Console.WriteLine($"    Data: {teste.DataExecucao:yyyy-MM-dd HH:mm:ss} | Eventos: {teste.TotalEventos}");
        }

        Console.Write("\nDigite o ID do teste para gerenciar (ou 0 para cancelar): ");
        if (!int.TryParse(Console.ReadLine(), out int testeId) || testeId == 0)
        {
            return;
        }

        var testeEscolhido = testes.FirstOrDefault(t => t.Id == testeId);
        if (testeEscolhido == null)
        {
            Console.WriteLine("Teste não encontrado.");
            Console.WriteLine("Pressione qualquer tecla para voltar...");
            Console.ReadKey();
            return;
        }

        Console.WriteLine($"\n=== TESTE SELECIONADO ===");
        Console.WriteLine($"ID: {testeEscolhido.Id}");
        Console.WriteLine($"Número: {testeEscolhido.Numero}");
        Console.WriteLine($"Nome: {testeEscolhido.Nome}");
        Console.WriteLine($"Descrição: {testeEscolhido.Descricao}");
        Console.WriteLine($"Data: {testeEscolhido.DataExecucao:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Total de Eventos: {testeEscolhido.TotalEventos}");

        Console.WriteLine("\nEscolha uma ação:");
        Console.WriteLine("1. Editar informações");
        Console.WriteLine("2. Excluir teste");
        Console.WriteLine("3. Cancelar");
        Console.Write("\nOpção: ");

        var opcao = Console.ReadKey(true);
        Console.WriteLine();

        switch (opcao.Key)
        {
            case ConsoleKey.D1:
            case ConsoleKey.NumPad1:
                EditarTeste(store, testeEscolhido);
                break;

            case ConsoleKey.D2:
            case ConsoleKey.NumPad2:
                ExcluirTeste(store, testeEscolhido);
                break;

            default:
                Console.WriteLine("Operação cancelada.");
                break;
        }

        Console.WriteLine("\nPressione qualquer tecla para voltar ao menu...");
        Console.ReadKey();
    }

    /// <summary>
    /// Edita informações de um teste catalogado
    /// </summary>
    static void EditarTeste(SqliteEventStore store, TesteAtomico teste)
    {
        Console.WriteLine("\n=== EDITAR TESTE ===");
        Console.WriteLine("Deixe em branco para manter o valor atual.\n");

        Console.Write($"Número da técnica [{teste.Numero}]: ");
        string? novoNumero = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(novoNumero))
            novoNumero = null;

        Console.Write($"Nome [{teste.Nome}]: ");
        string? novoNome = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(novoNome))
            novoNome = null;

        Console.Write($"Descrição [{teste.Descricao}]: ");
        string? novaDescricao = Console.ReadLine();
        if (novaDescricao != null && string.IsNullOrWhiteSpace(novaDescricao))
            novaDescricao = ""; // Permite limpar descrição

        try
        {
            store.AtualizarTesteAtomico(teste.Id, novoNumero, novoNome, novaDescricao);
            Console.WriteLine("\n✅ Teste atualizado com sucesso!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Erro ao atualizar teste: {ex.Message}");
        }
    }

    /// <summary>
    /// Exclui um teste catalogado
    /// </summary>
    static void ExcluirTeste(SqliteEventStore store, TesteAtomico teste)
    {
        Console.WriteLine("\n⚠️  ATENÇÃO: Esta ação não pode ser desfeita!");
        Console.WriteLine($"Você está prestes a excluir:");
        Console.WriteLine($"  - Teste: {teste.Numero} - {teste.Nome}");
        Console.WriteLine($"  - {teste.TotalEventos} eventos capturados");
        Console.WriteLine($"  - Sessão de monitoramento associada");

        Console.Write("\nDigite 'EXCLUIR' (em maiúsculas) para confirmar: ");
        string? confirmacao = Console.ReadLine();

        if (confirmacao != "EXCLUIR")
        {
            Console.WriteLine("\n❌ Exclusão cancelada.");
            return;
        }

        try
        {
            bool sucesso = store.ExcluirTesteAtomico(teste.Id);
            if (sucesso)
            {
                Console.WriteLine("\n✅ Teste excluído com sucesso!");
            }
            else
            {
                Console.WriteLine("\n❌ Falha ao excluir teste (não encontrado).");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Erro ao excluir teste: {ex.Message}");
        }
    }
}