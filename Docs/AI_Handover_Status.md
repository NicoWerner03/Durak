# AI Handover Status - Durak Unity Prototype

Stand: 2026-04-07  
Scope: Schritt 5 abgeschlossen.

## 1. Bereits implementiert / stabilisiert

### Projektbasis
- Unity-Projektstruktur mit getrennten Bereichen (`Core`, `Gameplay`, `Network`, `UI`, `Tests`) aufgebaut.
- Startfluss mit Menu, Lobby und Match umgesetzt.

### Regel-Engine (Core)
- Podkidnoy-Durak-Logik als reine C#-Domane umgesetzt:
  - Deck/Trumpf/Austeilen
  - Attack/Defend/Add/EndAttack/Take
  - Rundenauswertung inkl. Nachziehen
  - Matchende mit Durak und Winners
- Deterministische Snapshot-Logik (`StateSnapshot`) vorhanden.

### Offline Vertical Slice
- Offline-Spielbar mit 1 Mensch + Bots.
- Bot-Strategie (einfach heuristisch) integriert.

### Multiplayer / Netcode (Host-authoritative)
- NGO/UTP-Flow implementiert (Host validiert Intents, Clients senden Inputs).
- Custom-Messages fuer:
  - Matchstart
  - Client-Intent
  - Autoritative Intents
  - Full Snapshot / Snapshot-Request
  - Lobby Ready / State
- Relay-Host mit Direct-Fallback (`DIRECT:127.0.0.1:7777`) integriert.

### Rejoin / Identity / Lobby-Sync
- Persistente `PlayerIdentity` fuer Rejoin eingefuehrt.
- Rejoin ins laufende Match ist robust ueber Identity-Rebinding und Snapshot-Recovery.
- Lobby/Ready-Sync stabilisiert:
  - lokale `YOU`-Zuordnung ist an die echte Client-Identity gebunden
  - Ready-State wird ueber das korrekte Seat-Mapping gespiegelt
  - Reconnects bleiben nicht mehr in einer falschen Lobby-Ansicht haengen
- Session-Reset zwischen Lobbies umgesetzt.
  - Neue Lobby startet mit sauberem State statt uebernommenen Restdaten.
  - Vorherige Ready-/Seat-Zustaende werden nicht in die naechste Lobby getragen.
- Host-Disconnect-Hinweis im UI ergaenzt.
  - Client bekommt einen klaren Hinweis, wenn der Host die Sitzung beendet.

### UI / UX (laufend verbessert)
- Runtime-Canvas fuer Menu / Lobby / Match mit dynamischen Aktionsbuttons.
- InputSystem / Legacy-Font-Probleme behoben.
- Versionsanzeige unten links inklusive Build-Hash-Stamping.

### Bereits behobene Fehler (historisch)
- Fehlende NGO/UTP-Namespaces und Assemblies.
- `FastBufferWriter` / `using`-Fehler.
- Input-System-Konflikte.
- Durak-Regelfehler bei Turn-Reihenfolge und Defender-Take-Fall.
- Ergebnisanzeige / Ueberlappungen im Endscreen.

## 2. Abschlussstatus (validiert)

1. Rejoin in laufendem Match validiert.
- Rejoin-Regression wurde automatisiert und erfolgreich ausgefuehrt.
- Ergebnis: Reconnect/Resume stabil, Match-Rejoin funktioniert.

2. Lobby/Ready-Sync validiert.
- `YOU`-Zuordnung und Ready-Mapping sind in Folge-Lobbies konsistent.
- Ergebnis: Ready-Status bleibt nach Matchende/Neulobby synchron.

3. Session-Reset zwischen Lobbies validiert.
- Second-Lobby-Regressionslauf (2 Zyklen) erfolgreich.
- Ergebnis: keine Ready-/Seat-Restzustaende aus der Vor-Lobby.

4. End-to-End-Regressionen gruen.
- Voller automatisierter Lauf erfolgreich:
  - Second-Lobby
  - Stability
  - Rejoin
- Logs unter `TestResults/ScenarioRuns/*`.

## 3. Naechste konkrete Schritte

1. Relay-Flow produktionsnaeher haerten.
2. Eindeutige Statusuebergaenge Lobby -> Match -> Ende weiter schliessen.
3. Doku/Testprotokoll bei neuen Findings laufend nachziehen.

## 4. Empfohlene Testmatrix

### Offline
- 1 Human + 1 Bot: komplette Partie bis Matchende.
- 1 Human + 3 Bots: mehrere Runden, Regeln und Turnfolge pruefen.

### Online (Direct + Relay)
- 2 Spieler: Host / Client kompletter Match.
- 3 und 4 Spieler: Sync bei parallelen Inputs pruefen.
- Mehrfach Match starten / abbrechen / neu starten.

### Rejoin
- Client verlaesst laufenden Match und joint erneut innerhalb von 10s, 30s und 60s.
- Rejoin waehrend:
  - Angriffsphase
  - Verteidigungsphase
  - kurz vor Matchende
- Erwartung: Client landet wieder im laufenden Match, korrekter LocalPlayer, identischer State.

### Fehlerfaelle
- Host beendet Sitzung waehrend Match.
- Client disconnect ohne Leave, dann Reconnect.
- Ungueltige Join-Codes / Direct-Fallback testen.

## 5. Wichtige Dateien / Module

- `Assets/Scripts/Core/DurakGameRulesEngine.cs`
- `Assets/Scripts/Core/Models.cs`
- `Assets/Scripts/Core/PlayerIntent.cs`
- `Assets/Scripts/Network/DurakNetcodeBridge.cs`
- `Assets/Scripts/Network/UnityNetworkSessionService.cs`
- `Assets/Scripts/Network/NetworkSessionModels.cs`
- `Assets/Scripts/Network/LobbyIdentityResolver.cs`
- `Assets/Scripts/UI/DurakAppController.cs`
- `Assets/Scripts/UI/DurakCanvasView.cs`
- `Assets/Scripts/UI/DurakScenarioAutomation.cs`
- `Tools/run_e2e_regression.ps1`
- `Assets/Scripts/Build/BuildVersionProvider.cs`
- `Assets/Scripts/Editor/BuildVersionStampUpdater.cs`

## 6. Architektur-Reminder fuer neue Agents

- Host-authoritative beibehalten: nur Host mutiert Regeln.
- Clients senden ausschliesslich Intents.
- Snapshot ist die Recovery-Quelle bei Desync / Rejoin.
- Rejoin muss funktional unabhaengig von der Lobby-READY-Ansicht bleiben.
