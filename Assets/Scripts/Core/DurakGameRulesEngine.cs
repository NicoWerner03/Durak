using System;
using System.Collections.Generic;

namespace DurakGame.Core
{
    public class DurakGameRulesEngine : IGameRulesEngine
    {
        private const int InitialHandSize = 6;
        private const int MinPlayers = 2;
        private const int MaxPlayers = 4;

        private readonly List<Card> _drawPile = new List<Card>();
        private readonly List<Card> _discardPile = new List<Card>();
        private readonly HashSet<int> _passedAttackers = new HashSet<int>();
        private int _attackerCursor;
        private int _sequence;

        public GameState State { get; private set; } = new GameState();

        public void InitializeMatch(IReadOnlyList<PlayerSeat> seats, int seed)
        {
            if (seats == null)
            {
                throw new ArgumentNullException(nameof(seats));
            }

            if (seats.Count < MinPlayers || seats.Count > MaxPlayers)
            {
                throw new ArgumentOutOfRangeException(nameof(seats), "Durak requires 2-4 players.");
            }

            State = new GameState
            {
                Phase = GamePhase.InRound,
                TurnSequence = 0,
                CurrentTurnPlayerId = -1,
            };

            _drawPile.Clear();
            _discardPile.Clear();
            _passedAttackers.Clear();
            _attackerCursor = 0;
            _sequence = 0;

            var orderedSeats = new List<PlayerSeat>(seats);
            orderedSeats.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

            for (var i = 0; i < orderedSeats.Count; i++)
            {
                var seat = orderedSeats[i];
                var player = new PlayerState
                {
                    PlayerId = seat.PlayerId,
                    DisplayName = string.IsNullOrWhiteSpace(seat.DisplayName) ? "Player " + (i + 1) : seat.DisplayName,
                    IsBot = seat.IsBot,
                    OwnerClientId = seat.OwnerClientId,
                    PlayerIdentity = seat.PlayerIdentity ?? string.Empty,
                    IsConnected = true,
                    Hand = new List<Card>(),
                };

                State.Players.Add(player);
                State.PlayerOrder.Add(player.PlayerId);
            }

            BuildAndShuffleDeck(seed);
            State.TrumpSuit = _drawPile[0].Suit;

            DealInitialHands();
            State.DeckCount = _drawPile.Count;

            var attackerId = FindLowestTrumpAttacker();
            var defenderId = FindNextPlayerWithCards(attackerId, false);
            if (defenderId < 0)
            {
                throw new InvalidOperationException("Could not choose defender.");
            }

            SetupRound(attackerId, defenderId);
            RecomputeTurn();
        }

        public IReadOnlyList<PlayerIntent> GetLegalIntents(int playerId)
        {
            var intents = new List<PlayerIntent>();

            if (State.Phase != GamePhase.InRound)
            {
                return intents;
            }

            if (playerId != State.CurrentTurnPlayerId)
            {
                return intents;
            }

            var player = State.GetPlayer(playerId);
            if (player == null)
            {
                return intents;
            }

            if (playerId == State.Round.DefenderId && HasUndefendedAttacks())
            {
                for (var pairIndex = 0; pairIndex < State.Round.Table.Count; pairIndex++)
                {
                    var pair = State.Round.Table[pairIndex];
                    if (pair.IsDefended)
                    {
                        continue;
                    }

                    for (var cardIndex = 0; cardIndex < player.Hand.Count; cardIndex++)
                    {
                        var defenseCard = player.Hand[cardIndex];
                        if (CanBeat(defenseCard, pair.AttackCard))
                        {
                            intents.Add(PlayerIntent.Defend(playerId, pairIndex, defenseCard));
                        }
                    }
                }

                intents.Add(PlayerIntent.TakeCards(playerId));
                return intents;
            }

            var tableEmpty = State.Round.Table.Count == 0;
            if (tableEmpty)
            {
                if (playerId != State.Round.AttackerId)
                {
                    return intents;
                }

                for (var i = 0; i < player.Hand.Count; i++)
                {
                    intents.Add(PlayerIntent.Attack(playerId, player.Hand[i]));
                }
            }
            else
            {
                if (!IsAttackerForRound(playerId))
                {
                    return intents;
                }

                for (var i = 0; i < player.Hand.Count; i++)
                {
                    if (CanAddAttackCard(player.Hand[i]))
                    {
                        intents.Add(PlayerIntent.AddCard(playerId, player.Hand[i]));
                    }
                }

                intents.Add(PlayerIntent.EndAttack(playerId));
            }

            return intents;
        }

