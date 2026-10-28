# MAF Showcase (Module 3, trainer repository)

Minimal console code that shows each Module 3 concept in isolation. One branch per session, cumulative:
`session-1` (this content), `session-2`, `session-3`, `session-4`.

Fictional data only. No real patient data, ever.

## Status of this code

Written against the Microsoft Agent Framework docs and samples as of 2026-10-21.
**Not yet compiled or run.** It must be built, run with the shared key and tested at rehearsal before Session 1.

## Setup (PowerShell or Git Bash)

```bash
dotnet restore src/MafShowcase
dotnet user-secrets set "OpenAI:ApiKey" "<shared key from IT>" --project src/MafShowcase
dotnet run --project src/MafShowcase -- step01
```

Steps: `step01` first agent · `step02` run vs stream · `step03` function tools · `step04` multi-turn session · `step05` interactive console chat (type `/new` to reset the conversation, `/exit` to quit).

Session 2 steps (branch `session-2`): `step06` PHI pseudonymisation · `step07` input and output guards · `step08` tool guard · `step09` tool approval (interactive y/n) · `step10` OpenTelemetry traces to the console (trainer demo).

**Session 2 status:** written 2026-10-26 against the Learn pages for agent middleware, tool approval, structured outputs and observability. **Not compiled or run yet.** Rehearsal checks: build, `dotnet test`, then run steps 06 to 10 with the key.

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
| `Agents/GuardedWardAssistantFactory.cs` | Session 2: the whole guardrail stack, composed in the order a request travels |
| `Guardrails/PseudonymMap.cs`, `PseudonymisingChatClient.cs` | Chat client middleware: names and MRNs become placeholders on every model call and are restored on the way back |
| `Guardrails/InputGuard.cs`, `OutputGuard.cs`, `RunGuardrails.cs` | Agent run middleware: a classifier with structured output before the run, deterministic checks after it |
| `Guardrails/ToolGuard.cs`, `AdministrationValidator.cs` | Function middleware: argument checks on every tool call, after any approval |
| `Tools/RecordingTools.cs`, `AdministrationDraft.cs` | The one write tool, wrapped in `ApprovalRequiredAIFunction`; the draft as a typed contract |
| `Ward/AdministrationRecorder.cs` | Stand-in for MedTrack's existing recording handler, and `WardContext`: everything the server fixes |
| `Guardrails/WireTapChatClient.cs` | Showcase only: prints what actually leaves the process |
| `tests/.../GuardrailTests.cs` | Every guardrail proven with the scripted client, including "an approved but invalid draft is still blocked" |
