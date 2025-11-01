using System.IO;
using NavShieldTracer.Modules.Models;

namespace NavShieldTracer.Tests.Utils;

/// <summary>
/// Simula eventos do Sysmon com dados deterministicos para os testes.
/// </summary>
public class EventSimulator
{
    private readonly Random _random;
    private int _recordIdCounter;

    private static readonly string[] ProcessNames =
    {
        "powershell.exe", "cmd.exe", "notepad.exe", "explorer.exe",
        "svchost.exe", "chrome.exe", "firefox.exe", "winword.exe",
        "excel.exe", "outlook.exe", "teams.exe", "msedge.exe"
    };

    private static readonly string[] FileExtensions =
    {
        ".exe", ".dll", ".txt", ".docx", ".pdf", ".xlsx",
        ".bat", ".ps1", ".vbs", ".js", ".tmp", ".log"
    };

    private static readonly string[] IpAddresses =
    {
        "192.168.1.100", "10.0.0.50", "172.16.0.10",
        "8.8.8.8", "1.1.1.1", "208.67.222.222",
        "185.220.101.1", "93.184.216.34", "151.101.1.140"
    };

    private static readonly string[] Domains =
    {
        "google.com", "microsoft.com", "github.com", "stackoverflow.com",
        "amazon.com", "cloudflare.com", "api.openai.com",
        "malicious-c2.example", "suspicious-domain.net", "phishing-site.test"
    };

