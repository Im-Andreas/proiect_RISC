using System;
using System.Linq;

namespace proiect_RISC.Models
{
    public class CacheSet
    {
        public CacheLine[] Ways { get; }
        public int Associativity { get; }

        public CacheSet(int associativity)
        {
            Associativity = associativity;
            Ways = new CacheLine[associativity];
            for (int i = 0; i < associativity; i++)
                Ways[i] = new CacheLine();
        }

        public void Reset()
        {
            foreach (var way in Ways) way.Reset();
        }
        
        public int FindWay(uint tag)
        {
            for (int i = 0; i < Ways.Length; i++)
            {
                if (Ways[i].Valid && Ways[i].Tag == tag)
                    return i;
            }
            return -1;
        }
        
        public int FindFreeWay()
        {
            for (int i = 0; i < Ways.Length; i++)
                if (!Ways[i].Valid) return i;
            return -1;
        }
        
        public int ChooseVictimWayRandom(Random rng)
        {
            return rng.Next(Associativity);
        }
    }
}