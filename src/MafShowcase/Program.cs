using MafShowcase.Agents;
using MafShowcase.Configuration;
using MafShowcase.Steps;
using Microsoft.Extensions.Configuration;

// Usage: dotnet run --project src/MafShowcase -- step01   (step01 to step10)

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
    case "step05": await Step05_ConsoleChat.RunAsync(factory); break;
    // Session 2: guardrails
    case "step06": await Step06_PhiRedaction.RunAsync(factory); break;
    case "step07": await Step07_InputOutputGuards.RunAsync(factory); break;
    case "step08": await Step08_ToolGuard.RunAsync(factory); break;
    case "step09": await Step09_ToolApproval.RunAsync(factory); break;
    case "step10": await Step10_Observability.RunAsync(factory); break;
    default:
        Console.WriteLine("Choose a step: step01 to step05 (Session 1), step06 to step10 (Session 2)");
        break;
}