        public IntentResult ApplyIntent(PlayerIntent intent)
        {
            if (intent == null)
            {
                return IntentResult.Reject("Intent is null.", State);
            }

            if (State.Phase != GamePhase.InRound)
            {
                return IntentResult.Reject("Match is not running.", State);
            }

            if (intent.PlayerId != State.CurrentTurnPlayerId)
            {
                return IntentResult.Reject("It is not this player's turn.", State);
            }

            var success = false;
            string error;
            switch (intent.Type)
            {
                case PlayerIntentType.Attack:
                    success = TryApplyAttack(intent, out error);
                    break;
                case PlayerIntentType.Defend:
                    success = TryApplyDefend(intent, out error);
                    break;
                case PlayerIntentType.AddCard:
                    success = TryApplyAddCard(intent, out error);
                    break;
                case PlayerIntentType.TakeCards:
                    success = TryApplyTakeCards(intent, out error);
                    break;
                case PlayerIntentType.EndAttack:
                    success = TryApplyEndAttack(intent, out error);
                    break;
                default:
                    error = "Unknown intent type.";
                    break;
            }

            if (!success)
            {
                return IntentResult.Reject(error, State);
            }

            _sequence += 1;
            State.TurnSequence = _sequence;

            return new IntentResult
            {
                Accepted = true,
                Error = string.Empty,
                Snapshot = new StateSnapshot
                {
                    Sequence = _sequence,
                    State = State.Clone(),
                },
                Delta = new StateDelta
                {
                    Sequence = _sequence,
                    AppliedIntent = intent.Clone(),
                },
            };
        }

        public StateSnapshot CreateSnapshot()
        {
            return new StateSnapshot
            {
                Sequence = _sequence,
                State = State.Clone(),
            };
        }

        public void RestoreSnapshot(StateSnapshot snapshot)
        {
            if (snapshot == null || snapshot.State == null)
            {
                return;
            }

            State = snapshot.State.Clone();
            _sequence = snapshot.Sequence > 0 ? snapshot.Sequence : State.TurnSequence;

            if (State.Round == null)
            {
                State.Round = new RoundState();
            }

            if (State.PlayerOrder == null)
            {
                State.PlayerOrder = new List<int>();
            }

            if (State.Players == null)
            {
                State.Players = new List<PlayerState>();
            }

            if (State.Round.AttackerOrder == null)
            {
                State.Round.AttackerOrder = new List<int>();
            }

            if (State.Round.Table == null)
            {
                State.Round.Table = new List<TablePair>();
            }

            _passedAttackers.Clear();
            _attackerCursor = 0;
            if (State.Round.AttackerOrder.Count > 0)
            {
                var index = State.Round.ActiveAttackerIndex;
                if (index < 0 || index >= State.Round.AttackerOrder.Count)
                {
                    index = 0;
                }

                _attackerCursor = index;
            }
        }

        private void BuildAndShuffleDeck(int seed)
        {
            _drawPile.Clear();

            for (var suit = (int)Suit.Clubs; suit <= (int)Suit.Spades; suit++)
            {
                for (var rank = (int)Rank.Six; rank <= (int)Rank.Ace; rank++)
                {
                    _drawPile.Add(new Card((Suit)suit, (Rank)rank));
                }
            }

            var random = new Random(seed);
            for (var i = _drawPile.Count - 1; i > 0; i--)
            {
                var swapIndex = random.Next(i + 1);
                var tmp = _drawPile[i];
                _drawPile[i] = _drawPile[swapIndex];
                _drawPile[swapIndex] = tmp;
            }
        }

        private void DealInitialHands()
        {
            for (var cardRound = 0; cardRound < InitialHandSize; cardRound++)
            {
                for (var i = 0; i < State.PlayerOrder.Count; i++)
                {
                    if (_drawPile.Count <= 0)
                    {
                        return;
                    }

                    var playerId = State.PlayerOrder[i];
                    var player = State.GetPlayer(playerId);
                    player.Hand.Add(DrawTopCard());
                }
            }
        }

