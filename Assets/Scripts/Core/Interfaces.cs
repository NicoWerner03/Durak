using System.Collections.Generic;

namespace DurakGame.Core
{
    public interface IGameRulesEngine
    {
        GameState State { get; }

        void InitializeMatch(IReadOnlyList<PlayerSeat> seats, int seed);

        IReadOnlyList<PlayerIntent> GetLegalIntents(int playerId);

        IntentResult ApplyIntent(PlayerIntent intent);

        StateSnapshot CreateSnapshot();

        void RestoreSnapshot(StateSnapshot snapshot);
    }

    public interface IBotStrategy
    {
        PlayerIntent ChooseIntent(GameState state, IReadOnlyList<PlayerIntent> legalIntents);
    }
}
