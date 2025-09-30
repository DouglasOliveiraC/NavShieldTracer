using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly Regex TechniqueIdPattern = new("^T\\d{4,}(\\.\\d{3})?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== NAVSHIELD - INTERFACE ATOMIC RED TEAM ===");
        Console.WriteLine("Baseado na documentacao oficial do Invoke-AtomicRedTeam (Red Canary)");
        Console.WriteLine();

        var service = new AtomicRedTeamService();
        var status = await service.EnsureModuleAsync();

        if (!status.IsLoaded)
        {
            ShowMissingModuleInstructions(status.Message);
            return;
        }

        Console.WriteLine($"? Invoke-AtomicRedTeam carregado (versao {status.Version})");
        Console.WriteLine($"  Caminho do modulo: {status.ModuleBase}");
        Console.WriteLine();

        await MenuLoopAsync(service, status);
    }

    private static async Task MenuLoopAsync(AtomicRedTeamService service, ModuleStatus status)
    {
        while (true)
        {
            Console.WriteLine("Selecione uma opcao:");
            Console.WriteLine("  1) Buscar tecnicas");
            Console.WriteLine("  2) Ver detalhes e testes de uma tecnica");
            Console.WriteLine("  3) Executar teste atomico");
            Console.WriteLine("  4) Criar teste customizado (YAML)");
            Console.WriteLine("  5) Atualizar repositorio Atomic Red Team");
            Console.WriteLine("  6) Sair");
            Console.Write("Opcao: ");

            var key = Console.ReadLine();
            Console.WriteLine();

            switch (key)
            {
                case "1":
                    await BuscarTecnicasAsync(service);
                    break;
                case "2":
                    await MostrarDetalhesTecnicaAsync(service);
                    break;
                case "3":
                    await ExecutarTesteAsync(service);
                    break;
                case "4":
                    await CriarTesteCustomAsync(status);
                    break;
                case "5":
                    await AtualizarRepositorioAsync(service);
                    break;
                case "6":
                    Console.WriteLine("Encerrando...");
                    return;
                default:
                    Console.WriteLine("Opcao invalida. Tente novamente.\n");
                    break;
            }
        }
    }

    private static async Task BuscarTecnicasAsync(AtomicRedTeamService service)
    {
        Console.Write("Informe termo de busca (ID ou parte do nome): ");
        var termo = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(termo))
        {
            Console.WriteLine("Busca cancelada.\n");
            return;
        }

        var escaped = EscapeForSingleQuotes(termo);
        var script = $@"
