# School AI Support Agent

A **.NET 10** reference implementation of a school-facing AI assistant: role-aware chat, lightweight document retrieval, support ticketing, and helper tools—built with **ASP.NET Core Minimal APIs**, **Microsoft.Extensions.AI**, **Azure OpenAI**, optional **.NET Aspire** orchestration, and **DevUI** for debugging agent flows.

This repository is suitable as a starting point for a real deployment; tighten security, persistence, and identity before production use.

---

## Table of contents

- [What it does](#what-it-does)
- [Features](#features)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Setup](#setup)
- [Azure OpenAI configuration](#azure-openai-configuration)
- [Adding school documents](#adding-school-documents)
- [API reference (examples)](#api-reference-examples)
- [Observability](#observability)
- [Screenshots](#screenshots)
- [Roadmap](#roadmap)
- [Security notes](#security-notes)
- [License](#license)

---

## What it does

The **School AI Support Agent** helps **students**, **teachers**, **parents**, and **admin** staff with common questions. It:

- Answers in **Arabic first**, switching to **English** when the user writes in English.
- Pulls **grounding context** from your own **`.txt` / `.md`** files under a `KnowledgeBase` folder (keyword search, not vectors).
- Keeps **short in-memory conversation history** per `conversationId`.
- Offers **REST APIs** for **support tickets**, **quiz generation**, and **document summarization** using the same Azure OpenAI deployment.

---

## Features

| Area | Description |
|------|-------------|
| **Chat** | `POST /api/chat` with `message`, `userRole`, optional `conversationId` / `userName`; validated input and structured JSON response. |
| **Prompts** | Centralized system prompt + per-role supplements (`PromptBuilderService`). |
| **Knowledge base** | Files loaded at startup, chunked, scored by keywords; excerpts injected into the agent prompt or explicit “no school info” instruction. |
| **Support tickets** | In-memory store; create and fetch by id; admin list with header gate (see [Security notes](#security-notes)). |
| **Tools** | Quiz generation and school-text summarization via `IChatClient`. |
| **Dev / ops** | **DevUI** at `/devui`; **OpenTelemetry** for AI/agent spans; **Aspire AppHost** for dashboard and multi-service layout. |

---

## Architecture

```
src/
├── AppHost/                 # .NET Aspire orchestrator (optional run experience)
├── ServiceDefaults/         # Shared HTTP resilience, service discovery, OpenTelemetry
└── WebApi/                  # Minimal API: agent, chat, knowledge base, tools
    ├── KnowledgeBase/       # School documents (.txt, .md) → copied to output
    ├── Models/
    ├── Services/
    └── Program.cs           # Endpoints and DI registration
```

**Request flow (chat)**

1. Client calls `POST /api/chat`.
2. `AgentService` loads recent turns from `ConversationMemoryService`.
3. `SchoolKnowledgeService` retrieves top keyword-matched chunks from loaded documents.
4. `SupportRequestService` may add escalation hints for sensitive keywords.
5. `PromptBuilderService` supplies role-specific instructions in the user payload.
6. Keyed `AIAgent` runs against **Azure OpenAI** via `IChatClient`.
7. Assistant reply and user message are appended to memory.

---

## Prerequisites

- [.NET SDK 10](https://dotnet.microsoft.com/download) (or the version targeted by the repo)
- An **Azure OpenAI** resource with a chat-capable deployment
- For local auth as configured in code: [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) login (`AzureCliCredential`)
- Optional: **.NET Aspire** workload for running `AppHost`

---

## Setup

### 1. Clone and restore

```bash
git clone <your-fork-or-upstream-url>
cd minimal-agent
dotnet restore src/MinimalAgent.slnx
```

### 2. Configure environment

Set Azure OpenAI variables (see next section). You can use user-secrets, a `.env` file (not committed; see `.gitignore`), or your IDE launch profile.

### 3. Run the API

**Option A — Web API only**

```bash
cd src/WebApi
dotnet run
```

Default HTTP URL is often `http://localhost:5043` (see `Properties/launchSettings.json`).

**Option B — Aspire (dashboard + Web API)**

```bash
dotnet run --project src/AppHost/AppHost.csproj
```

Use the Aspire dashboard link for traces; open **DevUI** at `{webapi-base-url}/devui` (the AppHost also adds a convenience URL in the dashboard when configured).

---

## Azure OpenAI configuration

| Variable | Required | Description |
|----------|----------|-------------|
| `AZURE_OPENAI_ENDPOINT` | **Yes** | Resource endpoint, e.g. `https://your-resource.openai.azure.com/` |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | No | Defaults to `gpt-5-mini` if unset |

**Example (PowerShell)**

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "your-chat-deployment-name"
```

**Example (bash)**

```bash
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_DEPLOYMENT_NAME="your-chat-deployment-name"
```

The sample code uses `AzureCliCredential` for local development. For production, replace with a managed identity or another credential strategy appropriate to your host.

---

## Adding school documents

1. Add or edit files under:

   `src/WebApi/KnowledgeBase/`

2. Supported extensions: **`.txt`** and **`.md`** (any subfolder is allowed).

3. Files are included in the build output (`WebApi.csproj` copies `KnowledgeBase/**`).

4. Restart the application after changing files so `SchoolKnowledgeStartupLoader` reloads content from disk.

5. Retrieval is **keyword-based** (no embeddings). Use clear headings and vocabulary your users will actually type (including Arabic and English terms where relevant).

---

## API reference (examples)

Base URL in samples: `http://localhost:5043` (adjust for your environment).

### Chat

```http
POST /api/chat
Content-Type: application/json

{
  "message": "متى موعد الاختبار؟",
  "userRole": "student",
  "conversationId": "abc123",
  "userName": "Ali"
}
```

**Success (shape)**

```json
{
  "conversationId": "abc123",
  "response": "…"
}
```

Roles: `student` | `teacher` | `parent` | `admin` (JSON camelCase enums).

### Create support ticket

```http
POST /api/support/requests
Content-Type: application/json

{
  "issue": "Cannot access LMS after password reset.",
  "category": "technical",
  "priority": "high",
  "userRole": "teacher",
  "userName": "Samira"
}
```

Categories: `academic` | `attendance` | `technical` | `admin` | `other`  
Priorities: `low` | `medium` | `high`

### Get ticket by id

```http
GET /api/support/requests/{id}
```

### List all tickets (admin)

```http
GET /api/admin/support-requests
X-User-Role: admin
```

### Generate quiz

```http
POST /api/tools/generate-quiz
Content-Type: application/json

{
  "topic": "Photosynthesis for grade 8",
  "questionCount": 3
}
```

Provide **`topic` and/or `sourceText`**.

### Summarize school document text

```http
POST /api/tools/summarize-document
Content-Type: application/json

{
  "text": "Students must submit absence notes within 48 hours..."
}
```

More requests are collected in [`src/WebApi/WebApi.http`](src/WebApi/WebApi.http) for IDE REST clients.

---

## Observability

- **OpenTelemetry**: HTTP and runtime metrics; tracing includes wildcard sources for `Microsoft.Extensions.AI` and `Microsoft.Extensions.Agents` (see `ServiceDefaults/Extensions.cs`).
- **Sensitive data**: The chat client enables OpenTelemetry **sensitive** payload capture for local debugging—**disable or redact for production**.
- **DevUI**: Inspect agent-related activity at `/devui` during development.

---

## Screenshots

Replace these placeholders with your own images (e.g. under `docs/images/`).

| Placeholder | Suggested caption |
|-------------|-------------------|
| ![Chat / API test](docs/images/01-chat-placeholder.png) | Example chat or REST client calling `/api/chat` |
| ![DevUI](docs/images/02-devui-placeholder.png) | DevUI agent trace or event view |
| ![Aspire Dashboard](docs/images/03-aspire-placeholder.png) | Aspire dashboard traces (optional) |

```markdown
<!-- Example once you add real files:
![Chat](docs/images/chat.png)
![DevUI](docs/images/devui.png)
-->
```

---

## Roadmap

Ideas for hardening and extending this sample:

- **Retrieval**: Embeddings + vector store; hybrid search; citation links.
- **Persistence**: Database for tickets, conversations, and audit logs.
- **Auth**: OAuth2 / Entra ID; map claims to `userRole`; remove header-only admin checks.
- **Streaming**: SSE or chunked responses for `/api/chat`.
- **Rate limiting** and abuse controls; content safety filters.
- **Configuration**: Key Vault, deployment-specific prompts, feature flags.
- **Tests**: Integration tests against Azure OpenAI or a mock `IChatClient`.

Contributions via issues and PRs are welcome.

---

## Security notes

This project prioritizes **clarity and local development** over production hardening. Before exposing to the internet:

| Topic | Current behavior | Recommendation |
|--------|------------------|----------------|
| **Admin API** | `X-User-Role: admin` header | Replace with real authentication and authorization. |
| **Support tickets** | In-memory only | Persist securely; restrict who can read PII. |
| **Credentials** | `AzureCliCredential` in sample | Use managed identity or workload identity in Azure. |
| **Telemetry** | May log prompts/completions | Turn off sensitive capture in production; control OTLP endpoints. |
| **Chat input** | Length-limited but not scanned | Add moderation, PII policies, and school-specific safeguards as needed. |
| **Knowledge base** | File-based, trusted content | Treat uploads as sensitive; version and review documents. |

Do not commit secrets. Use environment variables, Key Vault, or CI secret stores.

---

## License

This project is licensed under the **MIT License** — see the [`LICENSE`](LICENSE) file for the full text.

Copyright (c) 2026 Hazem Alyaari

---

## Acknowledgments

Built with **ASP.NET Core**, **Microsoft.Extensions.AI**, **Azure OpenAI**, and **Microsoft Agents** packages. Optional orchestration via **.NET Aspire**.