        private Card DrawTopCard()
        {
            var lastIndex = _drawPile.Count - 1;
            var card = _drawPile[lastIndex];
            _drawPile.RemoveAt(lastIndex);
            State.DeckCount = _drawPile.Count;
            return card;
        }

        private int FindLowestTrumpAttacker()
        {
            var bestPlayerId = State.PlayerOrder[0];
            var bestRank = int.MaxValue;

            for (var i = 0; i < State.Players.Count; i++)
            {
                var player = State.Players[i];
                for (var cardIndex = 0; cardIndex < player.Hand.Count; cardIndex++)
                {
                    var card = player.Hand[cardIndex];
                    if (card.Suit != State.TrumpSuit)
                    {
                        continue;
                    }

                    if ((int)card.Rank < bestRank)
                    {
                        bestRank = (int)card.Rank;
                        bestPlayerId = player.PlayerId;
                    }
                }
            }

            return bestPlayerId;
        }

        private void SetupRound(int attackerId, int defenderId)
        {
            var nextRoundNumber = State.Round != null ? State.Round.RoundNumber + 1 : 1;
            var defender = State.GetPlayer(defenderId);

            State.Round = new RoundState
            {
                RoundNumber = nextRoundNumber,
                AttackerId = attackerId,
                DefenderId = defenderId,
                DefenderInitialHandCount = defender != null ? defender.Hand.Count : 0,
                AttackLimit = Math.Min(InitialHandSize, defender != null ? defender.Hand.Count : 0),
                AttackerOrder = BuildAttackerOrder(attackerId, defenderId),
                Table = new List<TablePair>(),
                ActiveAttackerIndex = 0,
            };

            if (State.Round.AttackLimit <= 0)
            {
                State.Round.AttackLimit = 1;
            }

            _passedAttackers.Clear();
            _attackerCursor = 0;
        }

        private List<int> BuildAttackerOrder(int attackerId, int defenderId)
        {
            var order = new List<int>();
            var attackerIndex = State.GetPlayerOrderIndex(attackerId);
            if (attackerIndex < 0)
            {
                return order;
            }

            for (var offset = 0; offset < State.PlayerOrder.Count; offset++)
            {
                var index = (attackerIndex + offset) % State.PlayerOrder.Count;
                var playerId = State.PlayerOrder[index];
                if (playerId == defenderId)
                {
                    continue;
                }

                var player = State.GetPlayer(playerId);
                if (player != null && player.Hand.Count > 0)
                {
                    order.Add(playerId);
                }
            }

            if (order.Count == 0)
            {
                order.Add(attackerId);
            }

            return order;
        }

        private bool TryApplyAttack(PlayerIntent intent, out string error)
        {
            error = string.Empty;

            if (intent.PlayerId != State.Round.AttackerId)
            {
                error = "Only the round attacker can open attack.";
                return false;
            }

            if (!intent.HasCard)
            {
                error = "Attack requires a card.";
                return false;
            }

            if (State.Round.Table.Count != 0)
            {
                error = "Attack can only be used to open the round.";
                return false;
            }

            var attacker = State.GetPlayer(intent.PlayerId);
            if (attacker == null || !RemoveCardFromHand(attacker, intent.Card))
            {
                error = "Attacker card not in hand.";
                return false;
            }

            State.Round.Table.Add(new TablePair
            {
                AttackCard = intent.Card,
                IsDefended = false,
            });

            _passedAttackers.Remove(intent.PlayerId);
            MoveCursorToNextAttackerAfter(intent.PlayerId);
            RecomputeTurn();
            return true;
        }

        private bool TryApplyAddCard(PlayerIntent intent, out string error)
        {
            error = string.Empty;

            if (!IsAttackerForRound(intent.PlayerId))
            {
                error = "Only attackers can add cards.";
                return false;
            }

            if (!intent.HasCard)
            {
                error = "AddCard requires a card.";
                return false;
            }

            if (State.Round.Table.Count == 0)
            {
                error = "Cannot add before the first attack card.";
                return false;
            }

            if (State.Round.Table.Count >= State.Round.AttackLimit)
            {
                error = "Attack limit reached.";
                return false;
            }

            if (!CanAddAttackCard(intent.Card))
            {
                error = "Card rank cannot be added to the table.";
                return false;
            }

            var attacker = State.GetPlayer(intent.PlayerId);
            if (attacker == null || !RemoveCardFromHand(attacker, intent.Card))
            {
                error = "Attacker card not in hand.";
                return false;
            }

            State.Round.Table.Add(new TablePair
            {
                AttackCard = intent.Card,
                IsDefended = false,
            });

            _passedAttackers.Remove(intent.PlayerId);
            MoveCursorToNextAttackerAfter(intent.PlayerId);
            RecomputeTurn();
            return true;
        }

