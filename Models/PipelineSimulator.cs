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

            // CRITICAL: ExecuteEX PRIMUL - calculeaza ResultValue pentru ca forwarding-ul sa-l poata folosi!
            ExecuteEX(log);

            // Citește operanzii din RegisterFile (fără invalidare încă!)
            if (_stageDEC != null)
            {
                if (_stageDEC.Rs1.HasValue && _stageDEC.Rs1.Value >= 0) _stageDEC.Op1Value = Registers.Read(_stageDEC.Rs1.Value);
                if (_stageDEC.Rs2.HasValue && _stageDEC.Rs2.Value >= 0) _stageDEC.Op2Value = Registers.Read(_stageDEC.Rs2.Value);
            }

            // Forwarding: suprascrie Op1Value/Op2Value cu valori forward-ate (EX/MEM au deja ResultValue)
            ForwardingInfo fwdInfo = ForwardingInfo.None;
            if (ForwardingEnabled && _stageDEC != null && (_stageEX != null || _stageMEM != null))
                fwdInfo = CheckAndApplyForwarding(log);

            // Hazard detection
            HazardInfo hazardInfo = HazardInfo.None;
            bool stallInserted = false;
            if (HazardDetectionEnabled && _stageDEC != null)
            {
                hazardInfo = DetectHazard(_stageDEC);
                
                // Stall DOAR daca hazard exista SI nu s-a aplicat forwarding
                if (hazardInfo.HasHazard && !fwdInfo.IsActive && !_stageDEC.IsForwarded)
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
                // INVALIDEAZĂ registrul de destinație DOAR când instrucțiunea avansează la EX!
                if (_stageDEC != null && _stageDEC.GetWriteRegister() >= 0)
                    Registers.InvalidateRegister(_stageDEC.GetWriteRegister());

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

            // HALT se opreste doar dupa ce termina WB
            if (_stageWB != null && _stageWB.Opcode == Opcode.HALT)
            {
                IsHalted = true;
                log.AppendLine("[HALT] Instructiunea HALT a terminat WB � simulatorul se opreste.");
            }

            if (_stageIF == null && _stageDEC == null && _stageEX == null && _stageMEM == null && _stageWB == null)
            {
                IsRunning = false;
                IsHalted = true;
                log.AppendLine("[END] Pipeline gol � program terminat.");
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
                        // Calculează offset pe baza adresei instrucțiunii BRANCH (instr.Address), nu pe baza PC curent
                        uint target = (uint)((int)instr.Address + (instr.Imm ?? 0));
                        PC = target;
                        _programCounter = FindInstructionIndex(PC);
                        
                        if (_programCounter >= 0)
                        {
                            log.AppendLine($"  BRANCH taken -> PC = 0x{PC:X4} (index {_programCounter}) = '{_program[_programCounter].ToShortString()}'");
                        }
                        else
                        {
                            log.AppendLine($"  BRANCH taken -> PC = 0x{PC:X4} (ERROR: instrucțiune nu gasita!)");
                            log.AppendLine($"  [DEBUG] Program addresses: {string.Join(", ", _program.Select(i => $"0x{i.Address:X4}:{i.ToShortString()}"))}");
                            log.AppendLine($"  [DEBUG] _programCounter = {_programCounter}, _program.Count = {_program.Count}");
                        }
                        
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

            // CRITICAL: Citeste valorile INAINTE de a invalida registrul destinatie!
            // Altfel ADDI R7, R7, 1 invalideaza R7 inainte de a citi valoarea pentru Rs1
            if (instr.Rs1.HasValue && instr.Rs1.Value >= 0) instr.Op1Value = Registers.Read(instr.Rs1.Value);
            if (instr.Rs2.HasValue && instr.Rs2.Value >= 0) instr.Op2Value = Registers.Read(instr.Rs2.Value);

            if (instr.GetWriteRegister() >= 0) Registers.InvalidateRegister(instr.GetWriteRegister());
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
            ForwardingInfo firstFwd = null;

            // Aplică forwarding pentru TOȚI registrii citiți (nu returna după primul!)
            foreach (int regIdx in readRegs)
            {
                if (regIdx < 0) continue;

                // EX->EX forwarding: prioritate mai mare (date mai proaspete)
                if (_stageEX != null && _stageEX.GetWriteRegister() == regIdx && _stageEX.Class != InstructionClass.LOAD)
                {
                    int fwdValue = _stageEX.ResultValue ?? 0;
                    ApplyForwardedValue(_stageDEC, regIdx, fwdValue);
                    var fwd = new ForwardingInfo { IsActive = true, Path = "EX?EX", Register = $"R{regIdx}", ForwardedValue = fwdValue, Description = $"[FWD EX?EX] R{regIdx} de la '{_stageEX.ToShortString()}' catre '{_stageDEC.ToShortString()}'" };
                    log.AppendLine(fwd.Description);
                    _stageDEC.IsForwarded = true;
                    _stageDEC.ForwardSource = fwd.Path;
                    if (firstFwd == null) firstFwd = fwd;
                    continue; // Nu verifica MEM pentru acest registru
                }

                // MEM->EX forwarding
                if (_stageMEM != null && _stageMEM.GetWriteRegister() == regIdx && _stageMEM.ResultValue.HasValue)
                {
                    ApplyForwardedValue(_stageDEC, regIdx, _stageMEM.ResultValue.Value);
                    var fwd = new ForwardingInfo { IsActive = true, Path = "MEM?EX", Register = $"R{regIdx}", ForwardedValue = _stageMEM.ResultValue.Value, Description = $"[FWD MEM?EX] R{regIdx} = {_stageMEM.ResultValue.Value} de la '{_stageMEM.ToShortString()}' catre '{_stageDEC.ToShortString()}'" };
                    log.AppendLine(fwd.Description);
                    _stageDEC.IsForwarded = true;
                    _stageDEC.ForwardSource = fwd.Path;
                    if (firstFwd == null) firstFwd = fwd;
                }
            }

            return firstFwd ?? ForwardingInfo.None;
        }

        private void ApplyForwardedValue(RISCInstruction consumer, int regIdx, int value)
        {
            if (consumer.Rs1 == regIdx) consumer.Op1Value = value;
            if (consumer.Rs2 == regIdx) consumer.Op2Value = value;
        }

        private RISCInstruction FetchNextInstruction(StringBuilder log)
        {
            if (_program == null || _programCounter < 0 || _programCounter >= _program.Count)
            {
                IsRunning = false;
                return null;
            }

            var instr = _program[_programCounter];
            if (instr == null)
            {
                _programCounter++;
                return null;
            }

            _programCounter++;
            PC = instr.Address;
            instr.EnterCycle = ClockCycle;
            instr.CurrentStage = PipelineStage.IF;
            log.AppendLine($"[IF]  Fetch: {instr.ToShortString()} @ 0x{instr.Address:X4}");
            return instr;
        }

        private RISCInstruction CreateNOPBubble()
        {
            return new RISCInstruction 
            { 
                Opcode = Opcode.NOP, 
                Class = InstructionClass.NOP, 
                RawText = "NOP (bubble)", 
                CurrentStage = PipelineStage.EX,
                IsBubble = true,
                ProgramIndex = -1
            };
        }

        private void RecordSpaceTime(RISCInstruction instr, int cycle, string stageLabel)
        {
            if (instr == null || instr.IsBubble) return;
            if (instr.ProgramIndex >= 0 && instr.ProgramIndex < SpaceTimeTable.Count)
            {
                SpaceTimeTable[instr.ProgramIndex].SetStage(cycle, stageLabel);
            }
        }

        private void RecordSpaceTimeAll(int cycle)
        {
            RecordSpaceTime(_stageIF, cycle, "IF");
            RecordSpaceTime(_stageDEC, cycle, "DEC");
            RecordSpaceTime(_stageEX, cycle, "EX");
            RecordSpaceTime(_stageMEM, cycle, "MEM");
            RecordSpaceTime(_stageWB, cycle, "WB");
        }

        private int FindInstructionIndex(uint address)
        {
            int index = _program.FindIndex(i => i.Address == address);
            if (index < 0)
            {
                // Debug: printează toate adresele din program
                var addresses = string.Join(", ", _program.Select(i => $"0x{i.Address:X4}"));
                // Log ca string, nu ca apel cu log param
                // Temporar, nu avem log aqui, deci nu putem printa
            }
            return index;
        }

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
                LogText = log,
                DetailIF = _stageIF != null ? $"0x{_stageIF.Address:X4}: {_stageIF.ToShortString()}" : "",
                DetailDEC = _stageDEC != null ? $"Op1={_stageDEC.Op1Value?.ToString() ?? "?"}, Op2={_stageDEC.Op2Value?.ToString() ?? "?"}" : "",
                DetailEX = _stageEX != null ? (_stageEX.ResultValue.HasValue ? $"ALU?{_stageEX.ResultValue.Value}" : "ALU...") : "",
                DetailMEM = _stageMEM != null ? (_stageMEM.Class == InstructionClass.LOAD ? $"MEM[{_stageMEM.Op1Value}]?R{_stageMEM.Rd}" : _stageMEM.Class == InstructionClass.STORE ? $"MEM[{_stageMEM.Op1Value}]?{_stageMEM.Op2Value}" : "") : "",
                DetailWB = _stageWB != null ? (_stageWB.GetWriteRegister() >= 0 && _stageWB.ResultValue.HasValue ? $"R{_stageWB.Rd}?{_stageWB.ResultValue.Value}" : "") : ""
            };
        }
    }
}