# DurakGameCodex

Unity 6 prototype for **Podkidnoy Durak (36 cards)** with:

- Offline mode (1 human + bots)
- Online host/client flow (NGO + UTP + Relay)
- Host-authoritative intent validation
- Deterministic core rules engine with edit-mode tests

## Unity Version

- `6000.3.9f1`

## Scenes

- `Assets/Scenes/MenuScene.unity` (default build scene)
- `Assets/Scenes/MatchScene.unity`

## Packages Added

- `com.unity.netcode.gameobjects`
- `com.unity.transport`
- `com.unity.services.core`
- `com.unity.services.authentication`
- `com.unity.services.relay`

## Runtime Architecture

- `DurakGameRulesEngine`: pure rules/state transitions
- `SimpleBotStrategy`: deterministic heuristic bot
- `UnityNetworkSessionService`: Relay host/join lifecycle
- `DurakNetcodeBridge`: message-based authoritative intent sync
- `DurakAppController`: menu/lobby/match UI + orchestration

## Quick Start

1. Open project in Unity `6000.3.9f1`.
2. Wait for package import and compile.
3. Open `MenuScene`.
4. Press Play.
5. Choose one of:
   - `Start Offline (1 Human + 1 Bot)`
   - `Start Offline (1 Human + 3 Bots)`
   - `Host Session` / `Join Session` for online play.

## Notes

- Host migration is intentionally not implemented.
- On host disconnect, match is expected to end.
- In this prototype, all clients run deterministic logic from host-approved intents.
