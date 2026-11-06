using UnityEngine;

namespace HauntedCasino.Combat
{
    public enum GambleMode { Attack, Defence }

    public enum OutcomeKind { Normal, Jackpot, Bust }

    /// <summary>Result of one gamble. Power becomes damage when attacking, block when defending.</summary>
    public struct GambleOutcome
    {
        public int Power;
        public OutcomeKind Kind;
        public string Label;

        public GambleOutcome(int power, OutcomeKind kind, string label)
        {
            Power = power;
            Kind = kind;
            Label = label;
        }
    }

    public struct GambleContext
    {
        public GambleMode Mode;
        public bool IsAI;
        public string ActorName;
        public Color ActorColor;
    }
}
