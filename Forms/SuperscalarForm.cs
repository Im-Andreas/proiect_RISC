using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class SuperscalarForm : Form
    {
        private DataGridView dgv1; // Status Instructiuni
        private DataGridView dgv2; // Status Unitati Functionale
        private DataGridView dgv3; // Status Registri
        private Label lblClock;
        private Button btnStep;
        private Button btnReset;
        private RichTextBox rtbLog;

        private int _currentCycle = 0;
        private List<ScoreboardInstruction> _instructions = new List<ScoreboardInstruction>();
        private List<ScoreboardUnit> _units = new List<ScoreboardUnit>();
        private Dictionary<string, string> _registerStatus = new Dictionary<string, string>(); 
        
        // Salvam programul primit de la Form1
        private List<RISCInstruction> _loadedProgram;
        private Form1 _mainForm;

        // Modificam constructorul sa primeasca programul incarcat
        public SuperscalarForm(List<RISCInstruction> program, Form1 mainForm)
        {
            _loadedProgram = program;
            _mainForm = mainForm;

            this.Text = "Superscalar Execution & Hazard Control (Scoreboard)";
            this.Size = new Size(1150, 750);
            this.StartPosition = FormStartPosition.CenterScreen;

            var tc = new TabControl { Dock = DockStyle.Fill };
            var tabScoreboard = new TabPage("Scoreboard Algorithm (Course 11)");
            tc.TabPages.Add(tabScoreboard);
            this.Controls.Add(tc);

            dgv1 = new DataGridView 
            { 
                AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 160,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.Fixed3D
            };
            dgv1.Columns.Add("Instruction", "Instruction");
            dgv1.Columns[0].Width = 180;
            dgv1.Columns.Add("IFDone", "Issue");
            dgv1.Columns.Add("OFDone", "Read Operands");
            dgv1.Columns.Add("EXDone", "Execution Complete");
            dgv1.Columns.Add("WBDone", "Write Result (WB)");

            dgv2 = new DataGridView 
            { 
                AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 160,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.Fixed3D
            };
            dgv2.Columns.Add("Unit", "Unit #");
            dgv2.Columns.Add("Name", "Unit Name");
            dgv2.Columns.Add("Busy", "Busy");
            dgv2.Columns.Add("Op", "Operation");
            dgv2.Columns.Add("Dest", "Dest (Fi)");
            dgv2.Columns.Add("Rs1", "Source 1 (Fj)");
            dgv2.Columns.Add("Rs1Ready", "S1 Ready? (Rj)");
            dgv2.Columns.Add("Rs2", "Source 2 (Fk)");
            dgv2.Columns.Add("Rs2Ready", "S2 Ready? (Rk)");

            dgv3 = new DataGridView 
            { 
                AllowUserToAddRows = false, ReadOnly = true, Dock = DockStyle.Top, Height = 75,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.Fixed3D
            };
            for (int i = 0; i < 16; i++) dgv3.Columns.Add($"R{i}", $"R{i}");
            dgv3.Rows.Add();
            dgv3.Rows[0].HeaderCell.Value = "Unit";

            var pnlDb = new Panel { Dock = DockStyle.Top, Height = 55, BackColor = Color.FromArgb(0xF5, 0xF5, 0xF5) };
            
            btnStep = new Button 
            { 
                Text = "Next Step", Left = 15, Top = 12, Width = 140, Height = 30,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnStep.Font = new Font(btnStep.Font, FontStyle.Bold);
            btnStep.Click += BtnStep_Click;

            btnReset = new Button 
            { 
                Text = "Reset", Left = 165, Top = 12, Width = 120, Height = 30,
                BackColor = Color.FromArgb(0x75, 0x75, 0x75), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            btnReset.Click += BtnReset_Click;

            lblClock = new Label 
            { 
                Text = "Clock Cycle: 0", Name = "lblScoreboardClock", Left = 305, Top = 18, Width = 150,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold)
            };

            pnlDb.Controls.Add(btnStep);
            pnlDb.Controls.Add(btnReset);
            pnlDb.Controls.Add(lblClock);

            // Am facut log-ul alb cu text negru pentru un aspect curat
            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true,
                BackColor = Color.White, ForeColor = Color.Black,
                Font = new Font("Consolas", 10F)
            };

            tabScoreboard.Controls.Add(rtbLog);
            tabScoreboard.Controls.Add(pnlDb);
            tabScoreboard.Controls.Add(dgv3);
            tabScoreboard.Controls.Add(dgv2);
            tabScoreboard.Controls.Add(dgv1);

            InitializeSimulationData();
            UpdateUI();
        }

        private void InitializeSimulationData()
        {
            _currentCycle = 0;
            _instructions.Clear();
            _units.Clear();
            _registerStatus.Clear();

            // Preluam dinamic instructiunile din programul principal
            // In cazul in care apelul vine din Reset, actualizam sursa de date pentru a prelua programul corect (chiar daca el e modificat curent in MainForm)
            if (_currentCycle == 0 && _mainForm != null) 
            {
                _loadedProgram = _mainForm.GetCurrentProgram();
            }

            foreach (var instr in _loadedProgram)
            {
                if (instr.Opcode == Opcode.NOP || instr.Opcode == Opcode.HALT || instr.Opcode == Opcode.UNKNOWN)
                    continue;

                int latency = 1;
                if (instr.Class == InstructionClass.LOAD || instr.Class == InstructionClass.STORE) latency = 2;
                else if (instr.Opcode == Opcode.MUL) latency = 4;

                string dest = "-";
                string src1 = instr.Rs1.HasValue ? $"R{instr.Rs1.Value}" : "-";
                string src2 = instr.Rs2.HasValue ? $"R{instr.Rs2.Value}" : "-";

                // Mapare corecta in functie de clasa instructiunii
                if (instr.Class == InstructionClass.STORE || instr.Class == InstructionClass.BRANCH)
                {
                    dest = "-"; // Store-ul si Branch-ul nu scriu in registru
                }
                else if (instr.Class == InstructionClass.LOAD)
                {
                    dest = instr.Rd.HasValue ? $"R{instr.Rd.Value}" : "-";
                    src2 = "-";
                }
                else
                {
                    dest = instr.Rd.HasValue ? $"R{instr.Rd.Value}" : "-";
                    if (instr.Class == InstructionClass.ALUI) src2 = "-"; // Operanzii imediati nu sunt in registru
                }

                _instructions.Add(new ScoreboardInstruction {
                    Text = instr.RawText, Opcode = instr.Opcode.ToString(),
                    Dest = dest, Src1 = src1, Src2 = src2, Latency = latency
                });
            }

            _units.Add(new ScoreboardUnit { Id = "1", Name = "Load/Store" });
            _units.Add(new ScoreboardUnit { Id = "2", Name = "Multiplier" });
            _units.Add(new ScoreboardUnit { Id = "3", Name = "Adder1" });
            _units.Add(new ScoreboardUnit { Id = "4", Name = "Adder2" });

            for (int i = 0; i < 16; i++) _registerStatus[$"R{i}"] = "";

            rtbLog.Clear();
            if (_instructions.Count > 0)
                LogMessage("Scoreboard system loaded the main program successfully. Click on 'Next Step'.\n", Color.Blue);
            else
                LogMessage("No instructions to analyze. Please load a program first.\n", Color.DarkGray);
        }

        private void LogMessage(string message, Color color)
        {
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(message);
            rtbLog.SelectionColor = rtbLog.ForeColor;
        }

        private void BtnStep_Click(object sender, EventArgs e)
        {
            // Dacă s-a încercat o resetare sau dacă dinamic adăugările se fac acum, refacem colecția
            if (_instructions.Count == 0 && _mainForm != null && _mainForm.GetCurrentProgram().Count > 0)
            {
               InitializeSimulationData();
               UpdateUI();
            }

            if (_instructions.Count == 0)
            {
                MessageBox.Show("Please load a valid program in the main window before executing the Superscalar extension!", 
                                "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _currentCycle++;
            LogMessage($"\n--- CYCLE {_currentCycle} ---\n", Color.Black);

            // STADIUL 1: WRITE RESULT (WB)
            foreach (var unit in _units.Where(u => u.Busy == "Yes" && u.CurrentInstruction.Executed && !u.CurrentInstruction.Written))
            {
                var instr = unit.CurrentInstruction;
                bool warHazard = _units.Any(u => u.Busy == "Yes" && u != unit && 
                    ((u.Rs1 == instr.Dest && u.Rs1Ready == "Yes" && !u.CurrentInstruction.OperandsRead) || 
                     (u.Rs2 == instr.Dest && u.Rs2Ready == "Yes" && !u.CurrentInstruction.OperandsRead)));

                if (!warHazard)
                {
                    instr.Written = true;
                    instr.WB = _currentCycle.ToString();
                    unit.Busy = "No";
                    
                    if (instr.Dest != "-" && _registerStatus.ContainsKey(instr.Dest) && _registerStatus[instr.Dest] == unit.Name)
                        _registerStatus[instr.Dest] = "";

                    foreach (var u in _units.Where(f => f.Busy == "Yes"))
                    {
                        if (u.Rs1 == instr.Dest) u.Rs1Ready = "Yes";
                        if (u.Rs2 == instr.Dest) u.Rs2Ready = "Yes";
                    }

                    LogMessage($"[WB] Unit {unit.Name} released the result for: {instr.Text}\n", Color.DarkGreen);
                    unit.CurrentInstruction = null;
                    unit.Op = ""; unit.Dest = ""; unit.Rs1 = ""; unit.Rs2 = "";
                }
                else
                {
                    LogMessage($"[Hazard WAR] Write in {instr.Dest} blocked for {instr.Text}.\n", Color.DarkRed);
                }
            }

            // STADIUL 2: EXECUTE (EX)
            foreach (var unit in _units.Where(u => u.Busy == "Yes" && u.CurrentInstruction.OperandsRead && !u.CurrentInstruction.Executed))
            {
                var instr = unit.CurrentInstruction;
                instr.CyclesRemaining--;
                
                if (instr.CyclesRemaining <= 0)
                {
                    instr.Executed = true;
                    instr.EX = _currentCycle.ToString();
                    LogMessage($"[EX] {unit.Name} finished execution for: {instr.Text}\n", Color.Black);
                }
                else
                {
                    LogMessage($"[EX] {unit.Name} is working on {instr.Text} ({instr.CyclesRemaining} cycles remaining).\n", Color.Gray);
                }
            }

            // STADIUL 3: READ OPERANDS (RO)
            foreach (var unit in _units.Where(u => u.Busy == "Yes" && u.CurrentInstruction.Issued && !u.CurrentInstruction.OperandsRead))
            {
                if (unit.Rs1Ready == "Yes" && unit.Rs2Ready == "Yes")
                {
                    unit.CurrentInstruction.OperandsRead = true;
                    unit.CurrentInstruction.CyclesRemaining = unit.CurrentInstruction.Latency;
                    unit.CurrentInstruction.OF = _currentCycle.ToString();
                    LogMessage($"[RO] {unit.Name} read operands for: {unit.CurrentInstruction.Text}\n", Color.Black);
                }
                else
                {
                    LogMessage($"[Hazard RAW] {unit.Name} waits for data for {unit.CurrentInstruction.Text} (S1 ready: {unit.Rs1Ready}, S2 ready: {unit.Rs2Ready}).\n", Color.DarkRed);
                }
            }

            // STADIUL 4: ISSUE (Lansare in Executie)
            var nextToIssue = _instructions.FirstOrDefault(i => !i.Issued);
            if (nextToIssue != null)
            {
                ScoreboardUnit targetUnit = null;
                if (nextToIssue.Opcode == "LD" || nextToIssue.Opcode == "ST") targetUnit = _units.FirstOrDefault(u => u.Name == "Load/Store" && u.Busy == "No");
                else if (nextToIssue.Opcode == "MUL") targetUnit = _units.FirstOrDefault(u => u.Name == "Multiplier" && u.Busy == "No");
                else targetUnit = _units.FirstOrDefault(u => u.Name.StartsWith("Adder") && u.Busy == "No");

                if (targetUnit != null)
                {
                    bool wawHazard = nextToIssue.Dest != "-" && _registerStatus.ContainsKey(nextToIssue.Dest) && _registerStatus[nextToIssue.Dest] != "";

                    if (!wawHazard)
                    {
                        nextToIssue.Issued = true;
                        nextToIssue.IF = _currentCycle.ToString();
                        
                        targetUnit.Busy = "Yes";
                        targetUnit.Op = nextToIssue.Opcode;
                        targetUnit.Dest = nextToIssue.Dest;
                        targetUnit.Rs1 = nextToIssue.Src1;
                        targetUnit.Rs2 = nextToIssue.Src2;
                        targetUnit.CurrentInstruction = nextToIssue;

                        targetUnit.Rs1Ready = (nextToIssue.Src1 != "-" && _registerStatus.ContainsKey(nextToIssue.Src1) && _registerStatus[nextToIssue.Src1] != "") ? "No" : "Yes";
                        targetUnit.Rs2Ready = (nextToIssue.Src2 != "-" && _registerStatus.ContainsKey(nextToIssue.Src2) && _registerStatus[nextToIssue.Src2] != "") ? "No" : "Yes";

                        if (nextToIssue.Dest != "-") _registerStatus[nextToIssue.Dest] = targetUnit.Name;

                        LogMessage($"[ISSUE] Instruction {nextToIssue.Text} issued to {targetUnit.Name}.\n", Color.Blue);
                    }
                    else
                    {
                        LogMessage($"[Hazard WAW] Issuing {nextToIssue.Text} delayed. Register {nextToIssue.Dest} is reserved.\n", Color.DarkRed);
                    }
                }
                else
                {
                    LogMessage($"[Structural Hazard] No functional unit available for {nextToIssue.Text}.\n", Color.DarkRed);
                }
            }

            UpdateUI();

            if (_instructions.All(i => i.Written))
            {
                LogMessage("\n Success! All instructions were executed completely Out-of-Order.\n", Color.DarkGreen);
                btnStep.Enabled = false;
            }
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            InitializeSimulationData();
            UpdateUI();
            btnStep.Enabled = true;
        }

        private void UpdateUI()
        {
            lblClock.Text = $"Clock Cycle: {_currentCycle}";
            dgv1.Rows.Clear();
            foreach (var instr in _instructions) dgv1.Rows.Add(instr.Text, instr.IF, instr.OF, instr.EX, instr.WB);
            
            dgv2.Rows.Clear();
            foreach (var u in _units) dgv2.Rows.Add(u.Id, u.Name, u.Busy, u.Op, u.Dest, u.Rs1, u.Rs1Ready, u.Rs2, u.Rs2Ready);
            
            for (int i = 0; i < 16; i++)
            {
                dgv3.Rows[0].Cells[i].Value = _registerStatus.ContainsKey($"R{i}") ? _registerStatus[$"R{i}"] : "";
                dgv3.Rows[0].Cells[i].Style.BackColor = string.IsNullOrEmpty(dgv3.Rows[0].Cells[i].Value?.ToString()) ? Color.White : Color.FromArgb(0xFF, 0xEC, 0xB3);
            }
        }
    }

    public class ScoreboardInstruction
    {
        public string Text { get; set; }
        public string Opcode { get; set; }
        public string Dest { get; set; }
        public string Src1 { get; set; }
        public string Src2 { get; set; }
        public string IF { get; set; } = "";
        public string OF { get; set; } = "";
        public string EX { get; set; } = "";
        public string WB { get; set; } = "";
        public bool Issued { get; set; } = false;
        public bool OperandsRead { get; set; } = false;
        public bool Executed { get; set; } = false;
        public bool Written { get; set; } = false;
        public int Latency { get; set; }
        public int CyclesRemaining { get; set; }
    }

    public class ScoreboardUnit
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Busy { get; set; } = "No";
        public string Op { get; set; } = "";
        public string Dest { get; set; } = "";
        public string Rs1 { get; set; } = "";
        public string Rs1Ready { get; set; } = "Yes";
        public string Rs2 { get; set; } = "";
        public string Rs2Ready { get; set; } = "Yes";
        public ScoreboardInstruction CurrentInstruction { get; set; } = null;
    }
}