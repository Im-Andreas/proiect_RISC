using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public class SuperscalarSimulator
    {
        private List<RISCInstruction> _program = new List<RISCInstruction>();
        public uint PC { get; private set; }

        // Stagiile din partea de aducere/decodificare a pipeline-ului
        private RISCInstruction _stageIF;
        private RISCInstruction _stageDEC;

        public List<FunctionalUnit> FunctionalUnits { get; }

        // Coada de instrucțiuni pregătite pentru Write-Back (terminate în EX)
        private List<RISCInstruction> _readyForWB = new List<RISCInstruction>();

        public RegisterFile Registers { get; } = new RegisterFile();
        public Memory DataMemory { get; } = new Memory();

        public int ClockCycle { get; private set; } = 0;
        public int TotalStalls { get; private set; } = 0;
        public bool IsRunning { get; private set; } = false;
        public bool IsHalted { get; private set; } = false;

        // Mecanismul simplu de detecție: vector de biți (true = registrul este blocat/busy deoarece o instrucțiune urmează să scrie în el)
        private bool[] _registerBusy = new bool[32];

        public List<PipelineState> History { get; } = new List<PipelineState>();
        public List<SpaceTimeEntry> SpaceTimeTable { get; } = new List<SpaceTimeEntry>();

        public event Action<PipelineState> CycleCompleted;

        public HazardInfo ActiveHazard { get; private set; } = HazardInfo.None;

        public SuperscalarSimulator()
        {
            // Instanțiem unitățile specializate conform cerințelor ramurii E1.1
            FunctionalUnits = new List<FunctionalUnit>
            {
                new AdderUnit("Adder/ALU"),
                new MultiplyUnit(latency: 3), // Latență mărită pentru a evidenția OoO/Superscalaritatea
                new LoadStoreUnit(DataMemory, latency: 2) // Încorporează calcul adresă + acces memorie
            };
        }

        public void LoadProgram(List<RISCInstruction> program, uint startAddress)
        {
            _program = program.Select(instruction => new RISCInstruction
            {
                Address = instruction.Address,
                RawText = instruction.RawText,
                Comment = instruction.Comment,
                Opcode = instruction.Opcode,
                Class = instruction.Class,
                Rd = instruction.Rd,
                Rs1 = instruction.Rs1,
                Rs2 = instruction.Rs2,
                Imm = instruction.Imm
            }).ToList();

            PC = startAddress;
            Reset();
        }

        public void Reset()
        {
            ClockCycle = 0;
            TotalStalls = 0;
            IsRunning = false;
            IsHalted = false;
            _stageIF = null;
            _stageDEC = null;
            _readyForWB.Clear();
            ActiveHazard = HazardInfo.None;

            Array.Clear(_registerBusy, 0, _registerBusy.Length);
            Registers.Reset();
            DataMemory.Reset();
            History.Clear();
            SpaceTimeTable.Clear();

            foreach (var unit in FunctionalUnits)
            {
                unit.Release();
            }
        }

        /// <summary>
        /// Execută un singur ciclu de ceas (un pas de simulare).
        /// </summary>
        public bool Step()
        {
            if (IsHalted) return false;

            ClockCycle++;
            ActiveHazard = HazardInfo.None;
            string logMessage = $"Ciclu {ClockCycle}: ";

            // Ordinea procesării stagiilor (de la WB înapoi spre IF pentru a elibera resursele)
            ExecuteWB(ref logMessage);
            ExecuteEX(ref logMessage);
            ExecuteDEC(ref logMessage);
            ExecuteIF(ref logMessage);

            // Verificăm dacă programul s-a terminat complet
            CheckHaltCondition();

            // Salvăm starea curentă în istoric și în tabelul Spațiu-Timp
            UpdateSpaceTimeAndHistory(logMessage);

            return !IsHalted;
        }

        private void ExecuteWB(ref string log)
        {
            if (_readyForWB.Count == 0) return;

            // Procesăm instrucțiunile care au terminat etapa de execuție
            var completedThisCycle = _readyForWB.ToList();
            foreach (var instr in completedThisCycle)
            {
                // Dacă instrucțiunea scrie într-un registru, efectuăm scrierea
                if (instr.GetWriteRegister() >= 0 && instr.ResultValue.HasValue)
                {
                    Registers.Write(instr.GetWriteRegister(), instr.ResultValue.Value);

                    // Eliberăm bitul de ocupat al registrului (Mecanismul de Biți pe Registru)
                    _registerBusy[instr.GetWriteRegister()] = false;
                }

                instr.CurrentStage = PipelineStage.Completed;
                log += $"[WB] {instr.ToShortString()} salvat. ";
                _readyForWB.Remove(instr);
            }
        }

        private void ExecuteEX(ref string log)
        {
            foreach (var unit in FunctionalUnits)
            {
                if (unit.IsBusy)
                {
                    // Incrementăm tactul unității funcționale
                    bool isFinished = unit.Tick();

                    if (isFinished)
                    {
                        var finishedInstr = unit.Release();
                        finishedInstr.CurrentStage = PipelineStage.WB;
                        _readyForWB.Add(finishedInstr);
                        log += $"[EX] {unit.Name} a terminat {finishedInstr.ToShortString()}. ";
                    }
                    else
                    {
                        log += $"[EX] {unit.Name} lucrează la {unit.CurrentInstruction.ToShortString()} ({unit.CyclesRemaining} cic rămase). ";
                    }
                }
            }
        }

        private void ExecuteDEC(ref string log)
        {
            if (_stageDEC == null) return;

            if (_stageDEC.Opcode == Opcode.HALT)
            {
                // Instrucțiunea HALT așteaptă ca toate unitățile să devină libere înainte de a opri procesorul
                if (FunctionalUnits.Any(u => u.IsBusy) || _readyForWB.Count > 0)
                {
                    _stageDEC.StallsAccumulated++;
                    TotalStalls++;
                    log += "[DEC] HALT blochează pipeline-ul până se golesc unitățile. ";
                    return;
                }
                _stageDEC.CurrentStage = PipelineStage.Completed;
                IsHalted = true;
                _stageDEC = null;
                return;
            }

            if (_stageDEC.Opcode == Opcode.NOP)
            {
                _stageDEC.CurrentStage = PipelineStage.Completed;
                _stageDEC = null;
                return;
            }

            // 1. DETECȚIE HAZARDURI DE DATE (Mecanismul de biți solicitat pentru nota 5 / baze Scoreboard)
            bool dataHazard = false;
            string conflictReg = "";

            if (_stageDEC.Rs1.HasValue && _stageDEC.Rs1.Value >= 0 && _registerBusy[_stageDEC.Rs1.Value])
            {
                dataHazard = true;
                conflictReg = $"R{_stageDEC.Rs1.Value}";
            }
            if (_stageDEC.Rs2.HasValue && _stageDEC.Rs2.Value >= 0 && _registerBusy[_stageDEC.Rs2.Value])
            {
                dataHazard = true;
                conflictReg = $"R{_stageDEC.Rs2.Value}";
            }
            // Evitare hazard WAW (dacă o instrucțiune anterioară scrie deja în același registru destinație)
            int writeReg = _stageDEC.GetWriteRegister();
            if (writeReg >= 0 && _registerBusy[writeReg])
            {
                dataHazard = true;
                conflictReg = $"R{writeReg} (WAW)";
            }

            if (dataHazard)
            {
                _stageDEC.StallsAccumulated++;
                TotalStalls++;
                ActiveHazard = new HazardInfo
                {
                    HasHazard = true,
                    HazardType = "RAW/WAW (Biți Registru)",
                    ConflictRegister = conflictReg,
                    Consumer = _stageDEC,
                    Description = $"Așteaptă eliberarea registrului {conflictReg}"
                };
                log += $"[DEC] Stall hazard date pe {conflictReg} pentru {_stageDEC.ToShortString()}. ";
                return;
            }

            // 2. DETECȚIE HAZARD STRUCTURAL (Căutăm o unitate specializată liberă și compatibilă)
            var targetUnit = FunctionalUnits.FirstOrDefault(u => !u.IsBusy && u.CanExecute(_stageDEC));

            if (targetUnit == null)
            {
                _stageDEC.StallsAccumulated++;
                TotalStalls++;
                log += $"[DEC] Stall structural (Nicio unitate funcțională disponibilă pentru {_stageDEC.ToShortString()}). ";
                return;
            }

            // 3. LANSARE (ISSUE) - Citim valorile din regiştri şi trimitem instrucțiunea în unitate
            _stageDEC.Op1Value = _stageDEC.Rs1.HasValue ? Registers.Read(_stageDEC.Rs1.Value) : (int?)null;
            _stageDEC.Op2Value = _stageDEC.Rs2.HasValue ? Registers.Read(_stageDEC.Rs2.Value) : (int?)null;

            // Marcăm registrul destinație ca "Ocupat" (lock pe registru)
            if (writeReg >= 0)
            {
                _registerBusy[writeReg] = true;
            }

            targetUnit.TryIssue(_stageDEC);
            _stageDEC.CurrentStage = PipelineStage.EX;
            log += $"[DEC] Lansat {_stageDEC.ToShortString()} către {targetUnit.Name}. ";

            // Eliberăm stadiul DEC pentru următoarea instrucțiune
            _stageDEC = null;
        }

        private void ExecuteIF(ref string log)
        {
            // Dacă stadiul DEC este ocupat (blocat de un stall), nu putem aduce o nouă instrucțiune
            if (_stageDEC != null)
            {
                log += "[IF] Blocat (Stall în DEC). ";
                return;
            }

            // Căutăm instrucțiunea de la PC-ul curent
            var currentInstr = _program.FirstOrDefault(i => i.Address == PC);

            if (currentInstr != null)
            {
                // Clonăm instrucțiunea pentru a o introduce în fluxul activ al pipeline-ului
                _stageIF = new RISCInstruction
                {
                    Address = currentInstr.Address,
                    RawText = currentInstr.RawText,
                    Comment = currentInstr.Comment,
                    Opcode = currentInstr.Opcode,
                    Class = currentInstr.Class,
                    Rd = currentInstr.Rd,
                    Rs1 = currentInstr.Rs1,
                    Rs2 = currentInstr.Rs2,
                    Imm = currentInstr.Imm,
                    CurrentStage = PipelineStage.DEC,
                    EnterCycle = ClockCycle
                };

                // Tranziție imediată spre stadiul DEC pentru ciclul următor
                _stageDEC = _stageIF;
                log += $"[IF] Adus {_stageIF.ToShortString()} de la 0x{PC:X4}. ";

                // Rezolvare control/salturi: Pentru că instrucțiunea de salt ocupă AdderUnit doar pentru simulare,
                // calculul noului PC se face aici în mod simplificat sau predictiv.
                if (_stageIF.Class == InstructionClass.JUMP || _stageIF.Class == InstructionClass.BRANCH)
                {
                    // În acest model superscalar de bază, tratăm saltul necondiționat direct:
                    if (_stageIF.Opcode == Opcode.JMP && _stageIF.Imm.HasValue)
                    {
                        PC = (uint)_stageIF.Imm.Value;
                        _stageIF = null;
                        return;
                    }
                    // Pentru BEQ/BNE/BGT/BLT, evaluăm condiția pe loc (cu valorile curente) ca să știm unde continuă IF:
                    bool branchTaken = EvaluateBranchOnTheFly(_stageIF);
                    if (branchTaken && _stageIF.Imm.HasValue)
                    {
                        PC = (uint)_stageIF.Imm.Value;
                        _stageIF = null;
                        return;
                    }
                }

                // Incrementăm PC-ul clasic (cu 4 octeți per instrucțiune)
                PC += 4;
                _stageIF = null;
            }
            else
            {
                log += "[IF] Idle (Sfârșitul memoriei de instrucțiuni sau PC invalid). ";
            }
        }

        private bool EvaluateBranchOnTheFly(RISCInstruction instr)
        {
            int val1 = instr.Rs1.HasValue ? Registers.Read(instr.Rs1.Value) : 0;
            int val2 = instr.Rs2.HasValue ? Registers.Read(instr.Rs2.Value) : 0;

            switch (instr.Opcode)
            {
                case Opcode.BEQ: return val1 == val2;
                case Opcode.BNE: return val1 != val2;
                case Opcode.BGT: return val1 > val2;
                case Opcode.BLT: return val1 < val2;
                default: return false;
            }
        }

        private void CheckHaltCondition()
        {
            // Procesorul se oprește definitiv dacă s-a dat HALT sau dacă nu mai există nicio activitate nicăieri
            if (_stageDEC == null && !FunctionalUnits.Any(u => u.IsBusy) && _readyForWB.Count == 0 && !_program.Any(i => i.Address == PC))
            {
                IsHalted = true;
            }
        }

        private void UpdateSpaceTimeAndHistory(string logText)
        {
            // Salvăm snapshot-ul stării curente a simulatorului
            var snapshot = new PipelineState
            {
                ClockCycle = ClockCycle,
                PC = PC,
                TotalStalls = TotalStalls,
                StageIF = null, // În acest model, instrucțiunea trece instant din IF în DEC
                StageDEC = _stageDEC,
                StageEX = FunctionalUnits.FirstOrDefault(u => u.Type == FunctionalUnitType.Adder)?.CurrentInstruction,
                StageMEM = FunctionalUnits.FirstOrDefault(u => u.Type == FunctionalUnitType.LoadStore)?.CurrentInstruction,
                StageWB = _readyForWB.FirstOrDefault(),
                ActiveHazard = ActiveHazard,
                ActiveForwarding = ForwardingInfo.None, // Fără forwarding în modelul de bază E1.1
                RegisterSnapshot = Registers.GetSnapshot(),
                LogMessage = logText,
                LogText = logText
            };

            // Adăugăm detalii text specifice pentru interfața grafică Windows Forms
            snapshot.DetailIF = _stageDEC != null ? $"Pregătit DEC: {_stageDEC.ToShortString()}" : "Liber";
            snapshot.DetailDEC = _stageDEC != null ? $"Rs1={_stageDEC.Rs1}, Rs2={_stageDEC.Rs2}" : "";
            snapshot.DetailEX = string.Join(" | ", FunctionalUnits.Where(u => u.IsBusy).Select(u => $"{u.Name}:{u.CyclesRemaining}c"));
            snapshot.DetailMEM = "";
            snapshot.DetailWB = _readyForWB.Count > 0 ? $"{_readyForWB.Count} instr" : "";

            History.Add(snapshot);

            // Actualizăm Tabelul Spațiu-Timp (Space-Time Diagram)
            UpdateSpaceTimeEntries();

            // Declanșăm evenimentul către Form-ul principal din Windows Forms
            CycleCompleted?.Invoke(snapshot);
        }

        private void UpdateSpaceTimeEntries()
        {
            // Înregistrăm stadiul curent al fiecărei instrucțiuni active pentru diagrama vizuală
            if (_stageDEC != null) AddSpaceTimePoint(_stageDEC, "DEC");

            foreach (var unit in FunctionalUnits)
            {
                if (unit.IsBusy) AddSpaceTimePoint(unit.CurrentInstruction, "EX");
            }

            foreach (var instr in _readyForWB)
            {
                AddSpaceTimePoint(instr, "WB");
            }
        }

        private void AddSpaceTimePoint(RISCInstruction instr, string stageLabel)
        {
            var entry = SpaceTimeTable.FirstOrDefault(e => e.InstructionLabel.Contains(instr.RawText) && e.CycleStages.ContainsKey(instr.EnterCycle));

            if (entry == null)
            {
                entry = new SpaceTimeEntry
                {
                    InstructionLabel = $"0x{instr.Address:X4}: {instr.RawText}",
                    InstructionIndex = SpaceTimeTable.Count
                };
                SpaceTimeTable.Add(entry);
            }

            entry.SetStage(ClockCycle, stageLabel);
        }

        // Returnează o listă cu starea tuturor unităților pentru afișarea în Scoreboard / DataGridView
        public List<FunctionalUnitState> GetFunctionalUnitStates()
        {
            return FunctionalUnits.Select(u => u.GetState()).ToList();
        }
    }
}