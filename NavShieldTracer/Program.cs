using Microsoft.Extensions.DependencyInjection;
using NavShieldTracer.Components;
using NavShieldTracer.Modules.Storage;
using NavShieldTracer.Services;
using RazorConsole.Core;
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
    var app = AppHost.Create<App>(builder =>
    {
        builder.Services.AddSingleton<SqliteEventStore>();
        builder.Services.AddSingleton<NavShieldAppService>();
        builder.Services.AddSingleton(_ => new ConsoleAppOptions
        {
            AutoClearConsole = false
        });
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Erro fatal: {ex}");
    throw;
}
