# GHCP Suite

GHCP Suite is a local Blazor Server portal for organizing GitHub Copilot CLI work around **workspaces**.

Instead of treating sessions as isolated terminal history, the suite turns them into a more structured operating model:

- choose or create a **workspace**
- keep Copilot work scoped to that workspace folder
- review sessions, agents, config, files, telemetry, and recurring prompts from one place
- resume or launch GHCP work back into a local PowerShell or Windows Terminal window

The current direction of the app is **workspace-first**:

- **Workspace pages** are where execution happens
- **Work** is the cross-workspace portfolio view
- **Dashboards** is the telemetry/analytics view
- **Sessions** is the detailed session management surface

---

## Table of contents

1. [What the app is for](#what-the-app-is-for)
2. [Core concepts](#core-concepts)
3. [Technology stack](#technology-stack)
4. [Repository structure](#repository-structure)
5. [Getting started](#getting-started)
6. [Configuration](#configuration)
7. [Data and storage](#data-and-storage)
8. [Navigation and page guide](#navigation-and-page-guide)
9. [Recommended usage patterns](#recommended-usage-patterns)
10. [Ticker model](#ticker-model)
11. [Agent model](#agent-model)
12. [Session tracking model](#session-tracking-model)
13. [Extension model](#extension-model)
14. [Architecture notes](#architecture-notes)
15. [Known behaviors and limitations](#known-behaviors-and-limitations)
16. [Developer notes](#developer-notes)

---

## What the app is for

GHCP Suite is designed for people who use GitHub Copilot CLI regularly and want a more operational way to work:

- group work under named workspaces
- keep new GHCP sessions tied to a workspace root
- inspect previous sessions without digging through raw session-state folders
- resume a session back into a local terminal
- track workflow status, priorities, tasks, notes, blockers, and next steps
- define recurring GHCP prompts with tickers
- observe workspace activity and telemetry over time
- expose a clean surface for future extensions

The app is especially useful when Copilot work is no longer "one terminal, one prompt", but an ongoing stream of:

- investigations
- implementation sessions
- planning sessions
- recurring review prompts
- custom agents
- multiple active projects

---

## Core concepts

### Workspace

A **workspace** is the main organizing unit of the suite.

Each workspace has:

- an ID
- a name
- an optional description
- a root folder

Workspaces are expected to live under a configured **workspace working directory**. New workspaces are created as subfolders under that root. Existing folders under that root can be imported explicitly as managed workspaces.

### Session

A **session** is a GHCP/Copilot CLI session discovered from local session state. The suite enriches it with structured work metadata:

- project
- workflow status
- priority
- next action
- tasks
- decisions
- blockers
- next steps
- outcome
- category

### Ticker

A **ticker** is a recurring prompt that runs GHCP on a schedule inside a workspace. It can optionally target a custom agent.

### Work

**Work** is the cross-workspace portfolio page. It is not the execution page for one workspace. It exists to help you:

- compare workspaces
- spot blocked or stale work
- jump into the right workspace
- reuse saved views and templates
- search across tracked work

### Dashboard

**Dashboards** is the telemetry page for counts, activity, time spent, and workspace-level reporting.

---

## Technology stack

- **.NET 8**
- **Blazor Server**
- **Razor Components**
- **Microsoft.Data.Sqlite** package present in the project
- local filesystem integration for settings, session-state, config files, and workspace content

Project file:

- `GHCP.Suite.csproj`

Repository folder:

- `GHCPSuite`

Target framework:

- `net8.0`

---

## Repository structure

```text
GHCPSuite/
|-- Components/
|   |-- Layout/
|   |   `-- MainLayout.razor
|   `-- Pages/
|       |-- Home.razor
|       |-- WorkspaceDetail.razor
|       |-- Work.razor
|       |-- Dashboards.razor
|       |-- Tickers.razor
|       |-- Sessions.razor
|       |-- SessionDetail.razor
|       |-- Agents.razor
|       |-- AgentDetail.razor
|       |-- Config.razor
|       |-- Settings.razor
|       |-- Extensions.razor
|       |-- Terminal.razor
|       `-- Error.razor
|-- Models/
|-- Services/
|-- wwwroot/
|-- Program.cs
`-- README.md
```

### Important folders

- `Components/Pages` - app pages and route surfaces
- `Services` - environment access, work aggregation, persistence, launch/resume, config access, ticker execution
- `Models` - DTOs and view models for workspaces, sessions, telemetry, templates, and ticker history
- `wwwroot` - styles and static assets

### Important root files

- `Program.cs` - service registration and app startup
- `%USERPROFILE%\.ghcpsuite\customSettings.json` - user-editable suite settings
- `%USERPROFILE%\.ghcpsuite\suiteData.json` - suite-owned persisted work data

---

## Getting started

### Prerequisites

- Windows
- .NET 8 SDK
- GitHub Copilot CLI installed and available on `PATH` as `copilot.exe`
- PowerShell and/or PowerShell 7
- Windows Terminal if you want resume/start actions to prefer Windows Terminal tabs

### Run locally

```powershell
dotnet restore
dotnet build
dotnet run
```

If you want a fixed URL:

```powershell
dotnet run --urls http://localhost:5184
```

### First-run setup

1. Open **Settings**
2. Set **Workspace working directory**
3. Optionally set **Startup directory**
4. Save settings
5. Return to **Workspaces**
6. Create a new workspace or import an existing folder
7. Open the workspace and start a GHCP session

---

## Configuration

Settings are loaded from `%USERPROFILE%\.ghcpsuite\customSettings.json` under the `CopilotSuite` section.

Example:

```json
{
  "CopilotSuite": {
    "WorkspaceRootDirectory": "C:\\Projects",
    "StartupDirectory": "C:\\Projects",
    "PreferWindowsTerminal": true,
    "SessionCategories": {}
  }
}
```

### Supported settings

`CopilotSuiteOptions` currently includes:

- `WorkspaceRootDirectory`
- `StartupDirectory`
- `UserProfile`
- `CopilotHome`
- `SessionStateDirectory`
- `SessionStoreDatabasePath`
- `ConfigPath`
- `SettingsPath`
- `CommandHistoryPath`
- `PreferWindowsTerminal`
- `SessionCategories`

### What these settings do

#### `WorkspaceRootDirectory`

The root folder under which managed workspaces live.

- new workspaces are created here
- available folders are discovered here
- ignored folders are tracked relative to this workflow
- each managed workspace gets a dedicated folder inside `%USERPROFILE%\.ghcpsuite` for suite-owned assets

#### `StartupDirectory`

Fallback directory for launching or resuming GHCP when a more specific directory is unavailable.

#### `PreferWindowsTerminal`

Controls whether resume/start actions should open a new **Windows Terminal** tab when possible. If Windows Terminal is not available, the suite falls back to PowerShell.

#### Path overrides

The other path settings allow the app to find local Copilot files and state in non-default locations.

---

## Data and storage

The app uses two main local files inside the per-user suite home:

### `customSettings.json`

User-edited settings file. The Settings page reads and writes this file at `%USERPROFILE%\.ghcpsuite\customSettings.json`.

### `suiteData.json`

Suite-owned persistence file. It stores data at `%USERPROFILE%\.ghcpsuite\suiteData.json`.

- workspaces
- ignored workspace folders
- active workspace selection
- ticker definitions
- ticker run history
- per-session work metadata
- saved views
- work templates
- activity history

This shape is defined by `SuiteDataDocument`.

It now also persists:

- workspace-cloned agent metadata
- ticker clone provenance

### Per-workspace suite folder

Each managed workspace keeps suite-owned artifacts in its own folder under the suite home:

```text
%USERPROFILE%\.ghcpsuite\<workspace-name>\
```

The suite currently scaffolds:

```text
%USERPROFILE%\.ghcpsuite\<workspace-name>\agents\
%USERPROFILE%\.ghcpsuite\<workspace-name>\init\
%USERPROFILE%\.ghcpsuite\<workspace-name>\tickers\
```

This keeps suite-managed files out of the project tree while preserving the selected workspace root as the execution directory.

Legacy `<workspace>\.ghcpsuite` and `<workspace>\.ghcp-suite` folders are migrated into the suite home when the suite touches a managed workspace.

Workspace-cloned agent definitions are stored canonically in:

```text
%USERPROFILE%\.ghcpsuite\<workspace-name>\agents\<workspace-agent-name>.agent.md
```

For Copilot CLI runtime compatibility, the suite mirrors those files into the Copilot user agent store before execution. The workspace copy remains the source of truth.

### Workspace ticker output

Ticker output is written into the workspace's suite data folder:

```text
%USERPROFILE%\.ghcpsuite\<workspace-name>\tickers\<ticker-name>\YYYYMMDD-HHMMSS.md
```

This keeps recurring prompt output in the permanent suite data home while still running the prompt in the actual workspace root.

### Concurrency note

`CopilotWorkDataService` serializes access to `suiteData.json` with a `SemaphoreSlim` to avoid file sharing and write contention between the web UI and background ticker execution.

---

## Navigation and page guide

The app shell uses a grouped left navigation. The default navigation is:

- **Workspace** (collapsible group)
  - Work
  - Agents
  - Tickers
  - Sessions
- **Global**
  - Agents
  - Tickers
  - Sessions
- **Suite**
  - Dashboards
  - Config
  - Settings
  - Extensions

Below is the purpose of each page, its major sections, and how to use it.

---

## Workspaces page (`/`)

**Purpose:** main landing page and the primary entry point into the suite.

This is where users choose the workspace context that should anchor new GHCP work.

### Sections

#### Summary metrics

Shows:

- total workspaces
- active workspace
- scoped sessions
- active items
- available folders
- ignored folders

Use this row as a high-level snapshot of how much tracked work exists under the configured workspace root.

#### Choose a workspace

Lists all managed workspaces.

Each row shows:

- name
- optional description
- root path
- whether it is selected
- session count
- an **Open** action

Use this when you already have managed workspaces and want to jump directly into one.

#### Create a workspace

Creates a new workspace folder under the configured workspace root.

Fields:

- **Name**
- **Description**

Behavior:

- shows a folder preview based on the configured root
- blocks creation if no workspace root is configured
- offers a shortcut to Settings when the root is missing

Use this for new projects or work streams that should get their own scoped folder and Copilot activity.

#### Available folders

Shows unmanaged folders already present under the workspace root.

Capabilities:

- real-time search
- capped result list
- **Import**
- **Ignore**

Use this when the working directory already contains folders and you want to decide which ones should become first-class workspaces.

#### Ignored folders

Shows folders that were intentionally hidden from import.

Capabilities:

- real-time search
- capped result list
- **Restore**

Use this to undo a previous ignore choice.

#### Selected workspace

Quick panel for the currently active workspace.

Actions:

- **Enter workspace**
- **Open work view**

Use this when you want to move quickly between workspace selection and the current workspace's execution surfaces.

### Best usage pattern

1. Set the workspace root in Settings
2. Create or import a workspace
3. Open the workspace
4. Start GHCP from that workspace page

---

## Workspace detail page (`/workspaces/{workspaceId}`)

**Purpose:** execution hub for one workspace.

If the Workspaces page selects context, the Workspace detail page is where you actually operate inside that context.

### Sections

#### Summary metrics

Shows:

- workspace name/root
- session count
- ticker count
- agents used
- time spent

Use this to understand the footprint and health of the selected workspace.

#### Workspace command panel

Shows workspace description and root path.

Actions:

- **Start GHCP session**
- **Create ticker**
- **Open folder**
- **Workspace dashboard**
- **Workspace work**

Use **Start GHCP session** when you want to launch a brand new Copilot CLI session directly inside the workspace folder.

#### Workspace sessions

Shows all sessions tied to this workspace.

Each row includes:

- meaningful session title
- optional description
- workflow status
- working directory
- updated time
- **Resume**

Use this when you want to continue previous work without leaving workspace context.

#### Workspace tickers

Shows all tickers assigned to this workspace.

Each row includes:

- ticker name
- prompt
- interval
- agent or GHCP mode
- next run
- last run
- current status
- **Run now**
- **Edit**
- **Enable/Disable**

Use this to review or manually execute recurring workspace automation.

#### Workspace agents

Shows workspace-local cloned agents managed by the suite.

Each row includes:

- display name
- invocation name
- definition path
- enabled/disabled status
- **Run**
- **Modify**
- **Enable/Disable**

Use this when you want to customize agent behavior per workspace without changing global agent definitions.

#### Agents used

Shows agents observed in this workspace, either through direct use or ticker assignment.

Use this to answer, "Which Copilot agents are actually part of this workspace's workflow?"

#### Recent activity

Timeline-style history of workspace events such as:

- workspace session starts
- resumes
- agent runs
- ticker runs
- config opens
- work updates

Use this as the short operational audit trail for the workspace.

#### Workspace files

Searchable explorer for the workspace root.

Capabilities:

- real-time search
- filter by **All / Folders / Files**
- capped result list
- click to open files or folders with the configured editor/shell behavior

Use this when you want lightweight navigation without leaving the suite.

### Best usage pattern

This should be the page users live in most of the time.

Typical flow:

1. open workspace
2. start or resume a session
3. inspect workspace files
4. review ticker activity
5. monitor recent activity

---

## Work page (`/work`)

**Purpose:** workspace-scoped workboard for the active workspace.

This page stays **inside** the current workspace. It should help answer:

- What needs attention in this workspace right now?
- Which sessions are active or blocked here?
- What recurring automation exists in this workspace?
- Which agents and activity have been recorded for this workspace?

### Sections

#### Workspace workboard

Introductory panel explaining that this page only shows work tied to the active workspace.

Actions:

- **Open overview**
- **Open workspace sessions**
- **Open workspace tickers**
- **Open workspace agents**

#### Metrics row

Shows:

- sessions
- active work
- blocked work
- done work
- resumed this week
- recurring runs
- tracked agents

Use this as the portfolio health strip.

#### Workspace portfolio

Comparison table for recent workspaces.

Columns:

- workspace
- last worked
- sessions
- blocked
- tickers
- time spent

Use this to decide which workspace deserves attention next.

#### Needs attention

Prioritized list of workspaces that appear:

- blocked
- stale
- currently active

Use this as the app's operational triage queue.

#### Saved views

Saved filters created from the Sessions page.

Use this when you routinely reopen the same slices of work, such as:

- blocked sessions
- research sessions
- one project only

#### Templates

Reusable work templates for session workflow data.

Default templates are seeded by the app when none exist.

Use this to standardize how you track certain types of work, such as bugfixes or planning.

#### Most-used agents

Aggregated agent usage across tracked activity.

Use this to see which agents actually matter in practice.

#### Project signals

Aggregates sessions by inferred project.

Use this when your session/project mapping still matters as a view independent of workspace boundaries.

#### Timeline

Cross-workspace activity history.

Use this when you want to reconstruct recent actions without entering each workspace.

#### Search

Cross-session search surface.

Searches tracked material such as:

- sessions
- notes
- plans
- checkpoints
- tasks
- file names

Use this when you remember a concept, note, or artifact but not the workspace or session it came from.

---

## Dashboards page (`/dashboards`)

**Purpose:** telemetry and reporting.

This page focuses on counts, scope, time spent, and recent activity by workspace.

### Sections

#### Scope

Workspace selector for narrowing the dashboard to one workspace.

Actions:

- choose **All workspaces**
- pick a single workspace
- open the selected workspace directly

Use this when you want the same dashboard surface to work globally or within one workspace.

#### Metrics row

Shows scoped:

- workspaces
- sessions
- tickers
- agents used
- time spent
- latest worked workspace

Use this to answer portfolio questions quickly.

#### Workspace focus

Visible when a single workspace is selected.

Shows:

- name
- root
- active sessions
- blocked sessions
- file count
- folder count

Use this for a compact workspace telemetry summary without leaving Dashboards.

#### Workspaces by recent activity

Table of workspace telemetry ordered by recent activity.

Use this to identify where effort is concentrated.

#### Recent activity

Scope-aware activity feed.

Use this to understand what changed in the portfolio or selected workspace recently.

---

## Tickers page (`/tickers`)

**Purpose:** manage recurring GHCP prompts.

Tickers are recurring prompts executed by a background service.

### Sections

#### Summary metrics

Shows:

- total tickers
- enabled tickers
- recent runs

#### Create / customize ticker

Form fields:

- **Name**
- **Workspace**
- **Agent (optional)**
- **Interval minutes**
- **Prompt**
- **Enable ticker after save**

The same form is also used to edit an existing ticker, including cloned tickers.

Use this when you want scheduled GHCP work such as:

- backlog review
- periodic architecture summaries
- codebase scanning
- recurring workspace health reports

#### Recent runs

Timeline of ticker executions.

Each item can include:

- completed time
- status
- workspace
- summary
- **Open output**

Use this to inspect what recurring prompts actually produced.

#### Saved tickers

Operational list of all ticker definitions.

Actions:

- **Run now**
- **Edit**
- **Clone**
- **Enable/Disable**
- **Delete**

Cloned tickers start disabled so they can be customized safely before they begin running in the target workspace.

Use **Run now** for ad hoc execution without waiting for schedule.

### Best usage pattern

Keep tickers narrow and workspace-specific. The best ticker prompts usually:

- operate on one workspace
- have a focused objective
- produce output worth saving

Examples:

- "Review the backlog and propose the next 3 actions."
- "Summarize recent sessions and identify blockers."
- "Inspect open work items and suggest a validation checklist."

---

## Sessions page (`/sessions`)

**Purpose:** organize and browse all discovered sessions.

This is the broadest session inventory page.

### Sections

#### Organize work

Filter and grouping controls:

- search
- project
- status
- category
- group by
- save current view
- clear filters

Saved views appear here as reusable chips with delete actions.

Use this page when you want to slice the session inventory differently from workspace context.

#### Sessions by group

Expandable groups based on the selected grouping mode:

- project
- category
- status

Table columns:

- session
- workflow
- project
- category
- priority
- tasks
- updated
- actions

Important actions:

- inline category editing
- **Resume**
- **Open**

### Best usage pattern

Use Sessions when you want to:

- bulk review session health
- group work by category or status
- maintain categories
- save reusable filters

---

## Session detail page (`/sessions/{sessionId}`)

**Purpose:** structured work tracking for one GHCP session.

This page combines raw session context with suite-owned workflow metadata.

### Sections

#### Session summary

Shows:

- session id
- project
- workflow
- priority
- category
- open/completed tasks
- working directory
- updated time
- session folder

Actions:

- category editing
- **Resume in Terminal**
- **Save work data**

Use this as the main "session operating record".

#### Workflow

Editable fields:

- project
- status
- priority
- next action
- template selection and apply

Use this to keep a session in a meaningful operational state.

#### Tasks

Structured task list with:

- title
- status
- dependency
- carry forward flag

Use this when a session becomes more than a single next step and needs lightweight work management.

#### Notes and decision log

Editable text areas for:

- decisions
- blockers
- next steps
- outcome

Use this to preserve context that would otherwise stay trapped in terminal history.

#### Session files

Tree-style view of files under the session directory.

Use this to inspect session-local artifacts such as plans, checkpoints, or generated files.

#### Workspace

Preview of workspace content associated with the session.

#### Plan

Preview of `plan.md` content when present.

#### Checkpoint index

Preview of checkpoint index content.

#### Recent events

Flattened event log for the session.

### Best usage pattern

Use Session detail after an important session to convert raw activity into durable structure:

1. set status and priority
2. write next action
3. add tasks
4. capture blockers/decisions
5. save

---

## Agents page (`/agents`)

**Purpose:** browse installed Copilot agents and clone them into workspaces.

The page separates:

- **Workspace clones**
- **Custom** agents
- **Default** agents

### Sections

#### Installed Copilot agents

Main table includes:

- agent display name
- description
- kind
- model
- tool access
- source
- enabled/disabled state
- package version
- definition path
- actions

Actions:

- **Run**
- **Clone**
- **Modify** for custom agents
- **Enable/Disable** for workspace-cloned agents

### Notes

- suite-managed workspace assets belong under `%USERPROFILE%\.ghcpsuite\<workspace-name>\`
- workspace clones are mirrored into `~/.copilot/agents\ghcpsuite\...` only so Copilot CLI can run them without polluting the workspace root
- Copilot-native global custom agents are still typically discovered from `~/.copilot/agents` or legacy `.github/agents`
- built-in agents come from the Copilot CLI package definitions

### Best usage pattern

Use this page to audit what agents are installed locally, clone them into a workspace, and manage the enabled state of workspace-specific variants.

---

## Agent detail page (`/agents/{agentKey}`)

**Purpose:** inspect and run one agent.

### Sections

#### Agent summary

Shows:

- name
- kind
- model
- tools
- source
- package
- definition path
- enabled/disabled state
- workspace scope when applicable
- whether it is custom

Actions:

- **Run**
- **Clone**
- **Modify** for custom agents
- **Enable/Disable** for workspace-cloned agents

#### Definition file

Raw preview of the agent definition file content.

Use this page when you want a detailed look at one agent before running or editing it.

---

## Config page (`/config`)

**Purpose:** inspect and open important GHCP-related config files.

### Sections

#### Known files

Selector list of known config files discovered by the app.

#### Selected file detail

Shows:

- file category
- path
- existence
- last modified time
- content preview

Action:

- **Edit**

Use this page when you need to inspect or edit configuration without manually hunting for file paths.

---

## Settings page (`/settings`)

**Purpose:** manage suite settings and path resolution.

### Sections

#### Suite settings file

Editable fields for runtime configuration.

Key fields:

- workspace working directory
- startup directory
- user profile override
- Copilot home override
- session-state override
- session store override
- config/settings/history path overrides
- prefer Windows Terminal

Actions:

- **Save settings**
- **Reload file**

#### Effective runtime paths

Read-only view of the resolved paths the app is actually using.

Use this when diagnosing why a file, session, or config source is not showing up.

#### Session categories

Explains that categories are stored in `%USERPROFILE%\.ghcpsuite\customSettings.json` and managed primarily from the Sessions page.

### Best usage pattern

Visit Settings first on a new machine or when the suite does not discover the expected local Copilot state.

---

## Extensions page (`/extensions`)

**Purpose:** describe and expose the app's extension surface.

### Sections

#### Registered module providers

Lists each `ISuiteModuleProvider` registered in DI and the modules it contributes.

#### Module surface

Lists:

- title
- route
- description

for all registered modules.

#### Extension note

Documents the extension direction:

- add another `ISuiteModuleProvider`
- return additional `SuiteModuleDescriptor` values
- extend navigation without rewriting the shell

Use this page as the current contract for extending the suite.

---

## Terminal page (`/terminal`)

**Purpose:** legacy informational page.

The embedded browser terminal approach was removed.

Current behavior:

- the page explains that terminal actions should happen through **Resume**
- sessions now resume in a local PowerShell or Windows Terminal window instead

This page exists mainly to document the change in approach.

---

## Error page (`/Error`)

Standard Blazor error surface for unhandled exceptions.

---

## Recommended usage patterns

### 1. Workspace-first daily flow

Use this for normal day-to-day work.

1. Open **Change workspace**
2. Select or create a workspace
3. Open the workspace detail page
4. Start a GHCP session from there
5. Use **Workspace > Work**, **Workspace > Sessions**, **Workspace > Tickers**, and **Workspace > Agents** to stay inside that context

### 2. Portfolio triage flow

Use this when you want to decide where to focus next.

1. Open **Dashboards**
2. Review workspace-level telemetry
3. Choose the workspace to switch into
4. Return to **Workspace > Work** for scoped execution

### 3. Telemetry/reporting flow

Use this for oversight or retrospectives.

1. Open **Dashboards**
2. Review all-workspace metrics
3. Scope to one workspace if needed
4. Inspect recent activity and time spent

### 4. Session curation flow

Use this after important sessions.

1. Open **Sessions**
2. Find the session
3. Open **Session detail**
4. Set status, priority, next action
5. Add tasks and notes
6. Save work data

### 5. Automation flow with tickers

Use this for recurring analysis or routine prompts.

1. Open a workspace
2. Use **Create ticker**
3. Define the prompt and interval
4. Let the background scheduler run it
5. Inspect output from **Recent runs**

### 6. Agent governance flow

Use this when maintaining local custom agents.

1. Open **Agents**
2. Switch between **Custom** and **Default**
3. Inspect a custom agent in **Agent detail**
4. Modify the definition if needed
5. Re-run it from the suite

---

## Ticker model

Tickers are implemented by `CopilotTickerService`, which acts as both:

- an application service for CRUD and queueing
- a hosted background scheduler

### Execution behavior

When a ticker runs, the service:

1. loads the ticker definition
2. resolves the target workspace
3. ensures the workspace root exists
4. marks the ticker as running
5. executes `copilot.exe` in non-interactive mode
6. optionally adds `--agent <name>`
7. writes output to the workspace's suite data folder
8. records ticker run history
9. records activity history

### Important CLI arguments used

Ticker execution currently uses arguments like:

- `-C <workspace-root>`
- `-p <prompt>`
- `--allow-all`
- `--no-ask-user`
- `--name ticker:<name>`
- `--share <output-path>`
- optional `--agent <agent-name>`

### Good ticker candidates

- backlog review
- issue triage
- architecture summary
- dependency or file inventory review
- release checklist prompts

### Bad ticker candidates

- prompts that require interactive clarification
- extremely broad prompts with no workspace focus
- anything unsafe to run unattended

---

## Agent model

Agents are discovered through the local Copilot installation, global custom agent locations, and workspace-cloned definitions tracked by the suite.

The suite distinguishes:

- built-in/default agents
- global custom agents
- workspace-cloned agents

Workspace clones are:

1. stored in `%USERPROFILE%\.ghcpsuite\<workspace-name>\agents`
2. tracked in `%USERPROFILE%\.ghcpsuite\suiteData.json` with workspace-specific enabled state
3. mirrored into the Copilot user-agents area right before launch or ticker execution so they remain runnable

Activity for agent runs is recorded into suite work history so the app can surface:

- top agents
- recent agent activity
- workspace-level agent usage

---

## Session tracking model

The suite separates:

1. **raw session data** discovered from Copilot state
2. **structured work metadata** owned by the suite

This structured metadata includes:

- project
- status
- priority
- next action
- tasks
- decisions
- blockers
- next steps
- outcome

This is what lets the app behave less like a file browser and more like a work operating system.

---

## Extension model

The current extension seam is navigation/module based.

To extend the shell:

1. implement `ISuiteModuleProvider`
2. register it in DI
3. return additional `SuiteModuleDescriptor` values

This allows new top-level pages or module groups to participate in the shell without hardcoding them into the layout.

Current default modules are registered by `DefaultSuiteModuleProvider`.

Future extension directions could include:

- alternate session providers
- remote agent providers
- richer analytics providers
- workspace health modules
- deeper command integrations

---

## Architecture notes

### Application startup

`Program.cs` configures:

- Razor components with interactive server rendering
- settings service
- environment service
- agent catalog/run services
- work data and work aggregation services
- ticker service as both singleton and hosted service
- workspace launch service
- session service
- config services
- resume service
- module provider

### Layout

`MainLayout.razor` builds navigation from module providers rather than hardcoding each link directly into the layout.

### Persistence responsibilities

#### `CopilotWorkDataService`

Owns `%USERPROFILE%\.ghcpsuite\suiteData.json` read/write and normalization.

#### `CopilotWorkService`

Owns aggregation and higher-level work/workspace/dashboard behaviors.

#### `CopilotSettingsService`

Owns `%USERPROFILE%\.ghcpsuite\customSettings.json` reads/writes and settings-facing operations.

### Launch/resume responsibilities

#### `CopilotWorkspaceLaunchService`

Starts a new GHCP session in a workspace root.

#### `CopilotResumeService`

Resumes an existing session using `copilot --resume=<sessionId>`.

Both services:

- prefer Windows Terminal when configured and available
- fall back to PowerShell otherwise
- use encoded PowerShell commands

---

## Known behaviors and limitations

- The embedded browser terminal was intentionally removed.
- Resume/start actions depend on the local machine having the required terminal tools available.
- The app is Windows-oriented in its current launcher behavior.
- Tickers are non-interactive by design.
- Ticker execution assumes GHCP prompts can safely run with the current non-interactive flags.
- `suiteData.json` access is serialized, but the overall data model is still file-based rather than transactional.
- Workspace telemetry is derived from tracked data and session timing heuristics; it is not a full time-tracking system.
- Existing folders under the workspace root are not auto-promoted into workspaces; they must be imported explicitly.

---

## Developer notes

### If you are adding a new page

Recommended checklist:

1. create the Razor page under `Components/Pages`
2. wire required services through DI if needed
3. add a module descriptor through a module provider if it should appear in navigation
4. decide whether the page is:
   - workspace-scoped
   - cross-workspace
   - global utility/config
5. record relevant actions into activity history if the page triggers durable work events

### If you are changing workspace behavior

Respect the current product direction:

- workspaces are the primary operating context
- new GHCP activity should originate from or map back to a workspace whenever possible

### If you are changing persistence

Be careful with:

- `suiteData.json` normalization
- existing saved data compatibility
- ticker run history retention
- background ticker access patterns

### If you are changing launch behavior

Review both:

- `CopilotWorkspaceLaunchService`
- `CopilotResumeService`

They intentionally share the same Windows Terminal / PowerShell fallback model.

---

## Summary

GHCP Suite is a local workspace-first operations portal for GitHub Copilot CLI.

Use:

- **Workspaces** to choose context
- **Workspace detail** to execute work
- **Work** to triage across workspaces
- **Dashboards** to measure and report
- **Sessions** to curate session records
- **Tickers** to automate recurring prompts
- **Agents**, **Config**, and **Settings** to maintain the environment around that work

The repo is structured so the suite can keep growing into a more organized, extensible operating surface for GHCP-heavy workflows.
