using NavShieldTracer.Modules;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics; // Adicionado para Process.GetProcesses()
using System.Linq; // Adicionado para LINQ

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

        bool sysmonInstalado = false;
        try
        {
            var query = new EventLogQuery("Microsoft-Windows-Sysmon/Operational", PathType.LogName);
            using (var reader = new EventLogReader(query))
            {
                sysmonInstalado = true;
            }
        }
        catch
        {
            sysmonInstalado = false;
        }

        if (!sysmonInstalado)
        {
            Console.WriteLine("❌ Sysmon não está instalado ou o log não está acessível.");
            Console.WriteLine("   Para funcionalidade completa, a instalação do Sysmon é recomendada.");
            Console.WriteLine("   1. Baixe o Sysmon em https://docs.microsoft.com/sysinternals/downloads/sysmon");
            Console.WriteLine("   2. Execute como Administrador: sysmon -i -accepteula");
            Console.WriteLine("\nPressione Enter para sair.");
            Console.ReadLine();
            return;
        }
        
        Console.WriteLine("✅ Sysmon detectado. O monitoramento completo está pronto.");

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
                return;
            }
        }

        // Determina um PID raiz para nomear a pasta de log. Usa o primeiro processo encontrado ou 0 se nenhum.
        int rootPid = matchingProcesses.FirstOrDefault()?.Id ?? 0;
        var logger = new MonitorLogger(targetExecutable, rootPid);

        var tracker = new ProcessActivityTracker(targetExecutable, logger);
        var monitor = new SysmonEventMonitor(tracker);

        Console.WriteLine($"\n⏳ Aguardando o início de '{targetExecutable}'...");
        Console.WriteLine("   Execute o software que deseja analisar. O rastreamento começará automaticamente.");
        
        monitor.Start();

        Console.WriteLine("\n✅ O monitoramento está ativo.");
        Console.WriteLine("   Pressione Enter a qualquer momento para parar o monitoramento e salvar os logs.");
        Console.ReadLine();

        monitor.Stop();

        var monitoredProcesses = tracker.MonitoredProcesses;
        var resumo = new 
        {
            ProcessoAlvo = targetExecutable,
            TotalProcessosMonitorados = monitoredProcesses.Count,
            Processos = monitoredProcesses.Select(p => new { PID = p.Key, Imagem = p.Value }).ToList()
        };
        logger.SalvarResumo(resumo);

        Console.WriteLine("Pressione qualquer tecla para sair.");
        Console.ReadKey();
    }
}