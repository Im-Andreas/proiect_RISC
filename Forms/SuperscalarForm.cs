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
        private readonly PipelineSimulator _sim;

        // Scoreboard tab
        private DataGridView _dgvSbFUs;
        private DataGridView _dgvSbQi;
        private Label _lblSbStatus;

        // Tomasulo tab
        private DataGridView _dgvTomRS;
        private DataGridView _dgvTomRAT;
        private Label _lblTomStatus;

        // Issue Buffer tab
        private DataGridView _dgvIssue;
        private Label _lblIssueStatus;

        public SuperscalarForm() : this(null, 0) { }
        public SuperscalarForm(PipelineSimulator simulator, int initialTab = 0)
        {
            _sim = simulator;
            Text = "Superscalar Execution State";
            Size = new Size(1000, 560);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(800, 460);

            var tc = new TabControl { Dock = DockStyle.Fill };
            tc.TabPages.Add(BuildScoreboardTab());
            tc.TabPages.Add(BuildTomasuloTab());
            tc.TabPages.Add(BuildIssueBufferTab());
            tc.SelectedIndex = Math.Min(initialTab, 2);
            Controls.Add(tc);

            if (_sim != null)
            {
                _sim.CycleCompleted += OnCycle;
                FormClosed += (s, e) => _sim.CycleCompleted -= OnCycle;
                Shown += (s, e) => Refresh_All();
            }
        }

        // ── Event ──────────────────────────────────────────────────────────────
        private void OnCycle(PipelineState _)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { Invoke(new Action(Refresh_All)); } catch { } return; }
            Refresh_All();
        }

        private void Refresh_All()
        {
            RefreshScoreboard();
            RefreshTomasulo();
            RefreshIssueBuffer();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TAB 1 — SCOREBOARD (tabela de marcaj)
        // ══════════════════════════════════════════════════════════════════════
        private TabPage BuildScoreboardTab()
        {
            var tab = new TabPage("Scoreboard (tabela de marcaj)");
            var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            _lblSbStatus = new Label
            {
                Dock = DockStyle.Top, Height = 28,
                Font = new Font("Consolas", 8.5f),
                Padding = new Padding(6, 6, 6, 0),
                Text = "Model: —   Ciclu: —"
            };

            // FU state table
            var lblFU = new Label { Text = "Stare unități funcționale:", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), Padding = new Padding(6, 2, 0, 0) };

            _dgvSbFUs = MakeGrid(new[] { "Unitate", "Stage", "Op", "Fi", "Fj", "Qj", "Rj", "Fk", "Qk", "Rk", "CycLeft" });
            _dgvSbFUs.Dock = DockStyle.Top;
            _dgvSbFUs.Height = 180;
            _dgvSbFUs.Columns["Unitate"].Width = 90;
            _dgvSbFUs.Columns["Stage"].Width = 45;
            _dgvSbFUs.Columns["Op"].Width = 80;
            foreach (var c in new[] { "Fi", "Fj", "Fk" }) _dgvSbFUs.Columns[c].Width = 38;
            foreach (var c in new[] { "Qj", "Qk" }) _dgvSbFUs.Columns[c].Width = 70;
            foreach (var c in new[] { "Rj", "Rk" }) _dgvSbFUs.Columns[c].Width = 35;
            _dgvSbFUs.Columns["CycLeft"].Width = 55;

            // Qi register table
            var lblQi = new Label { Text = "Qi — registru → unitate producătoare:", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };

            _dgvSbQi = MakeGrid(Enumerable.Range(0, 16).Select(i => $"R{i}").ToArray());
            _dgvSbQi.Dock = DockStyle.Top;
            _dgvSbQi.Height = 52;
            foreach (DataGridViewColumn c in _dgvSbQi.Columns) c.Width = 48;
            _dgvSbQi.Rows.Add();

            var legend = new Label
            {
                Dock = DockStyle.Top, Height = 42,
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.DimGray,
                Padding = new Padding(6, 2, 0, 0),
                Text = "Stage: RO=Read Operands (așteptare), EX=Execute, WR=Write Result\n" +
                       "Rj/Rk: true=operand disponibil (necitit); false=deja citit (protecție WAR)\n" +
                       "Qj/Qk: gol=registru gata; altfel=FU producător"
            };

            // Stack controls (pnl is AutoScroll, so Top-docked controls stack)
            pnl.Controls.Add(legend);
            pnl.Controls.Add(_dgvSbQi);
            pnl.Controls.Add(lblQi);
            pnl.Controls.Add(_dgvSbFUs);
            pnl.Controls.Add(lblFU);
            pnl.Controls.Add(_lblSbStatus);

            tab.Controls.Add(pnl);
            return tab;
        }

        private void RefreshScoreboard()
        {
            if (_sim == null) return;
            _lblSbStatus.Text = $"Model: {_sim.ExecutionModel}   Ciclu: {_sim.ClockCycle}   FUs active: {_sim.ScoreboardFUs.Count}";

            _dgvSbFUs.SuspendLayout();
            _dgvSbFUs.Rows.Clear();
            foreach (var fu in _sim.ScoreboardFUs)
            {
                int row = _dgvSbFUs.Rows.Add(
                    fu.Name,
                    fu.Stage.ToString(),
                    fu.Instr?.Opcode.ToString() ?? "—",
                    fu.Fi >= 0 ? $"R{fu.Fi}" : "—",
                    fu.Fj >= 0 ? $"R{fu.Fj}" : "—",
                    fu.Qj.Length > 0 ? fu.Qj : "✓",
                    fu.Rj ? "✓" : "✗",
                    fu.Fk >= 0 ? $"R{fu.Fk}" : "—",
                    fu.Qk.Length > 0 ? fu.Qk : "✓",
                    fu.Rk ? "✓" : "✗",
                    fu.CyclesLeft);

                // Colour by stage
                Color bg = fu.Stage == SbStage.RO ? Color.FromArgb(0x29, 0xB6, 0xF6) :
                           fu.Stage == SbStage.EX ? Color.Orange :
                                                     Color.FromArgb(0x4C, 0xAF, 0x50);
                _dgvSbFUs.Rows[row].DefaultCellStyle.BackColor = bg;
                _dgvSbFUs.Rows[row].DefaultCellStyle.ForeColor = Color.White;
                // Highlight not-ready operands
                if (!fu.Rj || fu.Qj.Length > 0) { _dgvSbFUs.Rows[row].Cells["Qj"].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02); _dgvSbFUs.Rows[row].Cells["Qj"].Style.ForeColor = Color.Black; }
                if (!fu.Rk || fu.Qk.Length > 0) { _dgvSbFUs.Rows[row].Cells["Qk"].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02); _dgvSbFUs.Rows[row].Cells["Qk"].Style.ForeColor = Color.Black; }
            }
            _dgvSbFUs.ResumeLayout();

            // Qi table
            if (_dgvSbQi.Rows.Count == 0) _dgvSbQi.Rows.Add();
            for (int r = 0; r < 16; r++)
            {
                if (_sim.ScoreboardQi.TryGetValue(r, out string fuName))
                {
                    _dgvSbQi.Rows[0].Cells[r].Value = fuName;
                    _dgvSbQi.Rows[0].Cells[r].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02);
                    _dgvSbQi.Rows[0].Cells[r].Style.ForeColor = Color.Black;
                }
                else
                {
                    _dgvSbQi.Rows[0].Cells[r].Value = "";
                    _dgvSbQi.Rows[0].Cells[r].Style.BackColor = _dgvSbQi.DefaultCellStyle.BackColor;
                    _dgvSbQi.Rows[0].Cells[r].Style.ForeColor = _dgvSbQi.DefaultCellStyle.ForeColor;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TAB 2 — TOMASULO (stații de rezervare + RAT)
        // ══════════════════════════════════════════════════════════════════════
        private TabPage BuildTomasuloTab()
        {
            var tab = new TabPage("Tomasulo (RS + RAT)");
            var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            _lblTomStatus = new Label
            {
                Dock = DockStyle.Top, Height = 28,
                Font = new Font("Consolas", 8.5f),
                Padding = new Padding(6, 6, 6, 0),
                Text = "Model: —   Ciclu: —"
            };

            var lblRS = new Label { Text = "Stații de rezervare (Reservation Stations):", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), Padding = new Padding(6, 2, 0, 0) };

            _dgvTomRS = MakeGrid(new[] { "Tag", "Busy", "Op", "Vj", "Qj", "Vk", "Qk", "Dest", "Disp", "CycLeft" });
            _dgvTomRS.Dock = DockStyle.Top;
            _dgvTomRS.Height = 230;
            _dgvTomRS.Columns["Tag"].Width = 80;
            _dgvTomRS.Columns["Busy"].Width = 40;
            _dgvTomRS.Columns["Op"].Width = 70;
            _dgvTomRS.Columns["Vj"].Width = 80;
            _dgvTomRS.Columns["Qj"].Width = 80;
            _dgvTomRS.Columns["Vk"].Width = 80;
            _dgvTomRS.Columns["Qk"].Width = 80;
            _dgvTomRS.Columns["Dest"].Width = 50;
            _dgvTomRS.Columns["Disp"].Width = 40;
            _dgvTomRS.Columns["CycLeft"].Width = 55;

            var lblRAT = new Label { Text = "RAT — Register Alias Table (redenumire registre):", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), Padding = new Padding(6, 4, 0, 0) };

            _dgvTomRAT = MakeGrid(Enumerable.Range(0, 16).Select(i => $"R{i}").ToArray());
            _dgvTomRAT.Dock = DockStyle.Top;
            _dgvTomRAT.Height = 52;
            foreach (DataGridViewColumn c in _dgvTomRAT.Columns) c.Width = 48;
            _dgvTomRAT.Rows.Add();

            var legend = new Label
            {
                Dock = DockStyle.Top, Height = 32,
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.DimGray,
                Padding = new Padding(6, 2, 0, 0),
                Text = "Vj/Vk: valoarea operandului (dacă e disponibil)\n" +
                       "Qj/Qk: tag-ul RS producător (gol=operand gata)  |  Disp=trimis la unitate funcțională"
            };

            pnl.Controls.Add(legend);
            pnl.Controls.Add(_dgvTomRAT);
            pnl.Controls.Add(lblRAT);
            pnl.Controls.Add(_dgvTomRS);
            pnl.Controls.Add(lblRS);
            pnl.Controls.Add(_lblTomStatus);

            tab.Controls.Add(pnl);
            return tab;
        }

        private void RefreshTomasulo()
        {
            if (_sim == null) return;
            int busyCount = _sim.TomasuloRS.Count(r => r.Busy);
            _lblTomStatus.Text = $"Model: {_sim.ExecutionModel}   Ciclu: {_sim.ClockCycle}   RS ocupate: {busyCount}/{_sim.TomasuloRS.Count}";

            _dgvTomRS.SuspendLayout();
            _dgvTomRS.Rows.Clear();
            foreach (var rs in _sim.TomasuloRS)
            {
                int row = _dgvTomRS.Rows.Add(
                    rs.Tag,
                    rs.Busy ? "✓" : "",
                    rs.Busy ? rs.Op.ToString() : "",
                    rs.Busy ? (rs.Vj.HasValue ? rs.Vj.Value.ToString() : "?") : "",
                    rs.Busy ? (rs.Qj.Length > 0 ? rs.Qj : "✓") : "",
                    rs.Busy ? (rs.Vk.HasValue ? rs.Vk.Value.ToString() : "?") : "",
                    rs.Busy ? (rs.Qk.Length > 0 ? rs.Qk : "✓") : "",
                    rs.Busy && rs.DestReg >= 0 ? $"R{rs.DestReg}" : "",
                    rs.Dispatched ? "✓" : "",
                    rs.Busy ? rs.CyclesLeft.ToString() : "");

                if (!rs.Busy) continue;
                Color bg = !rs.Dispatched ? Color.FromArgb(0x29, 0xB6, 0xF6) : // waiting → cyan
                           rs.CyclesLeft > 0 ? Color.Orange :                    // executing → orange
                                               Color.FromArgb(0x4C, 0xAF, 0x50); // done → green
                _dgvTomRS.Rows[row].DefaultCellStyle.BackColor = bg;
                _dgvTomRS.Rows[row].DefaultCellStyle.ForeColor = Color.White;
                // Highlight pending operands
                if (rs.Qj.Length > 0) { _dgvTomRS.Rows[row].Cells["Qj"].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02); _dgvTomRS.Rows[row].Cells["Qj"].Style.ForeColor = Color.Black; }
                if (rs.Qk.Length > 0) { _dgvTomRS.Rows[row].Cells["Qk"].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02); _dgvTomRS.Rows[row].Cells["Qk"].Style.ForeColor = Color.Black; }
            }
            _dgvTomRS.ResumeLayout();

            // RAT table
            if (_dgvTomRAT.Rows.Count == 0) _dgvTomRAT.Rows.Add();
            for (int r = 0; r < 16; r++)
            {
                if (_sim.TomasuloRAT.TryGetValue(r, out string tag))
                {
                    _dgvTomRAT.Rows[0].Cells[r].Value = tag;
                    _dgvTomRAT.Rows[0].Cells[r].Style.BackColor = Color.FromArgb(0xFF, 0xCC, 0x02);
                    _dgvTomRAT.Rows[0].Cells[r].Style.ForeColor = Color.Black;
                }
                else
                {
                    _dgvTomRAT.Rows[0].Cells[r].Value = "";
                    _dgvTomRAT.Rows[0].Cells[r].Style.BackColor = _dgvTomRAT.DefaultCellStyle.BackColor;
                    _dgvTomRAT.Rows[0].Cells[r].Style.ForeColor = _dgvTomRAT.DefaultCellStyle.ForeColor;
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  TAB 3 — ISSUE BUFFER (buffer de prefetch / emisie)
        // ══════════════════════════════════════════════════════════════════════
        private TabPage BuildIssueBufferTab()
        {
            var tab = new TabPage("Issue Buffer (prefetch)");
            var pnl = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

            _lblIssueStatus = new Label
            {
                Dock = DockStyle.Top, Height = 28,
                Font = new Font("Consolas", 8.5f),
                Padding = new Padding(6, 6, 6, 0),
                Text = "Buffer emisie: —   IssueWidth: —"
            };

            var lblB = new Label { Text = "Instrucțiuni în buffer-ul de emisie (decode → dispatch):", Dock = DockStyle.Top, Height = 20, Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold), Padding = new Padding(6, 2, 0, 0) };

            _dgvIssue = MakeGrid(new[] { "Slot", "Instrucțiune", "Rs1", "Rs2", "Rd", "Adresă", "Clasă" });
            _dgvIssue.Dock = DockStyle.Top;
            _dgvIssue.Height = 200;
            _dgvIssue.Columns["Slot"].Width = 40;
            _dgvIssue.Columns["Instrucțiune"].Width = 200;
            _dgvIssue.Columns["Rs1"].Width = 45;
            _dgvIssue.Columns["Rs2"].Width = 45;
            _dgvIssue.Columns["Rd"].Width = 40;
            _dgvIssue.Columns["Adresă"].Width = 70;
            _dgvIssue.Columns["Clasă"].Width = 70;

            var explain = new Label
            {
                Dock = DockStyle.Top, Height = 56,
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.DimGray,
                Padding = new Padding(6, 4, 0, 0),
                Text = "Buffer-ul de emisie conține instrucțiuni decodate, gata să fie trimise la unități funcționale.\n" +
                       "InOrder: emitere în ordine (prima instrucțiune cu hazard blochează emiterea).\n" +
                       "Scoreboard/Tomasulo: emitere în ordine la RS/FU, execuție out-of-order.\n" +
                       "IssueWidth = numărul maxim de instrucțiuni emise simultan per ciclu (superscalar)."
            };

            pnl.Controls.Add(explain);
            pnl.Controls.Add(_dgvIssue);
            pnl.Controls.Add(lblB);
            pnl.Controls.Add(_lblIssueStatus);

            tab.Controls.Add(pnl);
            return tab;
        }

        private void RefreshIssueBuffer()
        {
            if (_sim == null) return;
            var buf = _sim.IssueBuffer;
            _lblIssueStatus.Text = $"Buffer emisie: {buf.Count} instrucțiuni   |   IssueWidth: {_sim.IssueWidth}   |   Model: {_sim.ExecutionModel}   |   Ciclu: {_sim.ClockCycle}";

            _dgvIssue.SuspendLayout();
            _dgvIssue.Rows.Clear();
            for (int i = 0; i < buf.Count; i++)
            {
                var instr = buf[i];
                int row = _dgvIssue.Rows.Add(
                    i,
                    instr.ToShortString(),
                    instr.Rs1.HasValue ? $"R{instr.Rs1}" : "—",
                    instr.Rs2.HasValue ? $"R{instr.Rs2}" : "—",
                    instr.Rd.HasValue  ? $"R{instr.Rd}"  : "—",
                    $"0x{instr.Address:X4}",
                    instr.Class.ToString());
                _dgvIssue.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(0xE3, 0xF2, 0xFD);
            }
            _dgvIssue.ResumeLayout();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static DataGridView MakeGrid(string[] columns)
        {
            var dgv = new DataGridView
            {
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 22,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            };
            foreach (var col in columns)
                dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = col, HeaderText = col, SortMode = DataGridViewColumnSortMode.NotSortable });
            return dgv;
        }
    }
}
