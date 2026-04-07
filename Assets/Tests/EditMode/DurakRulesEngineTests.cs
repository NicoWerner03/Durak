using System;
using System.Collections.Generic;
using DurakGame.Core;
using DurakGame.Gameplay;
using NUnit.Framework;

namespace DurakGame.Tests
{
    public class DurakRulesEngineTests
    {
        [Test]
        public void InitializeMatch_DealsSixCardsAndSetsDeckCount()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 42);

            Assert.AreEqual(GamePhase.InRound, engine.State.Phase);
            Assert.AreEqual(6, engine.State.GetPlayer(0).Hand.Count);
            Assert.AreEqual(6, engine.State.GetPlayer(1).Hand.Count);
            Assert.AreEqual(24, engine.State.DeckCount);
            Assert.AreEqual(engine.State.Round.AttackerId, engine.State.CurrentTurnPlayerId);
        }

        [Test]
        public void NonCurrentPlayerIntent_IsRejected()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 7);

            var current = engine.State.CurrentTurnPlayerId;
            var wrongPlayer = current == 0 ? 1 : 0;
            var wrongCard = engine.State.GetPlayer(wrongPlayer).Hand[0];
            var result = engine.ApplyIntent(PlayerIntent.Attack(wrongPlayer, wrongCard));

            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("not this player's turn"));
        }

        [Test]
        public void FirstAttack_CreatesTableCardAndDefenderGetsTurn()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 123);

            var attacker = engine.State.CurrentTurnPlayerId;
            var attackIntent = engine.GetLegalIntents(attacker)[0];
            var result = engine.ApplyIntent(attackIntent);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(1, engine.State.Round.Table.Count);
            Assert.AreEqual(engine.State.Round.DefenderId, engine.State.CurrentTurnPlayerId);
        }

        [Test]
        public void BotPlaysDeterministicLoop_UntilMatchEnds()
        {
            var engine = new DurakGameRulesEngine();
            var bot = new SimpleBotStrategy();
            engine.InitializeMatch(CreateSeats(4, includeBots: true), seed: 987654);

            const int maxTurns = 2500;
            for (var turn = 0; turn < maxTurns && engine.State.Phase == GamePhase.InRound; turn++)
            {
                var current = engine.State.CurrentTurnPlayerId;
                Assert.GreaterOrEqual(current, 0);

                var legal = engine.GetLegalIntents(current);
                Assert.IsNotEmpty(legal);

                var intent = bot.ChooseIntent(engine.State, legal);
                Assert.IsNotNull(intent);

                var result = engine.ApplyIntent(intent);
                Assert.IsTrue(result.Accepted, "Turn " + turn + " failed: " + result.Error);
            }

            Assert.AreEqual(GamePhase.Completed, engine.State.Phase, "Match did not complete within turn limit.");
            Assert.AreNotEqual(-1, engine.State.MatchResult.DurakPlayerId);
        }

        [Test]
        public void BotStrategy_AlwaysReturnsLegalIntent()
        {
            var engine = new DurakGameRulesEngine();
            var bot = new SimpleBotStrategy();
            engine.InitializeMatch(CreateSeats(3, includeBots: true), seed: 31415);

            for (var i = 0; i < 200; i++)
            {
                if (engine.State.Phase != GamePhase.InRound)
                {
                    break;
                }

                var current = engine.State.CurrentTurnPlayerId;
                var legal = engine.GetLegalIntents(current);
                var selected = bot.ChooseIntent(engine.State, legal);

                Assert.IsTrue(ContainsEquivalentIntent(legal, selected));
                var result = engine.ApplyIntent(selected);
                Assert.IsTrue(result.Accepted);
            }
        }

        [Test]
        public void InitializeMatch_WithSameSeed_IsDeterministic()
        {
            var engineA = new DurakGameRulesEngine();
            var engineB = new DurakGameRulesEngine();

            var seats = CreateSeats(4, includeBots: false);
            const int seed = 20260323;

            engineA.InitializeMatch(seats, seed);
            engineB.InitializeMatch(seats, seed);

            Assert.AreEqual(engineA.State.TrumpSuit, engineB.State.TrumpSuit);
            Assert.AreEqual(engineA.State.CurrentTurnPlayerId, engineB.State.CurrentTurnPlayerId);

            for (var pid = 0; pid < 4; pid++)
            {
                var handA = engineA.State.GetPlayer(pid).Hand;
                var handB = engineB.State.GetPlayer(pid).Hand;
                Assert.AreEqual(handA.Count, handB.Count);
                for (var i = 0; i < handA.Count; i++)
                {
                    Assert.IsTrue(handA[i].Equals(handB[i]));
                }
            }
        }

        [Test]
        public void InitializeMatch_CopiesPlayerIdentityFromSeats()
        {
            var engine = new DurakGameRulesEngine();
            var seats = new List<PlayerSeat>
            {
                new PlayerSeat
                {
                    PlayerId = 0,
                    DisplayName = "P1",
                    IsBot = false,
                    OwnerClientId = 11,
                    PlayerIdentity = "identity-A",
                },
                new PlayerSeat
                {
                    PlayerId = 1,
                    DisplayName = "P2",
                    IsBot = false,
                    OwnerClientId = 22,
                    PlayerIdentity = "identity-B",
                },
            };

            engine.InitializeMatch(seats, seed: 99);

            Assert.AreEqual("identity-A", engine.State.GetPlayer(0).PlayerIdentity);
            Assert.AreEqual("identity-B", engine.State.GetPlayer(1).PlayerIdentity);
        }

        [Test]
        public void DefenderLegalIntents_AlwaysContainTakeCards_WhenUndefendedAttackExists()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 11);

            var attacker = engine.State.CurrentTurnPlayerId;
            var openAttack = engine.GetLegalIntents(attacker)[0];
            var attackResult = engine.ApplyIntent(openAttack);
            Assert.IsTrue(attackResult.Accepted);

            var defender = engine.State.CurrentTurnPlayerId;
            Assert.AreEqual(engine.State.Round.DefenderId, defender);

            var legalDefenderIntents = engine.GetLegalIntents(defender);
            Assert.IsTrue(ContainsIntentType(legalDefenderIntents, PlayerIntentType.TakeCards));
        }

        [Test]
        public void TakeCards_IsRejected_WhenAllTablePairsAreAlreadyDefended()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Hearts,
                currentTurnPlayerId: 1,
                attackLimit: 6,
                attackerOrder: new[] { 0 },
                players: new[]
                {
                    CreatePlayer(0, "A", false, new Card(Suit.Spades, Rank.Ace)),
                    CreatePlayer(1, "D", false, new Card(Suit.Hearts, Rank.King)),
                },
                table: new[]
                {
                    CreatePair(new Card(Suit.Clubs, Rank.Queen), defended: true, new Card(Suit.Clubs, Rank.King)),
                });

            var result = engine.ApplyIntent(PlayerIntent.TakeCards(1));
            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("only valid while attacks are still undefended"));
        }

        [Test]
        public void Defend_WithLowerSameSuitCard_IsRejected()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Spades,
                currentTurnPlayerId: 1,
                attackLimit: 6,
                attackerOrder: new[] { 0 },
                players: new[]
                {
                    CreatePlayer(0, "A", false, new Card(Suit.Clubs, Rank.Ace)),
                    CreatePlayer(1, "D", false, new Card(Suit.Clubs, Rank.Jack)),
                },
                table: new[]
                {
                    CreatePair(new Card(Suit.Clubs, Rank.Queen), defended: false, new Card()),
                });

            var result = engine.ApplyIntent(PlayerIntent.Defend(1, 0, new Card(Suit.Clubs, Rank.Jack)));
            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("does not beat"));
        }

        [Test]
        public void Defend_WithTrumpAgainstNonTrump_IsAccepted()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Hearts,
                currentTurnPlayerId: 1,
                attackLimit: 6,
                attackerOrder: new[] { 0 },
                players: new[]
                {
                    CreatePlayer(0, "A", false, new Card(Suit.Diamonds, Rank.Ace)),
                    CreatePlayer(1, "D", false, new Card(Suit.Hearts, Rank.Six)),
                },
                table: new[]
                {
                    CreatePair(new Card(Suit.Clubs, Rank.Ace), defended: false, new Card()),
                });

            var result = engine.ApplyIntent(PlayerIntent.Defend(1, 0, new Card(Suit.Hearts, Rank.Six)));
            Assert.IsTrue(result.Accepted, result.Error);
            Assert.IsTrue(engine.State.Round.Table[0].IsDefended);
        }

        [Test]
        public void AddCard_WithNonMatchingRank_IsRejected()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Spades,
                currentTurnPlayerId: 2,
                attackLimit: 6,
                attackerOrder: new[] { 0, 2 },
                players: new[]
                {
                    CreatePlayer(0, "A1", false, new Card(Suit.Clubs, Rank.Six)),
                    CreatePlayer(1, "D", false, new Card(Suit.Hearts, Rank.Ace)),
                    CreatePlayer(2, "A2", false, new Card(Suit.Diamonds, Rank.King), new Card(Suit.Hearts, Rank.Nine)),
                },
                table: new[]
                {
                    CreatePair(new Card(Suit.Clubs, Rank.Queen), defended: true, new Card(Suit.Spades, Rank.Six)),
                });

            var invalidAdd = PlayerIntent.AddCard(2, new Card(Suit.Diamonds, Rank.King));
            var result = engine.ApplyIntent(invalidAdd);

            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("cannot be added"));
        }

        [Test]
        public void SuccessfulDefense_ThenEndAttack_StartsNextRoundWithPreviousDefenderAsAttacker()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 101);

            var originalAttacker = engine.State.CurrentTurnPlayerId;
            var originalDefender = engine.State.Round.DefenderId;

            var attackIntent = engine.GetLegalIntents(originalAttacker)[0];
            var attackResult = engine.ApplyIntent(attackIntent);
            Assert.IsTrue(attackResult.Accepted);

            var defender = engine.State.CurrentTurnPlayerId;
            var defenderIntents = engine.GetLegalIntents(defender);
            var defendIntent = FindFirstIntent(defenderIntents, PlayerIntentType.Defend);
            if (defendIntent == null)
            {
                Assert.Inconclusive("Chosen seed did not produce a defendable first attack.");
            }

            var defendResult = engine.ApplyIntent(defendIntent);
            Assert.IsTrue(defendResult.Accepted, defendResult.Error);

            if (engine.State.Round.RoundNumber == 1)
            {
                var current = engine.State.CurrentTurnPlayerId;
                var endAttackIntent = FindFirstIntent(engine.GetLegalIntents(current), PlayerIntentType.EndAttack);
                Assert.IsNotNull(endAttackIntent, "EndAttack should be legal while the same round is still active.");

                var endAttackResult = engine.ApplyIntent(endAttackIntent);
                Assert.IsTrue(endAttackResult.Accepted, endAttackResult.Error);
            }

            Assert.GreaterOrEqual(engine.State.Round.RoundNumber, 2);
            Assert.AreEqual(originalDefender, engine.State.Round.AttackerId);
            Assert.AreEqual(engine.State.Round.AttackerId, engine.State.CurrentTurnPlayerId);
        }

        [Test]
        public void DefenderTakeCards_StartsNextRoundWithoutChangingAttacker()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(2, includeBots: false), seed: 77);

            var originalAttacker = engine.State.CurrentTurnPlayerId;

            var attackIntent = engine.GetLegalIntents(originalAttacker)[0];
            var attackResult = engine.ApplyIntent(attackIntent);
            Assert.IsTrue(attackResult.Accepted);

            var defender = engine.State.CurrentTurnPlayerId;
            var takeIntent = FindFirstIntent(engine.GetLegalIntents(defender), PlayerIntentType.TakeCards);
            Assert.IsNotNull(takeIntent);

            var defenderHandBeforeTake = engine.State.GetPlayer(defender).Hand.Count;
            var takeResult = engine.ApplyIntent(takeIntent);
            Assert.IsTrue(takeResult.Accepted, takeResult.Error);

            Assert.GreaterOrEqual(engine.State.Round.RoundNumber, 2);
            Assert.AreEqual(originalAttacker, engine.State.Round.AttackerId);
            Assert.AreEqual(engine.State.Round.AttackerId, engine.State.CurrentTurnPlayerId);
            Assert.GreaterOrEqual(engine.State.GetPlayer(defender).Hand.Count, defenderHandBeforeTake);
        }

        [Test]
        public void DefenderTakeCards_DoesNotMakeDefenderImmediateAttacker_InThreePlayerRound()
        {
            var engine = new DurakGameRulesEngine();
            engine.InitializeMatch(CreateSeats(3, includeBots: false), seed: 91);

            var attacker = engine.State.CurrentTurnPlayerId;
            var defender = engine.State.Round.DefenderId;

            var attackIntent = engine.GetLegalIntents(attacker)[0];
            var attackResult = engine.ApplyIntent(attackIntent);
            Assert.IsTrue(attackResult.Accepted);

            var takeIntent = FindFirstIntent(engine.GetLegalIntents(defender), PlayerIntentType.TakeCards);
            Assert.IsNotNull(takeIntent);

            var takeResult = engine.ApplyIntent(takeIntent);
            Assert.IsTrue(takeResult.Accepted, takeResult.Error);

            Assert.AreEqual(attacker, engine.State.Round.AttackerId);
            Assert.AreNotEqual(defender, engine.State.Round.AttackerId);
            Assert.AreEqual(engine.State.Round.AttackerId, engine.State.CurrentTurnPlayerId);
        }

        [Test]
        public void SnapshotRestore_PreservesAuthoritativeState_AndAllowsPlayToContinue()
        {
            var source = new DurakGameRulesEngine();
            source.InitializeMatch(CreateSeats(3, includeBots: false), seed: 12345);

            var opener = source.State.CurrentTurnPlayerId;
            var openIntent = source.GetLegalIntents(opener)[0];
            var openResult = source.ApplyIntent(openIntent);
            Assert.IsTrue(openResult.Accepted, openResult.Error);

            var snapshot = source.CreateSnapshot();
            Assert.IsNotNull(snapshot);
            Assert.IsNotNull(snapshot.State);

            var restored = new DurakGameRulesEngine();
            restored.InitializeMatch(CreateSeats(3, includeBots: false), seed: 1);
            restored.RestoreSnapshot(snapshot);

            Assert.AreEqual(source.State.TurnSequence, restored.State.TurnSequence);
            Assert.AreEqual(source.State.CurrentTurnPlayerId, restored.State.CurrentTurnPlayerId);
            Assert.AreEqual(source.State.Round.AttackerId, restored.State.Round.AttackerId);
            Assert.AreEqual(source.State.Round.DefenderId, restored.State.Round.DefenderId);
            Assert.AreEqual(source.State.DeckCount, restored.State.DeckCount);
            Assert.AreEqual(source.State.Players.Count, restored.State.Players.Count);

            for (var i = 0; i < source.State.Players.Count; i++)
            {
                var sourcePlayer = source.State.Players[i];
                var restoredPlayer = restored.State.GetPlayer(sourcePlayer.PlayerId);
                Assert.IsNotNull(restoredPlayer);
                Assert.AreEqual(sourcePlayer.Hand.Count, restoredPlayer.Hand.Count);
            }

            var current = restored.State.CurrentTurnPlayerId;
            var legal = restored.GetLegalIntents(current);
            Assert.IsNotEmpty(legal);
            var nextResult = restored.ApplyIntent(legal[0]);
            Assert.IsTrue(nextResult.Accepted, nextResult.Error);
        }

        [Test]
        public void Attack_ByNonRoundAttacker_IsRejected()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Spades,
                currentTurnPlayerId: 2,
                attackLimit: 6,
                attackerOrder: new[] { 0, 2 },
                players: new[]
                {
                    CreatePlayer(0, "A1", false, new Card(Suit.Clubs, Rank.Six)),
                    CreatePlayer(1, "D", false, new Card(Suit.Hearts, Rank.Ace)),
                    CreatePlayer(2, "A2", false, new Card(Suit.Diamonds, Rank.King)),
                },
                table: Array.Empty<TablePair>());

            var result = engine.ApplyIntent(PlayerIntent.Attack(2, new Card(Suit.Diamonds, Rank.King)));
            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("round attacker"));
        }

        [Test]
        public void Defend_ByNonDefender_IsRejected()
        {
            var engine = CreateControlledEngine(
                attackerId: 0,
                defenderId: 1,
                trump: Suit.Hearts,
                currentTurnPlayerId: 0,
                attackLimit: 6,
                attackerOrder: new[] { 0 },
                players: new[]
                {
                    CreatePlayer(0, "A", false, new Card(Suit.Spades, Rank.Ace)),
                    CreatePlayer(1, "D", false, new Card(Suit.Hearts, Rank.King)),
                },
                table: new[]
                {
                    CreatePair(new Card(Suit.Clubs, Rank.Queen), defended: false, new Card()),
                });

            var result = engine.ApplyIntent(PlayerIntent.Defend(0, 0, new Card(Suit.Spades, Rank.Ace)));
            Assert.IsFalse(result.Accepted);
            Assert.That(result.Error, Does.Contain("Only defender can defend"));
        }

        private static bool ContainsEquivalentIntent(IReadOnlyList<PlayerIntent> legal, PlayerIntent candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            for (var i = 0; i < legal.Count; i++)
            {
                var intent = legal[i];
                if (intent.Type != candidate.Type || intent.PlayerId != candidate.PlayerId)
                {
                    continue;
                }

                if (intent.TargetPairIndex != candidate.TargetPairIndex)
                {
                    continue;
                }

                if (intent.HasCard != candidate.HasCard)
                {
                    continue;
                }

                if (!intent.HasCard || intent.Card.Equals(candidate.Card))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIntentType(IReadOnlyList<PlayerIntent> intents, PlayerIntentType type)
        {
            for (var i = 0; i < intents.Count; i++)
            {
                if (intents[i].Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static PlayerIntent FindFirstIntent(IReadOnlyList<PlayerIntent> intents, PlayerIntentType type)
        {
            for (var i = 0; i < intents.Count; i++)
            {
                if (intents[i].Type == type)
                {
                    return intents[i];
                }
            }

            return null;
        }

        private static DurakGameRulesEngine CreateControlledEngine(
            int attackerId,
            int defenderId,
            Suit trump,
            int currentTurnPlayerId,
            int attackLimit,
            int[] attackerOrder,
            PlayerState[] players,
            TablePair[] table)
        {
            var engine = new DurakGameRulesEngine();
            var seats = new List<PlayerSeat>();
            for (var i = 0; i < players.Length; i++)
            {
                seats.Add(new PlayerSeat
                {
                    PlayerId = players[i].PlayerId,
                    DisplayName = players[i].DisplayName,
                    IsBot = players[i].IsBot,
                    OwnerClientId = players[i].OwnerClientId,
                });
            }

            engine.InitializeMatch(seats, seed: 1);

            var state = engine.State;
            state.Phase = GamePhase.InRound;
            state.TrumpSuit = trump;
            state.CurrentTurnPlayerId = currentTurnPlayerId;
            state.TurnSequence = 0;

            state.Players = new List<PlayerState>();
            state.PlayerOrder = new List<int>();
            for (var i = 0; i < players.Length; i++)
            {
                state.Players.Add(players[i].Clone());
                state.PlayerOrder.Add(players[i].PlayerId);
            }

            state.Round = new RoundState
            {
                RoundNumber = 1,
                AttackerId = attackerId,
                DefenderId = defenderId,
                AttackLimit = attackLimit,
                DefenderInitialHandCount = state.GetPlayer(defenderId).Hand.Count,
                ActiveAttackerIndex = 0,
                AttackerOrder = new List<int>(attackerOrder),
                Table = new List<TablePair>(),
            };

            for (var i = 0; i < table.Length; i++)
            {
                state.Round.Table.Add(table[i].Clone());
            }

            return engine;
        }

        private static PlayerState CreatePlayer(int id, string name, bool isBot, params Card[] cards)
        {
            return new PlayerState
            {
                PlayerId = id,
                DisplayName = name,
                IsBot = isBot,
                OwnerClientId = (ulong)id,
                IsConnected = true,
                Hand = new List<Card>(cards),
            };
        }

        private static TablePair CreatePair(Card attackCard, bool defended, Card defenseCard)
        {
            return new TablePair
            {
                AttackCard = attackCard,
                IsDefended = defended,
                DefenseCard = defenseCard,
            };
        }

        private static IReadOnlyList<PlayerSeat> CreateSeats(int count, bool includeBots)
        {
            if (count < 2 || count > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var seats = new List<PlayerSeat>(count);
            for (var i = 0; i < count; i++)
            {
                seats.Add(new PlayerSeat
                {
                    PlayerId = i,
                    DisplayName = "P" + (i + 1),
                    IsBot = includeBots,
                    OwnerClientId = (ulong)i,
                });
            }

            return seats;
        }
    }
}
