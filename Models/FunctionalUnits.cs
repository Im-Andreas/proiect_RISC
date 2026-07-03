using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public enum FunctionalUnitType { ADD, MUL, LD_ST, BRANCH }

    public class FunctionalUnitConfig
    {
        public FunctionalUnitType UnitType { get; }
        public int Count   { get; set; }
        public int Latency { get; set; }

        public FunctionalUnitConfig(FunctionalUnitType type, int count, int latency)
        {
            UnitType = type;
            Count    = count;
            Latency  = latency;
        }
    }

    public class FunctionalUnitInstance
    {
        public FunctionalUnitType UnitType    { get; }
        public int                UnitIndex   { get; }
        public RISCInstruction    OccupiedBy  { get; set; }
        public int                CyclesLeft  { get; set; }
        public bool               IsAvailable => OccupiedBy == null;

        public FunctionalUnitInstance(FunctionalUnitType type, int index)
        {
            UnitType  = type;
            UnitIndex = index;
        }

        public void Free() { OccupiedBy = null; CyclesLeft = 0; }
    }

    public class OccupancyRecord
    {
        public int                Cycle               { get; set; }
        public FunctionalUnitType UnitType            { get; set; }
        public int                UnitIndex           { get; set; }
        public string             InstructionLabel    { get; set; }
        public int                InstructionProgIdx  { get; set; }
        public bool               IsFirstCycle        { get; set; }
    }

    public class FunctionalUnitSet
    {
        private readonly Dictionary<FunctionalUnitType, FunctionalUnitConfig> _configs;
        private readonly List<FunctionalUnitInstance> _units = new List<FunctionalUnitInstance>();

        public IReadOnlyList<FunctionalUnitInstance>                     Units   => _units;
        public IReadOnlyDictionary<FunctionalUnitType, FunctionalUnitConfig> Configs => _configs;
        public List<OccupancyRecord> OccupancyLog { get; } = new List<OccupancyRecord>();

        public FunctionalUnitSet()
        {
            _configs = new Dictionary<FunctionalUnitType, FunctionalUnitConfig>
            {
                [FunctionalUnitType.ADD]    = new FunctionalUnitConfig(FunctionalUnitType.ADD,    1, 1),
                [FunctionalUnitType.MUL]    = new FunctionalUnitConfig(FunctionalUnitType.MUL,    1, 3),
                [FunctionalUnitType.LD_ST]  = new FunctionalUnitConfig(FunctionalUnitType.LD_ST,  1, 1),
                [FunctionalUnitType.BRANCH] = new FunctionalUnitConfig(FunctionalUnitType.BRANCH, 1, 1),
            };
            RebuildInstances();
        }

        public void Configure(FunctionalUnitType type, int count, int latency)
        {
            _configs[type].Count   = Math.Max(1, count);
            _configs[type].Latency = Math.Max(1, latency);
            RebuildInstances();
        }

        private void RebuildInstances()
        {
            // keep occupancy for units that still exist after config change
            var busy = _units
                .Where(u => !u.IsAvailable)
                .ToDictionary(u => (u.UnitType, u.UnitIndex), u => (u.OccupiedBy, u.CyclesLeft));

            _units.Clear();
            foreach (var cfg in _configs.Values)
                for (int i = 0; i < cfg.Count; i++)
                {
                    var inst = new FunctionalUnitInstance(cfg.UnitType, i);
                    if (busy.TryGetValue((cfg.UnitType, i), out var s))
                    { inst.OccupiedBy = s.OccupiedBy; inst.CyclesLeft = s.CyclesLeft; }
                    _units.Add(inst);
                }
        }

        public static FunctionalUnitType? GetRequiredUnit(RISCInstruction instr)
        {
            if (instr == null || instr.IsBubble) return null;
            switch (instr.Opcode)
            {
                case Opcode.MUL:                                            return FunctionalUnitType.MUL;
                case Opcode.LD: case Opcode.LDI: case Opcode.ST:           return FunctionalUnitType.LD_ST;
                case Opcode.JMP: case Opcode.BEQ: case Opcode.BNE:
                case Opcode.BGT: case Opcode.BLT:                          return FunctionalUnitType.BRANCH;
                case Opcode.NOP: case Opcode.HALT:                         return null;
                default:                                                    return FunctionalUnitType.ADD;
            }
        }

        /// <summary>Dispatch instruction to a free unit. Returns the unit, or null on structural hazard.</summary>
        public FunctionalUnitInstance TryDispatch(RISCInstruction instr)
        {
            var utNullable = GetRequiredUnit(instr);
            if (utNullable == null) return null;
            var ut = utNullable.Value;
            var unit = _units.FirstOrDefault(u => u.UnitType == ut && u.IsAvailable);
            if (unit == null) return null;
            unit.OccupiedBy = instr;
            unit.CyclesLeft = _configs[ut].Latency;
            return unit;
        }

        public bool HasAvailableUnit(FunctionalUnitType type)
            => _units.Any(u => u.UnitType == type && u.IsAvailable);

        public int GetLatency(FunctionalUnitType type) => _configs[type].Latency;

        /// <summary>Record current cycle occupancy in the log (call once per clock cycle).</summary>
        public void RecordOccupancy(int clockCycle)
        {
            foreach (var u in _units)
                if (!u.IsAvailable)
                    OccupancyLog.Add(new OccupancyRecord
                    {
                        Cycle              = clockCycle,
                        UnitType           = u.UnitType,
                        UnitIndex          = u.UnitIndex,
                        InstructionLabel   = u.OccupiedBy.ToShortString(),
                        InstructionProgIdx = u.OccupiedBy.ProgramIndex,
                        IsFirstCycle       = u.CyclesLeft == _configs[u.UnitType].Latency,
                    });
        }

        /// <summary>Decrement CyclesLeft; free units that complete. Does NOT record occupancy.</summary>
        public List<RISCInstruction> TickUnits()
        {
            var done = new List<RISCInstruction>();
            foreach (var u in _units)
                if (!u.IsAvailable)
                {
                    u.CyclesLeft--;
                    if (u.CyclesLeft <= 0) { done.Add(u.OccupiedBy); u.Free(); }
                }
            return done;
        }

        public void Reset()
        {
            foreach (var u in _units) u.Free();
            OccupancyLog.Clear();
        }
    }
}
