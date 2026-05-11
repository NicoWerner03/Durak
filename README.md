# DurakGame

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

## Quick Start (Unity Editor)

1. Open project in Unity `6000.3.9f1`.
2. Wait for package import and compile.
3. Open `MenuScene`.
4. Press Play.
5. Choose one of:
   - `Start Offline (1 Human + 1 Bot)`
   - `Start Offline (1 Human + 3 Bots)`
   - `Host Session` / `Join Session` for online play.

## Run From Pre-Built Executable (Windows)

A ready-to-run Windows build is included in the `Builds/` folder — no
Unity installation required.

1. Open the `Builds/` folder.
2. Double-click `DurakGame.exe`.
3. Press `ESC` at any time to open the pause menu (Resume / Options /
   Leave Match / Quit Game).

A second build at `Builds/ScenarioRunner/DurakGame.exe` is the same game
compiled with the automation hooks used by the regression scripts in
`Tools/`. For normal play, use the top-level `Builds/DurakGame.exe`.

## Notes

- Host migration is intentionally not implemented.
- On host disconnect, match is expected to end.
- In this prototype, all clients run deterministic logic from host-approved intents.
