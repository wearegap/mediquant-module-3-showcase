# MAF Showcase (Module 3, trainer repository)

Minimal console code that shows each Module 3 concept in isolation. One branch per session, cumulative:
`session-5` (this content), `session-6`, `session-7`, `session-8`.

Fictional data only. No real patient data, ever.

## Status of this code

Written against the Microsoft Agent Framework docs and samples as of 2026-10-21.
Module 3 sessions are numbered 5 to 8, after Module 2's sessions 1 to 4.

## Setup (PowerShell or Git Bash)

```bash
dotnet restore src/MafShowcase
dotnet user-secrets set "OpenAI:ApiKey" "<shared key from IT>" --project src/MafShowcase
dotnet run --project src/MafShowcase -- step01
```

Steps: `step01` first agent · `step02` run vs stream · `step03` function tools · `step04` multi-turn session · `step05` interactive console chat (type `/new` to reset the conversation, `/exit` to quit).

## Tests (no network, no key needed)

Create the test project once, then add the two files already in `tests/MafShowcase.Tests/`:

```bash
dotnet new xunit -o tests/MafShowcase.Tests --force
dotnet add tests/MafShowcase.Tests reference src/MafShowcase
dotnet test tests/MafShowcase.Tests
```

Delete the template's `UnitTest1.cs` after `dotnet new`. `--force` keeps the existing files.

## Layout

| Path | What it shows |
|---|---|
| `Configuration/OpenAISettings.cs` | One `OpenAI` config section; key from user-secrets, model from appsettings.json, fail fast |
| `Agents/WardAssistantFactory.cs` | The only place that builds the chat client and the agent |
| `Tools/WardTools.cs` | Read-only tools with descriptions, explicit snake_case names, delegating to a query layer |
| `Ward/WardQueries.cs` | Stand-in for MedTrack's existing query layer, fictional data |
| `Steps/` | One file per concept, run from `Program.cs` |
| `tests/.../ScriptedChatClient.cs` | Fake `IChatClient` so unit tests never call OpenAI |
