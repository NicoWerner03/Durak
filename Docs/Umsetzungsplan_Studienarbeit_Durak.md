# Umsetzungsplan Studienarbeit: Durak in Unity

Stand: 23.03.2026  
Abgabeziel: 11.06.2026

## Zusammenfassung

Der Prototyp wird als onlinefähiges Multiplayer-Kartenspiel für 2-4 Spieler in Unity 6 umgesetzt, mit NGO + UTP und einem einfachen Bot.  
Verbindliches Regelset: Podkidnoy Durak (36 Karten).  
Fokus: Implementierung zuerst, schriftliche Ausarbeitung folgt separat.

## Schritt-für-Schritt-Umsetzung

1. **Projektbasis festziehen (24.03-30.03.2026)**  
   Projektstruktur für `Core`, `Gameplay`, `Network`, `UI`, `Tests` festlegen, Git-Repository initialisieren, `.gitignore` für Unity setzen, Netcode/Relay-Pakete ergänzen, Startszene in Menu + Match-Szene aufteilen.  
   Abnahmekriterium: Projekt baut ohne Fehler, saubere Ordner-/Assembly-Struktur, leere Multiplayer-Verbindung kann technisch gestartet werden.

2. **Regel-Engine als reine C#-Domäne bauen (31.03-13.04.2026)**  
   Vollständige Spiellogik ohne UI/Netzwerk implementieren: Kartendeck, Trumpf, Austeilen, Angriff/Verteidigung, Anlegen, Karten aufnehmen, Rundenende, Nachziehen, Sieger-/Verliererlogik.  
   Abnahmekriterium: alle Kernregeln laufen deterministisch in Unit-Tests.

3. **Offline Vertical Slice + Bot (14.04-27.04.2026)**  
   Spieltisch-UI, Handkarteninteraktion, Spielzustandsanzeige und Zugsteuerung integrieren; einfacher Bot mit heuristischer Kartenwahl (niedrigste gültige Karte, bei Verteidigung minimaler Gewinn).  
   Abnahmekriterium: 1 Mensch + Bot können eine vollständige Partie lokal bis zum Ende spielen.

4. **Netcode-Grundlage mit NGO/UTP (28.04-11.05.2026)**  
   Architektur auf host-authoritative Modell umstellen: Clients senden nur Eingabe-Intents, Host validiert Regeln und verteilt autoritative Zustandsupdates.  
   Abnahmekriterium: 2-4 Clients im lokalen Testnetz spielen synchron ohne Zustandsdrift.

5. **Onlinefähigkeit via Relay + Match-Flow (12.05-25.05.2026)**  
   Relay-Host/Join mit Join-Code, Match-Start-Flow, Spieler-Slots, Ready-Status und Spielstart nur bei gültiger Besetzung umsetzen.  
   Abnahmekriterium: externe Clients können per Join-Code einer Partie beitreten und vollständig spielen.

6. **Stabilisierung & UX-Qualität (26.05-02.06.2026)**  
   Fehlerfälle behandeln (ungültige Eingaben, Timeouts, Disconnect ohne Host-Migration), klare Benutzerhinweise, Runde/Match-Ende, Revanche-Flow.  
   Abnahmekriterium: keine blockerkritischen UX-/Sync-Fehler in 10+ Testpartien.

7. **Testphase & Bugfix-Freeze (03.06-08.06.2026)**  
   Systematische Testläufe, Regressionssuite, Performance-Messung (Zuglatenz, CPU/RAM bei 4 Spielern), kritische Fehler vor Freeze beheben.  
   Abnahmekriterium: definierte Akzeptanztests grün, keine offenen P1/P2-Bugs.

8. **Abgabevorbereitung Prototyp (09.06-11.06.2026)**  
   Demo-Build, reproduzierbares Testprotokoll, Install-/Startanleitung, kurze technische Architekturzusammenfassung für spätere Übernahme in den Bericht.  
   Abnahmekriterium: lauffähiger, demonstrierbarer Prototyp mit nachvollziehbarer Testdokumentation.

## Wichtige Interfaces/Typen (verbindlich)

- `Card`, `Suit`, `Rank`, `PlayerState`, `GameState`, `RoundState`, `MatchResult`
- `IGameRulesEngine`: validiert Intents und erzeugt den nächsten autoritativen Zustand
- `PlayerIntent` (Client -> Host): `Attack`, `Defend`, `AddCard`, `TakeCards`, `EndAttack`
- `StateSnapshot/StateDelta` (Host -> Clients): vollständiger bzw. inkrementeller Spielzustand
- `INetworkSessionService`: `CreateSession`, `JoinSession`, `LeaveSession`, `StartMatch`
- `IBotStrategy`: liefert gültigen Zug basierend auf aktuellem `GameState`
- Architekturregel: nur Host darf Regeln mutieren, Clients sind darstellend + eingabeorientiert

## Testplan (Akzeptanzszenarien)

1. Regeltests: Kartenverteilung, Trumpf, gültige/ungültige Angriffe, Verteidigungsregeln, Nachziehlogik.
2. Matchtests: kompletter Spielverlauf bis Sieger/Verlierer bei 2, 3 und 4 Spielern.
3. Bot-Tests: Bot erzeugt ausschließlich regelkonforme Züge.
4. Sync-Tests: parallele Aktionen mehrerer Clients führen zu identischem Endzustand.
5. Netzwerkfehler-Tests: Client-Disconnect, Reconnect innerhalb Match, Host-Abbruch mit sauberem Match-Ende.
6. Performance-Tests: stabile Framerate und akzeptable Zuglatenz bei 4 Spielern in 30-min-Session.

## Annahmen und gesetzte Defaults

- Unity-Version bleibt `6000.3.9f1`.
- Zielplattform primär: Windows-Desktop-Prototyp.
- Regelvariante: Podkidnoy Durak, 36-Karten-Deck, 2-4 Spieler.
- Host-Migration ist nicht Teil des Pflichtumfangs; bei Host-Verlust endet die Partie kontrolliert.
- Persistente Accounts/Backend-Statistiken sind nicht im Scope dieser Implementationsphase.
