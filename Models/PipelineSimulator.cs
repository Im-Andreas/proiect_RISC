using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace proiect_RISC.Models
{
    public class PipelineSimulator
    {
        private List<RISCInstruction> _program = new List<RISCInstruction>();
        private int _programCounter;
        public uint PC { get; private set; }

        // Single-slot IF/DEC for display; EX replaced by buffers
        private RISCInstruction _stageIF;
        private RISCInstruction _stageDEC;
        private RISCInstruction _stageMEM;
        private RISCInstruction _stageWB;

        // ── Execution model ───────────────────────────────────────────────────
        public ExecutionModel ExecutionModel { get; set; } = ExecutionModel.InOrder;

        // ── Scoreboard (tabela de marcaj) state ───────────────────────────────
        private readonly List<SbFUState> _sbFUs = new List<SbFUState>();
        private readonly Dictionary<int, string> _sbQi = new Dictionary<int, string>(); // reg → producing FU name

        // ── Tomasulo (stații de rezervare) state ──────────────────────────────
        private readonly List<ReservationStation> _tomasuloRS = new List<ReservationStation>();
        private readonly Dictionary<int, string> _tomasuloRAT = new Dictionary<int, string>(); // reg → RS tag

        // ── Superscalar structures ────────────────────────────────────────────
        public int IssueWidth { get; set; } = 1;
        private readonly List<RISCInstruction> _issueBuffer    = new List<RISCInstruction>();
        private readonly List<RISCInstruction> _inFlightEX     = new List<RISCInstruction>();
        private readonly Dictionary<RISCInstruction, int> _exEntryCycle = new Dictionary<RISCInstruction, int>();
        private readonly List<RISCInstruction> _completionList = new List<RISCInstruction>(); // completed EX, waiting MEM

        public RegisterFile Registers { get; } = new RegisterFile();
        public Memory DataMemory { get; } = new Memory();

        public WritePolicyCache InstructionCache { get; } = new WritePolicyCache(4, 1, 16,
            WritePolicy.WriteThrough, WriteMissPolicy.NoWriteAllocate, ReplacementPolicy.LRU);
        public WritePolicyCache DataCache { get; } = new WritePolicyCache(4, 2, 16,
            WritePolicy.WriteThrough, WriteMissPolicy.WriteAllocate, ReplacementPolicy.LRU);
        public VirtualMemorySimulator VirtualMemory { get; } = new VirtualMemorySimulator();

        public int ICacheMissPenalty { get; set; } = 10;
        public int DCacheMissPenalty { get; set; } = 10;
        public int ICacheStallCycles { get; private set; } = 0;
        public int DCacheStallCycles { get; private set; } = 0;

        public FunctionalUnitSet FunctionalUnits { get; } = new FunctionalUnitSet();
        public int FuStructuralStallCycles { get; private set; } = 0;
        public int FuMultiCycleExtraCycles { get; private set; } = 0;

        public bool VmStallsEnabled { get; set; } = true;
        public int VmInstStallCycles { get; private set; } = 0;
        public int VmDataStallCycles { get; private set; } = 0;

        private int _cacheStallsRemaining = 0;
        private bool _cacheStallIsICache = false;
        private RISCInstruction _cacheStallInstruction = null;
        private bool _dcacheMissPending = false;

        private int _vmStallsRemaining = 0;
        private bool _vmStallIsInst = false;
        private RISCInstruction _vmStallInstruction = null;
        private bool _vmMissPending = false;

        public void ConfigureInstructionCache(int numSets, int assoc, int blockSize, ReplacementPolicy replacement)
            => InstructionCache.Configure(numSets, assoc, blockSize, WritePolicy.WriteThrough, WriteMissPolicy.NoWriteAllocate, replacement);

        public void ConfigureDataCache(int numSets, int assoc, int blockSize,
            WritePolicy writePolicy, WriteMissPolicy writeMissPolicy, ReplacementPolicy replacement)
            => DataCache.Configure(numSets, assoc, blockSize, writePolicy, writeMissPolicy, replacement);

        public int ClockCycle { get; private set; } = 0;
        public int TotalStalls { get; private set; } = 0;
        public bool IsRunning { get; private set; } = false;
        public bool IsHalted { get; private set; } = false;

        public bool ForwardingEnabled { get; set; } = true;
        public bool HazardDetectionEnabled { get; set; } = true;

        public List<PipelineState> History { get; } = new List<PipelineState>();
        public List<SpaceTimeEntry> SpaceTimeTable { get; } = new List<SpaceTimeEntry>();

        public event Action<PipelineState> CycleCompleted;

        // ── Read-only display helpers ─────────────────────────────────────────
        public RISCInstruction DisplayEX  => _inFlightEX.Count  > 0 ? _inFlightEX[0]  : null;
        public IReadOnlyList<RISCInstruction> InFlightEX => _inFlightEX;

        // ── Superscalar state exposed for UI ──────────────────────────────────
        public IReadOnlyList<SbFUState>          ScoreboardFUs => _sbFUs;
        public IReadOnlyDictionary<int, string>  ScoreboardQi  => _sbQi;
        public IReadOnlyList<ReservationStation> TomasuloRS    => _tomasuloRS;
        public IReadOnlyDictionary<int, string>  TomasuloRAT   => _tomasuloRAT;
        public IReadOnlyList<RISCInstruction>    IssueBuffer   => _issueBuffer;

        public void LoadProgram(List<RISCInstruction> program, uint startAddress)
        {
            Reset();
            _program = program ?? throw new ArgumentNullException(nameof(program));
            PC = startAddress;
            _programCounter = 0;
            IsRunning = _program.Count > 0;

            SpaceTimeTable.Clear();
            for (int i = 0; i < _program.Count; i++)
            {
                _program[i].ProgramIndex = i;
                _program[i].IsBubble = false;
                SpaceTimeTable.Add(new SpaceTimeEntry
                {
                    InstructionLabel = _program[i].ToShortString(),
                    InstructionIndex = i
                });
            }
        }

        public void Reset()
        {
            ClockCycle = 0;
            TotalStalls = 0;
            IsHalted = false;
            IsRunning = false;
            _programCounter = 0;
            PC = 0;
            _stageIF = _stageDEC = _stageMEM = _stageWB = null;
            _issueBuffer.Clear();
            _inFlightEX.Clear();
            _exEntryCycle.Clear();
            _completionList.Clear();
            Registers.Reset();
            DataMemory.Reset();
            InstructionCache.Reset();
            DataCache.Reset();
            VirtualMemory.Reset();
            ICacheStallCycles = 0;
            DCacheStallCycles = 0;
            FunctionalUnits.Reset();
            FuStructuralStallCycles = 0;
            FuMultiCycleExtraCycles = 0;
            VmInstStallCycles = 0;
            VmDataStallCycles = 0;
            _cacheStallsRemaining = 0;
            _cacheStallInstruction = null;
            _dcacheMissPending = false;
            _vmStallsRemaining = 0;
            _vmStallInstruction = null;
            _vmMissPending = false;
            _sbFUs.Clear();
            _sbQi.Clear();
            InitTomasuloRS();
            _tomasuloRAT.Clear();
            History.Clear();
            foreach (var entry in SpaceTimeTable) entry.CycleStages.Clear();
        }

        public PipelineState Step()
        {
            if (IsHalted) return BuildSnapshot("Simulatorul este oprit (HALT).", null, null);
            ClockCycle++;
            if (ExecutionModel == ExecutionModel.Scoreboard) return StepScoreboard();
            if (ExecutionModel == ExecutionModel.Tomasulo)   return StepTomasulo();
            return StepInOrder();
        }

        private PipelineState StepInOrder()
        {
            var log = new StringBuilder();
            log.AppendLine($"=== Ciclu {ClockCycle} ===");

            // ── VM/TLB stall handler ──────────────────────────────────────────
            if (_vmStallsRemaining > 0)
            {
                _vmStallsRemaining--;
                TotalStalls++;
                string vmWhich = _vmStallIsInst ? "IF/TLB" : "MEM/TLB";
                log.AppendLine($"[TLB STALL] {vmWhich} @ 0x{_vmStallInstruction?.Address:X4} ({_vmStallsRemaining} cicli rămași)");
                if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                if (_vmStallIsInst)
                {
                    // IF-side TLB stall: only fetch frozen; EX/MEM/WB and dispatch continue
                    VmInstStallCycles++;
                    TickAndDrainFUs(log);
                    ExecuteWB(log);
                    ExecuteMEM(log);
                    AdvanceMEM();
                    // Promote _stageDEC — decode completes during fetch stall
                    if (_stageDEC != null && !_stageDEC.IsBubble)
                    {
                        _issueBuffer.Add(_stageDEC);
                        _stageDEC = null;
                    }
                    // Dispatch already-decoded instructions
                    DispatchFromIssueBuffer(log);
                    // Keep stall instruction marked as "vm", not overwritten by dispatch stall
                    if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                    foreach (var inf in _inFlightEX)
                    {
                        bool isFirst = _exEntryCycle.TryGetValue(inf, out int ec) && ec == ClockCycle;
                        RecordSpaceTime(inf, ClockCycle, isFirst ? "EX" : "ex_multi");
                    }
                }
                else
                {
                    VmDataStallCycles++;
                    // MEM-side stall: freeze everything
                    foreach (var inf in _inFlightEX) RecordSpaceTime(inf, ClockCycle, "ex_multi");
                }
                RecordSpaceTime(_stageMEM, ClockCycle, "MEM");
                RecordSpaceTime(_stageWB,  ClockCycle, "WB");
                var vmS = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(vmS); CycleCompleted?.Invoke(vmS);
                return vmS;
            }

            // ── Cache stall handler ───────────────────────────────────────────
            if (_cacheStallsRemaining > 0)
            {
                _cacheStallsRemaining--;
                string which = _cacheStallIsICache ? "ICache" : "DCache";
                log.AppendLine($"[CACHE STALL] {which} MISS @ 0x{_cacheStallInstruction?.Address:X4} ({_cacheStallsRemaining} cicli rămași)");
                if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                if (_cacheStallIsICache)
                {
                    // IF-side miss: only fetch is frozen; EX/MEM/WB and dispatch continue
                    ICacheStallCycles++;
                    TickAndDrainFUs(log);
                    ExecuteWB(log);
                    ExecuteMEM(log);
                    AdvanceMEM();
                    // Promote _stageDEC to issue buffer — decode finishes independently of fetch
                    if (_stageDEC != null && !_stageDEC.IsBubble)
                    {
                        _issueBuffer.Add(_stageDEC);
                        _stageDEC = null;
                    }
                    // Dispatch already-decoded instructions (fetch stall ≠ issue stall)
                    DispatchFromIssueBuffer(log);
                    // Ensure stall instruction stays marked as "cache", not overwritten by dispatch stall
                    if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                    // Record EX for all in-flight, including newly dispatched this cycle
                    foreach (var inf in _inFlightEX)
                    {
                        bool isFirst = _exEntryCycle.TryGetValue(inf, out int ec) && ec == ClockCycle;
                        RecordSpaceTime(inf, ClockCycle, isFirst ? "EX" : "ex_multi");
                    }
                }
                else
                {
                    // DCache miss (MEM stage): freeze everything
                    DCacheStallCycles++;
                    foreach (var inf in _inFlightEX) RecordSpaceTime(inf, ClockCycle, "ex_multi");
                }
                RecordSpaceTime(_stageMEM, ClockCycle, "MEM");
                RecordSpaceTime(_stageWB,  ClockCycle, "WB");
                var cStall = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(cStall); CycleCompleted?.Invoke(cStall);
                return cStall;
            }

            // ── 1. WB: write back ─────────────────────────────────────────────
            ExecuteWB(log);

            // ── 2. MEM: process memory stage ──────────────────────────────────
            ExecuteMEM(log);

            // ── VM/DCache miss triggered in MEM → stall before advancing ──────
            if (_vmMissPending)
            {
                _vmMissPending = false;
                if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                log.AppendLine($"[TLB STALL] MEM/TLB MISS → +{_vmStallsRemaining} cicli");
                var vmP = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(vmP); CycleCompleted?.Invoke(vmP);
                return vmP;
            }
            if (_dcacheMissPending)
            {
                _dcacheMissPending = false;
                DCacheStallCycles++;
                _stageWB = null;
                if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                log.AppendLine($"[CACHE STALL] DCache MISS → +{DCacheMissPenalty} cicli");
                var dStall = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(dStall); CycleCompleted?.Invoke(dStall);
                return dStall;
            }

            // ── 3+4. Tick FUs first so fresh completions land in _stageMEM ──────
            // TickAndDrainFUs BEFORE AdvanceMEM: prevents a 1-cycle empty cell gap
            // between EX completion and MEM in the space-time diagram.
            TickAndDrainFUs(log);
            AdvanceMEM();

            // Count extra EX cycles (instructions still in flight past their first cycle)
            foreach (var inf in _inFlightEX)
                FuMultiCycleExtraCycles++;

            // ── 5. Multi-width fetch FIRST — fill issue buffer before dispatch ───
            // Filling before dispatch eliminates the 1-cycle empty gap between DEC
            // and EX: instructions promoted from _stageDEC this cycle are dispatched
            // in the same cycle rather than waiting until the next.
            for (int _fill = 0; _fill < IssueWidth; _fill++)
            {
                if (_stageDEC != null && !_stageDEC.IsBubble)
                    _issueBuffer.Add(_stageDEC);
                if (_stageIF != null && !_stageIF.IsBubble)
                    RecordSpaceTime(_stageIF, ClockCycle, "DEC");
                _stageDEC = _stageIF;
                _stageIF = FetchNextInstruction(log);
                if (_vmStallsRemaining > 0 || _cacheStallsRemaining > 0) break;
                if (_stageIF == null && _stageDEC == null) break;
            }

            // ── 6. Dispatch up to IssueWidth from issue buffer ────────────────────
            var (lastHazard, lastFwd) = DispatchFromIssueBuffer(log);

            // ── 7. Record space-time ───────────────────────────────────────────
            // IF is recorded inside FetchNextInstruction; DEC was recorded in fill loop.
            // Re-record _stageIF and _stageDEC for safety (last fetch/decode pair).
            RecordSpaceTime(_stageIF, ClockCycle, "IF");
            RecordSpaceTime(_stageDEC, ClockCycle, "DEC");
            foreach (var inf in _inFlightEX)
            {
                bool isFirst = _exEntryCycle.TryGetValue(inf, out int ec) && ec == ClockCycle;
                RecordSpaceTime(inf, ClockCycle, isFirst ? "EX" : "ex_multi");
            }
            RecordSpaceTime(_stageMEM, ClockCycle, "MEM");
            RecordSpaceTime(_stageWB, ClockCycle, "WB");

            // ── 8. Check halt / end ───────────────────────────────────────────
            if (_stageWB != null && _stageWB.Opcode == Opcode.HALT)
            {
                IsHalted = true;
                log.AppendLine("[HALT] Instructiunea HALT a terminat WB.");
            }

            bool allEmpty = _stageIF == null && _stageDEC == null &&
                            _issueBuffer.Count == 0 && _inFlightEX.Count == 0 &&
                            _completionList.Count == 0 && _stageMEM == null && _stageWB == null;
            if (allEmpty)
            {
                IsRunning = false;
                IsHalted = true;
                log.AppendLine("[END] Pipeline gol; program terminat.");
            }

            var state = BuildSnapshot(log.ToString(), lastHazard, lastFwd);
            History.Add(state);
            CycleCompleted?.Invoke(state);
            return state;
        }

        public List<PipelineState> RunToEnd(int maxCycles = 10000)
        {
            var states = new List<PipelineState>();
            while (!IsHalted && ClockCycle < maxCycles) states.Add(Step());
            return states;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SCOREBOARD (tabela de marcaj — CDC 6600)
        // ══════════════════════════════════════════════════════════════════════

        private PipelineState StepScoreboard()
        {
            var log = new StringBuilder();
            log.AppendLine($"=== Ciclu {ClockCycle} [Scoreboard] ===");
            HazardInfo lastHazard = HazardInfo.None;

            // ── IF-side VM stall: fetch frozen, but FUs continue ──────────────
            if (_vmStallsRemaining > 0)
            {
                _vmStallsRemaining--;
                TotalStalls++;
                if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                log.AppendLine($"[TLB STALL] {(_vmStallIsInst ? "IF" : "MEM")}");
                if (_vmStallIsInst)
                {
                    VmInstStallCycles++;
                    SbStepWR(log); SbStepEX(log); SbStepRO(log);
                    if (_stageDEC != null && !_stageDEC.IsBubble) { _issueBuffer.Add(_stageDEC); _stageDEC = null; }
                    SbStepIssue(log, ref lastHazard);
                    if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                }
                else VmDataStallCycles++;
                RecordSpaceTime(_stageIF, ClockCycle, "IF");
                var vs = BuildSnapshot(log.ToString(), lastHazard, ForwardingInfo.None);
                History.Add(vs); CycleCompleted?.Invoke(vs); return vs;
            }

            // ── ICache stall: fetch frozen, FUs continue ──────────────────────
            if (_cacheStallsRemaining > 0)
            {
                _cacheStallsRemaining--;
                if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                log.AppendLine($"[CACHE STALL] {(_cacheStallIsICache ? "ICache" : "DCache")}");
                if (_cacheStallIsICache)
                {
                    ICacheStallCycles++;
                    SbStepWR(log); SbStepEX(log); SbStepRO(log);
                    if (_stageDEC != null && !_stageDEC.IsBubble) { _issueBuffer.Add(_stageDEC); _stageDEC = null; }
                    SbStepIssue(log, ref lastHazard);
                    if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                }
                else DCacheStallCycles++;
                RecordSpaceTime(_stageIF, ClockCycle, "IF");
                var cs = BuildSnapshot(log.ToString(), lastHazard, ForwardingInfo.None);
                History.Add(cs); CycleCompleted?.Invoke(cs); return cs;
            }

            // ── Normal cycle ──────────────────────────────────────────────────
            SbStepWR(log);
            SbStepEX(log);
            SbStepRO(log);

            // ── Fetch BEFORE Issue to eliminate DEC→IS gap ───────────────────
            for (int fill = 0; fill < IssueWidth; fill++)
            {
                if (_stageDEC != null && !_stageDEC.IsBubble) _issueBuffer.Add(_stageDEC);
                if (_stageIF != null && !_stageIF.IsBubble) RecordSpaceTime(_stageIF, ClockCycle, "DEC");
                _stageDEC = _stageIF;
                _stageIF = FetchNextInstruction(log);
                if (_vmStallsRemaining > 0 || _cacheStallsRemaining > 0) break;
                if (_stageIF == null && _stageDEC == null) break;
            }

            SbStepIssue(log, ref lastHazard);

            RecordSpaceTime(_stageIF, ClockCycle, "IF");
            RecordSpaceTime(_stageDEC, ClockCycle, "DEC");

            bool allDone = _stageIF == null && _stageDEC == null &&
                           _issueBuffer.Count == 0 && _sbFUs.Count == 0;
            if (allDone) { IsRunning = false; IsHalted = true; log.AppendLine("[END] Pipeline gol."); }

            var state = BuildSnapshot(log.ToString(), lastHazard, ForwardingInfo.None);
            History.Add(state); CycleCompleted?.Invoke(state); return state;
        }

        // Write Result stage — check WAR, write register file, update Qj/Rj
        private void SbStepWR(StringBuilder log)
        {
            foreach (var fu in _sbFUs.Where(f => f.Stage == SbStage.WR).ToList())
            {
                // WAR hazard: another FU has Rj=YES (ready to read) and Fj==our Fi
                bool war = _sbFUs.Any(f2 => f2 != fu &&
                    ((f2.Fj == fu.Fi && f2.Rj) || (f2.Fk == fu.Fi && f2.Rk)));
                if (war)
                {
                    TotalStalls++;
                    RecordSpaceTime(fu.Instr, ClockCycle, "war");
                    SbAddGantt(fu, false);
                    log.AppendLine($"[SB WAR] '{fu.Instr.ToShortString()}' stall WAR pe R{fu.Fi}");
                    continue;
                }
                // Write result
                if (fu.Fi >= 0 && fu.ResultValue.HasValue)
                    Registers.Write(fu.Fi, fu.ResultValue.Value);
                // Notify waiting FUs that their operand is now available
                foreach (var f2 in _sbFUs)
                {
                    if (f2.Qj == fu.Name) { f2.Qj = ""; f2.Rj = true; }
                    if (f2.Qk == fu.Name) { f2.Qk = ""; f2.Rk = true; }
                }
                if (fu.Fi >= 0) _sbQi.Remove(fu.Fi);
                if (fu.Instr.Opcode == Opcode.HALT) { IsHalted = true; }
                RecordSpaceTime(fu.Instr, ClockCycle, "WB");
                log.AppendLine($"[SB WR] '{fu.Instr.ToShortString()}' → R{fu.Fi}={fu.ResultValue}");
                _sbFUs.Remove(fu);
            }
        }

        // Execute stage — tick CyclesLeft, advance to WR when done
        private void SbStepEX(StringBuilder log)
        {
            foreach (var fu in _sbFUs.Where(f => f.Stage == SbStage.EX).ToList())
            {
                SbAddGantt(fu, false);
                RecordSpaceTime(fu.Instr, ClockCycle, fu.CyclesLeft > 0 ? "ex_multi" : "EX");
                if (fu.CyclesLeft > 0) { fu.CyclesLeft--; if (fu.CyclesLeft == 0) fu.Stage = SbStage.WR; }
                else fu.Stage = SbStage.WR;
            }
        }

        // Read Operands stage — advance to EX when Rj && Rk
        private void SbStepRO(StringBuilder log)
        {
            foreach (var fu in _sbFUs.Where(f => f.Stage == SbStage.RO).ToList())
            {
                if (fu.Rj && fu.Rk)
                {
                    // Operands ready: read register file
                    if (fu.Fj >= 0) fu.Instr.Op1Value = Registers.Read(fu.Fj);
                    if (fu.Fk >= 0) fu.Instr.Op2Value = Registers.Read(fu.Fk);
                    else            fu.Instr.Op2Value = fu.Instr.Imm;
                    fu.Rj = fu.Rk = false; // mark as consumed (WAR check uses Rj/Rk=false = "already read")
                    // Execute instruction
                    ExecuteEXInstruction(fu.Instr, log);
                    fu.ResultValue = fu.Instr.ResultValue;
                    // Handle LOAD/STORE memory access (simplified: direct DataMemory, no DCache stall)
                    if (fu.Instr.Class == InstructionClass.LOAD && fu.Instr.Op1Value.HasValue)
                        fu.ResultValue = fu.Instr.ResultValue = DataMemory.Read((uint)fu.Instr.Op1Value.Value);
                    if (fu.Instr.Class == InstructionClass.STORE && fu.Instr.Op1Value.HasValue && fu.Instr.Op2Value.HasValue)
                        DataMemory.Write((uint)fu.Instr.Op1Value.Value, fu.Instr.Op2Value.Value);
                    // Transition to EX (or directly to WR if latency=1)
                    int lat = FunctionalUnits.GetLatency(fu.UnitType);
                    fu.CyclesLeft = lat - 1;
                    fu.Stage = fu.CyclesLeft > 0 ? SbStage.EX : SbStage.WR;
                    SbAddGantt(fu, true); // first cycle in FU
                    RecordSpaceTime(fu.Instr, ClockCycle, "EX");
                    log.AppendLine($"[SB RO→EX] '{fu.Instr.ToShortString()}' lat={lat}");
                }
                else
                {
                    RecordSpaceTime(fu.Instr, ClockCycle, "IS"); // still waiting for operands
                    log.AppendLine($"[SB RO WAIT] '{fu.Instr.ToShortString()}' Qj={fu.Qj}({fu.Rj}) Qk={fu.Qk}({fu.Rk})");
                }
            }
        }

        // Issue stage — in-order from issueBuffer; check structural hazard and WAW
        private void SbStepIssue(StringBuilder log, ref HazardInfo lastHazard)
        {
            int issued = 0;
            int slot = 0;
            while (slot < _issueBuffer.Count && issued < IssueWidth)
            {
                var instr = _issueBuffer[slot];
                if (instr == null || instr.IsBubble) { _issueBuffer.RemoveAt(slot); continue; }

                var reqFU = FunctionalUnitSet.GetRequiredUnit(instr);
                int dest = instr.GetWriteRegister();

                // Structural hazard: count occupied FU instances of this type
                if (reqFU.HasValue)
                {
                    int occupied = _sbFUs.Count(f => f.UnitType == reqFU.Value);
                    if (occupied >= FunctionalUnits.Configs[reqFU.Value].Count)
                    {
                        TotalStalls++; FuStructuralStallCycles++;
                        RecordSpaceTime(instr, ClockCycle, "stall");
                        log.AppendLine($"[SB STRUCTURAL] Toate UF {reqFU.Value} ocupate");
                        lastHazard = new HazardInfo { HasHazard = true, HazardType = "Structural", Consumer = instr };
                        break; // in-order: stop issuing
                    }
                }

                // WAW hazard: Qi[dest] != "" means another instruction will write dest
                if (dest >= 0 && _sbQi.ContainsKey(dest))
                {
                    TotalStalls++;
                    RecordSpaceTime(instr, ClockCycle, "waw");
                    log.AppendLine($"[SB WAW] R{dest} produs de {_sbQi[dest]}");
                    lastHazard = new HazardInfo { HasHazard = true, HazardType = "WAW", Consumer = instr, ConflictRegister = $"R{dest}" };
                    break; // in-order: stop issuing
                }

                // NOP: no FU needed — consume without dispatching to any unit
                if (!reqFU.HasValue && instr.Opcode != Opcode.HALT)
                {
                    _issueBuffer.RemoveAt(slot);
                    issued++;
                    RecordSpaceTime(instr, ClockCycle, "IS");
                    continue;
                }

                // Find first free FU index of required type
                var fuType = reqFU ?? FunctionalUnitType.ADD;
                int fuCount = FunctionalUnits.Configs[fuType].Count;
                int fuIdx = Enumerable.Range(0, fuCount).FirstOrDefault(i => !_sbFUs.Any(f => f.UnitType == fuType && f.UnitIndex == i));

                // Set up scoreboard entry
                var fu = new SbFUState
                {
                    UnitType = fuType,
                    UnitIndex = fuIdx,
                    Instr = instr,
                    Fi = dest,
                    Fj = instr.Rs1 ?? -1,
                    Fk = instr.Rs2.HasValue ? instr.Rs2.Value : -1,
                    Stage = SbStage.RO,
                };

                // Qj / Rj: source 1
                if (fu.Fj >= 0 && _sbQi.TryGetValue(fu.Fj, out string qj)) { fu.Qj = qj; fu.Rj = false; }
                else { fu.Qj = ""; fu.Rj = true; }

                // Qk / Rk: source 2 — if no register (immediate), always ready
                if (instr.Rs2.HasValue && instr.Rs2.Value >= 0 && _sbQi.TryGetValue(instr.Rs2.Value, out string qk))
                { fu.Qk = qk; fu.Rk = false; }
                else { fu.Qk = ""; fu.Rk = true; }

                if (dest >= 0) { _sbQi[dest] = fu.Name; Registers.InvalidateRegister(dest); }

                _sbFUs.Add(fu);
                _issueBuffer.RemoveAt(slot);
                issued++;
                RecordSpaceTime(instr, ClockCycle, "IS");
                log.AppendLine($"[SB ISSUE] '{instr.ToShortString()}' → {fu.Name} (Qj={fu.Qj},Rj={fu.Rj} Qk={fu.Qk},Rk={fu.Rk})");
            }
        }

        private void SbAddGantt(SbFUState fu, bool isFirst)
        {
            FunctionalUnits.OccupancyLog.Add(new OccupancyRecord
            {
                Cycle = ClockCycle, UnitType = fu.UnitType, UnitIndex = fu.UnitIndex,
                InstructionLabel = fu.Instr.ToShortString(), InstructionProgIdx = fu.Instr.ProgramIndex,
                IsFirstCycle = isFirst
            });
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TOMASULO — stații de rezervare + RAT + CDB
        // ══════════════════════════════════════════════════════════════════════

        private void InitTomasuloRS()
        {
            _tomasuloRS.Clear();
            foreach (var cfg in FunctionalUnits.Configs.Values)
            {
                int rsCount = cfg.Count + 2; // extra RS slots beyond FU count
                for (int i = 0; i < rsCount; i++)
                    _tomasuloRS.Add(new ReservationStation { UnitType = cfg.UnitType, RSIndex = i });
            }
        }

        private PipelineState StepTomasulo()
        {
            var log = new StringBuilder();
            log.AppendLine($"=== Ciclu {ClockCycle} [Tomasulo] ===");

            // ── IF-side VM stall ──────────────────────────────────────────────
            if (_vmStallsRemaining > 0)
            {
                _vmStallsRemaining--;
                TotalStalls++;
                if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                if (_vmStallIsInst)
                {
                    VmInstStallCycles++;
                    TomStepCDB(log); TomStepExecute(log); TomStepDispatch(log);
                    if (_stageDEC != null && !_stageDEC.IsBubble) { _issueBuffer.Add(_stageDEC); _stageDEC = null; }
                    TomStepIssue(log);
                    if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                }
                else VmDataStallCycles++;
                RecordSpaceTime(_stageIF, ClockCycle, "IF");
                var vs = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(vs); CycleCompleted?.Invoke(vs); return vs;
            }

            // ── ICache stall ──────────────────────────────────────────────────
            if (_cacheStallsRemaining > 0)
            {
                _cacheStallsRemaining--;
                if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                if (_cacheStallIsICache)
                {
                    ICacheStallCycles++;
                    TomStepCDB(log); TomStepExecute(log); TomStepDispatch(log);
                    if (_stageDEC != null && !_stageDEC.IsBubble) { _issueBuffer.Add(_stageDEC); _stageDEC = null; }
                    TomStepIssue(log);
                    if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                }
                else DCacheStallCycles++;
                RecordSpaceTime(_stageIF, ClockCycle, "IF");
                var cs = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(cs); CycleCompleted?.Invoke(cs); return cs;
            }

            // ── Normal cycle ──────────────────────────────────────────────────
            TomStepCDB(log);
            TomStepExecute(log);
            TomStepDispatch(log);

            // ── Fetch BEFORE Issue to eliminate DEC→IS gap ───────────────────
            for (int fill = 0; fill < IssueWidth; fill++)
            {
                if (_stageDEC != null && !_stageDEC.IsBubble) _issueBuffer.Add(_stageDEC);
                if (_stageIF != null && !_stageIF.IsBubble) RecordSpaceTime(_stageIF, ClockCycle, "DEC");
                _stageDEC = _stageIF;
                _stageIF = FetchNextInstruction(log);
                if (_vmStallsRemaining > 0 || _cacheStallsRemaining > 0) break;
                if (_stageIF == null && _stageDEC == null) break;
            }

            TomStepIssue(log);

            RecordSpaceTime(_stageIF, ClockCycle, "IF");
            RecordSpaceTime(_stageDEC, ClockCycle, "DEC");

            bool allDone = _stageIF == null && _stageDEC == null &&
                           _issueBuffer.Count == 0 && !_tomasuloRS.Any(r => r.Busy);
            if (allDone) { IsRunning = false; IsHalted = true; log.AppendLine("[END] Pipeline gol."); }

            var state = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
            History.Add(state); CycleCompleted?.Invoke(state); return state;
        }

        // CDB: broadcast results of completed RSes → update RAT and waiting RSes
        private void TomStepCDB(StringBuilder log)
        {
            foreach (var rs in _tomasuloRS.Where(r => r.Busy && r.Dispatched && r.CyclesLeft == 0 && !r.WRDone).ToList())
            {
                int value = rs.ResultValue ?? 0;

                // Handle LOAD (memory access at WB time)
                if (rs.Instr.Class == InstructionClass.LOAD && rs.Instr.Op1Value.HasValue)
                { rs.ResultValue = DataMemory.Read((uint)rs.Instr.Op1Value.Value); value = rs.ResultValue.Value; }
                if (rs.Instr.Class == InstructionClass.STORE && rs.Instr.Op1Value.HasValue && rs.Instr.Op2Value.HasValue)
                    DataMemory.Write((uint)rs.Instr.Op1Value.Value, rs.Instr.Op2Value.Value);

                // Snoop: update Vj/Vk of all waiting RSes watching this tag
                foreach (var rs2 in _tomasuloRS.Where(r => r.Busy && !r.Dispatched))
                {
                    if (rs2.Qj == rs.Tag) { rs2.Vj = value; rs2.Qj = ""; }
                    if (rs2.Qk == rs.Tag) { rs2.Vk = value; rs2.Qk = ""; }
                }

                // Write register file if RAT[dest] still points to this RS (no later rename)
                if (rs.DestReg >= 0)
                {
                    if (_tomasuloRAT.TryGetValue(rs.DestReg, out string tag) && tag == rs.Tag)
                    {
                        Registers.Write(rs.DestReg, value);
                        _tomasuloRAT.Remove(rs.DestReg);
                    }
                }

                // Free FU instance
                var unit = FunctionalUnits.Units.FirstOrDefault(u => u.UnitType == rs.UnitType && u.UnitIndex == rs.FUInstanceIndex);
                unit?.Free();

                if (rs.Instr.Opcode == Opcode.HALT) IsHalted = true;
                RecordSpaceTime(rs.Instr, ClockCycle, "WB");
                log.AppendLine($"[CDB] {rs.Tag} → R{rs.DestReg}={value}");
                rs.WRDone = true;
                rs.Busy = false;
            }
        }

        // Execute: tick CyclesLeft for all dispatched RSes
        private void TomStepExecute(StringBuilder log)
        {
            foreach (var rs in _tomasuloRS.Where(r => r.Busy && r.Dispatched && r.CyclesLeft > 0))
            {
                // Gantt: record occupancy
                FunctionalUnits.OccupancyLog.Add(new OccupancyRecord
                {
                    Cycle = ClockCycle, UnitType = rs.UnitType, UnitIndex = rs.FUInstanceIndex,
                    InstructionLabel = rs.Instr.ToShortString(), InstructionProgIdx = rs.Instr.ProgramIndex,
                    IsFirstCycle = false
                });
                rs.CyclesLeft--;
                RecordSpaceTime(rs.Instr, ClockCycle, rs.CyclesLeft > 0 ? "ex_multi" : "EX");
            }
        }

        // Dispatch: RS entries with all operands ready → free FU → start executing
        private void TomStepDispatch(StringBuilder log)
        {
            foreach (var rs in _tomasuloRS.Where(r => r.Busy && !r.Dispatched && r.Qj == "" && r.Qk == ""))
            {
                // Find free FU instance of this type
                var freeUnit = FunctionalUnits.Units.FirstOrDefault(u => u.UnitType == rs.UnitType && u.IsAvailable);
                if (freeUnit == null) continue; // structural hazard: no FU available, wait

                // Dispatch to FU
                int lat = FunctionalUnits.GetLatency(rs.UnitType);
                freeUnit.OccupiedBy = rs.Instr;
                freeUnit.CyclesLeft = lat;
                rs.FUInstanceIndex = freeUnit.UnitIndex;
                rs.Dispatched = true;
                rs.CyclesLeft = lat - 1; // first EX cycle happens now (recorded as "EX")

                // Compute result
                rs.Instr.Op1Value = rs.Vj;
                rs.Instr.Op2Value = rs.Vk ?? rs.Imm;
                ExecuteEXInstruction(rs.Instr, log);
                rs.ResultValue = rs.Instr.ResultValue;

                // Gantt: first cycle
                FunctionalUnits.OccupancyLog.Add(new OccupancyRecord
                {
                    Cycle = ClockCycle, UnitType = rs.UnitType, UnitIndex = rs.FUInstanceIndex,
                    InstructionLabel = rs.Instr.ToShortString(), InstructionProgIdx = rs.Instr.ProgramIndex,
                    IsFirstCycle = true
                });
                RecordSpaceTime(rs.Instr, ClockCycle, "EX");
                log.AppendLine($"[TOM DISPATCH] '{rs.Instr.ToShortString()}' → {rs.UnitType}-{rs.FUInstanceIndex} (lat={lat})");
            }
        }

        // Issue: in-order from issueBuffer → reservation stations (with register renaming via RAT)
        private void TomStepIssue(StringBuilder log)
        {
            int issued = 0;
            int slot = 0;
            while (slot < _issueBuffer.Count && issued < IssueWidth)
            {
                var instr = _issueBuffer[slot];
                if (instr == null || instr.IsBubble) { _issueBuffer.RemoveAt(slot); continue; }

                var reqFU = FunctionalUnitSet.GetRequiredUnit(instr);

                // NOP: no RS/FU needed — just consume the issue slot
                if (!reqFU.HasValue && instr.Opcode != Opcode.HALT)
                {
                    _issueBuffer.RemoveAt(slot);
                    issued++;
                    RecordSpaceTime(instr, ClockCycle, "IS");
                    continue;
                }

                var fuType = reqFU ?? FunctionalUnitType.ADD;

                // Find free RS entry for this FU type
                var freeRS = _tomasuloRS.FirstOrDefault(r => r.UnitType == fuType && !r.Busy);
                if (freeRS == null)
                {
                    TotalStalls++; FuStructuralStallCycles++;
                    RecordSpaceTime(instr, ClockCycle, "stall");
                    log.AppendLine($"[TOM RS FULL] Nicio RS liberă pentru {fuType}");
                    break; // in-order: stop issuing
                }

                // Issue to RS: rename operands via RAT
                freeRS.Busy = true;
                freeRS.Op = instr.Opcode;
                freeRS.Instr = instr;
                freeRS.DestReg = instr.GetWriteRegister();
                freeRS.Imm = instr.Imm;
                freeRS.WRDone = false;
                freeRS.Dispatched = false;
                freeRS.CyclesLeft = 0;
                freeRS.ResultValue = null;
                freeRS.FUInstanceIndex = -1;

                // Source 1 via RAT
                int src1 = instr.Rs1 ?? -1;
                if (src1 >= 0 && _tomasuloRAT.TryGetValue(src1, out string t1))
                { freeRS.Vj = null; freeRS.Qj = t1; }
                else
                { freeRS.Vj = src1 >= 0 ? Registers.Read(src1) : 0; freeRS.Qj = ""; }

                // Source 2 via RAT (or immediate)
                int src2 = instr.Rs2 ?? -1;
                if (src2 >= 0 && _tomasuloRAT.TryGetValue(src2, out string t2))
                { freeRS.Vk = null; freeRS.Qk = t2; }
                else if (src2 >= 0)
                { freeRS.Vk = Registers.Read(src2); freeRS.Qk = ""; }
                else
                { freeRS.Vk = instr.Imm ?? 0; freeRS.Qk = ""; }

                // Register renaming: RAT[dest] = this RS tag → eliminates WAW/WAR
                if (freeRS.DestReg >= 0)
                {
                    _tomasuloRAT[freeRS.DestReg] = freeRS.Tag;
                    Registers.InvalidateRegister(freeRS.DestReg);
                }

                _issueBuffer.RemoveAt(slot);
                issued++;
                RecordSpaceTime(instr, ClockCycle, "IS");
                log.AppendLine($"[TOM ISSUE] '{instr.ToShortString()}' → {freeRS.Tag} (Qj={freeRS.Qj},Vj={freeRS.Vj} Qk={freeRS.Qk},Vk={freeRS.Vk})");
            }
        }

        // ── Helpers shared between Step() and stall handlers ─────────────────

        // Tick all functional units; move completions to _completionList.
        private void TickAndDrainFUs(StringBuilder log)
        {
            FunctionalUnits.RecordOccupancy(ClockCycle);
            var done = FunctionalUnits.TickUnits();
            foreach (var c in done)
            {
                _inFlightEX.Remove(c);
                _exEntryCycle.Remove(c);
                _completionList.Add(c);
                log?.AppendLine($"[EX DONE during stall] '{c.ToShortString()}' → MEM queue");
            }
            foreach (var inf in _inFlightEX)
            {
                bool isFirst = _exEntryCycle.TryGetValue(inf, out int ec) && ec == ClockCycle;
                RecordSpaceTime(inf, ClockCycle, isFirst ? "EX" : "ex_multi");
            }
        }

        // Advance _stageMEM ← completionList, _stageWB ← _stageMEM.
        private void AdvanceMEM()
        {
            _stageWB = _stageMEM;
            _stageMEM = _completionList.Count > 0 ? _completionList[0] : null;
            if (_completionList.Count > 0) _completionList.RemoveAt(0);
        }

        // Dispatch up to IssueWidth instructions from issue buffer (in-order).
        // Called from Step() and from IF-side stall handlers.
        private (HazardInfo lastHazard, ForwardingInfo lastFwd) DispatchFromIssueBuffer(StringBuilder log)
        {
            HazardInfo lastHazard = HazardInfo.None;
            ForwardingInfo lastFwd = ForwardingInfo.None;
            bool anyStall = false;
            var dispatchedThisCycle = new List<RISCInstruction>();

            int slot = 0;
            while (slot < _issueBuffer.Count && dispatchedThisCycle.Count < IssueWidth)
            {
                var candidate = _issueBuffer[slot];

                if (candidate == null || candidate.IsBubble)
                {
                    _issueBuffer.RemoveAt(slot);
                    continue;
                }

                // NOP and HALT require no functional unit — handle before FU dispatch
                if (FunctionalUnitSet.GetRequiredUnit(candidate) == null)
                {
                    _issueBuffer.RemoveAt(slot);
                    if (candidate.Opcode == Opcode.HALT)
                    {
                        // Route HALT through MEM→WB for proper pipeline visualization
                        _completionList.Add(candidate);
                        // Flush the fetch side — nothing after HALT should execute
                        _stageIF = null; _stageDEC = null; _issueBuffer.Clear();
                        // Record EX at dispatch time so there's no empty cell between DEC and MEM.
                        // (AdvanceMEM already ran this cycle before completionList was populated.)
                        RecordSpaceTime(candidate, ClockCycle, "EX");
                        log?.AppendLine("[HALT] HALT emis → MEM→WB (flush fetch pipeline)");
                        break;
                    }
                    // NOP: consume the issue slot without effect
                    dispatchedThisCycle.Add(candidate);
                    continue;
                }

                if (candidate.Rs1.HasValue && candidate.Rs1.Value >= 0)
                    candidate.Op1Value = Registers.Read(candidate.Rs1.Value);
                if (candidate.Rs2.HasValue && candidate.Rs2.Value >= 0)
                    candidate.Op2Value = Registers.Read(candidate.Rs2.Value);

                // Co-issue RAW: can't read a register written by an instruction dispatched THIS cycle
                bool coIssueRaw = dispatchedThisCycle.Any(d =>
                    d.GetWriteRegister() >= 0 &&
                    candidate.GetReadRegisters().Contains(d.GetWriteRegister()));
                if (coIssueRaw)
                {
                    if (!anyStall)
                    {
                        TotalStalls++;
                        anyStall = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[CO-ISSUE RAW] '{candidate.ToShortString()}' depinde de o instrucțiune co-emisă → stall");
                    }
                    break;
                }

                candidate.IsForwarded = false;
                ForwardingInfo fwd = ForwardingInfo.None;
                if (ForwardingEnabled)
                    fwd = ApplyForwardingForDispatch(candidate, log);

                HazardInfo hazard = HazardInfo.None;
                if (HazardDetectionEnabled)
                    hazard = DetectHazard(candidate);

                if (hazard.HasHazard && !candidate.IsForwarded)
                {
                    if (!anyStall)
                    {
                        TotalStalls++;
                        anyStall = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[STALL] RAW {hazard.ConflictRegister}: '{candidate.ToShortString()}' → '{hazard.Producer?.ToShortString()}'");
                        lastHazard = hazard;
                    }
                    break;
                }

                var reqUnit = FunctionalUnitSet.GetRequiredUnit(candidate);
                if (reqUnit.HasValue && !FunctionalUnits.HasAvailableUnit(reqUnit.Value))
                {
                    if (!anyStall)
                    {
                        TotalStalls++;
                        FuStructuralStallCycles++;
                        anyStall = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[STRUCTURAL] Toate unitățile {reqUnit.Value} ocupate → stall '{candidate.ToShortString()}'");
                    }
                    break;
                }

                if (candidate.GetWriteRegister() >= 0)
                    Registers.InvalidateRegister(candidate.GetWriteRegister());

                var unit = FunctionalUnits.TryDispatch(candidate);
                if (unit == null) break;

                _issueBuffer.RemoveAt(slot);
                _inFlightEX.Add(candidate);
                _exEntryCycle[candidate] = ClockCycle;
                dispatchedThisCycle.Add(candidate);
                if (fwd.IsActive) lastFwd = fwd;

                ExecuteEXInstruction(candidate, log);
                log.AppendLine($"[DISPATCH] '{candidate.ToShortString()}' → {unit.UnitType}-{unit.UnitIndex} (lat={unit.CyclesLeft})");

                // Branch: flush speculatively-fetched instructions
                if (candidate.Opcode == Opcode.JMP ||
                    candidate.Opcode == Opcode.BEQ || candidate.Opcode == Opcode.BNE ||
                    candidate.Opcode == Opcode.BGT || candidate.Opcode == Opcode.BLT)
                {
                    _stageIF = null;
                    _stageDEC = null;
                    _issueBuffer.Clear();
                    break;
                }
            }

            return (lastHazard, lastFwd);
        }

        // ── Stage handlers ────────────────────────────────────────────────────

        private void ExecuteWB(StringBuilder log)
        {
            var instr = _stageWB;
            if (instr == null || instr.Opcode == Opcode.NOP) return;
            log.AppendLine($"[WB] {instr.ToShortString()}");
            switch (instr.Class)
            {
                case InstructionClass.ALU:
                case InstructionClass.ALUI:
                case InstructionClass.LOAD:
                    if (instr.Rd.HasValue && instr.Rd.Value >= 0 && instr.ResultValue.HasValue)
                    {
                        Registers.Write(instr.Rd.Value, instr.ResultValue.Value);
                        log.AppendLine($"  R{instr.Rd.Value} ← {instr.ResultValue.Value} (0x{instr.ResultValue.Value:X8})");
                    }
                    break;
            }
        }

        private void ExecuteMEM(StringBuilder log)
        {
            var instr = _stageMEM;
            if (instr == null || instr.Opcode == Opcode.NOP) return;
            log.AppendLine($"[MEM] {instr.ToShortString()}");

            switch (instr.Class)
            {
                case InstructionClass.LOAD:
                    if (instr.Op1Value.HasValue)
                    {
                        uint memAddr = (uint)instr.Op1Value.Value;
                        if (instr == _cacheStallInstruction && !_cacheStallIsICache)
                        {
                            _cacheStallInstruction = null;
                            log.AppendLine($"  DCache miss stall resolvat — LOAD @ MEM[0x{memAddr:X4}]");
                            break;
                        }
                        bool skipVm = (instr == _vmStallInstruction);
                        if (skipVm) _vmStallInstruction = null;
                        if (!skipVm)
                        {
                            var vmr = VirtualMemory.Access(memAddr, MemoryAccessType.DataRead, ClockCycle);
                            int tlbStall = ComputeTlbStall(vmr);
                            if (tlbStall > 0)
                            {
                                _vmStallsRemaining = tlbStall; _vmStallIsInst = false;
                                _vmStallInstruction = instr; _vmMissPending = true;
                                log.AppendLine($"  [TLB MISS] LOAD @ 0x{memAddr:X4} → +{tlbStall} cicli"); break;
                            }
                            log.AppendLine($"  TLB {(vmr.TlbHit ? "HIT" : "MISS→resolve")} LOAD VA=0x{memAddr:X4} PA=0x{vmr.PhysicalAddress:X4}");
                        }
                        bool dHit = DataCache.Read(memAddr);
                        instr.ResultValue = DataMemory.Read(memAddr);
                        log.AppendLine($"  DCache {(dHit ? "HIT " : "MISS")} READ @ MEM[0x{memAddr:X4}] -> {instr.ResultValue.Value}");
                        if (!dHit && DCacheMissPenalty > 0)
                        {
                            _cacheStallsRemaining = DCacheMissPenalty - 1;
                            _cacheStallIsICache = false; _cacheStallInstruction = instr; _dcacheMissPending = true;
                        }
                    }
                    break;

                case InstructionClass.STORE:
                    if (instr.Op1Value.HasValue && instr.Op2Value.HasValue)
                    {
                        uint memAddr = (uint)instr.Op1Value.Value;
                        if (instr == _cacheStallInstruction && !_cacheStallIsICache)
                        {
                            _cacheStallInstruction = null;
                            log.AppendLine($"  DCache miss stall resolvat — STORE @ MEM[0x{memAddr:X4}]"); break;
                        }
                        bool skipVm = (instr == _vmStallInstruction);
                        if (skipVm) _vmStallInstruction = null;
                        if (!skipVm)
                        {
                            var vmr = VirtualMemory.Access(memAddr, MemoryAccessType.DataWrite, ClockCycle);
                            int tlbStall = ComputeTlbStall(vmr);
                            if (tlbStall > 0)
                            {
                                _vmStallsRemaining = tlbStall; _vmStallIsInst = false;
                                _vmStallInstruction = instr; _vmMissPending = true;
                                log.AppendLine($"  [TLB MISS] STORE @ 0x{memAddr:X4} → +{tlbStall} cicli"); break;
                            }
                            log.AppendLine($"  TLB {(vmr.TlbHit ? "HIT" : "MISS→resolve")} STORE VA=0x{memAddr:X4} PA=0x{vmr.PhysicalAddress:X4}");
                        }
                        bool dHit = DataCache.Write(memAddr);
                        DataMemory.Write(memAddr, instr.Op2Value.Value);
                        log.AppendLine($"  DCache {(dHit ? "HIT " : "MISS")} WRITE @ MEM[0x{memAddr:X4}] ← {instr.Op2Value.Value}");
                        if (!dHit && DCacheMissPenalty > 0)
                        {
                            _cacheStallsRemaining = DCacheMissPenalty - 1;
                            _cacheStallIsICache = false; _cacheStallInstruction = instr; _dcacheMissPending = true;
                        }
                    }
                    break;
            }
        }

        private void ExecuteEXInstruction(RISCInstruction instr, StringBuilder log)
        {
            if (instr == null || instr.Opcode == Opcode.NOP) return;
            log.AppendLine($"[EX]  {instr.ToShortString()}");

            int op1 = instr.Op1Value ?? (instr.Rs1.HasValue && instr.Rs1.Value >= 0 ? Registers.Read(instr.Rs1.Value) : 0);
            int op2 = instr.Op2Value ?? (instr.Rs2.HasValue && instr.Rs2.Value >= 0 ? Registers.Read(instr.Rs2.Value) : (instr.Imm ?? 0));

            switch (instr.Opcode)
            {
                case Opcode.ADD:  instr.ResultValue = op1 + op2; break;
                case Opcode.SUB:  instr.ResultValue = op1 - op2; break;
                case Opcode.MUL:  instr.ResultValue = op1 * op2; break;
                case Opcode.AND:  instr.ResultValue = op1 & op2; break;
                case Opcode.OR:   instr.ResultValue = op1 | op2; break;
                case Opcode.XOR:  instr.ResultValue = op1 ^ op2; break;
                case Opcode.SHL:  instr.ResultValue = op1 << (op2 & 31); break;
                case Opcode.SHR:  instr.ResultValue = (int)((uint)op1 >> (op2 & 31)); break;
                case Opcode.ADDI: instr.ResultValue = op1 + (instr.Imm ?? 0); break;
                case Opcode.SUBI: instr.ResultValue = op1 - (instr.Imm ?? 0); break;
                case Opcode.ANDI: instr.ResultValue = op1 & (instr.Imm ?? 0); break;
                case Opcode.ORI:  instr.ResultValue = op1 | (instr.Imm ?? 0); break;
                case Opcode.LD:   instr.Op1Value = op1; break;
                case Opcode.LDI:  instr.ResultValue = instr.Imm ?? 0; break;
                case Opcode.ST:
                    instr.Op1Value = op1;
                    instr.Op2Value = instr.Rs2.HasValue ? Registers.Read(instr.Rs2.Value) : 0;
                    break;
                case Opcode.JMP:
                    PC = (uint)(instr.Imm ?? 0);
                    _programCounter = FindInstructionIndex(PC);
                    log.AppendLine($"  JMP → PC = 0x{PC:X4}");
                    break;
                case Opcode.BEQ: case Opcode.BNE: case Opcode.BGT: case Opcode.BLT:
                    if (EvaluateBranch(instr.Opcode, op1, op2))
                    {
                        uint target = (uint)((int)instr.Address + (instr.Imm ?? 0));
                        PC = target;
                        _programCounter = FindInstructionIndex(PC);
                        log.AppendLine($"  BRANCH taken → PC = 0x{PC:X4} (idx {_programCounter})");
                    }
                    else log.AppendLine($"  BRANCH not taken");
                    break;
            }
            if (instr.ResultValue.HasValue)
                log.AppendLine($"  Result = {instr.ResultValue.Value} (0x{instr.ResultValue.Value:X8})");
        }

        // ── Forwarding ────────────────────────────────────────────────────────

        private ForwardingInfo ApplyForwardingForDispatch(RISCInstruction candidate, StringBuilder log)
        {
            ForwardingInfo first = null;
            foreach (int reg in candidate.GetReadRegisters())
            {
                if (reg < 0) continue;

                // MEM stage: highest priority (instruction just completed EX last cycle)
                if (_stageMEM != null && _stageMEM.GetWriteRegister() == reg
                    && _stageMEM.Class != InstructionClass.LOAD // LOAD result not yet available
                    && _stageMEM.ResultValue.HasValue)
                {
                    ApplyForwardedValue(candidate, reg, _stageMEM.ResultValue.Value);
                    candidate.IsForwarded = true;
                    var f = new ForwardingInfo { IsActive = true, Path = "MEM→EX", Register = $"R{reg}", ForwardedValue = _stageMEM.ResultValue.Value };
                    log.AppendLine($"[FWD MEM→EX] R{reg}={_stageMEM.ResultValue.Value} de la '{_stageMEM.ToShortString()}'");
                    if (first == null) first = f;
                    continue;
                }

                // Completion list: instructions that just finished EX this cycle
                var fromCL = _completionList.FirstOrDefault(c => c.GetWriteRegister() == reg && c.ResultValue.HasValue);
                if (fromCL != null)
                {
                    ApplyForwardedValue(candidate, reg, fromCL.ResultValue.Value);
                    candidate.IsForwarded = true;
                    var f = new ForwardingInfo { IsActive = true, Path = "EX→EX", Register = $"R{reg}", ForwardedValue = fromCL.ResultValue.Value };
                    log.AppendLine($"[FWD EX→EX] R{reg}={fromCL.ResultValue.Value} de la '{fromCL.ToShortString()}'");
                    if (first == null) first = f;
                }
            }
            return first ?? ForwardingInfo.None;
        }

        private void ApplyForwardedValue(RISCInstruction consumer, int regIdx, int value)
        {
            if (consumer.Rs1 == regIdx) consumer.Op1Value = value;
            if (consumer.Rs2 == regIdx) consumer.Op2Value = value;
        }

        // ── Hazard detection ──────────────────────────────────────────────────

        private HazardInfo DetectHazard(RISCInstruction consumer)
        {
            if (consumer == null) return HazardInfo.None;
            foreach (int reg in consumer.GetReadRegisters())
            {
                if (reg < 0) continue;
                if (!Registers.IsValid(reg))
                {
                    var producer = FindProducer(reg);
                    return new HazardInfo
                    {
                        HasHazard = true, HazardType = "RAW",
                        ConflictRegister = $"R{reg}",
                        Producer = producer, Consumer = consumer,
                        StallsRequired = 1,
                        Description = $"RAW R{reg}: '{consumer.ToShortString()}' ← '{producer?.ToShortString()}'"
                    };
                }
            }
            return HazardInfo.None;
        }

        private RISCInstruction FindProducer(int reg)
        {
            // Search in-flight EX, completion list, MEM, WB
            var fromFlight = _inFlightEX.FirstOrDefault(i => i.GetWriteRegister() == reg);
            if (fromFlight != null) return fromFlight;
            var fromCL = _completionList.FirstOrDefault(i => i.GetWriteRegister() == reg);
            if (fromCL != null) return fromCL;
            if (_stageMEM?.GetWriteRegister() == reg) return _stageMEM;
            if (_stageWB?.GetWriteRegister() == reg) return _stageWB;
            return null;
        }

        // ── Fetch ─────────────────────────────────────────────────────────────

        private RISCInstruction FetchNextInstruction(StringBuilder log)
        {
            if (_program == null || _programCounter < 0 || _programCounter >= _program.Count)
            {
                IsRunning = false;
                return null;
            }
            var instr = _program[_programCounter];
            if (instr == null) { _programCounter++; return null; }

            _programCounter++;
            PC = instr.Address;
            instr.EnterCycle = ClockCycle;
            instr.CurrentStage = PipelineStage.IF;
            RecordSpaceTime(instr, ClockCycle, "IF"); // record at fetch time for multi-width

            var vmrIF = VirtualMemory.Access(instr.Address, MemoryAccessType.Instruction, ClockCycle);
            int ifTlbStall = ComputeTlbStall(vmrIF);
            if (ifTlbStall > 0)
            {
                _vmStallsRemaining = ifTlbStall; _vmStallIsInst = true; _vmStallInstruction = instr;
                log.AppendLine($"[IF]  TLB MISS → +{ifTlbStall} cicli | {instr.ToShortString()}");
            }
            else
                log.AppendLine($"[IF]  TLB {(vmrIF.TlbHit ? "HIT " : "MISS→resolve")} VA=0x{instr.Address:X4} | {instr.ToShortString()}");

            bool iHit = InstructionCache.Read(instr.Address);
            if (!iHit && ICacheMissPenalty > 0)
            {
                _cacheStallsRemaining = ICacheMissPenalty; _cacheStallIsICache = true; _cacheStallInstruction = instr;
                log.AppendLine($"[IF]  ICache MISS → +{ICacheMissPenalty} cicli | {instr.ToShortString()}");
            }
            else
                log.AppendLine($"[IF]  ICache {(iHit ? "HIT " : "MISS")} | {instr.ToShortString()} @ 0x{instr.Address:X4}");

            return instr;
        }

        private int ComputeTlbStall(MemoryAccessResult vmr)
        {
            if (!VmStallsEnabled || vmr == null || vmr.TlbHit) return 0;
            int stall = VirtualMemory.Mmu.Latencies.CacheCycles;
            if (!vmr.PteInCache) stall += VirtualMemory.Mmu.Latencies.MainMemoryCycles;
            return stall;
        }

        private bool EvaluateBranch(Opcode op, int a, int b)
        {
            switch (op)
            {
                case Opcode.BEQ: return a == b;
                case Opcode.BNE: return a != b;
                case Opcode.BGT: return a > b;
                case Opcode.BLT: return a < b;
                default: return false;
            }
        }

        private int FindInstructionIndex(uint address)
            => _program.FindIndex(i => i.Address == address);

        // ── Space-time ────────────────────────────────────────────────────────

        private void RecordSpaceTime(RISCInstruction instr, int cycle, string stageLabel)
        {
            if (instr == null || instr.IsBubble) return;
            if (instr.ProgramIndex >= 0 && instr.ProgramIndex < SpaceTimeTable.Count)
                SpaceTimeTable[instr.ProgramIndex].SetStage(cycle, stageLabel);
        }

        // ── Snapshot ──────────────────────────────────────────────────────────

        private PipelineState BuildSnapshot(string log, HazardInfo hazard, ForwardingInfo fwd)
        {
            var exDisplay = _inFlightEX.Count > 0 ? _inFlightEX[0] : null;
            return new PipelineState
            {
                ClockCycle = ClockCycle,
                PC = PC,
                TotalStalls = TotalStalls,
                StageIF  = _stageIF,
                StageDEC = _stageDEC,
                StageEX  = exDisplay,
                StageMEM = _stageMEM,
                StageWB  = _stageWB,
                ActiveHazard    = hazard ?? HazardInfo.None,
                ActiveForwarding = fwd ?? ForwardingInfo.None,
                RegisterSnapshot = Registers.GetSnapshot(),
                LogMessage = log,
                LogText    = log,
                DetailIF  = _stageIF  != null ? $"0x{_stageIF.Address:X4}: {_stageIF.ToShortString()}" : "",
                DetailDEC = _stageDEC != null ? $"Op1={_stageDEC.Op1Value?.ToString() ?? "?"}, Op2={_stageDEC.Op2Value?.ToString() ?? "?"}" : "",
                DetailEX  = exDisplay != null ? (exDisplay.ResultValue.HasValue ? $"ALU→{exDisplay.ResultValue.Value}" : "ALU...") : "",
                DetailMEM = _stageMEM != null ? (_stageMEM.Class == InstructionClass.LOAD  ? $"MEM[{_stageMEM.Op1Value}]→R{_stageMEM.Rd}" :
                                                  _stageMEM.Class == InstructionClass.STORE ? $"MEM[{_stageMEM.Op1Value}]←{_stageMEM.Op2Value}" : "") : "",
                DetailWB  = _stageWB  != null ? (_stageWB.GetWriteRegister() >= 0 && _stageWB.ResultValue.HasValue ? $"R{_stageWB.Rd}←{_stageWB.ResultValue.Value}" : "") : ""
            };
        }
    }
}
