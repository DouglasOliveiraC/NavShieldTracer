using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;

internal static class Program
{
    private static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== NAVSHIELD - ATOMIC RED TEAM SHELL ===");
        Console.WriteLine();

        var service = new AtomicRedTeamService();
        var status = await service.EnsureModuleAsync();

        if (!status.IsLoaded)
        {
            ShowMissingModuleInstructions(status.Message);
            return;
        }

        Console.WriteLine($"✓ Invoke-AtomicRedTeam carregado (versao {status.Version})");
        Console.WriteLine($"  Caminho: {status.ModuleBase}");
        Console.WriteLine();

        MostrarComandosUteis();
        Console.WriteLine();

        await ShellLoopAsync(service);
    }

    private static void MostrarComandosUteis()
    {
        Console.WriteLine("=== COMANDOS UTEIS DO INVOKE-ATOMICREDTEAM ===");
        Console.WriteLine();
        Console.WriteLine("# Listar todas as tecnicas:");
        Console.WriteLine("  Get-AtomicTechnique | Select-Object Technique, TechniqueName | Format-Table");
        Console.WriteLine();
        Console.WriteLine("# Buscar tecnica especifica:");
        Console.WriteLine("  Get-AtomicTechnique | Where-Object { $_.Technique -like '*T1055*' }");
        Console.WriteLine();
        Console.WriteLine("# Ver detalhes de uma tecnica:");
        Console.WriteLine("  Invoke-AtomicTest T1055 -ShowDetailsBrief");
        Console.WriteLine("  Invoke-AtomicTest T1055 -ShowDetails");
        Console.WriteLine();
        Console.WriteLine("# Verificar pre-requisitos:");
        Console.WriteLine("  Invoke-AtomicTest T1055 -CheckPrereqs");
        Console.WriteLine();
        Console.WriteLine("# Instalar pre-requisitos:");
        Console.WriteLine("  Invoke-AtomicTest T1055 -GetPrereqs");
        Console.WriteLine();
        Console.WriteLine("# Executar teste especifico:");
        Console.WriteLine("  Invoke-AtomicTest T1055 -TestNumbers 1");
        Console.WriteLine("  Invoke-AtomicTest T1055 -TestNumbers 1,2,3");
        Console.WriteLine();
        Console.WriteLine("# Executar todos os testes:");
        Console.WriteLine("  Invoke-AtomicTest T1055");
        Console.WriteLine();
        Console.WriteLine("# Atualizar repositorio:");
        Console.WriteLine("  Update-AtomicRedTeam");
        Console.WriteLine();
        Console.WriteLine("# Controlar verbosidade (debug):");
        Console.WriteLine("  verbose on   - Ativar mensagens VERBOSE e DEBUG");
        Console.WriteLine("  verbose off  - Desativar (saída limpa, padrão)");
        Console.WriteLine();
        Console.WriteLine("# Sair:");
        Console.WriteLine("  exit");
        Console.WriteLine();
        Console.WriteLine("==============================================");
    }

    private static async Task ShellLoopAsync(AtomicRedTeamService service)
    {
        while (true)
        {
            Console.Write("ART> ");
            var comando = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(comando))
            {
                continue;
            }

            comando = comando.Trim();

            if (comando.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                comando.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                comando.Equals("sair", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Encerrando...");
                break;
            }

            if (comando.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                comando.Equals("ajuda", StringComparison.OrdinalIgnoreCase) ||
                comando == "?")
            {
                MostrarComandosUteis();
                continue;
            }

            if (comando.Equals("verbose on", StringComparison.OrdinalIgnoreCase))
            {
                service.VerboseEnabled = true;
                Console.WriteLine("✓ Modo verbose ativado (mensagens VERBOSE e DEBUG serão exibidas)");
                Console.WriteLine();
                continue;
            }

            if (comando.Equals("verbose off", StringComparison.OrdinalIgnoreCase))
            {
                service.VerboseEnabled = false;
                Console.WriteLine("✓ Modo verbose desativado (saída limpa)");
                Console.WriteLine();
                continue;
            }

            var resultado = await service.InvokeAsync(comando);

            if (!string.IsNullOrWhiteSpace(resultado.Output))
            {
                Console.WriteLine(resultado.Output);
            }

            if (!resultado.Success && !string.IsNullOrWhiteSpace(resultado.Error))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERRO: {resultado.Error}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }


    private static void ShowMissingModuleInstructions(string? mensagem)
    {
        Console.WriteLine(" Invoke-AtomicRedTeam nao foi encontrado.");
        if (!string.IsNullOrWhiteSpace(mensagem))
        {
            Console.WriteLine(mensagem);
        }

        Console.WriteLine();
        Console.WriteLine("Instale seguindo a documentacao oficial:");
        Console.WriteLine("  1) Abrir PowerShell como admin.");
        Console.WriteLine("  2) Executar: Install-Module -Name Invoke-AtomicRedTeam -Scope CurrentUser");
        Console.WriteLine("     ou usar o script: Invoke-WebRequest https://redcanaryco.github.io/atomicredteam/install.ps1 -OutFile install.ps1");
        Console.WriteLine("                        .\\install.ps1");
        Console.WriteLine();
        Console.WriteLine("Depois execute novamente este programa.");
    }
}

internal sealed class AtomicRedTeamService
{
    private const string DefaultImportCommand = "Import-Module Invoke-AtomicRedTeam -ErrorAction Stop; ";
    private string _importCommand = DefaultImportCommand;
    private bool _verboseEnabled = false;

    public string ImportCommand => _importCommand;

    public bool VerboseEnabled
    {
        get => _verboseEnabled;
        set => _verboseEnabled = value;
    }

