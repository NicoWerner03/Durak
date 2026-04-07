using System;

namespace DurakGame.Core
{
    public enum PlayerIntentType
    {
        Attack = 0,
        Defend = 1,
        AddCard = 2,
        TakeCards = 3,
        EndAttack = 4,
    }

    [Serializable]
    public class PlayerIntent
    {
        public int PlayerId = -1;
        public PlayerIntentType Type;
        public bool HasCard;
        public Card Card;
        public int TargetPairIndex = -1;

        public static PlayerIntent Attack(int playerId, Card card)
        {
            return new PlayerIntent
            {
                PlayerId = playerId,
                Type = PlayerIntentType.Attack,
                HasCard = true,
                Card = card,
            };
        }

        public static PlayerIntent AddCard(int playerId, Card card)
        {
            return new PlayerIntent
            {
                PlayerId = playerId,
                Type = PlayerIntentType.AddCard,
                HasCard = true,
                Card = card,
            };
        }

        public static PlayerIntent Defend(int playerId, int targetPairIndex, Card card)
        {
            return new PlayerIntent
            {
                PlayerId = playerId,
                Type = PlayerIntentType.Defend,
                HasCard = true,
                Card = card,
                TargetPairIndex = targetPairIndex,
            };
        }

        public static PlayerIntent TakeCards(int playerId)
        {
            return new PlayerIntent
            {
                PlayerId = playerId,
                Type = PlayerIntentType.TakeCards,
                HasCard = false,
            };
        }

        public static PlayerIntent EndAttack(int playerId)
        {
            return new PlayerIntent
            {
                PlayerId = playerId,
                Type = PlayerIntentType.EndAttack,
                HasCard = false,
            };
        }

        public PlayerIntent Clone()
        {
            return new PlayerIntent
            {
                PlayerId = PlayerId,
                Type = Type,
                HasCard = HasCard,
                Card = Card,
                TargetPairIndex = TargetPairIndex,
            };
        }
    }

    [Serializable]
    public class StateSnapshot
    {
        public int Sequence;
        public GameState State = new GameState();

        public StateSnapshot Clone()
        {
            return new StateSnapshot
            {
                Sequence = Sequence,
                State = State.Clone(),
            };
        }
    }

    [Serializable]
    public class StateDelta
    {
        public int Sequence;
        public PlayerIntent AppliedIntent = new PlayerIntent();

        public StateDelta Clone()
        {
            return new StateDelta
            {
                Sequence = Sequence,
                AppliedIntent = AppliedIntent.Clone(),
            };
        }
    }

    [Serializable]
    public class IntentResult
    {
        public bool Accepted;
        public string Error = string.Empty;
        public StateSnapshot Snapshot = new StateSnapshot();
        public StateDelta Delta = new StateDelta();

        public static IntentResult Reject(string error, GameState state)
        {
            return new IntentResult
            {
                Accepted = false,
                Error = error,
                Snapshot = new StateSnapshot
                {
                    Sequence = state != null ? state.TurnSequence : 0,
                    State = state != null ? state.Clone() : new GameState(),
                },
                Delta = new StateDelta(),
            };
        }
    }
}
