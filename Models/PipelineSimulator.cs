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
            History.Clear();
            foreach (var entry in SpaceTimeTable) entry.CycleStages.Clear();
        }

        public PipelineState Step()
        {
            if (IsHalted) return BuildSnapshot("Simulatorul este oprit (HALT).", null, null);

            ClockCycle++;
            var log = new StringBuilder();
            log.AppendLine($"=== Ciclu {ClockCycle} ===");

            // ── VM/TLB stall handler ──────────────────────────────────────────
            if (_vmStallsRemaining > 0)
            {
                _vmStallsRemaining--;
                if (_vmStallIsInst) VmInstStallCycles++; else VmDataStallCycles++;
                TotalStalls++;
                string vmWhich = _vmStallIsInst ? "IF/TLB" : "MEM/TLB";
                log.AppendLine($"[TLB STALL] {vmWhich} @ 0x{_vmStallInstruction?.Address:X4} ({_vmStallsRemaining} cicli rămași)");
                if (_vmStallInstruction != null) RecordSpaceTime(_vmStallInstruction, ClockCycle, "vm");
                foreach (var inf in _inFlightEX) RecordSpaceTime(inf, ClockCycle, "ex_multi");
                var vmS = BuildSnapshot(log.ToString(), HazardInfo.None, ForwardingInfo.None);
                History.Add(vmS); CycleCompleted?.Invoke(vmS);
                return vmS;
            }

            // ── Cache stall handler ───────────────────────────────────────────
            if (_cacheStallsRemaining > 0)
            {
                _cacheStallsRemaining--;
                if (_cacheStallIsICache) ICacheStallCycles++; else DCacheStallCycles++;
                string which = _cacheStallIsICache ? "ICache" : "DCache";
                log.AppendLine($"[CACHE STALL] {which} MISS @ 0x{_cacheStallInstruction?.Address:X4} ({_cacheStallsRemaining} cicli rămași)");
                if (_cacheStallInstruction != null) RecordSpaceTime(_cacheStallInstruction, ClockCycle, "cache");
                foreach (var inf in _inFlightEX) RecordSpaceTime(inf, ClockCycle, "ex_multi");
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

            // ── 3. Advance MEM: WB ← MEM, MEM ← completion list ─────────────
            _stageWB = _stageMEM;
            _stageMEM = _completionList.Count > 0 ? _completionList[0] : null;
            if (_completionList.Count > 0) _completionList.RemoveAt(0);

            // ── 4. Tick FUs → collect completions ─────────────────────────────
            FunctionalUnits.RecordOccupancy(ClockCycle);
            var justCompleted = FunctionalUnits.TickUnits();
            foreach (var c in justCompleted)
            {
                _inFlightEX.Remove(c);
                _exEntryCycle.Remove(c);
                _completionList.Add(c);
                log.AppendLine($"[EX DONE] '{c.ToShortString()}' completes EX → MEM queue");
            }

            // Count extra EX cycles (instructions still in flight past their first cycle)
            foreach (var inf in _inFlightEX)
                FuMultiCycleExtraCycles++;

            // ── 5. Dispatch up to IssueWidth from issue buffer ─────────────────
            HazardInfo lastHazard = HazardInfo.None;
            ForwardingInfo lastFwd = ForwardingInfo.None;
            bool anyStallThisCycle = false;
            var dispatchedThisCycle = new List<RISCInstruction>();

            int slot = 0;
            while (slot < _issueBuffer.Count && dispatchedThisCycle.Count < IssueWidth)
            {
                var candidate = _issueBuffer[slot];

                // Bubble: pass through
                if (candidate == null || candidate.IsBubble)
                {
                    _issueBuffer.RemoveAt(slot);
                    continue;
                }

                // Read operands fresh
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
                    // Stop: in-order issue — can't skip
                    if (!anyStallThisCycle)
                    {
                        TotalStalls++;
                        anyStallThisCycle = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[CO-ISSUE RAW] '{candidate.ToShortString()}' depinde de o instrucțiune co-emisă → stall");
                    }
                    break;
                }

                // Apply forwarding from completion list and _stageMEM
                candidate.IsForwarded = false;
                ForwardingInfo fwd = ForwardingInfo.None;
                if (ForwardingEnabled)
                    fwd = ApplyForwardingForDispatch(candidate, log);

                // Detect RAW hazard
                HazardInfo hazard = HazardInfo.None;
                if (HazardDetectionEnabled)
                    hazard = DetectHazard(candidate);

                if (hazard.HasHazard && !candidate.IsForwarded)
                {
                    if (!anyStallThisCycle)
                    {
                        TotalStalls++;
                        anyStallThisCycle = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[STALL] RAW {hazard.ConflictRegister}: '{candidate.ToShortString()}' → '{hazard.Producer?.ToShortString()}'");
                        lastHazard = hazard;
                    }
                    break; // in-order: stop dispatching
                }

                // Structural hazard: FU availability
                var reqUnit = FunctionalUnitSet.GetRequiredUnit(candidate);
                if (reqUnit.HasValue && !FunctionalUnits.HasAvailableUnit(reqUnit.Value))
                {
                    if (!anyStallThisCycle)
                    {
                        TotalStalls++;
                        FuStructuralStallCycles++;
                        anyStallThisCycle = true;
                        RecordSpaceTime(candidate, ClockCycle, "stall");
                        RecordSpaceTime(_stageIF, ClockCycle, "stall");
                        log.AppendLine($"[STRUCTURAL] Toate unitățile {reqUnit.Value} ocupate → stall '{candidate.ToShortString()}'");
                    }
                    break; // in-order: stop
                }

                // ── Dispatch! ──────────────────────────────────────────────────
                if (candidate.GetWriteRegister() >= 0)
                    Registers.InvalidateRegister(candidate.GetWriteRegister());

                var unit = FunctionalUnits.TryDispatch(candidate);
                if (unit == null)
                {
                    // Shouldn't happen after HasAvailableUnit check, but defensive
                    break;
                }

                _issueBuffer.RemoveAt(slot);
                _inFlightEX.Add(candidate);
                _exEntryCycle[candidate] = ClockCycle;
                dispatchedThisCycle.Add(candidate);
                if (fwd.IsActive) lastFwd = fwd;

                // Compute result immediately (simplified model)
                ExecuteEXInstruction(candidate, log);

                int lat = unit.CyclesLeft;
                log.AppendLine($"[DISPATCH] '{candidate.ToShortString()}' → {unit.UnitType}-{unit.UnitIndex} (lat={lat})");

                // Single-cycle: tick immediately so unit is freed next cycle
                // (TickUnits at step 4 already ran; newly dispatched units will be ticked next cycle)

                // After branch dispatch: flush pipeline
                if (candidate.Opcode == Opcode.JMP ||
                    candidate.Opcode == Opcode.BEQ || candidate.Opcode == Opcode.BNE ||
                    candidate.Opcode == Opcode.BGT || candidate.Opcode == Opcode.BLT)
                {
                    // Flush IF, DEC, and remaining issue buffer (instructions fetched speculatively)
                    _stageIF = null;
                    _stageDEC = null;
                    _issueBuffer.Clear();
                    break; // stop dispatching after branch
                }

                // Don't increment slot — we removed the element
            }

            // ── 6. Move _stageDEC → issue buffer, advance IF/DEC ──────────────
            if (_stageDEC != null && !_stageDEC.IsBubble)
                _issueBuffer.Add(_stageDEC);

            _stageDEC = _stageIF;
            _stageIF = FetchNextInstruction(log);

            // ── 7. Record space-time ───────────────────────────────────────────
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
