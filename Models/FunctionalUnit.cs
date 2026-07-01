using System;

namespace proiect_RISC.Models
{
    public enum FunctionalUnitType
    {
        LoadStore,
        Multiply,
        Adder,
        Branch
    }

    /// <summary>
    /// Snapshot read-only al starii unei unitati, pentru afisare in scoreboard (SuperscalarForm).
    /// Corespunde coloanelor: Busy, Rd, Rs1, Rs1 Ready, Rs2, Rs2 Ready (Curs 11, pag. 8-11).
    /// </summary>
    public class FunctionalUnitState
    {
        public string Name { get; set; }
        public FunctionalUnitType Type { get; set; }
        public bool Busy { get; set; }
        public int? Rd { get; set; }
        public int? Rs1 { get; set; }
        public bool Rs1Ready { get; set; }
        public int? Rs2 { get; set; }
        public bool Rs2Ready { get; set; }
        public int CyclesRemaining { get; set; }
        public string CurrentInstructionText { get; set; }
    }

    /// <summary>
    /// Baza comuna pentru toate unitatile de executie specializate (E1.1 - Superscalaritate).
    /// Fiecare unitate: poate fi ocupata sau libera, accepta doar anumite clase/opcode-uri de
    /// instructiuni (CanExecute), si executa intr-un numar fix de cicluri (Latency).
    /// </summary>
    public abstract class FunctionalUnit
    {
        public string Name { get; }
        public FunctionalUnitType Type { get; }
        public int Latency { get; }

        public bool IsBusy { get; private set; }
        public RISCInstruction CurrentInstruction { get; private set; }
        public int CyclesRemaining { get; private set; }

        protected FunctionalUnit(string name, FunctionalUnitType type, int latency)
        {
            Name = name;
            Type = type;
            Latency = latency;
        }

        /// <summary>Decide daca aceasta unitate stie sa execute instructiunea data.</summary>
        public abstract bool CanExecute(RISCInstruction instr);

        /// <summary>Incearca sa preia instructiunea in unitate (daca e libera si compatibila).</summary>
        public bool TryIssue(RISCInstruction instr)
        {
            if (IsBusy || instr == null || !CanExecute(instr)) return false;
            CurrentInstruction = instr;
            CyclesRemaining = Latency;
            IsBusy = true;
            return true;
        }

        /// <summary>
        /// Avanseaza unitatea cu un ciclu de tact. Returneaza true cand executia s-a terminat
        /// </summary>
        public bool Tick()
        {
            if (!IsBusy) return false;
            CyclesRemaining--;
            if (CyclesRemaining <= 0)
            {
                Compute(CurrentInstruction);
                return true;
            }
            return false;
        }

        protected abstract void Compute(RISCInstruction instr);

        public RISCInstruction Release()
        {
            var instr = CurrentInstruction;
            IsBusy = false;
            CurrentInstruction = null;
            CyclesRemaining = 0;
            return instr;
        }

        public FunctionalUnitState GetState()
        {
            return new FunctionalUnitState
            {
                Name = Name,
                Type = Type,
                Busy = IsBusy,
                Rd = (CurrentInstruction != null && CurrentInstruction.GetWriteRegister() >= 0)
                        ? CurrentInstruction.GetWriteRegister() : (int?)null,
                Rs1 = CurrentInstruction?.Rs1,
                Rs1Ready = CurrentInstruction?.Op1Value.HasValue ?? false,
                Rs2 = CurrentInstruction?.Rs2,
                Rs2Ready = CurrentInstruction?.Op2Value.HasValue ?? false,
                CyclesRemaining = CyclesRemaining,
                CurrentInstructionText = CurrentInstruction?.ToShortString() ?? ""
            };
        }
    }

    /// <summary>
    /// Sumator: ALU intreg (ADD/SUB/AND/OR/XOR/SHL/SHR + variantele *I) si rezolvarea
    /// adresei/conditiei de salt (JMP/BEQ/BNE/BGT/BLT) - exact ca la curs, unde JMP/Branch
    /// se calculeaza tot in unitatea de executie aritmetica, nu intr-o unitate separata.
    /// </summary>
    public class AdderUnit : FunctionalUnit
    {
        public AdderUnit(string name) : base(name, FunctionalUnitType.Adder, latency: 1) { }

        public override bool CanExecute(RISCInstruction instr)
        {
            if (instr == null) return false;
            switch (instr.Opcode)
            {
                case Opcode.ADD:
                case Opcode.SUB:
                case Opcode.AND:
                case Opcode.OR:
                case Opcode.XOR:
                case Opcode.SHL:
                case Opcode.SHR:
                case Opcode.ADDI:
                case Opcode.SUBI:
                case Opcode.ANDI:
                case Opcode.ORI:
                case Opcode.LDI:
                    return true;
                default:
                    return false;
            }
        }

