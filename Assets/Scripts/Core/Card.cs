using System;

namespace DurakGame.Core
{
    public enum Suit
    {
        Clubs = 0,
        Diamonds = 1,
        Hearts = 2,
        Spades = 3,
    }

    public enum Rank
    {
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14,
    }

    [Serializable]
    public struct Card : IEquatable<Card>, IComparable<Card>
    {
        public Suit Suit;
        public Rank Rank;

        public Card(Suit suit, Rank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        public int CompareTo(Card other)
        {
            var rankCompare = Rank.CompareTo(other.Rank);
            if (rankCompare != 0)
            {
                return rankCompare;
            }

            return Suit.CompareTo(other.Suit);
        }

        public bool Equals(Card other)
        {
            return Suit == other.Suit && Rank == other.Rank;
        }

        public override bool Equals(object obj)
        {
            return obj is Card other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Suit * 397) ^ (int)Rank;
            }
        }

        public override string ToString()
        {
            return Rank + " of " + Suit;
        }
    }
}
