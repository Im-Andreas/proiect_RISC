using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public enum InstructionClass
    {
        ALU, ALUI, LOAD, STORE, BRANCH, JUMP, NOP, HALT, UNKNOWN
    }

    public enum Opcode
    {
        ADD, SUB, MUL, AND, OR, XOR, SHL, SHR,
        ADDI, SUBI, ANDI, ORI,
        LD, LDI, ST,
        JMP, BEQ, BNE, BGT, BLT,
        NOP, HALT, MOV,
        UNKNOWN
    }

    public enum PipelineStage
    {
        None, IF, DEC, EX, MEM, WB, Stall, Completed
    }

    public class RISCInstruction
    {
        public uint Address { get; set; }
        public string RawText { get; set; }
        public string Comment { get; set; }

        public Opcode Opcode { get; set; }
        public InstructionClass Class { get; set; }

        public int? Rd { get; set; }
        public int? Rs1 { get; set; }
        public int? Rs2 { get; set; }
        public int? Imm { get; set; }

        public PipelineStage CurrentStage { get; set; } = PipelineStage.None;
        public int EnterCycle { get; set; }
        public int StallsAccumulated { get; set; }

        public bool HasRAWHazard { get; set; }
        public bool HasWARHazard { get; set; }
        public bool HasWAWHazard { get; set; }
        public bool IsForwarded { get; set; }
        public string ForwardSource { get; set; }

        public int? Op1Value { get; set; }
        public int? Op2Value { get; set; }
        public int? ResultValue { get; set; }
        
        // Index in program (for SpaceTime tracking)
        public int ProgramIndex { get; set; } = -1;
        public bool IsBubble { get; set; } = false;

        public List<int> GetReadRegisters()
        {
            var regs = new List<int>();
            if (Rs1.HasValue) regs.Add(Rs1.Value);
            if (Rs2.HasValue) regs.Add(Rs2.Value);
            return regs;
        }

        public int GetWriteRegister() => Rd ?? -1;

        public string ToShortString() => string.IsNullOrEmpty(RawText) ? "---" : RawText.Trim();

        public override string ToString() => $"[0x{Address:X4}] {RawText}";
    }

    public class RegisterFile
    {
        public const int Count = 16;
        private readonly int[] _values = new int[Count];
        private readonly bool[] _valid = new bool[Count];

        public event Action<int> RegisterChanged;

        public RegisterFile() { Reset(); }

        public void Reset()
        {
            for (int i = 0; i < Count; i++) { _values[i] = 0; _valid[i] = true; }
        }

        public int Read(int index)
        {
            ValidateIndex(index);
            return index == 0 ? 0 : _values[index];
        }

        public void Write(int index, int value)
        {
            ValidateIndex(index);
            if (index == 0) return;
            _values[index] = value;
            _valid[index] = true;
            RegisterChanged?.Invoke(index);
        }

        public bool IsValid(int index)
        {
            ValidateIndex(index);
            return _valid[index];
        }

        public void InvalidateRegister(int index)
        {
            ValidateIndex(index);
            if (index == 0) return;
            _valid[index] = false;
            RegisterChanged?.Invoke(index);
        }

        public void ValidateRegister(int index)
        {
            ValidateIndex(index);
            _valid[index] = true;
            RegisterChanged?.Invoke(index);
        }

        public (int value, bool valid)[] GetSnapshot()
        {
            var snap = new (int, bool)[Count];
            for (int i = 0; i < Count; i++) snap[i] = (_values[i], _valid[i]);
            return snap;
        }

        private void ValidateIndex(int i)
        {
            if (i < 0 || i >= Count) throw new ArgumentOutOfRangeException(nameof(i));
        }
    }

    public class Memory
    {
        private readonly Dictionary<uint, int> _data = new Dictionary<uint, int>();
        public const uint DefaultCodeBase = 0x0100;

        public void Write(uint address, int value) { _data[address] = value; }
        public int Read(uint address) { return _data.TryGetValue(address, out int val) ? val : 0; }
        public bool Exists(uint address) => _data.ContainsKey(address);
        public void Reset() => _data.Clear();
        public IEnumerable<uint> GetUsedAddresses() => _data.Keys.OrderBy(a => a);
    }

    public class PipelineState
    {
        public int ClockCycle { get; set; }
        public uint PC { get; set; }
        public int TotalStalls { get; set; }

        public RISCInstruction StageIF { get; set; }
        public RISCInstruction StageDEC { get; set; }
        public RISCInstruction StageEX { get; set; }
        public RISCInstruction StageMEM { get; set; }
        public RISCInstruction StageWB { get; set; }

        public HazardInfo ActiveHazard { get; set; }
        public ForwardingInfo ActiveForwarding { get; set; }
        public (int value, bool valid)[] RegisterSnapshot { get; set; }

        public string DetailIF { get; set; } = "";
        public string DetailDEC { get; set; } = "";
        public string DetailEX { get; set; } = "";
        public string DetailMEM { get; set; } = "";
        public string DetailWB { get; set; } = "";

        public string LogMessage { get; set; } = "";
        public string LogText { get; set; } = ""; // Alias pentru log complet
    }

    public class HazardInfo
    {
        public bool HasHazard { get; set; }
        public string HazardType { get; set; }
        public string ConflictRegister { get; set; }
        public RISCInstruction Producer { get; set; }
        public RISCInstruction Consumer { get; set; }
        public int StallsRequired { get; set; }
        public string Description { get; set; }

        public static HazardInfo None => new HazardInfo { HasHazard = false };
    }

    public class ForwardingInfo
    {
        public bool IsActive { get; set; }
        public string Path { get; set; }
        public string Register { get; set; }
        public int ForwardedValue { get; set; }
        public string Description { get; set; }

        public static ForwardingInfo None => new ForwardingInfo { IsActive = false };
    }

    public class SpaceTimeEntry
    {
        public string InstructionLabel { get; set; }
        public int InstructionIndex { get; set; }
        public Dictionary<int, string> CycleStages { get; set; } = new Dictionary<int, string>();

        public void SetStage(int cycle, string stage) => CycleStages[cycle] = stage;
        public string GetStage(int cycle) => CycleStages.TryGetValue(cycle, out var s) ? s : "";
    }

    // ── Execution model selector ──────────────────────────────────────────────
    public enum ExecutionModel { InOrder, Scoreboard, Tomasulo }

    // ── Scoreboard (tabela de marcaj — CDC 6600) ──────────────────────────────
    public enum SbStage { RO, EX, WR }

    public class SbFUState
    {
        public FunctionalUnitType UnitType;
        public int UnitIndex;
        public RISCInstruction Instr;
        public int Fi = -1;              // destination register
        public int Fj = -1, Fk = -1;    // source register indices
        public string Qj = "", Qk = "";  // producing FU name ("" = operand ready)
        public bool Rj = true, Rk = true; // source operand ready-to-read?
        public int CyclesLeft;           // remaining EX cycles after first EX
        public SbStage Stage;
        public int? ResultValue;
        public string Name => $"{UnitType}-{UnitIndex}";
    }

    // ── Tomasulo — stații de rezervare + RAT + CDB ────────────────────────────
    public class ReservationStation
    {
        public FunctionalUnitType UnitType;
        public int RSIndex;
        public int FUInstanceIndex = -1;  // which FU instance is executing
        public bool Busy;
        public RISCInstruction Instr;
        public Opcode Op;
        public int? Vj, Vk;              // operand values (null = not yet known)
        public string Qj = "", Qk = "";  // RS tag that will produce Vj/Vk ("" = ready)
        public int? Imm;
        public int DestReg = -1;          // destination register
        public int CyclesLeft;            // EX cycles remaining after dispatch
        public bool Dispatched;           // FU is executing this RS entry
        public int? ResultValue;
        public bool WRDone;               // CDB broadcast completed
        public string Tag => $"{UnitType}-RS{RSIndex}";
    }
}