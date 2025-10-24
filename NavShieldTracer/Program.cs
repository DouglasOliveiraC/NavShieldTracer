using NavShieldTracer.Modules.Storage;
using NavShieldTracer.Services;
using NavShieldTracer.UI;
using Spectre.Console;

if (Console.IsOutputRedirected)
{
    Console.WriteLine("A interface interativa do NavShieldTracer requer um terminal com suporte a cursor.");
    Console.WriteLine("Saida redirecionada detectada - execute em um terminal real para visualizar o dashboard colorido.");
    return;
}

AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
{
    Ansi = AnsiSupport.Yes,
    ColorSystem = ColorSystemSupport.TrueColor,
    Interactive = InteractionSupport.Yes
});

try
{
    // Inicializa serviços
    var store = new SqliteEventStore();
    var appService = new NavShieldAppService(store);

    // Cria e executa a UI otimizada
    var ui = new ConsoleUI(appService);
    await ui.RunAsync();

    // Cleanup
    appService.Dispose();
}
catch (Exception ex)
{
    Console.WriteLine($"Erro fatal: {ex}");
    throw;
}
