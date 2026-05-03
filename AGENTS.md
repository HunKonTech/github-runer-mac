# AGENTS (GitRunnerManager)

## MODE
- concise (default)
- ask clarification if unclear

---

## LANGUAGE CONTRACT

INPUT:
- translate user prompt → professional English
- preserve technical terms

OUTPUT:
- Hungarian
---

## RESPONSE STYLE

- minimal output
- use as few tokens as possible
- prefer 1–2 word answers when sufficient
- no explanations unless explicitly requested

## CLARIFICATION RULE

- validate requirements before implementation
- if unclear → ask user
- do NOT assume missing details

---

## PROJECT CONTEXT

APP:
- macOS menu bar app
- manages GitHub self-hosted runner

CORE FEATURES:
- start / stop runner
- status monitoring
- network-aware behavior
- resource monitoring
- auto mode

---

## ARCHITECTURE

VARIANTS:
- Swift (native macOS)
- Avalonia (multiplatform)

PATTERN:
- Services
- Stores
- Models
- Views

---

## MODULES (REFERENCE)

SWIFT:
- App → entrypoint
- Models → data
- Services → logic
- Stores → state
- Views → UI
- Settings → preferences

AVALONIA:
- App → UI
- Core → business logic
- Platform → platform-specific
- Tests → unit tests

---

## KEY SERVICES

- RunnerController → process control
- NetworkConditionMonitor → network state
- RunnerResourceMonitor → CPU / memory
- RunnerLogParser → logs
- AppUpdateService → updates

---

## TECH STACK

SWIFT:
- Swift 6
- SwiftUI (MenuBarExtra)
- SPM
- @Observable
- @MainActor

AVALONIA:
- C#
- Avalonia UI
- NuGet

---

## SYSTEM RULES

- UI code → @MainActor
- state → @Observable
- process control → Process API
- network → NWPathMonitor

---

## BUILD

SWIFT:
- ./scripts/build_and_run_swift.sh --bundle
- ./scripts/build_dmg_swfit.sh

AVALONIA:
- ./scripts/build_and_run_avalonia.sh
- ./scripts/build.sh

---

## CODE RULES

- no comments unless requested
- follow existing patterns
- keep code modular
- do not introduce new architecture without reason

---

## TESTING

- all new logic → unit tests required
- Avalonia → GitRunnerManager.Tests
- Swift → tests/

---

## REFERENCES

ENTRY:
- Swift → GitRunnerManagerApp.swift

CORE FILES:
- RunnerController.swift
- RunnerMenuStore.swift
- NetworkConditionMonitor.swift

SCRIPTS:
- scripts/build_*