        private bool TryApplyDefend(PlayerIntent intent, out string error)
        {
            error = string.Empty;

            if (intent.PlayerId != State.Round.DefenderId)
            {
                error = "Only defender can defend.";
                return false;
            }

            if (!intent.HasCard)
            {
                error = "Defend requires a card.";
                return false;
            }

            if (intent.TargetPairIndex < 0 || intent.TargetPairIndex >= State.Round.Table.Count)
            {
                error = "Invalid target pair index.";
                return false;
            }

            var pair = State.Round.Table[intent.TargetPairIndex];
            if (pair.IsDefended)
            {
                error = "Target attack is already defended.";
                return false;
            }

            if (!CanBeat(intent.Card, pair.AttackCard))
            {
                error = "Defense card does not beat attack card.";
                return false;
            }

            var defender = State.GetPlayer(intent.PlayerId);
            if (defender == null || !RemoveCardFromHand(defender, intent.Card))
            {
                error = "Defender card not in hand.";
                return false;
            }

            pair.IsDefended = true;
            pair.DefenseCard = intent.Card;
            State.Round.Table[intent.TargetPairIndex] = pair;

            RecomputeTurn();
            return true;
        }

        private bool TryApplyTakeCards(PlayerIntent intent, out string error)
        {
            error = string.Empty;

            if (intent.PlayerId != State.Round.DefenderId)
            {
                error = "Only defender can take cards.";
                return false;
            }

            if (!HasUndefendedAttacks())
            {
                error = "TakeCards is only valid while attacks are still undefended.";
                return false;
            }

            ResolveDefenderTakeRound();
            return true;
        }

        private bool TryApplyEndAttack(PlayerIntent intent, out string error)
        {
            error = string.Empty;

            if (State.Round.Table.Count == 0)
            {
                error = "Cannot end attack before first attack card.";
                return false;
            }

            if (intent.PlayerId == State.Round.DefenderId)
            {
                error = "Defender cannot end attack.";
                return false;
            }

            if (!IsAttackerForRound(intent.PlayerId))
            {
                error = "Only attackers can end attack.";
                return false;
            }

            _passedAttackers.Add(intent.PlayerId);
            MoveCursorToNextAttackerAfter(intent.PlayerId);
            RecomputeTurn();
            return true;
        }

