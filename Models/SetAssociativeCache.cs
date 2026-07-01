using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public enum ReplacementPolicy
    {
        Random,
        LRU,
        LRUApprox
    }

    public class SetAccessResult
    {
        public int Index { get; set; }
        public uint Address { get; set; }
        public uint Tag { get; set; }
        public int SetIndex { get; set; }
        public int Offset { get; set; }
        public bool IsHit { get; set; }
        public int WayUsed { get; set; }      
        public bool WasEviction { get; set; }
    }

    public class SetAssociativeCache
    {
        public int NumSets { get; private set; }
        public int Associativity { get; private set; }
        public int BlockSizeBytes { get; private set; }
        public ReplacementPolicy ReplacementPolicy { get; private set; }

        private int _offsetBits;
        private int _indexBits;

        private CacheSet[] _sets;
        private readonly Random _rng = new Random();

        public List<SetAccessResult> AccessLog { get; } = new List<SetAccessResult>();

        public int TotalAccesses { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }

        public double HitRate => TotalAccesses == 0 ? 0.0 : (double)Hits / TotalAccesses;
        public double MissRate => TotalAccesses == 0 ? 0.0 : (double)Misses / TotalAccesses;

        public SetAssociativeCache(int numSets, int associativity, int blockSizeBytes,
            ReplacementPolicy replacementPolicy = ReplacementPolicy.Random)
        {
            Configure(numSets, associativity, blockSizeBytes, replacementPolicy);
        }
        
        public void Configure(int numSets, int associativity, int blockSizeBytes,
            ReplacementPolicy replacementPolicy = ReplacementPolicy.Random)
        {
            if (!IsPowerOfTwo(numSets)) throw new ArgumentException("NumSets must be a power of 2.");
            if (!IsPowerOfTwo(blockSizeBytes)) throw new ArgumentException("BlockSize must be a power of 2.");
            if (associativity < 1) throw new ArgumentException("Associativity must be >= 1.");

            NumSets = numSets;
            Associativity = associativity;
            BlockSizeBytes = blockSizeBytes;
            ReplacementPolicy = replacementPolicy;

            _offsetBits = Log2(BlockSizeBytes);
            _indexBits = Log2(NumSets);

            _sets = new CacheSet[NumSets];
            for (int i = 0; i < NumSets; i++)
                _sets[i] = new CacheSet(Associativity);

            Reset();
        }

        public void Reset()
        {
            foreach (var s in _sets) s.Reset();
            AccessLog.Clear();
            TotalAccesses = 0;
            Hits = 0;
            Misses = 0;
        }

        private (uint tag, int setIndex, int offset) Decompose(uint address)
        {
            int offset = (int)(address & (uint)(BlockSizeBytes - 1));
            uint afterOffset = address >> _offsetBits;
            int setIndex = (int)(afterOffset & (uint)(NumSets - 1));
            uint tag = afterOffset >> _indexBits;
            return (tag, setIndex, offset);
        }

        public bool Access(uint address)
        {
            TotalAccesses++;
            var (tag, setIndex, offset) = Decompose(address);
            var set = _sets[setIndex];

            int wayHit = set.FindWay(tag);
            bool isHit = wayHit >= 0;
            int wayUsed;
            bool wasEviction = false;

            if (isHit)
            {
                Hits++;
                wayUsed = wayHit;
                set.Ways[wayUsed].LastUsedCycle = TotalAccesses;
                set.Ways[wayUsed].ReferenceBit = true;
            }
            else
            {
                Misses++;
                int freeWay = set.FindFreeWay();
                if (freeWay >= 0)
                {
                    wayUsed = freeWay;
                }
                else
                {
                    wayUsed = ChooseVictimWay(set);
                    wasEviction = true;
                }

                set.Ways[wayUsed].Valid = true;
                set.Ways[wayUsed].Tag = tag;
                set.Ways[wayUsed].LastUsedCycle = TotalAccesses;
                set.Ways[wayUsed].ReferenceBit = true;
            }

            var result = new SetAccessResult
            {
                Index = TotalAccesses,
                Address = address,
                Tag = tag,
                SetIndex = setIndex,
                Offset = offset,
                IsHit = isHit,
                WayUsed = wayUsed,
                WasEviction = wasEviction
            };
            AccessLog.Add(result);
            return isHit;
        }
        
        public CacheSet GetSet(int index) => _sets[index];

        private int ChooseVictimWay(CacheSet set)
        {
            switch (ReplacementPolicy)
            {
                case ReplacementPolicy.LRU:
                    return set.ChooseVictimWayLRU();
                case ReplacementPolicy.LRUApprox:
                    return set.ChooseVictimWayClock();
                default:
                    return set.ChooseVictimWayRandom(_rng);
            }
        }

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
        private static int Log2(int n)
        {
            int bits = 0;
            while (n > 1) { n >>= 1; bits++; }
            return bits;
        }
    }
}