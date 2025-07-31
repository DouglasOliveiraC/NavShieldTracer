using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;

/// <summary>
/// Programa de teste para análise dinâmica pelo NavShieldTracer.
/// Simula comportamentos que podem ser detectados como suspeitos pelas heurísticas.
/// </summary>
class Program
{
    /// <summary>
    /// Ponto de entrada do programa de teste.
    /// </summary>
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== TESTE SOFTWARE PARA NAVSHIELDTRACER ===");
        Console.WriteLine($"PID: {Environment.ProcessId}");
        Console.WriteLine($"Iniciado em: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        // Fase 1: Período inerte (30 segundos)
        Console.WriteLine("🔄 FASE 1: Período inerte (30 segundos)");
        Console.WriteLine("   Aguardando para dar tempo de configurar o monitoramento...");
        
        for (int i = 30; i > 0; i--)
        {
            Console.Write($"\r   Iniciando atividades em {i:D2} segundos...");
            await Task.Delay(1000);
        }
        
        Console.WriteLine("\r   ✅ Período inerte concluído. Iniciando atividades...        ");
        Console.WriteLine();

        // Fase 2: Atividades de teste
        Console.WriteLine("🚀 FASE 2: Executando atividades de teste");
        
        try
        {
            // Teste 1: Criação de arquivo texto
            await TesteArquivoTexto();
            
            // Teste 2: Acesso ao registro
            await TesteAcessoRegistro();
            
            // Teste 3: Conexão de rede
            await TesteConexaoRede();
            
            // Teste 4: Criação de processo filho
            await TesteProcessoFilho();
            
            // Teste 5: Operações de arquivo suspeitas
            await TesteOperacoesArquivo();
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erro durante testes: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("✅ TESTES CONCLUÍDOS");
        Console.WriteLine("   Pressione qualquer tecla para encerrar...");
        Console.ReadKey();
    }

    /// <summary>
    /// Teste 1: Criação de arquivo de texto simples
    /// </summary>
    static async Task TesteArquivoTexto()
    {
        Console.WriteLine("📝 Teste 1: Criando arquivo de texto");
        
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var filePath = Path.Combine(desktopPath, "teste_navshield.txt");
        
        var conteudo = $@"=== ARQUIVO DE TESTE ===
Criado pelo TesteSoftware
Data/Hora: {DateTime.Now}
PID: {Environment.ProcessId}
Usuário: {Environment.UserName}
Máquina: {Environment.MachineName}

Este arquivo foi criado para testes de análise dinâmica.
";
        
        await File.WriteAllTextAsync(filePath, conteudo);
        Console.WriteLine($"   ✅ Arquivo criado: {filePath}");
        
        await Task.Delay(1000);
    }

    /// <summary>
    /// Teste 2: Acesso ao registro do Windows
    /// </summary>
    static async Task TesteAcessoRegistro()
    {
        Console.WriteLine("🔐 Teste 2: Acessando registro do Windows");
        
        try
        {
            // Leitura segura de uma chave comum
            using var key = Registry.CurrentUser.OpenSubKey(@"Software");
            if (key != null)
            {
                var subKeys = key.GetSubKeyNames();
                Console.WriteLine($"   ✅ Lidas {subKeys.Length} subchaves de HKCU\\Software");
            }

            // Criação de chave de teste (não perigosa)
            using var testKey = Registry.CurrentUser.CreateSubKey(@"Software\NavShieldTest");
            testKey?.SetValue("TesteData", DateTime.Now.ToString());
            testKey?.SetValue("TestePID", Environment.ProcessId);
            Console.WriteLine("   ✅ Chave de teste criada em HKCU\\Software\\NavShieldTest");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Erro no acesso ao registro: {ex.Message}");
        }
        
        await Task.Delay(1000);
    }

    /// <summary>
    /// Teste 3: Conexão de rede simples
    /// </summary>
    static async Task TesteConexaoRede()
    {
        Console.WriteLine("🌐 Teste 3: Fazendo conexão de rede");
        
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            
            // Consulta DNS/HTTP simples
            var response = await client.GetAsync("https://httpbin.org/ip");
            var content = await response.Content.ReadAsStringAsync();
            
            Console.WriteLine("   ✅ Conexão HTTP realizada com sucesso");
            Console.WriteLine($"   📡 Resposta recebida ({content.Length} chars)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Erro na conexão de rede: {ex.Message}");
        }
        
        await Task.Delay(1000);
    }

    /// <summary>
    /// Teste 4: Criação de processo filho (notepad)
    /// </summary>
    static async Task TesteProcessoFilho()
    {
        Console.WriteLine("👶 Teste 4: Criando processo filho (Notepad)");
        
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var filePath = Path.Combine(desktopPath, "teste_navshield.txt");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{filePath}\"",
                UseShellExecute = false
            };
            
            var process = Process.Start(startInfo);
            Console.WriteLine($"   ✅ Notepad iniciado com PID: {process?.Id}");
            
            // Aguarda um pouco e fecha o notepad
            await Task.Delay(3000);
            
            if (process != null && !process.HasExited)
            {
                process.CloseMainWindow();
                await Task.Delay(1000);
                
                if (!process.HasExited)
                {
                    process.Kill();
                }
                
                Console.WriteLine("   ✅ Notepad encerrado");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Erro ao criar processo filho: {ex.Message}");
        }
        
        await Task.Delay(1000);
    }

    /// <summary>
    /// Teste 5: Operações de arquivo que podem parecer suspeitas
    /// </summary>
    static async Task TesteOperacoesArquivo()
    {
        Console.WriteLine("📁 Teste 5: Operações de arquivo diversas");
        
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "NavShieldTest");
            Directory.CreateDirectory(tempDir);
            Console.WriteLine($"   ✅ Diretório temporário criado: {tempDir}");
            
            // Criar vários arquivos
            for (int i = 1; i <= 3; i++)
            {
                var filePath = Path.Combine(tempDir, $"teste_{i}.tmp");
                await File.WriteAllTextAsync(filePath, $"Arquivo de teste {i}\nCriado em {DateTime.Now}");
            }
            Console.WriteLine("   ✅ Múltiplos arquivos criados");
            
            // Listar arquivos
            var files = Directory.GetFiles(tempDir);
            Console.WriteLine($"   📋 {files.Length} arquivos encontrados no diretório");
            
            // Modificar timestamp (comportamento suspeito comum)
            if (files.Length > 0)
            {
                var fileInfo = new FileInfo(files[0]);
                var oldTime = fileInfo.CreationTime;
                fileInfo.CreationTime = DateTime.Now.AddDays(-30);
                Console.WriteLine("   ⏰ Timestamp de arquivo modificado");
                fileInfo.CreationTime = oldTime; // Restaura
            }
            
            // Cleanup
            Directory.Delete(tempDir, true);
            Console.WriteLine("   🗑️ Diretório temporário removido");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Erro nas operações de arquivo: {ex.Message}");
        }
        
        await Task.Delay(1000);
    }
}