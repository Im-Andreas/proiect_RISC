using System;
using System.Linq;

namespace proiect_RISC.Models
{
    public class CacheSet
    {
        public CacheLine[] Ways { get; }
        public int Associativity { get; }

        private int _clockHand = 0;

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
            _clockHand = 0;
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

        public int ChooseVictimWayLRU()
        {
            int victim = 0;
            int minCycle = Ways[0].LastUsedCycle;
            for (int i = 1; i < Ways.Length; i++)
            {
                if (Ways[i].LastUsedCycle < minCycle)
                {
                    minCycle = Ways[i].LastUsedCycle;
                    victim = i;
                }
            }
            return victim;
        }

        public int ChooseVictimWayClock()
        {
            while (true)
            {
                if (!Ways[_clockHand].ReferenceBit)
                {
                    int victim = _clockHand;
                    _clockHand = (_clockHand + 1) % Associativity;
                    return victim;
                }
                Ways[_clockHand].ReferenceBit = false;
                _clockHand = (_clockHand + 1) % Associativity;
            }
        }
    }
}