        private bool HasUndefendedAttacks()
        {
            for (var i = 0; i < State.Round.Table.Count; i++)
            {
                if (!State.Round.Table[i].IsDefended)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecomputeTurn()
        {
            if (State.Phase != GamePhase.InRound)
            {
                return;
            }

            if (TryFinalizeCompletedMatch())
            {
                return;
            }

            if (HasUndefendedAttacks())
            {
                State.CurrentTurnPlayerId = State.Round.DefenderId;
                return;
            }

            NormalizePassedAttackers();

            if (State.Round.Table.Count > 0 && !CanAnyAttackerAct())
            {
                ResolveSuccessfulDefenseRound();
                return;
            }

            var nextAttacker = FindNextAttackerForTurn();
            if (nextAttacker < 0)
            {
                ResolveSuccessfulDefenseRound();
                return;
            }

            State.CurrentTurnPlayerId = nextAttacker;
        }

        private void NormalizePassedAttackers()
        {
            for (var i = 0; i < State.Round.AttackerOrder.Count; i++)
            {
                var playerId = State.Round.AttackerOrder[i];
                var player = State.GetPlayer(playerId);
                if (player == null || player.Hand.Count == 0)
                {
                    _passedAttackers.Add(playerId);
                }
            }
        }

        private bool CanAnyAttackerAct()
        {
            for (var i = 0; i < State.Round.AttackerOrder.Count; i++)
            {
                if (CanAttackerAct(State.Round.AttackerOrder[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private int FindNextAttackerForTurn()
        {
            var attackers = State.Round.AttackerOrder;
            if (attackers.Count == 0)
            {
                return -1;
            }

            for (var offset = 0; offset < attackers.Count; offset++)
            {
                var index = (_attackerCursor + offset) % attackers.Count;
                var playerId = attackers[index];
                if (!CanAttackerAct(playerId))
                {
                    continue;
                }

                _attackerCursor = index;
                State.Round.ActiveAttackerIndex = index;
                return playerId;
            }

            return -1;
        }

        private bool CanAttackerAct(int playerId)
        {
            if (!IsAttackerForRound(playerId) || _passedAttackers.Contains(playerId))
            {
                return false;
            }

            var player = State.GetPlayer(playerId);
            if (player == null || player.Hand.Count == 0)
            {
                return false;
            }

            if (State.Round.Table.Count == 0)
            {
                return true;
            }

            if (State.Round.Table.Count >= State.Round.AttackLimit)
            {
                return false;
            }

            return HasAnyAddableCard(player);
        }

        private bool IsAttackerForRound(int playerId)
        {
            if (playerId < 0 || playerId == State.Round.DefenderId)
            {
                return false;
            }

            var order = State.Round.AttackerOrder;
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyAddableCard(PlayerState player)
        {
            for (var i = 0; i < player.Hand.Count; i++)
            {
                if (CanAddAttackCard(player.Hand[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveCursorToNextAttackerAfter(int playerId)
        {
            var attackers = State.Round.AttackerOrder;
            for (var i = 0; i < attackers.Count; i++)
            {
                if (attackers[i] == playerId)
                {
                    _attackerCursor = (i + 1) % attackers.Count;
                    return;
                }
            }
        }

        private bool CanAddAttackCard(Card card)
        {
            if (State.Round.Table.Count == 0)
            {
                return true;
            }

            if (State.Round.Table.Count >= State.Round.AttackLimit)
            {
                return false;
            }

            for (var i = 0; i < State.Round.Table.Count; i++)
            {
                var pair = State.Round.Table[i];
                if (pair.AttackCard.Rank == card.Rank)
                {
                    return true;
                }

                if (pair.IsDefended && pair.DefenseCard.Rank == card.Rank)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBeat(Card defenseCard, Card attackCard)
        {
            if (defenseCard.Suit == attackCard.Suit && (int)defenseCard.Rank > (int)attackCard.Rank)
            {
                return true;
            }

            if (defenseCard.Suit == State.TrumpSuit && attackCard.Suit != State.TrumpSuit)
            {
                return true;
            }

            if (defenseCard.Suit == State.TrumpSuit && attackCard.Suit == State.TrumpSuit &&
                (int)defenseCard.Rank > (int)attackCard.Rank)
            {
                return true;
            }

            return false;
        }

        private bool RemoveCardFromHand(PlayerState player, Card card)
        {
            for (var i = 0; i < player.Hand.Count; i++)
            {
                if (player.Hand[i].Equals(card))
                {
                    player.Hand.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        private void ResolveSuccessfulDefenseRound()
        {
            for (var i = 0; i < State.Round.Table.Count; i++)
            {
                _discardPile.Add(State.Round.Table[i].AttackCard);
                if (State.Round.Table[i].IsDefended)
                {
                    _discardPile.Add(State.Round.Table[i].DefenseCard);
                }
            }

            var previousAttacker = State.Round.AttackerId;
            var nextAttackerCandidate = State.Round.DefenderId;

            State.Round.Table.Clear();
            RefillHandsFrom(previousAttacker);

            if (TryFinalizeCompletedMatch())
            {
                return;
            }

            var nextAttacker = FindNextPlayerWithCards(nextAttackerCandidate, true);
            var nextDefender = FindNextPlayerWithCards(nextAttacker, false);

            if (nextAttacker < 0 || nextDefender < 0)
            {
                TryFinalizeCompletedMatch(forceComplete: true);
                return;
            }

            SetupRound(nextAttacker, nextDefender);
            State.CurrentTurnPlayerId = nextAttacker;
        }

        private void ResolveDefenderTakeRound()
        {
            var currentDefender = State.Round.DefenderId;
            var defender = State.GetPlayer(State.Round.DefenderId);
            for (var i = 0; i < State.Round.Table.Count; i++)
            {
                defender.Hand.Add(State.Round.Table[i].AttackCard);
                if (State.Round.Table[i].IsDefended)
                {
                    defender.Hand.Add(State.Round.Table[i].DefenseCard);
                }
            }

            var currentAttacker = State.Round.AttackerId;

            State.Round.Table.Clear();
            RefillHandsFrom(currentAttacker);

            if (TryFinalizeCompletedMatch())
            {
                return;
            }

            var nextAttacker = FindNextAttackerAfterDefenderTake(currentAttacker, currentDefender);
            var nextDefender = FindNextPlayerWithCards(nextAttacker, false);

            if (nextAttacker < 0 || nextDefender < 0)
            {
                TryFinalizeCompletedMatch(forceComplete: true);
                return;
            }

            SetupRound(nextAttacker, nextDefender);
            State.CurrentTurnPlayerId = nextAttacker;
        }

        private void RefillHandsFrom(int startPlayerId)
        {
            var startIndex = State.GetPlayerOrderIndex(startPlayerId);
            if (startIndex < 0)
            {
                startIndex = 0;
            }

            for (var offset = 0; offset < State.PlayerOrder.Count; offset++)
            {
                var index = (startIndex + offset) % State.PlayerOrder.Count;
                var playerId = State.PlayerOrder[index];
                var player = State.GetPlayer(playerId);
                if (player == null)
                {
                    continue;
                }

                while (player.Hand.Count < InitialHandSize && _drawPile.Count > 0)
                {
                    player.Hand.Add(DrawTopCard());
                }
            }

            State.DeckCount = _drawPile.Count;
        }

        private int FindNextPlayerWithCards(int fromPlayerId, bool includeFrom)
        {
            var fromIndex = State.GetPlayerOrderIndex(fromPlayerId);
            if (fromIndex < 0)
            {
                return -1;
            }

            var firstOffset = includeFrom ? 0 : 1;
            var steps = includeFrom ? State.PlayerOrder.Count : State.PlayerOrder.Count - 1;
            for (var step = 0; step < steps; step++)
            {
                var index = (fromIndex + firstOffset + step) % State.PlayerOrder.Count;
                var playerId = State.PlayerOrder[index];
                var player = State.GetPlayer(playerId);
                if (player != null && player.Hand.Count > 0)
                {
                    return playerId;
                }
            }

            return -1;
        }

        private int FindNextAttackerAfterDefenderTake(int currentAttacker, int currentDefender)
        {
            var attacker = State.GetPlayer(currentAttacker);
            if (attacker != null && attacker.Hand.Count > 0)
            {
                return currentAttacker;
            }

            var fromIndex = State.GetPlayerOrderIndex(currentAttacker);
            if (fromIndex < 0)
            {
                return -1;
            }

            for (var step = 1; step < State.PlayerOrder.Count; step++)
            {
                var index = (fromIndex + step) % State.PlayerOrder.Count;
                var playerId = State.PlayerOrder[index];
                if (playerId == currentDefender)
                {
                    continue;
                }

                var player = State.GetPlayer(playerId);
                if (player != null && player.Hand.Count > 0)
                {
                    return playerId;
                }
            }

            return -1;
        }

        private bool TryFinalizeCompletedMatch(bool forceComplete = false)
        {
            var playersWithCards = 0;
            var durakId = -1;

            for (var i = 0; i < State.Players.Count; i++)
            {
                if (State.Players[i].Hand.Count > 0)
                {
                    playersWithCards += 1;
                    durakId = State.Players[i].PlayerId;
                }
            }

            var shouldComplete = forceComplete || (_drawPile.Count == 0 && playersWithCards <= 1);
            if (!shouldComplete)
            {
                return false;
            }

            State.Phase = GamePhase.Completed;
            State.CurrentTurnPlayerId = -1;
            State.DeckCount = _drawPile.Count;
            State.MatchResult = new MatchResult
            {
                DurakPlayerId = playersWithCards == 1 ? durakId : -1,
                Winners = new List<int>(),
            };

            for (var i = 0; i < State.Players.Count; i++)
            {
                var player = State.Players[i];
                if (player.PlayerId != State.MatchResult.DurakPlayerId)
                {
                    State.MatchResult.Winners.Add(player.PlayerId);
                }
            }

            return true;
        }
    }
}
