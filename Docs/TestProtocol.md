# Test Protocol (Prototype)

## Functional Rules

1. Start offline `1 Human + 1 Bot`.
2. Verify legal actions are shown only for the current player.
3. Play at least one full round including:
   - Attack
   - Defend
   - Add card
   - End attack
   - Take cards
4. Complete one full match and confirm loser (`Durak`) is shown.

## Multiplayer Sync

1. Start one host and at least one client.
2. Join by relay code.
3. Start online match from host.
4. Validate:
   - Both clients show identical table progression.
   - Turns advance consistently.
   - Illegal actions are rejected by host.

## Rejoin Regression

1. Start a host and a client.
2. Reach a live match state, then disconnect the client without leaving cleanly.
3. Reconnect the same client with the same persistent `PlayerIdentity`.
4. Repeat the rejoin during each of these states:
   - Lobby before match start
   - Angriffsphase
   - Verteidigungsphase
   - After a take / round transition
   - Kurz vor Matchende
5. Validate:
   - Client returns to the laufenden Match instead of a stale Lobby view.
   - Local player ownership is restored correctly.
   - The visible table state matches the host snapshot after reconnect.
   - `YOU` and `READY` are mapped to the correct seat after reconnect.

## Second-Lobby Regression

1. Finish or abort a first lobby session with host and client.
2. Start a new lobby immediately in the same app session.
3. Validate before starting the second match:
   - No old ready state is carried over.
   - No old seat owner or local `YOU` marker remains on a stale slot.
   - All players enter the new lobby as fresh state.
4. Start the second match.
5. Validate:
   - Lobby -> Match transition works again without manual cleanup.
   - Ready state in the second lobby is independent from the first lobby.
   - Disconnect / reconnect in the second lobby behaves the same as in the first one.

## Disconnect Behavior

1. During online match, disconnect a client.
2. Confirm host remains running and player count updates in lobby / match status.
3. Disconnect host and confirm match session ends.
4. Verify the host-disconnect hint is shown clearly on the client.

## Regression (Edit Mode)

Run Unity Test Runner EditMode suite:

- `InitializeMatch_DealsSixCardsAndSetsDeckCount`
- `InitializeMatch_WithSameSeed_IsDeterministic`
- `NonCurrentPlayerIntent_IsRejected`
- `FirstAttack_CreatesTableCardAndDefenderGetsTurn`
- `DefenderLegalIntents_AlwaysContainTakeCards_WhenUndefendedAttackExists`
- `TakeCards_IsRejected_WhenAllTablePairsAreAlreadyDefended`
- `Defend_WithLowerSameSuitCard_IsRejected`
- `Defend_WithTrumpAgainstNonTrump_IsAccepted`
- `AddCard_WithNonMatchingRank_IsRejected`
- `SuccessfulDefense_ThenEndAttack_StartsNextRoundWithPreviousDefenderAsAttacker`
- `DefenderTakeCards_StartsNextRoundWithoutChangingAttacker`
- `BotPlaysDeterministicLoop_UntilMatchEnds`
- `BotStrategy_AlwaysReturnsLegalIntent`
- `InitializeMatch_CopiesPlayerIdentityFromSeats`
- `LobbyIdentityResolverTests` (complete fixture)

## Automated E2E Run (validated)

Command:

- `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -ExecutionPolicy Bypass -File C:\_dev\DurakGameCodex\Tools\run_e2e_regression.ps1 -ExePath C:\_dev\DurakGameCodex\Builds\ScenarioRunner\DurakGameCodex.exe -StabilityCycles 1 -TimeoutSeconds 420`

Validated on `2026-04-07`:

- second-lobby: passed
- stability: passed
- rejoin: passed

Logs:

- `C:\_dev\DurakGameCodex\TestResults\ScenarioRuns\second-lobby`
- `C:\_dev\DurakGameCodex\TestResults\ScenarioRuns\stability`
- `C:\_dev\DurakGameCodex\TestResults\ScenarioRuns\rejoin`
