using System;
using System.Collections.Generic;

namespace DurakGame.Core
{
    public enum GamePhase
    {
        Lobby = 0,
        InRound = 1,
        Completed = 2,
    }

    [Serializable]
    public class PlayerSeat
    {
        public int PlayerId;
        public string DisplayName = string.Empty;
        public bool IsBot;
        public ulong OwnerClientId;
        public string PlayerIdentity = string.Empty;

        public PlayerSeat Clone()
        {
            return new PlayerSeat
            {
                PlayerId = PlayerId,
                DisplayName = DisplayName,
                IsBot = IsBot,
                OwnerClientId = OwnerClientId,
                PlayerIdentity = PlayerIdentity,
            };
        }
    }

    [Serializable]
    public class PlayerState
    {
        public int PlayerId;
        public string DisplayName = string.Empty;
        public bool IsBot;
        public ulong OwnerClientId;
        public string PlayerIdentity = string.Empty;
        public List<Card> Hand = new List<Card>();
        public bool IsConnected = true;

        public PlayerState Clone()
        {
            var copy = new PlayerState
            {
                PlayerId = PlayerId,
                DisplayName = DisplayName,
                IsBot = IsBot,
                OwnerClientId = OwnerClientId,
                PlayerIdentity = PlayerIdentity,
                IsConnected = IsConnected,
                Hand = new List<Card>(Hand),
            };

            return copy;
        }
    }

    [Serializable]
    public class TablePair
    {
        public Card AttackCard;
        public bool IsDefended;
        public Card DefenseCard;

        public TablePair Clone()
        {
            return new TablePair
            {
                AttackCard = AttackCard,
                IsDefended = IsDefended,
                DefenseCard = DefenseCard,
            };
        }
    }

    [Serializable]
    public class RoundState
    {
        public int RoundNumber;
        public int AttackerId = -1;
        public int DefenderId = -1;
        public int AttackLimit = 6;
        public int DefenderInitialHandCount;
        public int ActiveAttackerIndex;
        public List<int> AttackerOrder = new List<int>();
        public List<TablePair> Table = new List<TablePair>();

        public RoundState Clone()
        {
            var copy = new RoundState
            {
                RoundNumber = RoundNumber,
                AttackerId = AttackerId,
                DefenderId = DefenderId,
                AttackLimit = AttackLimit,
                DefenderInitialHandCount = DefenderInitialHandCount,
                ActiveAttackerIndex = ActiveAttackerIndex,
                AttackerOrder = new List<int>(AttackerOrder),
                Table = new List<TablePair>(),
            };

            for (var i = 0; i < Table.Count; i++)
            {
                copy.Table.Add(Table[i].Clone());
            }

            return copy;
        }
    }

    [Serializable]
    public class MatchResult
    {
        public int DurakPlayerId = -1;
        public List<int> Winners = new List<int>();

        public MatchResult Clone()
        {
            return new MatchResult
            {
                DurakPlayerId = DurakPlayerId,
                Winners = new List<int>(Winners),
            };
        }
    }

    [Serializable]
    public class GameState
    {
        public GamePhase Phase = GamePhase.Lobby;
        public Suit TrumpSuit = Suit.Clubs;
        public int DeckCount;
        public int CurrentTurnPlayerId = -1;
        public int TurnSequence;
        public List<int> PlayerOrder = new List<int>();
        public List<PlayerState> Players = new List<PlayerState>();
        public RoundState Round = new RoundState();
        public MatchResult MatchResult = new MatchResult();

        public PlayerState GetPlayer(int playerId)
        {
            for (var i = 0; i < Players.Count; i++)
            {
                if (Players[i].PlayerId == playerId)
                {
                    return Players[i];
                }
            }

            return null;
        }

        public int GetPlayerOrderIndex(int playerId)
        {
            for (var i = 0; i < PlayerOrder.Count; i++)
            {
                if (PlayerOrder[i] == playerId)
                {
                    return i;
                }
            }

            return -1;
        }

        public GameState Clone()
        {
            var copy = new GameState
            {
                Phase = Phase,
                TrumpSuit = TrumpSuit,
                DeckCount = DeckCount,
                CurrentTurnPlayerId = CurrentTurnPlayerId,
                TurnSequence = TurnSequence,
                PlayerOrder = new List<int>(PlayerOrder),
                Players = new List<PlayerState>(),
                Round = Round.Clone(),
                MatchResult = MatchResult.Clone(),
            };

            for (var i = 0; i < Players.Count; i++)
            {
                copy.Players.Add(Players[i].Clone());
            }

            return copy;
        }
    }
}