    private static readonly string[] RegistryPaths =
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"HKLM\SYSTEM\CurrentControlSet\Services",
        @"HKCU\Software\Classes",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Defender"
    };

    private static readonly string[] PipeNames =
    {
        @"\msagent_", @"\mojo_", @"\chromesync", @"\PSHost.",
        @"\crashpad_", @"\LOCAL\DataTransferPipe", @"\ntsvcs",
        @"\PIPE\srvsvc", @"\samr", @"\lsarpc"
    };

    private static readonly string[] TargetProcesses =
    {
        "lsass.exe", "services.exe", "winlogon.exe", "csrss.exe"
    };

    public EventSimulator(int? seed = null)
    {
        var baseSeed = seed ?? 42;
        _random = new Random(baseSeed);
        _recordIdCounter = 100000 + Math.Abs(baseSeed % 700000);
    }

    public EventoProcessoCriado GerarProcessoCriado(int processId, int parentProcessId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var imagePath = Path.Combine(@"C:\Windows\System32", processName);

        return new EventoProcessoCriado
        {
            EventRecordId = GenerateRecordId(),
            EventId = 1,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            LinhaDeComando = imagePath + " -ExecutionPolicy Bypass",
            CurrentDirectory = @"C:\Windows\System32",
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}",
            LogonGuid = Guid.NewGuid().ToString("B"),
            TerminalSessionId = 1,
            IntegrityLevel = "High",
            ParentProcessGuid = Guid.NewGuid().ToString("B"),
            ParentProcessId = parentProcessId,
            ParentImage = @"C:\Windows\System32\explorer.exe",
            ParentCommandLine = @"C:\Windows\System32\explorer.exe",
            Hashes = $"SHA256={GenerateRandomHash()}"
        };
    }

    public EventoConexaoRede GerarConexaoRede(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var outbound = _random.Next(0, 2) == 0;
        var imagePath = Path.Combine(@"C:\Program Files", processName);

        return new EventoConexaoRede
        {
            EventRecordId = GenerateRecordId(),
            EventId = 3,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}",
            Protocolo = _random.Next(0, 2) == 0 ? "tcp" : "udp",
            Iniciada = outbound,
            IpOrigem = outbound ? "192.168.1.100" : IpAddresses[_random.Next(IpAddresses.Length)],
            PortaOrigem = (ushort)(outbound ? _random.Next(49152, 65535) : 0),
            IpDestino = outbound ? IpAddresses[_random.Next(IpAddresses.Length)] : "192.168.1.100",
            PortaDestino = (ushort)(outbound ? ChoosePort() : _random.Next(1024, 49151))
        };

        int ChoosePort() => _random.Next(0, 2) == 0 ? 443 : 80;
    }

    public EventoConsultaDns GerarDnsQuery(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var domain = Domains[_random.Next(Domains.Length)];
        var imagePath = Path.Combine(@"C:\Program Files", processName);

        return new EventoConsultaDns
        {
            EventRecordId = GenerateRecordId(),
            EventId = 22,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            NomeConsultado = domain,
            TipoConsulta = "A",
            Resultado = string.Join(";", Enumerable.Range(0, _random.Next(1, 4))
                .Select(_ => IpAddresses[_random.Next(IpAddresses.Length)])),
            Imagem = imagePath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoArquivoCriado GerarArquivoCriado(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var extension = FileExtensions[_random.Next(FileExtensions.Length)];
        var fileName = $"file_{_random.Next(1, 5000)}{extension}";
        var imagePath = Path.Combine(@"C:\Program Files", processName);
        var targetPath = Path.Combine(@"C:\Users", Environment.UserName, "AppData", "Local", "Temp", fileName);

        return new EventoArquivoCriado
        {
            EventRecordId = GenerateRecordId(),
            EventId = 11,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            ArquivoAlvo = targetPath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoAcessoRegistro GerarAcessoRegistro(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var regPath = RegistryPaths[_random.Next(RegistryPaths.Length)];
        var imagePath = Path.Combine(@"C:\Windows\System32", processName);

        return new EventoAcessoRegistro
        {
            EventRecordId = GenerateRecordId(),
            EventId = 13,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            TipoEvento = "SetValue",
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            ObjetoAlvo = regPath + @"\Value" + _random.Next(1, 100),
            Detalhes = $"DWORD (0x{_random.Next(0, 65536):X8})",
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoImagemCarregada GerarImagemCarregada(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var dllName = $"library_{_random.Next(1, 100)}.dll";
        var signed = _random.Next(0, 10) < 8;
        var imagePath = Path.Combine(@"C:\Program Files", processName);
        var loadedPath = Path.Combine(@"C:\Windows\System32", dllName);

        return new EventoImagemCarregada
        {
            EventRecordId = GenerateRecordId(),
            EventId = 7,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            ImagemCarregada = loadedPath,
            Hashes = $"SHA256={GenerateRandomHash()}",
            Assinada = signed ? "true" : "false",
            Assinatura = signed ? "Microsoft Windows" : string.Empty,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoThreadRemotaCriada GerarCreateRemoteThread(int sourceProcessId, int targetProcessId)
    {
        var sourceName = ProcessNames[_random.Next(ProcessNames.Length)];
        var targetName = ProcessNames[_random.Next(ProcessNames.Length)];
        var sourcePath = Path.Combine(@"C:\Program Files", sourceName);
        var targetPath = Path.Combine(@"C:\Windows\System32", targetName);

        return new EventoThreadRemotaCriada
        {
            EventRecordId = GenerateRecordId(),
            EventId = 8,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuidOrigem = Guid.NewGuid().ToString("B"),
            ProcessIdOrigem = sourceProcessId,
            ImagemOrigem = sourcePath,
            ProcessGuidDestino = Guid.NewGuid().ToString("B"),
            ProcessIdDestino = targetProcessId,
            ImagemDestino = targetPath,
            EnderecoInicioThread = $"0x{_random.NextInt64(0x10000000, 0x7FFFFFFF):X16}",
            FuncaoInicio = _random.Next(0, 2) == 0 ? "LoadLibraryA" : string.Empty,
            UsuarioOrigem = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoProcessoEncerrado GerarProcessoEncerrado(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var imagePath = Path.Combine(@"C:\Windows\System32", processName);

        return new EventoProcessoEncerrado
        {
            EventRecordId = GenerateRecordId(),
            EventId = 5,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath
        };
    }

    public EventoAcessoProcesso GerarProcessAccess(int sourceProcessId, int targetProcessId)
    {
        var sourceName = ProcessNames[_random.Next(ProcessNames.Length)];
        // 30% de chance de ser um processo crítico (lsass, etc)
        var targetName = _random.Next(0, 10) < 3
            ? TargetProcesses[_random.Next(TargetProcesses.Length)]
            : ProcessNames[_random.Next(ProcessNames.Length)];

        var sourcePath = Path.Combine(@"C:\Program Files", sourceName);
        var targetPath = Path.Combine(@"C:\Windows\System32", targetName);

        // Access masks comuns para injeção de processo
        var accessMasks = new[] { "0x1410", "0x1438", "0x1FFFFF", "0x1000" };

        return new EventoAcessoProcesso
        {
            EventRecordId = GenerateRecordId(),
            EventId = 10,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuidOrigem = Guid.NewGuid().ToString("B"),
            ProcessIdOrigem = sourceProcessId,
            ImagemOrigem = sourcePath,
            ProcessGuidDestino = Guid.NewGuid().ToString("B"),
            ProcessIdDestino = targetProcessId,
            ImagemDestino = targetPath,
            DireitosAcesso = accessMasks[_random.Next(accessMasks.Length)],
            TipoChamada = "OpenProcess",
            UsuarioOrigem = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoArquivoExcluido GerarFileDelete(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var extension = FileExtensions[_random.Next(FileExtensions.Length)];
        var fileName = $"evidence_{_random.Next(1, 5000)}{extension}";
        var imagePath = Path.Combine(@"C:\Program Files", processName);
        var targetPath = Path.Combine(@"C:\Users", Environment.UserName, "AppData", "Local", "Temp", fileName);

        return new EventoArquivoExcluido
        {
            EventRecordId = GenerateRecordId(),
            EventId = 23,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            Imagem = imagePath,
            ArquivoAlvo = targetPath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}",
            Hashes = $"SHA256={GenerateRandomHash()}",
            Arquivado = _random.Next(0, 2) == 0
        };
    }

    public EventoPipeCriado GerarPipeCreated(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var imagePath = Path.Combine(@"C:\Program Files", processName);

        // 20% de chance de usar nome de pipe suspeito
        var pipeName = _random.Next(0, 10) < 2
            ? $@"\msagent_{_random.Next(1000, 9999)}"
            : PipeNames[_random.Next(PipeNames.Length)] + _random.Next(100, 999);

        return new EventoPipeCriado
        {
            EventRecordId = GenerateRecordId(),
            EventId = 17,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            NomePipe = pipeName,
            Imagem = imagePath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoPipeConectado GerarPipeConnected(int processId)
    {
        var processName = ProcessNames[_random.Next(ProcessNames.Length)];
        var imagePath = Path.Combine(@"C:\Windows\System32", processName);

        // 20% de chance de conectar a pipe suspeito
        var pipeName = _random.Next(0, 10) < 2
            ? $@"\msagent_{_random.Next(1000, 9999)}"
            : PipeNames[_random.Next(PipeNames.Length)] + _random.Next(100, 999);

        return new EventoPipeConectado
        {
            EventRecordId = GenerateRecordId(),
            EventId = 18,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            ProcessGuid = Guid.NewGuid().ToString("B"),
            ProcessId = processId,
            NomePipe = pipeName,
            Imagem = imagePath,
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}"
        };
    }

    public EventoWmi GerarWmiFilter(int processId)
    {
        var wmiQueries = new[]
        {
            "SELECT * FROM __InstanceModificationEvent WITHIN 60 WHERE TargetInstance ISA 'Win32_PerfFormattedData_PerfOS_System'",
            "SELECT * FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'",
            "SELECT * FROM Win32_Process WHERE Name = 'explorer.exe'",
            "SELECT * FROM Win32_LogicalDisk WHERE DriveType = 3"
        };

        return new EventoWmi
        {
            EventRecordId = GenerateRecordId(),
            EventId = 19,
            ComputerName = Environment.MachineName,
            UtcTime = DateTime.UtcNow,
            Operacao = "Created",
            Usuario = $"{Environment.MachineName}\\{Environment.UserName}",
            Nome = "MaliciousFilter" + _random.Next(1, 100),
            Query = wmiQueries[_random.Next(wmiQueries.Length)]
        };
    }

    public List<EventoSysmonBase> GerarEventosMistos(int quantidade, int baseProcessId = 1000)
    {
        var eventos = new List<EventoSysmonBase>(quantidade);
        var eventTypes = new[] { 1, 3, 5, 7, 8, 10, 11, 13, 17, 18, 19, 22, 23 };

        for (var i = 0; i < quantidade; i++)
        {
            var eventType = eventTypes[_random.Next(eventTypes.Length)];
            var processId = baseProcessId + _random.Next(0, 100);

            eventos.Add(eventType switch
            {
                1 => GerarProcessoCriado(processId, processId - 1),
                3 => GerarConexaoRede(processId),
                5 => GerarProcessoEncerrado(processId),
                7 => GerarImagemCarregada(processId),
                8 => GerarCreateRemoteThread(processId, processId + 1),
                10 => GerarProcessAccess(processId, processId + 10),
                11 => GerarArquivoCriado(processId),
                13 => GerarAcessoRegistro(processId),
                17 => GerarPipeCreated(processId),
                18 => GerarPipeConnected(processId),
                19 => GerarWmiFilter(processId),
                22 => GerarDnsQuery(processId),
                23 => GerarFileDelete(processId),
                _ => GerarProcessoCriado(processId, processId - 1)
            });
        }

        return eventos;
    }

    private int GenerateRecordId()
    {
        var next = _recordIdCounter++;
        if (_recordIdCounter > 999999)
        {
            _recordIdCounter = 100000;
        }
        return next;
    }

    private string GenerateRandomHash()
    {
        Span<byte> buffer = stackalloc byte[32];
        _random.NextBytes(buffer);
        return Convert.ToHexString(buffer);
    }
}