        protected override void Compute(RISCInstruction instr)
        {
            int op1 = instr.Op1Value ?? 0;
            int op2 = instr.Op2Value ?? (instr.Imm ?? 0);

            switch (instr.Opcode)
            {
                case Opcode.ADD: instr.ResultValue = op1 + op2; break;
                case Opcode.SUB: instr.ResultValue = op1 - op2; break;
                case Opcode.AND: instr.ResultValue = op1 & op2; break;
                case Opcode.OR: instr.ResultValue = op1 | op2; break;
                case Opcode.XOR: instr.ResultValue = op1 ^ op2; break;
                case Opcode.SHL: instr.ResultValue = op1 << (op2 & 31); break;
                case Opcode.SHR: instr.ResultValue = (int)((uint)op1 >> (op2 & 31)); break;

                case Opcode.ADDI: instr.ResultValue = op1 + (instr.Imm ?? 0); break;
                case Opcode.SUBI: instr.ResultValue = op1 - (instr.Imm ?? 0); break;
                case Opcode.ANDI: instr.ResultValue = op1 & (instr.Imm ?? 0); break;
                case Opcode.ORI: instr.ResultValue = op1 | (instr.Imm ?? 0); break;

                case Opcode.LDI: instr.ResultValue = instr.Imm ?? 0; break;
            }
        }
    }

    /// <summary>Inmultitor: doar MUL, latenta mai mare (3 cicluri) ca sa evidentieze utilitatea suprascalaritatii.</summary>
    public class MultiplyUnit : FunctionalUnit
    {
        public MultiplyUnit(int latency = 3) : base("Inmultitor", FunctionalUnitType.Multiply, latency) { }

        public override bool CanExecute(RISCInstruction instr) => instr != null && instr.Opcode == Opcode.MUL;

        protected override void Compute(RISCInstruction instr)
        {
            int op1 = instr.Op1Value ?? 0;
            int op2 = instr.Op2Value ?? 0;
            instr.ResultValue = op1 * op2;
        }
    }

    /// <summary>
    /// Load/Store: calculeaza adresa efectiva si executa efectiv accesul la memorie (citire
    /// pentru LD, scriere pentru ST). In modelul superscalar nu mai exista un stage MEM separat
    /// ca la pipeline-ul de baza - latenta unitatii (implicit 2 cicluri) reprezinta tocmai
    /// adresare + acces la memorie, tratate impreuna.
    /// </summary>
    public class LoadStoreUnit : FunctionalUnit
    {
        private readonly Memory _memory;

        public LoadStoreUnit(Memory memory = null, int latency = 2)
            : base("Load/Store", FunctionalUnitType.LoadStore, latency)
        {
            _memory = memory;
        }

        public override bool CanExecute(RISCInstruction instr) =>
            instr != null && instr.Opcode != Opcode.LDI &&
            (instr.Class == InstructionClass.LOAD || instr.Class == InstructionClass.STORE);

        protected override void Compute(RISCInstruction instr)
        {
            uint effectiveAddress = (uint)(instr.Op1Value ?? 0);

            if (instr.Opcode == Opcode.LD)
            {
                instr.ResultValue = _memory?.Read(effectiveAddress) ?? 0;
            }
            else if (instr.Opcode == Opcode.ST)
            {
                int value = instr.Op2Value ?? 0;
                _memory?.Write(effectiveAddress, value);
            }
        }
    }

    public class BranchUnit : FunctionalUnit
    {
        public BranchUnit(int latency = 1)
            : base("Branch/Jump", FunctionalUnitType.Branch, latency)
        {
        }

        public override bool CanExecute(RISCInstruction instr) =>
            instr != null && (instr.Class == InstructionClass.BRANCH || instr.Class == InstructionClass.JUMP);

        protected override void Compute(RISCInstruction instr)
        {
            //evaluare a conditiilor de salt

            int op1 = instr.Op1Value ?? 0;
            int op2 = instr.Op2Value ?? 0;

            if (instr.Opcode == Opcode.BEQ)
            {
                instr.ResultValue = (op1 == op2) ? 1 : 0; //Salt efectuat
            }
            else if (instr.Opcode == Opcode.BNE)
            {
                instr.ResultValue = (op1 != op2) ? 1 : 0;
            }
            else if (instr.Opcode == Opcode.BGT)
            {
                instr.ResultValue = (op1 > op2) ? 1 : 0;
            }
            else if (instr.Opcode == Opcode.BLT)
            {
                instr.ResultValue = (op1 < op2) ? 1 : 0;
            }
            else if (instr.Opcode == Opcode.JMP)
            {
                instr.ResultValue = 1; //Salt necondiționat
            }
        }
    }
}
