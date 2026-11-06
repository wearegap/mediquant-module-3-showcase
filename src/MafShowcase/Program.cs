using MafShowcase.Agents;
using MafShowcase.Configuration;
using MafShowcase.Steps;
using Microsoft.Extensions.Configuration;

// Usage: dotnet run --project src/MafShowcase -- step01   (step01 to step14)

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
    // Session 6: guardrails
    case "step06": await Step06_PhiRedaction.RunAsync(factory); break;
    case "step07": await Step07_InputOutputGuards.RunAsync(factory); break;
    case "step08": await Step08_ToolGuard.RunAsync(factory); break;
    case "step09": await Step09_ToolApproval.RunAsync(factory); break;
    case "step10": await Step10_Observability.RunAsync(factory); break;
    // Session 7: RAG (run from the repository root; step11 builds the index the later steps use)
    case "step11": await Step11_Ingest.RunAsync(factory); break;
    case "step12": await Step12_Search.RunAsync(factory); break;
    case "step13": await Step13_ManualRag.RunAsync(factory); break;
    case "step14": await Step14_TextSearchProvider.RunAsync(factory); break;
    default:
        Console.WriteLine("Choose a step: step01 to step05 (Session 5), step06 to step10 (Session 6), step11 to step14 (Session 7)");
        break;
}
