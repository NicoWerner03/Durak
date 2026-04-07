# AI Handover Status - Durak Unity Prototype

Stand: 2026-04-07  
Scope: Schritt 4 abgeschlossen, Schritt 5 vorbereitet.

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

## 2. Aktuelle offene Punkte / nur noch manuell zu verifizieren

1. Rejoin in laufendem Match muss weiterhin gegen mehrere Zeitpunkte manuell getestet werden.
- Zu pruefen: Rejoin waehrend Angriffsphase, Verteidigungsphase und kurz vor Matchende.
- Erwartung: Client landet nach Reconnect immer wieder im laufenden Match mit korrekter Identity und identischem Snapshot.

2. Lobby/Ready-Sync braucht erneute Real-World-Validierung mit mehreren Clients.
- Zu pruefen: `YOU`-Markierung und Ready-Zuordnung bei Host, Client, Disconnect und Reconnect.
- Erwartung: Nur der eigene Slot ist als `YOU` markiert und Ready wirkt auf genau diesen Slot.

3. Session-Reset zwischen Lobbies muss im zweiten und dritten Durchlauf bestaetigt werden.
- Zu pruefen: Host beendet eine Lobby, startet direkt eine neue und alle alten Zustandsreste bleiben draussen.
- Erwartung: Keine uebernommenen Ready-, Seat- oder Match-Reste aus der Vor-Lobby.

4. Host-Disconnect-Hinweis muss im End-to-End-Run geprueft werden.
- Zu pruefen: Text / Zustand bei Host-Abbruch waehrend Lobby und waehrend Match.
- Erwartung: Client sieht einen klaren Hinweis und nicht nur einen stillen Zustand.

## 3. Naechste konkrete Schritte

### Prioritaet A
1. Rejoin-Flow in allen relevanten Spielphasen manuell durchspielen.
2. Lobby/Ready-Sync mit 2 bis 4 lokalen Clients inklusive Disconnect/Reconnect validieren.
3. Second-Lobby-Regression gegen Restzustand pruefen.

### Prioritaet B
1. Relay-Flow produktionsnaeher haerten.
2. Eindeutige Statusuebergaenge Lobby -> Match -> Ende weiter schliessen.
3. Doku / Testprotokoll bei weiteren Findings nachziehen.

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
- `Assets/Scripts/UI/DurakAppController.cs`
- `Assets/Scripts/UI/DurakCanvasView.cs`
- `Assets/Scripts/Build/BuildVersionProvider.cs`
- `Assets/Scripts/Editor/BuildVersionStampUpdater.cs`

## 6. Architektur-Reminder fuer neue Agents

- Host-authoritative beibehalten: nur Host mutiert Regeln.
- Clients senden ausschliesslich Intents.
- Snapshot ist die Recovery-Quelle bei Desync / Rejoin.
- Rejoin muss funktional unabhaengig von der Lobby-READY-Ansicht bleiben.