    public async Task<ModuleStatus> EnsureModuleAsync()
    {
        using var ps = CreatePowerShell();
        ps.AddScript("Get-Module -ListAvailable -Name Invoke-AtomicRedTeam | Select-Object -First 1");
        var lista = await Task.Run(() => ps.Invoke());

        string moduleScript;
        string? manifestUsed = null;

        if (lista.Count == 0)
        {
            manifestUsed = FindModuleManifest();
            if (manifestUsed is null)
            {
                return new ModuleStatus(false, null, null, "Modulo nao encontrado. Execute o script Install-AtomicRedTeam.ps1 para concluir a instalacao.");
            }

            var escapedManifest = EscapeSingleQuotes(manifestUsed);
            moduleScript = $"Import-Module '{escapedManifest}' -ErrorAction Stop; $mod = Get-Module Invoke-AtomicRedTeam; $mod.Version.ToString(); $mod.ModuleBase;";
        }
        else
        {
            moduleScript = DefaultImportCommand + "$mod = Get-Module Invoke-AtomicRedTeam; $mod.Version.ToString(); $mod.ModuleBase;";
        }

        ps.Commands.Clear();
        ps.AddScript(moduleScript);
        var import = await Task.Run(() => ps.Invoke());

        if (ps.HadErrors)
        {
            var erros = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
            return new ModuleStatus(false, null, manifestUsed, erros);
        }

        var versao = import.Count > 0 ? import[0]?.ToString() : null;
        var basePath = import.Count > 1 ? import[1]?.ToString() : null;

        if (string.IsNullOrWhiteSpace(basePath) && manifestUsed is not null)
        {
            basePath = Path.GetDirectoryName(manifestUsed);
        }

        _importCommand = manifestUsed is null
            ? DefaultImportCommand
            : $"Import-Module '{EscapeSingleQuotes(manifestUsed)}' -ErrorAction Stop; ";

        return new ModuleStatus(true, versao, basePath, null);
    }

    public async Task<PowerShellResult> InvokeAsync(string script)
    {
        using var ps = CreatePowerShell();

        // Configurar streams para capturar toda a saída
        var verbosePreference = _verboseEnabled ? "Continue" : "SilentlyContinue";
        ps.AddScript($"$InformationPreference = 'Continue'; $VerbosePreference = '{verbosePreference}'");
        ps.Invoke();
        ps.Commands.Clear();

        ps.AddScript(_importCommand + script);
        var saida = await Task.Run(() => ps.Invoke());

        var builder = new StringBuilder();

        // Capturar output padrão
        foreach (var item in saida)
        {
            if (item != null)
            {
                builder.AppendLine(item.ToString());
            }
        }

        // Capturar Verbose stream (apenas se verbose estiver habilitado)
        if (_verboseEnabled)
        {
            foreach (var item in ps.Streams.Verbose)
            {
                builder.AppendLine($"VERBOSE: {item.Message}");
            }
        }

        // Capturar Warning stream
        foreach (var item in ps.Streams.Warning)
        {
            builder.AppendLine($"WARNING: {item.Message}");
        }

        // Capturar Information stream (Write-Host redireciona para aqui)
        foreach (var item in ps.Streams.Information)
        {
            if (item.MessageData != null)
            {
                builder.AppendLine(item.MessageData.ToString());
            }
        }

        // Capturar Debug stream (apenas se verbose estiver habilitado)
        if (_verboseEnabled)
        {
            foreach (var item in ps.Streams.Debug)
            {
                builder.AppendLine($"DEBUG: {item.Message}");
            }
        }

        if (ps.HadErrors)
        {
            var erro = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
            return new PowerShellResult(false, builder.ToString(), erro);
        }

        return new PowerShellResult(true, builder.ToString(), null);
    }

    private static PowerShell CreatePowerShell()
    {
        var state = InitialSessionState.CreateDefault();
        var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();

        var ps = PowerShell.Create(runspace);
        try
        {
            ps.AddScript("Set-ExecutionPolicy -Scope Process Bypass -Force").Invoke();
        }
        catch
        {
            // Ignora se nao for possivel ajustar a policy no processo atual
        }

        ps.Commands.Clear();
        ps.Streams.Error.Clear();
        return ps;
    }

    private static string? FindModuleManifest()
    {
        var candidates = new List<string>();

        foreach (var directory in EnumerateCandidateDirectories())
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (var manifest in Directory.EnumerateFiles(directory, "Invoke-AtomicRedTeam.psd1", SearchOption.AllDirectories))
                {
                    candidates.Add(manifest);
                }
            }
            catch
            {
                // Ignora caminhos inacessiveis
            }
        }

        return candidates
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var modulePaths = (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in modulePaths)
        {
            var trimmed = path.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var moduleDir = Path.Combine(trimmed, "Invoke-AtomicRedTeam");
            if (visited.Add(moduleDir))
            {
                yield return moduleDir;
            }
        }

        var extras = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AtomicRedTeam"),
            @"C:\AtomicRedTeam",
            Path.Combine(@"C:\AtomicRedTeam", "invoke-atomicredteam"),
            Path.Combine(@"C:\AtomicRedTeam", "atomic-red-team")
        };

        foreach (var path in extras)
        {
            if (visited.Add(path))
            {
                yield return path;
            }
        }
    }

    private static string EscapeSingleQuotes(string value)
    {
        return value.Replace("'", "''");
    }
}

internal sealed record ModuleStatus(bool IsLoaded, string? Version, string? ModuleBase, string? Message);

internal sealed record PowerShellResult(bool Success, string? Output, string? Error);

