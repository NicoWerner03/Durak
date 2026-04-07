using System.Collections.Generic;
using DurakGame.Core;

namespace DurakGame.Gameplay
{
    public class SimpleBotStrategy : IBotStrategy
    {
        public PlayerIntent ChooseIntent(GameState state, IReadOnlyList<PlayerIntent> legalIntents)
        {
            if (legalIntents == null || legalIntents.Count == 0)
            {
                return null;
            }

            PlayerIntent bestDefense = null;
            PlayerIntent bestAttack = null;
            PlayerIntent endAttack = null;
            PlayerIntent takeCards = null;

            for (var i = 0; i < legalIntents.Count; i++)
            {
                var intent = legalIntents[i];
                switch (intent.Type)
                {
                    case PlayerIntentType.Defend:
                        if (bestDefense == null || CompareCards(intent.Card, bestDefense.Card) < 0)
                        {
                            bestDefense = intent;
                        }

                        break;
                    case PlayerIntentType.Attack:
                    case PlayerIntentType.AddCard:
                        if (bestAttack == null || CompareCards(intent.Card, bestAttack.Card) < 0)
                        {
                            bestAttack = intent;
                        }

                        break;
                    case PlayerIntentType.EndAttack:
                        endAttack = intent;
                        break;
                    case PlayerIntentType.TakeCards:
                        takeCards = intent;
                        break;
                }
            }

            if (bestDefense != null)
            {
                return bestDefense;
            }

            if (bestAttack != null)
            {
                return bestAttack;
            }

            if (endAttack != null)
            {
                return endAttack;
            }

            return takeCards ?? legalIntents[0];
        }

        private static int CompareCards(Card left, Card right)
        {
            var rankCmp = ((int)left.Rank).CompareTo((int)right.Rank);
            if (rankCmp != 0)
            {
                return rankCmp;
            }

            return ((int)left.Suit).CompareTo((int)right.Suit);
        }
    }
}