$term = '{escaped}';
Get-AtomicTechnique |
    Where-Object {{ $_.Technique -like ""*${{term}}*"" -or $_.TechniqueName -like ""*${{term}}*"" }} |
    Select-Object -First 25 Technique, TechniqueName |
    Sort-Object Technique |
    Format-Table -AutoSize |
    Out-String";

        var result = await service.InvokeAsync(script);

        if (!result.Success)
        {
            Console.WriteLine("Falha ao buscar tecnicas:");
            Console.WriteLine(result.Error);
        }
        else if (string.IsNullOrWhiteSpace(result.Output))
        {
            Console.WriteLine("Nenhuma tecnica encontrada.");
        }
        else
        {
            Console.WriteLine(result.Output);
        }

        Console.WriteLine();
    }

    private static async Task MostrarDetalhesTecnicaAsync(AtomicRedTeamService service)
    {
        var tecnica = LerTechniqueId();
        if (tecnica is null)
        {
            return;
        }

        Console.WriteLine($"\n=== Detalhes da Técnica {tecnica} ===\n");

        // Método 1: Usar -ShowDetailsBrief (mais confiável)
        var briefScript = $@"Invoke-AtomicTest '{tecnica}' -ShowDetailsBrief 2>&1 | Out-String";

        var brief = await service.InvokeAsync(briefScript);

        if (brief.Success && !string.IsNullOrWhiteSpace(brief.Output))
        {
            Console.WriteLine(brief.Output);
        }
        else
        {
            Console.WriteLine("⚠️ Método principal falhou. Tentando alternativas...\n");

            if (!brief.Success)
            {
                Console.WriteLine($"Erro: {brief.Error}\n");
            }

            // Método 2: Listar técnicas e filtrar
            var listScript = $@"
                $ErrorActionPreference = 'Stop'
                try {{
                    $allTechniques = Get-AtomicTechnique
                    $match = $allTechniques | Where-Object {{ $_.Technique -eq '{tecnica}' }}

                    if ($match) {{
                        Write-Output ""Técnica: $($match.Technique)""
                        Write-Output ""Nome: $($match.TechniqueName)""
                        Write-Output ""Descrição: $($match.Description)""
                        Write-Output """"
                        Write-Output ""Caminho do YAML: $($match.Path)""
                        Write-Output """"

                        # Tentar mostrar testes disponíveis
                        try {{
                            $tests = Invoke-AtomicTest '{tecnica}' -ShowDetails 2>&1
                            if ($tests) {{
                                Write-Output ""Testes disponíveis:""
                                $tests | Out-String
                            }}
                        }} catch {{
                            Write-Output ""Não foi possível carregar detalhes dos testes.""
                        }}
                    }} else {{
                        Write-Output ""❌ Técnica '{tecnica}' não encontrada no repositório local.""
                        Write-Output """"
                        Write-Output ""Sugestões:""
                        Write-Output ""1. Verificar se o ID está correto (ex: T1113, não t1113)""
                        Write-Output ""2. Atualizar repositório: Opção 5 do menu""
                        Write-Output ""3. Buscar técnicas similares: Opção 1 do menu""
                    }}
                }} catch {{
                    Write-Output ""Erro ao buscar técnica: $_""
                }}
            ";

            var list = await service.InvokeAsync(listScript);

            if (list.Success)
            {
                Console.WriteLine(list.Output);
            }
            else
            {
                Console.WriteLine("❌ Não foi possível obter detalhes da técnica.");
                Console.WriteLine($"Erro: {list.Error}");
            }
        }

        Console.WriteLine();
    }

    private static async Task ExecutarTesteAsync(AtomicRedTeamService service)
    {
        var tecnica = LerTechniqueId();
        if (tecnica is null)
        {
            return;
        }

        Console.Write("Informe numeros dos testes (ex.: 1 ou 1,3). Deixe vazio para todos: ");
        var numeros = (Console.ReadLine() ?? string.Empty).Trim();

        Console.Write("Coletar e aplicar prerequisites antes? (S/N): ");
        var coletarPreReq = LerSimNao();

        Console.Write("Executar em modo pausado (requer confirmacao a cada passo)? (S/N): ");
        var modoPausado = LerSimNao();

        Console.Write("Gerar logs no formato Sysmon (para comparacao com NavShield)? (S/N): ");
        var gerarLogsSysmon = LerSimNao();

        Console.Write("Deseja fornecer parametros personalizados? (S/N): ");
        var fornecerParametros = LerSimNao();
        var parametros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (fornecerParametros)
        {
            Console.WriteLine("Informe parametros no formato chave=valor. Linha vazia para encerrar.");
            while (true)
            {
                Console.Write("parametro: ");
                var entrada = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(entrada))
                {
                    break;
                }

                var separador = entrada.IndexOf('=');
                if (separador <= 0 || separador == entrada.Length - 1)
                {
                    Console.WriteLine("Formato invalido. Use chave=valor.");
                    continue;
                }

                var chave = entrada[..separador].Trim();
                var valor = entrada[(separador + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(chave))
                {
                    Console.WriteLine("Chave nao pode ser vazia.");
                    continue;
                }

                parametros[chave] = valor;
            }
        }

        var comando = new StringBuilder();
        comando.Append($"Invoke-AtomicTest '{tecnica}'");

        if (!string.IsNullOrWhiteSpace(numeros))
        {
            comando.Append(" -TestNumbers ");
            comando.Append(numeros);
        }

        if (coletarPreReq)
        {
            comando.Append(" -GetPreReqs");
        }

        if (modoPausado)
        {
            comando.Append(" -PromptForInputArgs");
        }

        if (gerarLogsSysmon)
        {
            // Atomic Red Team suporta logging module Syslog
            // Isso gera logs compatíveis com Sysmon para comparação
            comando.Append(" -LoggingModule 'Syslog'");

            // Definir caminho para salvar logs
            var logPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                "..", "..", "..", "..", "Logs", "ART_Sysmon"
            );
            Directory.CreateDirectory(logPath);
            comando.Append($" -ExecutionLogPath '{logPath}'");
        }

        if (parametros.Count > 0)
        {
            comando.Append(" -InputArgs @{ ");
            var primeiro = true;
            foreach (var kvp in parametros)
            {
                if (!primeiro)
                {
                    comando.Append("; ");
                }

                comando.Append($"'{EscapeForSingleQuotes(kvp.Key)}' = '{EscapeForSingleQuotes(kvp.Value)}'");
                primeiro = false;
            }
            comando.Append(" }");
        }

        comando.Append(" -Force | Out-String");

        Console.WriteLine("\nExecutando teste...\n");
        var resultado = await service.InvokeAsync(comando.ToString());

        if (resultado.Success)
        {
            Console.WriteLine(resultado.Output);
        }
        else
        {
            Console.WriteLine("Falha na execucao do teste:");
            Console.WriteLine(resultado.Error);
        }

        Console.WriteLine();
    }

    private static async Task AtualizarRepositorioAsync(AtomicRedTeamService service)
    {
        Console.WriteLine("Atualizando repositorio Atomic Red Team (Update-AtomicRedTeam)...");
        var resultado = await service.InvokeAsync("Update-AtomicRedTeam | Out-String");

        if (resultado.Success)
        {
            Console.WriteLine(resultado.Output);
        }
        else
        {
            Console.WriteLine("Falha ao atualizar:");
            Console.WriteLine(resultado.Error);
        }

        Console.WriteLine();
    }

    private static async Task CriarTesteCustomAsync(ModuleStatus status)
    {
        var tecnica = LerTechniqueId();
        if (tecnica is null)
        {
            return;
        }

        Console.Write("Nome visivel para a tecnica (enter para padrao): ");
        var displayName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = $"Custom {tecnica} tests";
        }

        Console.Write("Nome do teste atomico: ");
        var testName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(testName))
        {
            Console.WriteLine("Nome obrigatorio. Cancelando.\n");
            return;
        }

        Console.Write("Descricao resumida: ");
        var descricao = Console.ReadLine() ?? string.Empty;

        Console.Write("Plataformas suportadas (sep. por virgula, padrao windows): ");
        var plataformasEntrada = Console.ReadLine();
        var plataformas = (plataformasEntrada ?? "windows")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("windows")
            .Select(p => p.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.Write("Executor (ex.: powershell, command_prompt): ");
        var executor = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(executor))
        {
            executor = "powershell";
        }

        var comando = LerMultilinha("Comando a executar (digite EOF para finalizar):");
        if (string.IsNullOrWhiteSpace(comando))
        {
            Console.WriteLine("Comando obrigatorio. Cancelando.\n");
            return;
        }

        Console.Write("Deseja configurar cleanup? (S/N): ");
        var incluirCleanup = LerSimNao();
        var cleanup = string.Empty;
        if (incluirCleanup)
        {
            cleanup = LerMultilinha("Cleanup command (EOF para finalizar):");
        }

        var parametros = new List<AtomicParameter>();
        Console.Write("Adicionar parametros dinamicos? (S/N): ");
        if (LerSimNao())
        {
            Console.WriteLine("Informe parametros no formato nome|descricao|valor padrao. Linha vazia encerra.");
            while (true)
            {
                Console.Write("parametro: ");
                var linha = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(linha))
                {
                    break;
                }

                var partes = linha.Split('|');
                if (partes.Length < 3)
                {
                    Console.WriteLine("Use nome|descricao|valor");
                    continue;
                }

                parametros.Add(new AtomicParameter(partes[0].Trim(), partes[1].Trim(), partes[2].Trim()));
            }
        }

        var repoPath = ResolveAtomicRepoPath(status);
        var tecnicaDir = Path.Combine(repoPath, "atomics", tecnica.ToUpperInvariant());
        Directory.CreateDirectory(tecnicaDir);

        var slug = CriarSlug(testName);
        var arquivo = Path.Combine(tecnicaDir, $"{tecnica.ToLowerInvariant()}_{slug}.yaml");

        var yaml = MontarYaml(tecnica, displayName, testName, descricao, plataformas, executor, comando, cleanup, parametros);
        File.WriteAllText(arquivo, yaml, Encoding.UTF8);

        Console.WriteLine("Teste custom criado:");
        Console.WriteLine(arquivo);
        Console.WriteLine("Revise o YAML e rode Invoke-AtomicTest para validar.");
        Console.WriteLine();
    }

    private static string MontarYaml(
        string tecnica,
        string displayName,
        string testName,
        string descricao,
        IReadOnlyCollection<string> plataformas,
        string executor,
        string comando,
        string cleanup,
        IReadOnlyCollection<AtomicParameter> parametros)
    {
        var guid = Guid.NewGuid();
        var sb = new StringBuilder();
        sb.AppendLine($"attack_technique: {tecnica.ToUpperInvariant()}");
        sb.AppendLine($"display_name: {displayName}");
        sb.AppendLine("atomic_tests:");
        sb.AppendLine($"  - name: {testName}");
        sb.AppendLine($"    auto_generated_guid: {guid}");
        sb.AppendLine("    description: |");
        foreach (var linha in (descricao ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            sb.AppendLine($"      {linha}");
        }

        sb.AppendLine("    supported_platforms:");
        foreach (var plataforma in plataformas)
        {
            sb.AppendLine($"      - {plataforma}");
        }

        if (parametros.Count > 0)
        {
            sb.AppendLine("    input_arguments:");
            foreach (var parametro in parametros)
            {
                sb.AppendLine($"      {parametro.Name}:");
                sb.AppendLine($"        description: {parametro.Description}");
                sb.AppendLine("        type: string");
                sb.AppendLine($"        default: {parametro.Default}");
            }
        }

        sb.AppendLine("    executor:");
        sb.AppendLine($"      name: {executor}");
        sb.AppendLine("      command: |");
        foreach (var linha in comando.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            sb.AppendLine($"        {linha}");
        }

        if (!string.IsNullOrWhiteSpace(cleanup))
        {
            sb.AppendLine("    cleanup_command: |");
            foreach (var linha in cleanup.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                sb.AppendLine($"      {linha}");
            }
        }

        return sb.ToString();
    }

    private static string CriarSlug(string texto)
    {
        var normalized = texto.ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c) || c == '-' || c == '_')
            {
                sb.Append('-');
            }
        }

        var slug = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug)
            ? "custom"
            : slug;
    }

    private static string ResolveAtomicRepoPath(ModuleStatus status)
    {
        static bool HasAtomicContent(string path)
        {
            return Directory.Exists(Path.Combine(path, "atomics"));
        }

        var candidates = new List<string>();
        var registered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void Register(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = Path.GetFullPath(path);
            if (registered.Add(normalized) && Directory.Exists(normalized))
            {
                candidates.Add(normalized);
            }
        }

        Register(Path.Combine(home, "AtomicRedTeam"));
        Register(Path.Combine(home, "AtomicRedTeam", "atomic-red-team"));
        Register(@"C:\AtomicRedTeam");
        Register(@"C:\AtomicRedTeam\atomic-red-team");

        if (!string.IsNullOrWhiteSpace(status.ModuleBase))
        {
            Register(Path.Combine(status.ModuleBase, "..", "..", "..", "AtomicRedTeam"));
            Register(Path.Combine(status.ModuleBase, "..", "..", "..", "atomic-red-team"));
        }

        foreach (var candidate in candidates)
        {
            if (HasAtomicContent(candidate))
            {
                return candidate;
            }

            var nested = Path.Combine(candidate, "atomic-red-team");
            if (Directory.Exists(nested) && HasAtomicContent(nested))
            {
                return nested;
            }
        }

        Console.WriteLine("Repositorio AtomicRedTeam nao encontrado. Informe o caminho completo (enter para padrao ~\\AtomicRedTeam):");
        var manual = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(manual))
        {
            var manualPath = Path.GetFullPath(manual);
            if (Directory.Exists(manualPath))
            {
                return manualPath;
            }
        }

        var fallback = Path.Combine(home, "AtomicRedTeam");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static string LerMultilinha(string cabecalho)
    {
        Console.WriteLine(cabecalho);
        var linhas = new List<string>();
        while (true)
        {
            var linha = Console.ReadLine();
            if (linha == null)
            {
                break;
            }

            if (string.Equals(linha.Trim(), "EOF", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            linhas.Add(linha);
        }

        return string.Join(Environment.NewLine, linhas);
    }

    private static string? LerTechniqueId()
    {
        Console.Write("Informe o ID da tecnica (ex.: T1059 ou T1059.001): ");
        var tecnica = Console.ReadLine()?.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(tecnica))
        {
            Console.WriteLine("Operacao cancelada.\n");
            return null;
        }

        if (!TechniqueIdPattern.IsMatch(tecnica))
        {
            Console.WriteLine("ID invalido. Utilize o formato T#### ou T####.###.\n");
            return null;
        }

        return tecnica;
    }

    private static bool LerSimNao()
    {
        var resposta = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(resposta))
        {
            return false;
        }

        var normalized = resposta.Trim().ToUpperInvariant();
        return normalized is "S" or "SIM" or "Y" or "YES";
    }

    private static string EscapeForSingleQuotes(string texto)
    {
        return texto.Replace("'", "''");
    }

    private static void ShowMissingModuleInstructions(string? mensagem)
    {
        Console.WriteLine("?? Invoke-AtomicRedTeam nao foi encontrado.");
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

    private sealed record AtomicParameter(string Name, string Description, string Default);
}

internal sealed class AtomicRedTeamService
{
    private const string DefaultImportCommand = "Import-Module Invoke-AtomicRedTeam -ErrorAction Stop; ";
    private string _importCommand = DefaultImportCommand;

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
        ps.AddScript(_importCommand + script);
        var saida = await Task.Run(() => ps.Invoke());

        var builder = new StringBuilder();
        foreach (var item in saida)
        {
            if (item != null)
            {
                builder.AppendLine(item.ToString());
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

