using NavShieldTracer.Modules;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics;
using System.Linq;
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
    static void Main()
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

        // --- ETAPA 3: INTERAÇÃO COM O USUÁRIO ---
        Console.WriteLine("\nTop 10 processos por consumo de memória:");
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
            store.Dispose();
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
                store.Dispose();
                return;
            }
        }
        else
        {
            // Pega o nome do processo do primeiro da lista como referência
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
                store.Dispose();
                return;
            }
        }

        // --- ETAPA 4: INICIAR O MONITORAMENTO ---
        // Determina um PID raiz (pode ser 0) e inicia sessão em SQLite
        int rootPid = matchingProcesses.FirstOrDefault()?.Id ?? 0;
        var sessionId = store.BeginSession(new SessionInfo(
            StartedAt: DateTime.Now,
            TargetProcess: targetExecutable,
            RootPid: rootPid,
            Host: Environment.MachineName,
            User: Environment.UserName,
            OsVersion: Environment.OSVersion.ToString()
        ));

        using (store)
        {
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
        }

        Console.WriteLine("Pressione qualquer tecla para sair.");
        Console.ReadKey();
    }
}