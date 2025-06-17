using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoKiwi
{
    // 1. HLAVNÍ ENTRY POINT
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Spusť hlavní form s MCP API
            Application.Run(new MainFormWithAPI());
        }
    }

    // 2. ROZŠÍŘENÝ MAINFORM S API
    public partial class MainFormWithAPI : Form
    {
        private SimpleAutoKiwiAPI _api;
        private Button _testButton;
        private Label _statusLabel;
        private ComboBox _positionCombo;
        private TextBox _logTextBox;

        public MainFormWithAPI()
        {
            InitializeComponent();
            InitializeAPI();
        }

        private void InitializeComponent()
        {
            // Základní nastavení formu
            this.Text = "🥝 AutoKiwi with MCP API";
            this.Size = new System.Drawing.Size(600, 400);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Status label
            _statusLabel = new Label
            {
                Text = "🔄 Starting API...",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(300, 25),
                ForeColor = System.Drawing.Color.Blue
            };
            this.Controls.Add(_statusLabel);

            // Position selector
            var positionLabel = new Label
            {
                Text = "Claude Position:",
                Location = new System.Drawing.Point(10, 45),
                Size = new System.Drawing.Size(100, 20)
            };
            this.Controls.Add(positionLabel);

            _positionCombo = new ComboBox
            {
                Location = new System.Drawing.Point(120, 42),
                Size = new System.Drawing.Size(150, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _positionCombo.Items.AddRange(new[] {
                "Architect", "CodeGenerator", "Debugger", "Tester", "UIDesigner", "Optimizer"
            });
            _positionCombo.SelectedIndex = 0;
            _positionCombo.SelectedIndexChanged += PositionCombo_SelectedIndexChanged;
            this.Controls.Add(_positionCombo);

            // Test button
            _testButton = new Button
            {
                Text = "🧪 Test API",
                Location = new System.Drawing.Point(280, 40),
                Size = new System.Drawing.Size(100, 30),
                BackColor = System.Drawing.Color.LightGreen
            };
            _testButton.Click += TestButton_Click;
            this.Controls.Add(_testButton);

            // Log textbox
            var logLabel = new Label
            {
                Text = "API Log:",
                Location = new System.Drawing.Point(10, 80),
                Size = new System.Drawing.Size(100, 20)
            };
            this.Controls.Add(logLabel);

            _logTextBox = new TextBox
            {
                Location = new System.Drawing.Point(10, 105),
                Size = new System.Drawing.Size(560, 240),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = System.Drawing.Color.Black,
                ForeColor = System.Drawing.Color.LimeGreen,
                Font = new System.Drawing.Font("Consolas", 9)
            };
            this.Controls.Add(_logTextBox);

            // GitHub Pages button
            var githubButton = new Button
            {
                Text = "🌐 Open Dashboard",
                Location = new System.Drawing.Point(400, 40),
                Size = new System.Drawing.Size(120, 30),
                BackColor = System.Drawing.Color.LightBlue
            };
            githubButton.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start("https://piruekiwi.github.io/autokiwi-mcp-server/");
                }
                catch (Exception ex)
                {
                    AddLog($"❌ Failed to open browser: {ex.Message}");
                }
            };
            this.Controls.Add(githubButton);
        }

        private async void InitializeAPI()
        {
            try
            {
                _api = new SimpleAutoKiwiAPI();

                // Start API v background
                _ = Task.Run(async () => await _api.StartAsync());

                // Čekej chvilku na start
                await Task.Delay(1000);

                UpdateStatus("🎬 MCP API: Running on http://localhost:8080", System.Drawing.Color.Green);
                AddLog("✅ AutoKiwi MCP API started successfully");
                AddLog("🌐 Dashboard: https://piruekiwi.github.io/autokiwi-mcp-server/");
                AddLog("📡 Local API: http://localhost:8080/status");

                _testButton.Enabled = true;

                // Start status monitoring
                StartStatusMonitoring();
            }
            catch (Exception ex)
            {
                UpdateStatus($"❌ API Failed: {ex.Message}", System.Drawing.Color.Red);
                AddLog($"❌ Failed to start API: {ex.Message}");
            }
        }

        private void StartStatusMonitoring()
        {
            var timer = new Timer
            {
                Interval = 5000 // 5 sekund
            };
            timer.Tick += (s, e) =>
            {
                if (_api != null)
                {
                    AddLog($"📊 Status: Position={_api.CurrentPosition}, Memory={GC.GetTotalMemory(false) / 1024 / 1024}MB");
                }
            };
            timer.Start();
        }

        private void UpdateStatus(string text, System.Drawing.Color color)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatus(text, color)));
                return;
            }

            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void AddLog(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AddLog(message)));
                return;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\r\n";

            _logTextBox.AppendText(logEntry);
            _logTextBox.SelectionStart = _logTextBox.Text.Length;
            _logTextBox.ScrollToCaret();
        }

        private void PositionCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_api != null && _positionCombo.SelectedItem != null)
            {
                var position = _positionCombo.SelectedItem.ToString();
                _api.SwitchPositionFromUI(position);
                AddLog($"🎯 Claude switched to: {position}");
            }
        }

        private void TestButton_Click(object sender, EventArgs e)
        {
            if (_api != null)
            {
                _api.SwitchPositionFromUI("Debugger");
                _api.UpdateStatus("Test completed successfully");
                AddLog("🧪 API test completed - Claude switched to Debugger");

                // Update combo box
                _positionCombo.SelectedItem = "Debugger";

                MessageBox.Show(
                    "✅ API Test Successful!\n\n" +
                    "• Claude switched to Debugger\n" +
                    "• Status updated\n" +
                    "• Ready for GitHub Pages connection",
                    "AutoKiwi MCP API Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                MessageBox.Show("❌ API not initialized!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                _api?.Stop();
                AddLog("🛑 AutoKiwi MCP API stopped");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping API: {ex.Message}");
            }

            base.OnFormClosed(e);
        }

        // Keyboard shortcuts
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    TestButton_Click(null, null);
                    return true;

                case Keys.Control | Keys.D:
                    _positionCombo.SelectedItem = "Debugger";
                    return true;

                case Keys.Control | Keys.A:
                    _positionCombo.SelectedItem = "Architect";
                    return true;

                case Keys.Control | Keys.G:
                    _positionCombo.SelectedItem = "CodeGenerator";
                    return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // 3. POKUD MÁŠ UŽ EXISTUJÍCÍ MAINFORM
    // Můžeš použít tuto extension metodu:
    public static class MainFormExtensions
    {
        private static SimpleAutoKiwiAPI _staticAPI;

        public static async Task AddMCPSupport(this Form mainForm)
        {
            _staticAPI = new SimpleAutoKiwiAPI();
            _ = Task.Run(async () => await _staticAPI.StartAsync());

            // Přidej status label
            var statusLabel = new Label
            {
                Text = "🎬 MCP API: Starting...",
                Location = new System.Drawing.Point(10, mainForm.Height - 30),
                Size = new System.Drawing.Size(200, 20),
                ForeColor = System.Drawing.Color.Green
            };
            mainForm.Controls.Add(statusLabel);

            await Task.Delay(1000);
            statusLabel.Text = "🎬 MCP API: Running";

            // Hook do form close
            mainForm.FormClosed += (s, e) => _staticAPI?.Stop();
        }

        public static void SwitchClaudePosition(this Form mainForm, string position)
        {
            _staticAPI?.SwitchPositionFromUI(position);
        }
    }
}

// 4. USAGE V EXISTUJÍCÍM KÓDU:
/*
// Pokud už máš MainForm, přidej jen tohle:
public partial class MainForm : Form
{
    private async void MainForm_Load(object sender, EventArgs e)
    {
        // Tvůj existující kód...
        
        // Přidej MCP support
        await this.AddMCPSupport();
    }
    
    // Můžeš pak používat:
    private void SomeButton_Click(object sender, EventArgs e)
    {
        this.SwitchClaudePosition("Debugger");
    }
}
*/