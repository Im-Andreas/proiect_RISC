using System;
using System.Drawing;
using System.Windows.Forms;
using proiect_RISC.Models;

namespace proiect_RISC.Forms
{
    public class CacheForm : Form
    {
        private readonly CacheBlackBox _cache = new CacheBlackBox();
        private readonly SetAssociativeCache _saCache = new SetAssociativeCache(4, 2, 16); // default: 4 sets, 2-way, 16B line

        private DataGridView _dgvLog;
        private TextBox _txtAddress;
        private Label _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate;

        private NumericUpDown _numSets, _numWays, _numBlockSize;
        private TextBox _txtSaAddress;
        private DataGridView _dgvSaLog;
        private DataGridView _dgvSaContents;
        private Label _lblSaTotal, _lblSaHits, _lblSaMisses, _lblSaHitRate;

        public CacheForm()
        {
            this.Text = "Cache Memory Hierarchy";
            this.Size = new Size(1200, 750);

            var tc = new TabControl { Dock = DockStyle.Fill };

            var tabBlackBox = new TabPage("Cache Overview (Black-Box)");
            BuildBlackBoxTab(tabBlackBox);
            tc.TabPages.Add(tabBlackBox);

            var tabSetAssoc = new TabPage("Cache Configuration & Internals");
            BuildSetAssociativeTab(tabSetAssoc);
            tc.TabPages.Add(tabSetAssoc);

            tc.TabPages.Add(new TabPage("Write Policies & LRU Algorithms"));

            this.Controls.Add(tc);
        }

        private void BuildBlackBoxTab(TabPage tab)
        {
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

            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12, 8, 12, 8) };
            _lblTotal = MakeStat("Accesses: 0", 12);
            _lblHits = MakeStat("Hits: 0", 160);
            _lblMisses = MakeStat("Misses: 0", 300);
            _lblHitRate = MakeStat("Hit rate: 0%", 450);
            _lblMissRate = MakeStat("Miss rate: 0%", 640);
            pnlStats.Controls.AddRange(new Control[] { _lblTotal, _lblHits, _lblMisses, _lblHitRate, _lblMissRate });

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

        private void BuildSetAssociativeTab(TabPage tab)
        {
            var pnlLeft = new Panel { Dock = DockStyle.Left, Width = 300, Padding = new Padding(12) };

            var lblCfgTitle = new Label
            {
                Text = "Cache Parameters (Set-Associative)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Top = 12,
                Left = 12
            };

            var lblSets = new Label { Text = "Number of sets (power of 2):", AutoSize = true, Top = 45, Left = 12 };
            _numSets = new NumericUpDown { Top = 65, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 4 };

            var lblWays = new Label { Text = "Associativity (ways):", AutoSize = true, Top = 95, Left = 12 };
            _numWays = new NumericUpDown { Top = 115, Left = 12, Width = 100, Minimum = 1, Maximum = 16, Value = 2 };

            var lblBlock = new Label { Text = "Block size (bytes, power of 2):", AutoSize = true, Top = 145, Left = 12 };
            _numBlockSize = new NumericUpDown { Top = 165, Left = 12, Width = 100, Minimum = 1, Maximum = 1024, Value = 16 };

            var btnApply = new Button { Text = "Apply Parameters (Reset)", Top = 195, Left = 12, Width = 258 };
            btnApply.Click += (s, e) => ApplySaConfig();

            var lblAccess = new Label { Text = "Address to access:", AutoSize = true, Top = 235, Left = 12 };
            _txtSaAddress = new TextBox { Top = 255, Left = 12, Width = 160, Text = "0x0100" };
            var btnSaAccess = new Button
            {
                Text = "Access",
                Top = 253,
                Left = 180,
                Width = 90,
                BackColor = Color.FromArgb(0x19, 0x76, 0xD2),
                ForeColor = Color.White
            };
            btnSaAccess.Click += (s, e) => DoSaAccess();
            _txtSaAddress.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { DoSaAccess(); e.SuppressKeyPress = true; } };

            var btnSaReset = new Button { Text = "Reset Statistics", Top = 290, Left = 12, Width = 258 };
            btnSaReset.Click += (s, e) => { _saCache.Reset(); RefreshSaUI(); };

