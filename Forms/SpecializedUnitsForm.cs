using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class SpecializedUnitsForm : Form
    {
        private readonly PipelineSimulator _simulator;
        private DataGridView _dgvGantt;
        private Label _lblStats;

        // Config controls
        private readonly Dictionary<FunctionalUnitType, NumericUpDown> _nudCount   = new Dictionary<FunctionalUnitType, NumericUpDown>();
        private readonly Dictionary<FunctionalUnitType, NumericUpDown> _nudLatency = new Dictionary<FunctionalUnitType, NumericUpDown>();
        private NumericUpDown _nudIssueWidth;
        private ComboBox _cmbExecModel;

        public SpecializedUnitsForm() : this(null) { }

        public SpecializedUnitsForm(PipelineSimulator simulator)
        {
            _simulator = simulator;
            Text = "Unități Funcționale Specializate";
            Size = new Size(1200, 750);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 600);

            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(BuildGanttTab());
            tc.TabPages.Add(BuildConfigTab());
            Controls.Add(tc);

            if (_simulator != null)
            {
                _simulator.CycleCompleted += OnCycleCompleted;
                FormClosed += (s, e) => _simulator.CycleCompleted -= OnCycleCompleted;
                Shown += (s, e) => RefreshGantt();
            }
        }

        private FunctionalUnitSet Fu => _simulator?.FunctionalUnits;

        // ── Gantt tab ──────────────────────────────────────────────────────────
        private TabPage BuildGanttTab()
        {
            var tab = new TabPage("Ocupare unități (Gantt)");

            _lblStats = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Font = new Font("Consolas", 8.5f),
                Padding = new Padding(6, 4, 6, 0)
            };

            _dgvGantt = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 22,
            };
            _dgvGantt.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Unitate", Width = 110, Frozen = true });

            tab.Controls.Add(_dgvGantt);
            tab.Controls.Add(_lblStats);
            return tab;
        }

        private void EnsureGanttColumns(int upToCycle)
        {
            if (upToCycle < _dgvGantt.Columns.Count) return;
            int addUpTo = ((upToCycle / 50) + 1) * 50;
            _dgvGantt.SuspendLayout();
            for (int i = _dgvGantt.Columns.Count; i <= addUpTo; i++)
                _dgvGantt.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = $"T{i}", Width = 38 });
            _dgvGantt.ResumeLayout();
        }

        private void RebuildGanttRows()
        {
            _dgvGantt.SuspendLayout();
            _dgvGantt.Rows.Clear();
            if (Fu == null) { _dgvGantt.ResumeLayout(); return; }
            foreach (var u in Fu.Units)
                _dgvGantt.Rows.Add($"{u.UnitType}-{u.UnitIndex}");
            _dgvGantt.ResumeLayout();
        }

        private void RefreshGantt()
        {
            if (Fu == null) return;

            // Rebuild rows if unit count changed
            var expectedRows = Fu.Units.Select(u => $"{u.UnitType}-{u.UnitIndex}").ToList();
            bool needRebuild = _dgvGantt.Rows.Count != expectedRows.Count;
            if (!needRebuild)
                for (int i = 0; i < expectedRows.Count; i++)
                    if ((string)_dgvGantt.Rows[i].Cells[0].Value != expectedRows[i])
                    { needRebuild = true; break; }
            if (needRebuild) RebuildGanttRows();

            // Paint occupancy log
            var log = Fu.OccupancyLog;
            if (log.Count == 0) goto stats;

            int maxCycle = log.Max(r => r.Cycle);
            EnsureGanttColumns(maxCycle);

            // Build row-index lookup
            var rowOf = new Dictionary<(FunctionalUnitType, int), int>();
            for (int i = 0; i < _dgvGantt.Rows.Count; i++)
            {
                var lbl = (string)_dgvGantt.Rows[i].Cells[0].Value ?? "";
                var parts = lbl.Split('-');
                if (parts.Length == 2 && Enum.TryParse(parts[0], out FunctionalUnitType t) && int.TryParse(parts[1], out int idx))
                    rowOf[(t, idx)] = i;
            }

            // Colour palette per program instruction index
            Color[] palette =
            {
                Color.FromArgb(0x42, 0x85, 0xF4), Color.FromArgb(0xEA, 0x43, 0x35),
                Color.FromArgb(0x34, 0xA8, 0x53), Color.FromArgb(0xFB, 0xBC, 0x04),
                Color.FromArgb(0x9C, 0x27, 0xB0), Color.FromArgb(0x00, 0x96, 0x88),
                Color.FromArgb(0xFF, 0x57, 0x22), Color.FromArgb(0x60, 0x7D, 0x8B),
            };

            foreach (var rec in log)
            {
                if (!rowOf.TryGetValue((rec.UnitType, rec.UnitIndex), out int rowIdx)) continue;
                if (rec.Cycle >= _dgvGantt.Columns.Count) continue;
                var cell = _dgvGantt.Rows[rowIdx].Cells[rec.Cycle];
                cell.Value = rec.InstructionLabel.Length > 12
                    ? rec.InstructionLabel.Substring(0, 11) + "…"
                    : rec.InstructionLabel;
                int pi = rec.InstructionProgIdx;
                Color bg = pi >= 0 ? palette[pi % palette.Length] : Color.Gray;
                cell.Style.BackColor = bg;
                cell.Style.ForeColor = Color.White;
                cell.Style.Font = rec.IsFirstCycle
                    ? new Font(_dgvGantt.DefaultCellStyle.Font, FontStyle.Bold)
                    : _dgvGantt.DefaultCellStyle.Font;
            }

            stats:
            if (_simulator != null)
            {
                int iw = _simulator.IssueWidth;
                _lblStats.Text =
                    $"IssueWidth: {iw} ({(iw > 1 ? "superscalar" : "scalar")})  |  " +
                    $"Stall-uri structurale: {_simulator.FuStructuralStallCycles} cicli  |  " +
                    $"EX extra (multi-ciclu): {_simulator.FuMultiCycleExtraCycles} cicli  |  " +
                    $"Total cicli: {_simulator.ClockCycle}  |  " +
                    $"In-flight EX: {_simulator.InFlightEX.Count}";
            }
        }

        // ── Config tab ─────────────────────────────────────────────────────────
        private TabPage BuildConfigTab()
        {
            var tab = new TabPage("Configurare unități");
            var outer = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            var grp = new GroupBox
            {
                Text = "Parametri unități funcționale",
                Left = 8, Top = 8, Width = 640, Height = 340,
                Padding = new Padding(12)
            };

            // Execution model
            var grpEM = new GroupBox { Text = "Model de execuție", Left = 8, Top = 8, Width = 800, Height = 72 };
            MakeLabel(grpEM, "Algoritm:", 12, 26);
            _cmbExecModel = new ComboBox { Left = 100, Top = 22, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbExecModel.Items.AddRange(new object[] { "InOrder (in-ordine)", "Scoreboard (tabela de marcaj)", "Tomasulo (stații de rezervare)" });
            _cmbExecModel.SelectedIndex = (int)(_simulator?.ExecutionModel ?? ExecutionModel.InOrder);
            grpEM.Controls.Add(_cmbExecModel);
            grpEM.Controls.Add(new Label { Text = "Scoreboard=OoO execuție cu WAW/WAR  |  Tomasulo=OoO cu RAT+CDB (fără WAW/WAR)", Left = 312, Top = 26, AutoSize = true, ForeColor = Color.Gray, Font = new Font(Font.FontFamily, 7) });

            // Issue width
            var grpIW = new GroupBox { Text = "Superscalaritate — lățime emisie (IssueWidth)", Left = 8, Top = 86, Width = 640, Height = 68 };
            MakeLabel(grpIW, "Instrucțiuni emise per ciclu:", 12, 24);
            int curIW = _simulator?.IssueWidth ?? 1;
            _nudIssueWidth = new NumericUpDown { Left = 230, Top = 20, Width = 70, Minimum = 1, Maximum = 4, Value = curIW };
            grpIW.Controls.Add(_nudIssueWidth);
            grpIW.Controls.Add(new Label { Text = "(1 = scalar, 2-4 = superscalar)", Left = 310, Top = 24, AutoSize = true, ForeColor = Color.Gray });

            grp.Top = 162;
            grp.Height = 340;

            // Header
            MakeLabel(grp, "Unitate", 20,  20, bold: true);
            MakeLabel(grp, "Nr. unități", 180, 20, bold: true);
            MakeLabel(grp, "Latență (cicli)", 340, 20, bold: true);

            var types = new[]
            {
                (FunctionalUnitType.ADD,    "ADD / ALU",   "Operații aritmetice, logice, MOV"),
                (FunctionalUnitType.MUL,    "MUL",         "Multiplicare — implicit 3 cicli"),
                (FunctionalUnitType.LD_ST,  "LD / ST",     "Load/Store (penalizare cache se adaugă separat)"),
                (FunctionalUnitType.BRANCH, "BRANCH / JMP","Salturi condiționate și neconditionate"),
            };

            int row = 50;
            foreach (var (t, name, hint) in types)
            {
                int curCount   = Fu?.Configs[t].Count   ?? 1;
                int curLatency = Fu?.Configs[t].Latency ?? 1;

                MakeLabel(grp, name, 20, row);
                grp.Controls.Add(new Label { Text = hint, Left = 20, Top = row + 18, Width = 140, AutoSize = false, Font = new Font(Font.FontFamily, 7), ForeColor = Color.Gray });

                var nudC = new NumericUpDown { Left = 180, Top = row, Width = 70, Minimum = 1, Maximum = 8, Value = curCount };
                grp.Controls.Add(nudC);
                _nudCount[t] = nudC;

                var nudL = new NumericUpDown { Left = 340, Top = row, Width = 70, Minimum = 1, Maximum = 100, Value = curLatency };
                grp.Controls.Add(nudL);
                _nudLatency[t] = nudL;

                row += 62;
            }

            var btnApply = new Button
            {
                Text = "Aplică configurare",
                Left = 20, Top = row + 8, Width = 160, Height = 32,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2), ForeColor = Color.White
            };
            btnApply.Click += BtnApply_Click;
            grp.Controls.Add(btnApply);

            grp.Controls.Add(new Label
            {
                Text = "Notă: modificările se aplică la următoarea rulare (Reset + Run).\n" +
                       "Latența > 1 inserează cicli EX+ în diagrama spațiu-timp (portocaliu închis).\n" +
                       "Mai multe unități de același tip reduc hazardul structural.",
                Left = 200, Top = row + 14, Width = 420, AutoSize = false, Height = 54,
                ForeColor = Color.Gray
            });

            outer.Controls.Add(grp);
            outer.Controls.Add(grpIW);
            outer.Controls.Add(grpEM);

            // Legend
            var grpLeg = new GroupBox { Text = "Legendă diagrama spațiu-timp", Left = 8, Top = 510, Width = 800, Height = 120, Padding = new Padding(12) };
            AddLegendCell(grpLeg, "EX",  Color.Orange,                   Color.Black, "EX 1 ciclu (ADD/LD/BR)",  10, 20);
            AddLegendCell(grpLeg, "EX+", Color.FromArgb(230, 126, 34),   Color.White, "EX extra cicli (MUL...)",  210, 20);
            AddLegendCell(grpLeg, "S",   Color.FromArgb(255, 100, 100),  Color.White, "Stall hazard RAW/Struct",  410, 20);
            AddLegendCell(grpLeg, "CS",  Color.FromArgb(255, 165, 0),    Color.White, "Cache stall",              610, 20);
            AddLegendCell(grpLeg, "IS",  Color.FromArgb(0x29, 0xB6, 0xF6), Color.White, "Issued (Scoreboard/Tom)", 10, 52);
            AddLegendCell(grpLeg, "WAW", Color.FromArgb(0xC0, 0x39, 0x2B), Color.White, "WAW stall (Scoreboard)", 210, 52);
            AddLegendCell(grpLeg, "WAR", Color.FromArgb(0xD3, 0x54, 0x00), Color.White, "WAR stall (Scoreboard)", 410, 52);
            outer.Controls.Add(grpLeg);

            tab.Controls.Add(outer);
            return tab;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (Fu == null) return;
            foreach (var t in _nudCount.Keys)
                Fu.Configure(t, (int)_nudCount[t].Value, (int)_nudLatency[t].Value);
            if (_simulator != null && _nudIssueWidth != null)
                _simulator.IssueWidth = (int)_nudIssueWidth.Value;
            if (_simulator != null && _cmbExecModel != null)
                _simulator.ExecutionModel = (ExecutionModel)_cmbExecModel.SelectedIndex;
            RebuildGanttRows();
            MessageBox.Show(
                "Configurarea a fost aplicată.\n" +
                "Model execuție: " + (_simulator?.ExecutionModel.ToString() ?? "InOrder") + "\n" +
                "IssueWidth = " + (_simulator?.IssueWidth ?? 1) + ((_simulator?.IssueWidth ?? 1) > 1 ? " (superscalar)" : " (scalar)") + "\n" +
                "Resetează și rulează programul din nou pentru a vedea efectele.",
                "Aplicat", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnCycleCompleted(PipelineState state)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { Invoke(new Action(RefreshGantt)); } catch { } return; }
            RefreshGantt();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static Label MakeLabel(Control parent, string text, int x, int y, bool bold = false)
        {
            var lbl = new Label { Text = text, Left = x, Top = y, AutoSize = true };
            if (bold) lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static void AddLegendCell(Control parent, string cell, Color bg, Color fg, string desc, int x, int y)
        {
            var pnl = new Panel { Left = x, Top = y, Width = 38, Height = 20, BackColor = bg };
            pnl.Controls.Add(new Label { Text = cell, ForeColor = fg, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Font = new Font("Consolas", 7.5f, FontStyle.Bold) });
            parent.Controls.Add(pnl);
            parent.Controls.Add(new Label { Text = desc, Left = x + 42, Top = y + 2, AutoSize = true, Font = new Font(parent.Font.FontFamily, 7.5f) });
        }
    }
}
