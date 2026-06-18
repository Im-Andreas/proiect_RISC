using System;
using System.Drawing;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class CacheForm : Form
    {
        private readonly CacheBlackBox _cache = new CacheBlackBox();

        private DataGridView _dgvLog;
        private TextBox _txtAddress;
        private Label _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate;

        public CacheForm()
        {
            this.Text = "Cache Memory Hierarchy";
            this.Size = new Size(1200, 750);

            var tc = new TabControl { Dock = DockStyle.Fill };

            var tabBlackBox = new TabPage("Cache Overview (Black-Box)");
            BuildBlackBoxTab(tabBlackBox);
            tc.TabPages.Add(tabBlackBox);

            tc.TabPages.Add(new TabPage("Cache Configuration & Internals"));
            tc.TabPages.Add(new TabPage("Write Policies & LRU Algorithms"));

            this.Controls.Add(tc);
        }

        private void BuildBlackBoxTab(TabPage tab)
        {
            // ---- Left panel: input ----
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(12) };

            var lblTitle = new Label
            {
                Text = "Memory Access (Black-Box)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Top = 12,
                Left = 12
            };

            var lblHelp = new Label
            {
                Text = "Enter an address (decimal or 0x...) and press Access.\n" +
                       "First time you see an address = MISS.\n" +
                       "Next time the same address = HIT.",
                AutoSize = true,
                MaximumSize = new Size(270, 0),
                Top = 45,
                Left = 12,
                ForeColor = Color.DimGray
            };

            _txtAddress = new TextBox { Top = 110, Left = 12, Width = 160, Text = "0x0100" };

            var btnAccess = new Button
            {
                Text = "Access",
                Top = 108,
                Left = 180,
                Width = 90,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnAccess.Click += (s, e) => DoAccess();
            _txtAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoAccess(); e.SuppressKeyPress = true; } };

            var lblSeq = new Label { Text = "Or a sequence (comma/space separated):", AutoSize = true, Top = 150, Left = 12 };
            var txtSeq = new TextBox { Top = 172, Left = 12, Width = 258, Text = "0x100, 0x104, 0x100, 0x108, 0x104" };
            var btnRunSeq = new Button { Text = "Run sequence", Top = 200, Left = 12, Width = 160 };
            btnRunSeq.Click += (s, e) => RunSequence(txtSeq.Text);

            var btnReset = new Button { Text = "Reset", Top = 200, Left = 180, Width = 90 };
            btnReset.Click += (s, e) => { _cache.Reset(); RefreshUI(); };

            pnlLeft.Controls.AddRange(new Control[]
            { lblTitle, lblHelp, _txtAddress, btnAccess, lblSeq, txtSeq, btnRunSeq, btnReset });

            // ---- Bottom panel: statistics ----
            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12, 8, 12, 8) };
            _lblTotal = MakeStat("Accesses: 0", 12);
            _lblHits = MakeStat("Hits: 0", 160);
            _lblMisses = MakeStat("Misses: 0", 300);
            _lblHitRate = MakeStat("Hit rate: 0%", 450);
            _lblMissRate = MakeStat("Miss rate: 0%", 640);
            pnlStats.Controls.AddRange(new Control[] { _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate });

            // ---- Center: access table ----
            _dgvLog = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvLog.Columns.Add("Nr", "#");
            _dgvLog.Columns.Add("Addr", "Address");
            _dgvLog.Columns.Add("Result", "Result");

            tab.Controls.Add(_dgvLog);   // fill first, takes the rest
            tab.Controls.Add(pnlStats);  // then bottom
            tab.Controls.Add(pnlLeft);   // then left
        }

        private Label MakeStat(string text, int left) => new Label
        {
            Text = text,
            AutoSize = true,
            Top = 16,
            Left = left,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        private void DoAccess()
        {
            if (TryParseAddress(_txtAddress.Text, out uint addr))
            {
                _cache.Access(addr);
                RefreshUI();
            }
            else
            {
                MessageBox.Show("Invalid address. Use decimal (256) or hex (0x100).",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunSequence(string raw)
        {
            var tokens = raw.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var t in tokens)
                if (TryParseAddress(t, out uint addr))
                    _cache.Access(addr);
            RefreshUI();
        }

        private bool TryParseAddress(string text, out uint address)
        {
            text = text.Trim();
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    address = Convert.ToUInt32(text.Substring(2), 16);
                else
                    address = Convert.ToUInt32(text, 10);
                return true;
            }
            catch { address = 0; return false; }
        }

        private void RefreshUI()
        {
            // table
            _dgvLog.Rows.Clear();
            foreach (var a in _cache.AccessLog)
            {
                int rowIdx = _dgvLog.Rows.Add(a.Index, $"0x{a.Address:X4}", a.IsHit ? "HIT" : "MISS");
                _dgvLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9)   // light green
                            : Color.FromArgb(0xFF, 0xCD, 0xD2);  // light red
            }
            if (_dgvLog.Rows.Count > 0)
                _dgvLog.FirstDisplayedScrollingRowIndex = _dgvLog.Rows.Count - 1;

            // statistics
            _lblTotal.Text = $"Accesses: {_cache.TotalAccesses}";
            _lblHits.Text = $"Hits: {_cache.Hits}";
            _lblMisses.Text = $"Misses: {_cache.Misses}";
            _lblHitRate.Text = $"Hit rate: {_cache.HitRate:P1}";
            _lblMissRate.Text = $"Miss rate: {_cache.MissRate:P1}";
        }
    }
}