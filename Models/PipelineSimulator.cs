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

        private RISCInstruction _stageIF;
        private RISCInstruction _stageDEC;
        private RISCInstruction _stageEX;
        private RISCInstruction _stageMEM;
        private RISCInstruction _stageWB;

        public RegisterFile Registers { get; } = new RegisterFile();
        public Memory DataMemory { get; } = new Memory();

        public int ClockCycle { get; private set; } = 0;
        public int TotalStalls { get; private set; } = 0;
        public bool IsRunning { get; private set; } = false;
        public bool IsHalted { get; private set; } = false;

        public bool ForwardingEnabled { get; set; } = true;
        public bool HazardDetectionEnabled { get; set; } = true;

        public List<PipelineState> History { get; } = new List<PipelineState>();
        public List<SpaceTimeEntry> SpaceTimeTable { get; } = new List<SpaceTimeEntry>();

        public event Action<PipelineState> CycleCompleted;

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
            _stageIF = _stageDEC = _stageEX = _stageMEM = _stageWB = null;
            Registers.Reset();
            DataMemory.Reset();
            History.Clear();
            foreach (var entry in SpaceTimeTable) entry.CycleStages.Clear();
        }

        public PipelineState Step()
        {
            if (IsHalted) return BuildSnapshot("Simulatorul este oprit (HALT).", null, null);

            ClockCycle++;
            var log = new StringBuilder();
            log.AppendLine($"=== Ciclu {ClockCycle} ===");

            ExecuteWB(log);
            ExecuteMEM(log);

            ForwardingInfo fwdInfo = ForwardingInfo.None;
            if (ForwardingEnabled) fwdInfo = CheckAndApplyForwarding(log);

            ExecuteEX(log);

            HazardInfo hazardInfo = HazardInfo.None;
            bool stallInserted = false;
            if (HazardDetectionEnabled && _stageDEC != null)
            {
                hazardInfo = DetectHazard(_stageDEC);
                if (hazardInfo.HasHazard && !fwdInfo.IsActive)
                {
                    stallInserted = true;
                    TotalStalls++;
                    log.AppendLine($"[STALL] Hazard {hazardInfo.HazardType} pe {hazardInfo.ConflictRegister}: '{hazardInfo.Consumer?.ToShortString()}' asteapta '{hazardInfo.Producer?.ToShortString()}'");
                    RecordSpaceTime(_stageDEC, ClockCycle, "stall");
                    RecordSpaceTime(_stageIF, ClockCycle, "stall");
                }
            }

            if (!stallInserted)
            {
                _stageWB = _stageMEM;
                _stageMEM = _stageEX;
                _stageEX = _stageDEC;
                _stageDEC = _stageIF;
                _stageIF = FetchNextInstruction(log);
            }
            else
            {
                _stageWB = _stageMEM;
                _stageMEM = _stageEX;
                _stageEX = CreateNOPBubble();
            }

            RecordSpaceTimeAll(ClockCycle);

            if (_stageWB?.Opcode == Opcode.HALT || _stageMEM?.Opcode == Opcode.HALT || _stageEX?.Opcode == Opcode.HALT)
            {
                IsHalted = true;
                log.AppendLine("[HALT] Instructiunea HALT a ajuns in pipeline — simulatorul se opreste.");
            }

            if (_stageIF == null && _stageDEC == null && _stageEX == null && _stageMEM == null && _stageWB == null)
            {
                IsRunning = false;
                IsHalted = true;
                log.AppendLine("[END] Pipeline gol — program terminat.");
            }

            var state = BuildSnapshot(log.ToString(), hazardInfo, fwdInfo);
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
                        log.AppendLine($"  R{instr.Rd.Value} <- {instr.ResultValue.Value} (0x{instr.ResultValue.Value:X8})");
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
                        instr.ResultValue = DataMemory.Read(memAddr);
                        log.AppendLine($"  MEM[0x{memAddr:X4}] -> {instr.ResultValue.Value}");
                    }
                    break;

                case InstructionClass.STORE:
                    if (instr.Op1Value.HasValue && instr.Op2Value.HasValue)
                    {
                        uint memAddr = (uint)instr.Op1Value.Value;
                        DataMemory.Write(memAddr, instr.Op2Value.Value);
                        log.AppendLine($"  MEM[0x{memAddr:X4}] <- {instr.Op2Value.Value}");
                    }
                    break;
            }
        }

        private void ExecuteEX(StringBuilder log)
        {
            var instr = _stageEX;
            if (instr == null || instr.Opcode == Opcode.NOP) return;

            log.AppendLine($"[EX]  {instr.ToShortString()}");

            int op1 = instr.Op1Value ?? (instr.Rs1.HasValue && instr.Rs1.Value >= 0 ? Registers.Read(instr.Rs1.Value) : 0);
            int op2 = instr.Op2Value ?? (instr.Rs2.HasValue && instr.Rs2.Value >= 0 ? Registers.Read(instr.Rs2.Value) : (instr.Imm ?? 0));

            switch (instr.Opcode)
            {
                case Opcode.ADD: instr.ResultValue = op1 + op2; break;
                case Opcode.SUB: instr.ResultValue = op1 - op2; break;
                case Opcode.MUL: instr.ResultValue = op1 * op2; break;
                case Opcode.AND: instr.ResultValue = op1 & op2; break;
                case Opcode.OR: instr.ResultValue = op1 | op2; break;
                case Opcode.XOR: instr.ResultValue = op1 ^ op2; break;
                case Opcode.SHL: instr.ResultValue = op1 << (op2 & 31); break;
                case Opcode.SHR: instr.ResultValue = (int)((uint)op1 >> (op2 & 31)); break;

                case Opcode.ADDI: instr.ResultValue = op1 + (instr.Imm ?? 0); break;
                case Opcode.SUBI: instr.ResultValue = op1 - (instr.Imm ?? 0); break;
                case Opcode.ANDI: instr.ResultValue = op1 & (instr.Imm ?? 0); break;
                case Opcode.ORI: instr.ResultValue = op1 | (instr.Imm ?? 0); break;

                case Opcode.LD: instr.Op1Value = op1; break;
                case Opcode.LDI: instr.ResultValue = instr.Imm ?? 0; break;

                case Opcode.ST:
                    instr.Op1Value = op1;
                    instr.Op2Value = instr.Rs2.HasValue ? Registers.Read(instr.Rs2.Value) : 0;
                    break;

                case Opcode.JMP:
                    PC = (uint)(instr.Imm ?? 0);
                    _programCounter = FindInstructionIndex(PC);
                    log.AppendLine($"  JMP -> PC = 0x{PC:X4}");
                    _stageIF = null;
                    _stageDEC = null;
                    break;

                case Opcode.BEQ:
                case Opcode.BNE:
                case Opcode.BGT:
                case Opcode.BLT:
                    if (EvaluateBranch(instr.Opcode, op1, op2))
                        {
                        uint target = (uint)((int)PC + (instr.Imm ?? 0));
                        PC = target;
                        _programCounter = FindInstructionIndex(PC);
                        log.AppendLine($"  BRANCH taken -> PC = 0x{PC:X4}");
                        _stageIF = null;
                        _stageDEC = null;
                    }
                    else log.AppendLine($"  BRANCH not taken");
                    break;
            }

            if (instr.ResultValue.HasValue) log.AppendLine($"  Result = {instr.ResultValue.Value} (0x{instr.ResultValue.Value:X8})");
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

        private void PrepareDEC(RISCInstruction instr)
        {
            if (instr == null || instr.Opcode == Opcode.NOP) return;

            if (instr.GetWriteRegister() >= 0) Registers.InvalidateRegister(instr.GetWriteRegister());

            if (instr.Rs1.HasValue && instr.Rs1.Value >= 0) instr.Op1Value = Registers.Read(instr.Rs1.Value);
            if (instr.Rs2.HasValue && instr.Rs2.Value >= 0) instr.Op2Value = Registers.Read(instr.Rs2.Value);
        }

        private HazardInfo DetectHazard(RISCInstruction consumer)
        {
            if (consumer == null) return HazardInfo.None;

            foreach (int regIdx in consumer.GetReadRegisters())
            {
                if (regIdx < 0) continue;
                if (!Registers.IsValid(regIdx))
                {
                    var producer = FindProducer(regIdx);
                    return new HazardInfo
                    {
                        HasHazard = true,
                        HazardType = "RAW",
                        ConflictRegister = $"R{regIdx}",
                        Producer = producer,
                        Consumer = consumer,
                        StallsRequired = 1,
                        Description = $"RAW pe R{regIdx}: '{consumer.ToShortString()}' citeste inainte ca '{producer?.ToShortString()}' sa scrie."
                    };
                }
            }
            return HazardInfo.None;
        }

        private RISCInstruction FindProducer(int regIdx)
        {
            if (_stageEX?.GetWriteRegister() == regIdx) return _stageEX;
            if (_stageMEM?.GetWriteRegister() == regIdx) return _stageMEM;
            if (_stageWB?.GetWriteRegister() == regIdx) return _stageWB;
            return null;
        }

        private ForwardingInfo CheckAndApplyForwarding(StringBuilder log)
        {
            if (_stageDEC == null) return ForwardingInfo.None;

            var readRegs = _stageDEC.GetReadRegisters();

            if (_stageEX != null && _stageEX.GetWriteRegister() >= 0 && _stageEX.ResultValue.HasValue && _stageEX.Class != InstructionClass.LOAD)
            {
                int prodReg = _stageEX.GetWriteRegister();
                if (readRegs.Contains(prodReg))
                {
                    ApplyForwardedValue(_stageDEC, prodReg, _stageEX.ResultValue.Value);
                    var fwd = new ForwardingInfo { IsActive = true, Path = "EX?EX", Register = $"R{prodReg}", ForwardedValue = _stageEX.ResultValue.Value, Description = $"[FWD EX?EX] R{prodReg} = {_stageEX.ResultValue.Value} de la '{_stageEX.ToShortString()}' catre '{_stageDEC.ToShortString()}'" };
                    log.AppendLine(fwd.Description);
                    _stageDEC.IsForwarded = true;
                    _stageDEC.ForwardSource = fwd.Path;
                    return fwd;
                }
            }

            if (_stageMEM != null && _stageMEM.GetWriteRegister() >= 0 && _stageMEM.ResultValue.HasValue)
            {
                int prodReg = _stageMEM.GetWriteRegister();
                if (readRegs.Contains(prodReg))
                {
                    ApplyForwardedValue(_stageDEC, prodReg, _stageMEM.ResultValue.Value);
                    var fwd = new ForwardingInfo { IsActive = true, Path = "MEM?EX", Register = $"R{prodReg}", ForwardedValue = _stageMEM.ResultValue.Value, Description = $"[FWD MEM?EX] R{prodReg} = {_stageMEM.ResultValue.Value} de la '{_stageMEM.ToShortString()}' catre '{_stageDEC.ToShortString()}'" };
                    log.AppendLine(fwd.Description);
                    _stageDEC.IsForwarded = true;
                    _stageDEC.ForwardSource = fwd.Path;
                    return fwd;
                }
            }

            return ForwardingInfo.None;
        }

        private void ApplyForwardedValue(RISCInstruction consumer, int regIdx, int value)
        {
            if (consumer.Rs1 == regIdx) consumer.Op1Value = value;
            if (consumer.Rs2 == regIdx) consumer.Op2Value = value;
        }

        private RISCInstruction FetchNextInstruction(StringBuilder log)
        {
            if (_programCounter >= _program.Count) return null;
            var instr = _program[_programCounter];
            _programCounter++;
            PC = instr.Address;
            instr.EnterCycle = ClockCycle;
            instr.CurrentStage = PipelineStage.IF;
            log.AppendLine($"[IF]  Fetch: {instr.ToShortString()} @ 0x{instr.Address:X4}");
            return instr;
        }

        private RISCInstruction CreateNOPBubble()
        {
            return new RISCInstruction { Opcode = Opcode.NOP, Class = InstructionClass.NOP, RawText = "NOP (bubble)", CurrentStage = PipelineStage.EX };
        }

        private void RecordSpaceTime(RISCInstruction instr, int cycle, string stageLabel)
        {
            if (instr == null) return;
            var entry = SpaceTimeTable.FirstOrDefault(e => e.InstructionLabel == instr.ToShortString());
            entry?.SetStage(cycle, stageLabel);
        }

        private void RecordSpaceTimeAll(int cycle)
        {
            RecordSpaceTime(_stageIF, cycle, "IF");
            RecordSpaceTime(_stageDEC, cycle, "DEC");
            RecordSpaceTime(_stageEX, cycle, "EX");
            RecordSpaceTime(_stageMEM, cycle, "MEM");
            RecordSpaceTime(_stageWB, cycle, "WB");
        }

        private int FindInstructionIndex(uint address) => _program.FindIndex(i => i.Address == address);

        private PipelineState BuildSnapshot(string log, HazardInfo hazard, ForwardingInfo fwd)
        {
            return new PipelineState
            {
                ClockCycle = ClockCycle,
                PC = PC,
                TotalStalls = TotalStalls,
                StageIF = _stageIF,
                StageDEC = _stageDEC,
                StageEX = _stageEX,
                StageMEM = _stageMEM,
                StageWB = _stageWB,
                ActiveHazard = hazard ?? HazardInfo.None,
                ActiveForwarding = fwd ?? ForwardingInfo.None,
                RegisterSnapshot = Registers.GetSnapshot(),
                LogMessage = log,
                DetailIF = _stageIF != null ? $"0x{_stageIF.Address:X4}: {_stageIF.ToShortString()}" : "",
                DetailDEC = _stageDEC != null ? $"Op1={_stageDEC.Op1Value?.ToString() ?? "?"}, Op2={_stageDEC.Op2Value?.ToString() ?? "?"}" : "",
                DetailEX = _stageEX != null ? (_stageEX.ResultValue.HasValue ? $"ALU?{_stageEX.ResultValue.Value}" : "ALU...") : "",
                DetailMEM = _stageMEM != null ? (_stageMEM.Class == InstructionClass.LOAD ? $"MEM[{_stageMEM.Op1Value}]?R{_stageMEM.Rd}" : _stageMEM.Class == InstructionClass.STORE ? $"MEM[{_stageMEM.Op1Value}]?{_stageMEM.Op2Value}" : "") : "",
                DetailWB = _stageWB != null ? (_stageWB.GetWriteRegister() >= 0 && _stageWB.ResultValue.HasValue ? $"R{_stageWB.Rd}?{_stageWB.ResultValue.Value}" : "") : ""
            };
        }
    }
}