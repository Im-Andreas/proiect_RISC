using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public enum WritePolicy
    {
        WriteThrough,
        WriteBack
    }

    public enum WriteMissPolicy
    {
        WriteAllocate,
        NoWriteAllocate
    }

    public enum MemoryOperation
    {
        Read,
        Write
    }

    public class WritePolicyAccessResult
    {
        public int Index { get; set; }
        public uint Address { get; set; }
        public uint Tag { get; set; }
        public int SetIndex { get; set; }
        public int Offset { get; set; }
        public MemoryOperation Operation { get; set; }
        public bool IsHit { get; set; }
        public int WayUsed { get; set; }
        public bool WasEviction { get; set; }
        public bool WroteToMemory { get; set; }
        public bool WroteBackVictim { get; set; }
        public bool LoadedFromMemory { get; set; }
        public bool MarkedDirty { get; set; }
    }

    public class WritePolicyCache
    {
        public int NumSets { get; private set; }
        public int Associativity { get; private set; }
        public int BlockSizeBytes { get; private set; }

        public WritePolicy WritePolicy { get; private set; }
        public WriteMissPolicy WriteMissPolicy { get; private set; }
        public ReplacementPolicy ReplacementPolicy { get; private set; }

        private int _offsetBits;
        private int _indexBits;

        private CacheSet[] _sets;
        private readonly Random _rng = new Random();

        public List<WritePolicyAccessResult> AccessLog { get; } = new List<WritePolicyAccessResult>();

        public int TotalAccesses { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int MemoryWrites { get; private set; }
        public int MemoryReads { get; private set; }
        public int WriteBacks { get; private set; }

        public double HitRate => TotalAccesses == 0 ? 0.0 : (double)Hits / TotalAccesses;
        public double MissRate => TotalAccesses == 0 ? 0.0 : (double)Misses / TotalAccesses;

        public WritePolicyCache(int numSets, int associativity, int blockSizeBytes,
            WritePolicy writePolicy = WritePolicy.WriteThrough,
            WriteMissPolicy writeMissPolicy = WriteMissPolicy.WriteAllocate,
            ReplacementPolicy replacementPolicy = ReplacementPolicy.Random)
        {
            Configure(numSets, associativity, blockSizeBytes, writePolicy, writeMissPolicy, replacementPolicy);
        }

        public void Configure(int numSets, int associativity, int blockSizeBytes,
            WritePolicy writePolicy, WriteMissPolicy writeMissPolicy,
            ReplacementPolicy replacementPolicy = ReplacementPolicy.Random)
        {
            if (!IsPowerOfTwo(numSets)) throw new ArgumentException("NumSets must be a power of 2.");
            if (!IsPowerOfTwo(blockSizeBytes)) throw new ArgumentException("BlockSize must be a power of 2.");
            if (associativity < 1) throw new ArgumentException("Associativity must be >= 1.");

            NumSets = numSets;
            Associativity = associativity;
            BlockSizeBytes = blockSizeBytes;
            WritePolicy = writePolicy;
            WriteMissPolicy = writeMissPolicy;
            ReplacementPolicy = replacementPolicy;

            _offsetBits = Log2(BlockSizeBytes);
            _indexBits = Log2(NumSets);

            _sets = new CacheSet[NumSets];
            for (int i = 0; i < NumSets; i++)
                _sets[i] = new CacheSet(Associativity);

            Reset();
        }

        public void SetWritePolicy(WritePolicy policy)
        {
            WritePolicy = policy;
            Reset();
        }

        public void SetWriteMissPolicy(WriteMissPolicy policy)
        {
            WriteMissPolicy = policy;
            Reset();
        }

        public void SetReplacementPolicy(ReplacementPolicy policy)
        {
            ReplacementPolicy = policy;
            Reset();
        }

        public void Reset()
        {
            foreach (var s in _sets) s.Reset();
            AccessLog.Clear();
            TotalAccesses = 0;
            Reads = 0;
            Writes = 0;
            Hits = 0;
            Misses = 0;
            MemoryWrites = 0;
            MemoryReads = 0;
            WriteBacks = 0;
        }

        private (uint tag, int setIndex, int offset) Decompose(uint address)
        {
            int offset = (int)(address & (uint)(BlockSizeBytes - 1));
            uint afterOffset = address >> _offsetBits;
            int setIndex = (int)(afterOffset & (uint)(NumSets - 1));
            uint tag = afterOffset >> _indexBits;
            return (tag, setIndex, offset);
        }

        public bool Read(uint address) => Access(address, MemoryOperation.Read);
        public bool Write(uint address) => Access(address, MemoryOperation.Write);

        public bool Access(uint address, MemoryOperation operation)
        {
            TotalAccesses++;
            if (operation == MemoryOperation.Read) Reads++; else Writes++;

            var (tag, setIndex, offset) = Decompose(address);
            var set = _sets[setIndex];

            int wayHit = set.FindWay(tag);
            bool isHit = wayHit >= 0;

            var result = new WritePolicyAccessResult
            {
                Index = TotalAccesses,
                Address = address,
                Tag = tag,
                SetIndex = setIndex,
                Offset = offset,
                Operation = operation,
                IsHit = isHit
            };

            if (isHit)
            {
                Hits++;
                result.WayUsed = wayHit;
                set.Ways[wayHit].LastUsedCycle = TotalAccesses;
                set.Ways[wayHit].ReferenceBit = true;

                if (operation == MemoryOperation.Write)
                    HandleWriteHit(set, wayHit, result);

                AccessLog.Add(result);
                return true;
            }

            Misses++;

            if (operation == MemoryOperation.Write && WriteMissPolicy == WriteMissPolicy.NoWriteAllocate)
            {
                result.WayUsed = -1;
                MemoryWrites++;
                result.WroteToMemory = true;
                AccessLog.Add(result);
                return false;
            }

            int wayUsed = AllocateLine(set, tag, result);
            result.WayUsed = wayUsed;
            result.LoadedFromMemory = true;
            MemoryReads++;

            if (operation == MemoryOperation.Write)
                HandleWriteHit(set, wayUsed, result);

            AccessLog.Add(result);
            return false;
        }

        private void HandleWriteHit(CacheSet set, int way, WritePolicyAccessResult result)
        {
            if (WritePolicy == WritePolicy.WriteThrough)
            {
                MemoryWrites++;
                result.WroteToMemory = true;
                set.Ways[way].Dirty = false;
            }
            else
            {
                set.Ways[way].Dirty = true;
                result.MarkedDirty = true;
            }
        }

        private int AllocateLine(CacheSet set, uint tag, WritePolicyAccessResult result)
        {
            int freeWay = set.FindFreeWay();
            int wayUsed;

            if (freeWay >= 0)
            {
                wayUsed = freeWay;
            }
            else
            {
                wayUsed = ChooseVictimWay(set);
                result.WasEviction = true;

                if (WritePolicy == WritePolicy.WriteBack && set.Ways[wayUsed].Dirty)
                {
                    WriteBacks++;
                    MemoryWrites++;
                    result.WroteBackVictim = true;
                }
            }

            set.Ways[wayUsed].Valid = true;
            set.Ways[wayUsed].Tag = tag;
            set.Ways[wayUsed].Dirty = false;
            set.Ways[wayUsed].LastUsedCycle = TotalAccesses;
            set.Ways[wayUsed].ReferenceBit = true;
            return wayUsed;
        }

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

        public CacheSet GetSet(int index) => _sets[index];

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
        private static int Log2(int n)
        {
            int bits = 0;
            while (n > 1) { n >>= 1; bits++; }
            return bits;
        }
    }
}