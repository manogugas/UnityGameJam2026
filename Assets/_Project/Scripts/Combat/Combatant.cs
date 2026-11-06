using UnityEngine;

namespace HauntedCasino.Combat
{
    /// <summary>Plain data for anyone at the table: players and ghosts use the same rules.</summary>
    public class Combatant
    {
        public readonly string Name;
        public readonly Color Color;
        public readonly bool IsPlayer;
        public readonly int Index;
        public int MaxHP;
        public int HP;

        public bool IsDown => HP <= 0;

        public Combatant(string name, Color color, bool isPlayer, int index, int maxHP)
        {
            Name = name;
            Color = color;
            IsPlayer = isPlayer;
            Index = index;
            MaxHP = maxHP;
            HP = maxHP;
        }

        public int TakeDamage(int amount)
        {
            int dealt = Mathf.Clamp(amount, 0, HP);
            HP -= dealt;
            return dealt;
        }

        public int Heal(int amount)
        {
            int healed = Mathf.Clamp(amount, 0, MaxHP - HP);
            HP += healed;
            return healed;
        }
    }
}
