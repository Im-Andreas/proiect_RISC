using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public enum MemoryAccessType
    {
        Instruction,
        DataRead,
        DataWrite
    }

    public enum MmuCase
    {
        None = 0,
        Case1_TlbHit_CacheHit = 1,
        Case2_TlbHit_CacheMiss = 2,
        Case3_TlbMiss_PteCache_DataCache = 3,
        Case4_TlbMiss_PteCache_DataMp = 4,
        Case5_TlbMiss_PteMp_DataCache = 5,
        Case6_TlbMiss_PteMp_DataMp = 6
    }

    public class TlbEntry
    {
        public bool Valid { get; set; } = false;
        public uint VirtualPage { get; set; } = 0;
        public uint FrameNumber { get; set; } = 0;
        public int LastUsedCycle { get; set; } = 0;

        public void Reset()
        {
            Valid = false;
            VirtualPage = 0;
            FrameNumber = 0;
            LastUsedCycle = 0;
        }
    }

    public class TLB
    {
        public TlbEntry[] Entries { get; }
        public int NumEntries { get; }
        public ReplacementPolicy ReplacementPolicy { get; set; }

        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public double HitRate => (Hits + Misses) == 0 ? 0.0 : (double)Hits / (Hits + Misses);

        private readonly Random _rng = new Random();

        public TLB(int numEntries, ReplacementPolicy replacementPolicy = ReplacementPolicy.LRU)
        {
            NumEntries = numEntries;
            ReplacementPolicy = replacementPolicy;
            Entries = new TlbEntry[numEntries];
            for (int i = 0; i < numEntries; i++) Entries[i] = new TlbEntry();
        }

        public void Reset()
        {
            foreach (var e in Entries) e.Reset();
            Hits = 0;
            Misses = 0;
        }

        public bool Lookup(uint virtualPage, int cycle, out uint frameNumber)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Valid && Entries[i].VirtualPage == virtualPage)
                {
                    Entries[i].LastUsedCycle = cycle;
                    frameNumber = Entries[i].FrameNumber;
                    Hits++;
                    return true;
                }
            }
            frameNumber = 0;
            Misses++;
            return false;
        }

        public void Insert(uint virtualPage, uint frameNumber, int cycle)
        {
            int free = -1;
            for (int i = 0; i < Entries.Length; i++)
                if (!Entries[i].Valid) { free = i; break; }

            int way = free >= 0 ? free : ChooseVictim();
            Entries[way].Valid = true;
            Entries[way].VirtualPage = virtualPage;
            Entries[way].FrameNumber = frameNumber;
            Entries[way].LastUsedCycle = cycle;
        }

        private int ChooseVictim()
        {
            if (ReplacementPolicy == ReplacementPolicy.Random)
                return _rng.Next(Entries.Length);

            int victim = 0;
            int minCycle = Entries[0].LastUsedCycle;
            for (int i = 1; i < Entries.Length; i++)
            {
                if (Entries[i].LastUsedCycle < minCycle)
                {
                    minCycle = Entries[i].LastUsedCycle;
                    victim = i;
                }
            }
            return victim;
        }
    }

    public class MainMemory
    {
        public int NumFrames { get; }
        private readonly bool[] _frameAllocated;
        private readonly Queue<uint> _freeFrames = new Queue<uint>();

        public int Reads { get; private set; }
        public int Writes { get; private set; }

        public MainMemory(int numFrames)
        {
            NumFrames = numFrames;
            _frameAllocated = new bool[numFrames];
        }

        public void Reset()
        {
            for (int i = 0; i < _frameAllocated.Length; i++) _frameAllocated[i] = false;
            _freeFrames.Clear();
            for (uint f = 0; f < NumFrames; f++) _freeFrames.Enqueue(f);
            Reads = 0;
            Writes = 0;
        }

        public uint AllocateFrame()
        {
            if (_freeFrames.Count > 0)
            {
                uint f = _freeFrames.Dequeue();
                _frameAllocated[f] = true;
                return f;
            }
            uint victim = 0;
            _frameAllocated[victim] = true;
            return victim;
        }

        public void ReadBlock() => Reads++;
        public void WriteBlock() => Writes++;
    }

    public class MmuCacheLine
    {
        public bool Valid { get; set; } = false;
        public uint Tag { get; set; } = 0;
        public bool IsPageTableEntry { get; set; } = false;
        public int LastUsedCycle { get; set; } = 0;

        public void Reset()
        {
            Valid = false;
            Tag = 0;
            IsPageTableEntry = false;
            LastUsedCycle = 0;
        }
    }

    public class MmuCacheSet
    {
        public MmuCacheLine[] Lines { get; }
        public MmuCacheSet(int associativity)
        {
            Lines = new MmuCacheLine[associativity];
            for (int i = 0; i < associativity; i++) Lines[i] = new MmuCacheLine();
        }
        public void Reset() { foreach (var l in Lines) l.Reset(); }
    }

    public class Cache
    {
        public int NumSets { get; }
        public int Associativity { get; }
        public int BlockSizeBytes { get; }
        public ReplacementPolicy ReplacementPolicy { get; set; }

        private readonly int _offsetBits;
        private readonly int _indexBits;
        private readonly MmuCacheSet[] _sets;
        private readonly Random _rng = new Random();

        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public double HitRate => (Hits + Misses) == 0 ? 0.0 : (double)Hits / (Hits + Misses);

        public Cache(int numSets, int associativity, int blockSizeBytes, ReplacementPolicy replacementPolicy = ReplacementPolicy.LRU)
        {
            NumSets = numSets;
            Associativity = associativity;
            BlockSizeBytes = blockSizeBytes;
            ReplacementPolicy = replacementPolicy;
            _offsetBits = Log2(blockSizeBytes);
            _indexBits = Log2(numSets);
            _sets = new MmuCacheSet[numSets];
            for (int i = 0; i < numSets; i++) _sets[i] = new MmuCacheSet(associativity);
        }

        public void Reset()
        {
            foreach (var s in _sets) s.Reset();
            Hits = 0;
            Misses = 0;
        }

        private (uint tag, int setIndex) Decompose(uint physicalAddress)
        {
            uint afterOffset = physicalAddress >> _offsetBits;
            int setIndex = (int)(afterOffset & (uint)(NumSets - 1));
            uint tag = afterOffset >> _indexBits;
            return (tag, setIndex);
        }

        public bool Probe(uint physicalAddress)
        {
            var (tag, setIndex) = Decompose(physicalAddress);
            var set = _sets[setIndex];
            foreach (var line in set.Lines)
                if (line.Valid && line.Tag == tag) return true;
            return false;
        }

        public bool Access(uint physicalAddress, bool isPageTableEntry, int cycle)
        {
            var (tag, setIndex) = Decompose(physicalAddress);
            var set = _sets[setIndex];

            for (int i = 0; i < set.Lines.Length; i++)
            {
                if (set.Lines[i].Valid && set.Lines[i].Tag == tag)
                {
                    set.Lines[i].LastUsedCycle = cycle;
                    Hits++;
                    return true;
                }
            }

            Misses++;
            LoadBlock(physicalAddress, isPageTableEntry, cycle);
            return false;
        }

        public void LoadBlock(uint physicalAddress, bool isPageTableEntry, int cycle)
        {
            var (tag, setIndex) = Decompose(physicalAddress);
            var set = _sets[setIndex];

            int free = -1;
            for (int i = 0; i < set.Lines.Length; i++)
                if (!set.Lines[i].Valid) { free = i; break; }

            int way = free >= 0 ? free : ChooseVictim(set);
            set.Lines[way].Valid = true;
            set.Lines[way].Tag = tag;
            set.Lines[way].IsPageTableEntry = isPageTableEntry;
            set.Lines[way].LastUsedCycle = cycle;
        }

        private int ChooseVictim(MmuCacheSet set)
        {
            if (ReplacementPolicy == ReplacementPolicy.Random)
                return _rng.Next(set.Lines.Length);

            int victim = 0;
            int minCycle = set.Lines[0].LastUsedCycle;
            for (int i = 1; i < set.Lines.Length; i++)
            {
                if (set.Lines[i].LastUsedCycle < minCycle)
                {
                    minCycle = set.Lines[i].LastUsedCycle;
                    victim = i;
                }
            }
            return victim;
        }

        private static int Log2(int n)
        {
            int bits = 0;
            while (n > 1) { n >>= 1; bits++; }
            return bits;
        }
    }

    public class MmuLatencyConfig
    {
        public int TlbCycles { get; set; } = 1;
        public int CacheCycles { get; set; } = 1;
        public int MainMemoryCycles { get; set; } = 100;
    }

    public class MemoryAccessResult
    {
        public int Index { get; set; }
        public int ClockCycle { get; set; }
        public MemoryAccessType AccessType { get; set; }

        public uint VirtualAddress { get; set; }
        public uint VirtualPage { get; set; }
        public int PageOffset { get; set; }
        public uint FrameNumber { get; set; }
        public uint PhysicalAddress { get; set; }

        public bool TlbHit { get; set; }
        public bool PteInCache { get; set; }
        public bool DataInCache { get; set; }
        public bool PageFault { get; set; }

        public MmuCase Case { get; set; }
        public string CaseName { get; set; }
        public int CyclesCost { get; set; }
        public string Description { get; set; }
    }

    public class MMU
    {
        public int PageSizeBytes { get; private set; }
        public int NumVirtualPages { get; private set; }
        public int NumPhysicalFrames { get; private set; }

        public TLB Tlb { get; private set; }
        public Cache DataCache { get; private set; }
        public MainMemory MainMemory { get; private set; }
        public MmuLatencyConfig Latencies { get; } = new MmuLatencyConfig();

        private int _offsetBits;
        private bool[] _pageValid;
        private uint[] _pageFrame;
        private uint _pteBaseAddress = 0xF0000000;

        private int _clock = 0;

        public List<MemoryAccessResult> AccessLog { get; } = new List<MemoryAccessResult>();

        public int TotalAccesses { get; private set; }
        public int TlbHits => Tlb.Hits;
        public int TlbMisses => Tlb.Misses;
        public int CacheHits => DataCache.Hits;
        public int CacheMisses => DataCache.Misses;
        public int PageFaults { get; private set; }
        public int SimulationCycles { get; private set; }
        public int[] CaseCounts { get; } = new int[7];
        public int[] CaseCycles { get; } = new int[7];

        public MMU(
            int pageSizeBytes = 64,
            int numVirtualPages = 64,
            int numPhysicalFrames = 16,
            int tlbEntries = 4,
            int cacheSets = 8,
            int cacheAssociativity = 2,
            int cacheBlockSize = 16,
            ReplacementPolicy tlbReplacement = ReplacementPolicy.LRU,
            ReplacementPolicy cacheReplacement = ReplacementPolicy.LRU)
        {
            Configure(pageSizeBytes, numVirtualPages, numPhysicalFrames, tlbEntries,
                cacheSets, cacheAssociativity, cacheBlockSize, tlbReplacement, cacheReplacement);
        }

        public void Configure(
            int pageSizeBytes, int numVirtualPages, int numPhysicalFrames, int tlbEntries,
            int cacheSets, int cacheAssociativity, int cacheBlockSize,
            ReplacementPolicy tlbReplacement, ReplacementPolicy cacheReplacement)
        {
            if (!IsPowerOfTwo(pageSizeBytes)) throw new ArgumentException("PageSize must be a power of 2.");
            if (!IsPowerOfTwo(cacheSets)) throw new ArgumentException("CacheSets must be a power of 2.");
            if (!IsPowerOfTwo(cacheBlockSize)) throw new ArgumentException("CacheBlockSize must be a power of 2.");

            PageSizeBytes = pageSizeBytes;
            NumVirtualPages = numVirtualPages;
            NumPhysicalFrames = numPhysicalFrames;
            _offsetBits = Log2(pageSizeBytes);

            Tlb = new TLB(tlbEntries, tlbReplacement);
            DataCache = new Cache(cacheSets, cacheAssociativity, cacheBlockSize, cacheReplacement);
            MainMemory = new MainMemory(numPhysicalFrames);

            _pageValid = new bool[numVirtualPages];
            _pageFrame = new uint[numVirtualPages];

            Reset();
        }

        public void SetLatencies(int tlbCycles, int cacheCycles, int mainMemoryCycles)
        {
            Latencies.TlbCycles = tlbCycles;
            Latencies.CacheCycles = cacheCycles;
            Latencies.MainMemoryCycles = mainMemoryCycles;
        }

        public void Reset()
        {
            Tlb.Reset();
            DataCache.Reset();
            MainMemory.Reset();
            for (int i = 0; i < _pageValid.Length; i++) { _pageValid[i] = false; _pageFrame[i] = 0; }

            AccessLog.Clear();
            _clock = 0;
            TotalAccesses = 0;
            PageFaults = 0;
            SimulationCycles = 0;
            for (int i = 0; i < CaseCounts.Length; i++) { CaseCounts[i] = 0; CaseCycles[i] = 0; }
        }

        private (uint page, int offset) Decompose(uint virtualAddress)
        {
            int offset = (int)(virtualAddress & (uint)(PageSizeBytes - 1));
            uint page = (virtualAddress >> _offsetBits) % (uint)NumVirtualPages;
            return (page, offset);
        }

        private uint PteAddress(uint virtualPage) => _pteBaseAddress + virtualPage * 4u;

        public MemoryAccessResult ResolveMemoryAccess(uint virtualAddress, MemoryAccessType accessType, int externalCycle)
        {
            _clock++;
            TotalAccesses++;

            var (page, offset) = Decompose(virtualAddress);

            var result = new MemoryAccessResult
            {
                Index = TotalAccesses,
                ClockCycle = externalCycle,
                AccessType = accessType,
                VirtualAddress = virtualAddress,
                VirtualPage = page,
                PageOffset = offset
            };

            int cost = 0;
            uint frame;

            bool tlbHit = Tlb.Lookup(page, _clock, out frame);
            result.TlbHit = tlbHit;
            cost += Latencies.TlbCycles;

            if (!tlbHit)
            {
                uint pteAddr = PteAddress(page);
                bool pteInCache = DataCache.Probe(pteAddr);
                result.PteInCache = pteInCache;

                cost += Latencies.CacheCycles;
                if (pteInCache)
                {
                    DataCache.Access(pteAddr, true, _clock);
                }
                else
                {
                    MainMemory.ReadBlock();
                    cost += Latencies.MainMemoryCycles;
                    DataCache.LoadBlock(pteAddr, true, _clock);
                }

                if (!_pageValid[page])
                {
                    _pageValid[page] = true;
                    _pageFrame[page] = MainMemory.AllocateFrame();
                    PageFaults++;
                    result.PageFault = true;
                }
                frame = _pageFrame[page];

                Tlb.Insert(page, frame, _clock);
            }
            else
            {
                if (!_pageValid[page])
                {
                    _pageValid[page] = true;
                    _pageFrame[page] = frame;
                }
            }

            result.FrameNumber = frame;
            result.PhysicalAddress = (frame << _offsetBits) | (uint)offset;

            bool dataInCache = DataCache.Probe(result.PhysicalAddress);
            result.DataInCache = dataInCache;

            cost += Latencies.CacheCycles;
            if (dataInCache)
            {
                DataCache.Access(result.PhysicalAddress, false, _clock);
            }
            else
            {
                if (accessType == MemoryAccessType.DataWrite) MainMemory.WriteBlock();
                else MainMemory.ReadBlock();
                cost += Latencies.MainMemoryCycles;
                DataCache.LoadBlock(result.PhysicalAddress, false, _clock);
            }

            AssignCase(result);
            result.CyclesCost = cost;
            result.Description = BuildDescription(result);

            SimulationCycles += cost;
            int c = (int)result.Case;
            if (c >= 1 && c <= 6) { CaseCounts[c]++; CaseCycles[c] += cost; }

            AccessLog.Add(result);
            return result;
        }

        private void AssignCase(MemoryAccessResult r)
        {
            if (r.TlbHit)
            {
                if (r.DataInCache) { r.Case = MmuCase.Case1_TlbHit_CacheHit; r.CaseName = "TLB Hit, Cache Hit"; }
                else { r.Case = MmuCase.Case2_TlbHit_CacheMiss; r.CaseName = "TLB Hit, Cache Miss (data in MP)"; }
            }
            else if (r.PteInCache)
            {
                if (r.DataInCache) { r.Case = MmuCase.Case3_TlbMiss_PteCache_DataCache; r.CaseName = "TLB Miss, PTE in Cache, Data in Cache"; }
                else { r.Case = MmuCase.Case4_TlbMiss_PteCache_DataMp; r.CaseName = "TLB Miss, PTE in Cache, Data in MP"; }
            }
            else
            {
                if (r.DataInCache) { r.Case = MmuCase.Case5_TlbMiss_PteMp_DataCache; r.CaseName = "TLB Miss, PTE in MP, Data in Cache"; }
                else { r.Case = MmuCase.Case6_TlbMiss_PteMp_DataMp; r.CaseName = "TLB Miss, PTE in MP, Data in MP"; }
            }
        }

        private string BuildDescription(MemoryAccessResult r)
        {
            string tlb = r.TlbHit ? "TLB hit" : "TLB miss";
            string pte = r.TlbHit ? "" : (r.PteInCache ? ", PTE from cache" : ", PTE from MP");
            string data = r.DataInCache ? "data from cache" : "data from MP";
            string pf = r.PageFault ? " [PAGE FAULT]" : "";
            return $"Case {(int)r.Case} [{r.AccessType}]: {tlb}{pte}, {data}{pf}. VP={r.VirtualPage} -> Frame={r.FrameNumber}, PA=0x{r.PhysicalAddress:X4}, cost={r.CyclesCost} cycles.";
        }

        private static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;
        private static int Log2(int n)
        {
            int bits = 0;
            while (n > 1) { n >>= 1; bits++; }
            return bits;
        }
    }

    public class VirtualMemorySimulator
    {
        public MMU Mmu { get; }

        public VirtualMemorySimulator()
        {
            Mmu = new MMU();
        }

        public void Reset() => Mmu.Reset();

        public MemoryAccessResult Access(uint virtualAddress, MemoryAccessType accessType, int clockCycle)
            => Mmu.ResolveMemoryAccess(virtualAddress, accessType, clockCycle);
    }
}