            pnlLeft.Controls.AddRange(new Control[]
            {
                lblCfgTitle, lblSets, _numSets, lblWays, _numWays, lblBlock, _numBlockSize,
                btnApply, lblAccess, _txtSaAddress, btnSaAccess, btnSaReset
            });

            var pnlStats = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(12, 8, 12, 8) };
            _lblSaTotal = MakeStat("Accesses: 0", 12);
            _lblSaHits = MakeStat("Hits: 0", 160);
            _lblSaMisses = MakeStat("Misses: 0", 300);
            _lblSaHitRate = MakeStat("Hit rate: 0%", 450);
            pnlStats.Controls.AddRange(new Control[] { _lblSaTotal, _lblSaHits, _lblSaMisses, _lblSaHitRate });

            _dgvSaLog = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 260,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvSaLog.Columns.Add("Nr", "#");
            _dgvSaLog.Columns.Add("Addr", "Address");
            _dgvSaLog.Columns.Add("Tag", "Tag");
            _dgvSaLog.Columns.Add("SetIdx", "Set");
            _dgvSaLog.Columns.Add("Offset", "Offset");
            _dgvSaLog.Columns.Add("Way", "Way");
            _dgvSaLog.Columns.Add("Result", "Result");

            _dgvSaContents = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _dgvSaContents.Columns.Add("Set", "Set #");
            _dgvSaContents.Columns.Add("Way", "Way #");
            _dgvSaContents.Columns.Add("Valid", "Valid");
            _dgvSaContents.Columns.Add("TagVal", "Tag");

            tab.Controls.Add(_dgvSaContents);
            tab.Controls.Add(_dgvSaLog);
            tab.Controls.Add(pnlStats);
            tab.Controls.Add(pnlLeft);

            RefreshSaUI();
        }

        private void ApplySaConfig()
        {
            try
            {
                _saCache.Configure((int)_numSets.Value, (int)_numWays.Value, (int)_numBlockSize.Value);
                RefreshSaUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invalid Parameters", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DoSaAccess()
        {
            if (TryParseAddress(_txtSaAddress.Text, out uint addr))
            {
                _saCache.Access(addr);
                RefreshSaUI();
            }
            else
            {
                MessageBox.Show("Invalid address. Use decimal (256) or hex (0x100).",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshSaUI()
        {
            _dgvSaLog.Rows.Clear();
            foreach (var a in _saCache.AccessLog)
            {
                int rowIdx = _dgvSaLog.Rows.Add(
                    a.Index, $"0x{a.Address:X4}", $"0x{a.Tag:X}", a.SetIndex, a.Offset, a.WayUsed,
                    a.IsHit ? "HIT" : (a.WasEviction ? "MISS (evict)" : "MISS"));
                _dgvSaLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9) : Color.FromArgb(0xFF, 0xCD, 0xD2);
            }
            if (_dgvSaLog.Rows.Count > 0)
                _dgvSaLog.FirstDisplayedScrollingRowIndex = _dgvSaLog.Rows.Count - 1;

            _dgvSaContents.Rows.Clear();
            for (int s = 0; s < _saCache.NumSets; s++)
            {
                var set = _saCache.GetSet(s);
                for (int w = 0; w < set.Associativity; w++)
                {
                    var line = set.Ways[w];
                    _dgvSaContents.Rows.Add(s, w, line.Valid ? "1" : "0", line.Valid ? $"0x{line.Tag:X}" : "-");
                }
            }
            
            _lblSaTotal.Text = $"Accesses: {_saCache.TotalAccesses}";
            _lblSaHits.Text = $"Hits: {_saCache.Hits}";
            _lblSaMisses.Text = $"Misses: {_saCache.Misses}";
            _lblSaHitRate.Text = $"Hit rate: {_saCache.HitRate:P1}";
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
            _dgvLog.Rows.Clear();
            foreach (var a in _cache.AccessLog)
            {
                int rowIdx = _dgvLog.Rows.Add(a.Index, $"0x{a.Address:X4}", a.IsHit ? "HIT" : "MISS");
                _dgvLog.Rows[rowIdx].Cells["Result"].Style.BackColor =
                    a.IsHit ? Color.FromArgb(0xC8, 0xE6, 0xC9)  
                            : Color.FromArgb(0xFF, 0xCD, 0xD2);
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