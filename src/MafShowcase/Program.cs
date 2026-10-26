using MafShowcase.Agents;
using MafShowcase.Configuration;
using MafShowcase.Steps;
using Microsoft.Extensions.Configuration;

// Usage: dotnet run --project src/MafShowcase -- step01   (step01 to step04)

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .Build();

var factory = new WardAssistantFactory(OpenAISettings.FromConfiguration(configuration));

switch (args.FirstOrDefault()?.ToLowerInvariant())
{
    case "step01": await Step01_FirstAgent.RunAsync(factory); break;
    case "step02": await Step02_RunVsStream.RunAsync(factory); break;
    case "step03": await Step03_FunctionTools.RunAsync(factory); break;
    case "step04": await Step04_MultiTurnSession.RunAsync(factory); break;
    default:
        Console.WriteLine("Choose a step: step01 | step02 | step03 | step04");
        break;
}
