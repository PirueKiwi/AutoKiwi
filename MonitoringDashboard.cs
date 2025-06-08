using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Reflection;
using AutoKiwi.Minimal;
using AutoKiwi.Orchestration;

namespace AutoKiwi
{
    public class MonitoringDashboard : Form
    {
        // Událost pro komunikaci s hlavním programem
        public event Action<string> GenerationRequested;

        // Komponenty UI
        private TabControl _mainTabControl;
        private TextBox _descriptionTextBox;
        private Button _generateButton;
        private ListView _eventLogListView;
        private Panel _statusPanel;
        private Label _currentStageLabel;
        private Chart _performanceChart;
        private TreeView _workflowTreeView;

        // Data pro monitoring
        private readonly IEventBus _eventBus;
        private readonly List<SystemMessage> _systemMessages = new List<SystemMessage>();
        private readonly Dictionary<WorkflowStage, int> _stageTransitions = new Dictionary<WorkflowStage, int>();
        private readonly Dictionary<string, double> _modelSuccessRates = new Dictionary<string, double>();
        private WorkflowStage _currentStage = WorkflowStage.Idle;
        private readonly object _syncLock = new object();

        private TextBox _applicationDescriptionTextBox;
        private Button _startButton;
        private Dirigent _dirigent;




        public MonitoringDashboard(IEventBus eventBus)
        {
            if (eventBus == null)
                throw new ArgumentNullException(nameof(eventBus));

            _eventBus = eventBus;
            InitializeComponent();
            SubscribeEvents();
        }

        public void SetDirigent(Dirigent dirigent)
        {
            _dirigent = dirigent;
            AddGenerationControls();
        }                                                                                       

        private void InitializeComponent()
        {
            this.Text = "AutoKiwi Monitoring Dashboard";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            var inputPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 100,
                Padding = new Padding(10)
            };

            _applicationDescriptionTextBox = new TextBox
            {
                Multiline = true,
                Width = 500,
                Height = 60,
                Location = new Point(10, 10),
                Text = "Zadejte popis aplikace k vygenerování..."
            };

            _startButton = new Button
            {
                Text = "Start Generation",
                Width = 120,
                Height = 30,
                Location = new Point(520, 25)
            };

            _startButton.Click += StartButton_Click;

            inputPanel.Controls.Add(_applicationDescriptionTextBox);
            inputPanel.Controls.Add(_startButton);

            // Přidáme panel před current stage label
            overviewTab.Controls.Add(inputPanel);
            overviewTab.Controls.Add(_currentStageLabel);

            // Main layout
            TableLayoutPanel mainLayoutPanel = new TableLayoutPanel();
            mainLayoutPanel.Dock = DockStyle.Fill;
            mainLayoutPanel.ColumnCount = 1;
            mainLayoutPanel.RowCount = 2;
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            mainLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Top control panel
            Panel controlPanel = new Panel();
            controlPanel.Dock = DockStyle.Fill;
            controlPanel.Padding = new Padding(10);

            Label inputLabel = new Label();
            inputLabel.Text = "Popis aplikace:";
            inputLabel.Location = new Point(10, 15);
            inputLabel.AutoSize = true;

            _descriptionTextBox = new TextBox();
            _descriptionTextBox.Location = new Point(120, 12);
            _descriptionTextBox.Size = new Size(600, 60);
            _descriptionTextBox.Multiline = true;

            _generateButton = new Button();
            _generateButton.Text = "Generovat";
            _generateButton.Location = new Point(740, 12);
            _generateButton.Size = new Size(120, 60);
            _generateButton.Click += GenerateButton_Click;

            _currentStageLabel = new Label();
            _currentStageLabel.Text = "Aktuální stav: Idle";
            _currentStageLabel.Location = new Point(120, 80);
            _currentStageLabel.AutoSize = true;
            _currentStageLabel.Font = new Font(FontFamily.GenericSansSerif, 12, FontStyle.Bold);

            controlPanel.Controls.Add(inputLabel);
            controlPanel.Controls.Add(_descriptionTextBox);
            controlPanel.Controls.Add(_generateButton);
            controlPanel.Controls.Add(_currentStageLabel);

            // Tab control for monitoring
            _mainTabControl = new TabControl();
            _mainTabControl.Dock = DockStyle.Fill;

            // Záložky
            TabPage overviewPage = new TabPage("Přehled");
            TabPage eventsPage = new TabPage("Události");
            TabPage workflowPage = new TabPage("Workflow");
            TabPage modelsPage = new TabPage("Modely");

            InitializeOverviewTab(overviewPage);
            InitializeEventsTab(eventsPage);
            InitializeWorkflowTab(workflowPage);
            InitializeModelsTab(modelsPage);

            _mainTabControl.TabPages.Add(overviewPage);
            _mainTabControl.TabPages.Add(eventsPage);
            _mainTabControl.TabPages.Add(workflowPage);
            _mainTabControl.TabPages.Add(modelsPage);

            mainLayoutPanel.Controls.Add(controlPanel, 0, 0);
            mainLayoutPanel.Controls.Add(_mainTabControl, 0, 1);


            this.Controls.Add(mainLayoutPanel);
        }

        private void InitializeOverviewTab(TabPage page)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 2;

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            // Status panel
            _statusPanel = CreateStatusPanel();
            layout.Controls.Add(_statusPanel, 0, 0);

            // Performance chart
            _performanceChart = new Chart();
            _performanceChart.Dock = DockStyle.Fill;
            _performanceChart.BackColor = Color.WhiteSmoke;

            ChartArea chartArea = new ChartArea("MainArea");
            _performanceChart.ChartAreas.Add(chartArea);

            Series successSeries = new Series("Úspěšnost");
            successSeries.ChartType = SeriesChartType.Column;
            _performanceChart.Series.Add(successSeries);
            _performanceChart.Titles.Add(new Title("Úspěšnost modelů", Docking.Top));

            layout.Controls.Add(_performanceChart, 1, 0);

            // Recent events panel
            Panel recentEventsPanel = new Panel();
            recentEventsPanel.Dock = DockStyle.Fill;
            recentEventsPanel.BorderStyle = BorderStyle.FixedSingle;

            Label recentEventsLabel = new Label();
            recentEventsLabel.Text = "Nedávné události";
            recentEventsLabel.Dock = DockStyle.Top;
            recentEventsLabel.BackColor = Color.LightSteelBlue;
            recentEventsLabel.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
            recentEventsLabel.Height = 25;

            ListView recentEventsList = new ListView();
            recentEventsList.Dock = DockStyle.Fill;
            recentEventsList.View = View.Details;
            recentEventsList.FullRowSelect = true;

            recentEventsList.Columns.Add("Čas", 100);
            recentEventsList.Columns.Add("Typ", 100);
            recentEventsList.Columns.Add("Zpráva", 300);

            recentEventsPanel.Controls.Add(recentEventsList);
            recentEventsPanel.Controls.Add(recentEventsLabel);

            layout.Controls.Add(recentEventsPanel, 0, 1);

            // Details panel
            Panel detailsPanel = new Panel();
            detailsPanel.Dock = DockStyle.Fill;
            detailsPanel.BorderStyle = BorderStyle.FixedSingle;

            Label detailsLabel = new Label();
            detailsLabel.Text = "Detaily operace";
            detailsLabel.Dock = DockStyle.Top;
            detailsLabel.BackColor = Color.LightSteelBlue;
            detailsLabel.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
            detailsLabel.Height = 25;



            TextBox detailsTextBox = new TextBox();
            detailsTextBox.Dock = DockStyle.Fill;
            detailsTextBox.Multiline = true;
            detailsTextBox.ReadOnly = true;
            detailsTextBox.ScrollBars = ScrollBars.Vertical;



            detailsPanel.Controls.Add(detailsTextBox);
            detailsPanel.Controls.Add(detailsLabel);

            layout.Controls.Add(detailsPanel, 1, 1);

            page.Controls.Add(layout);
        }

        private void InitializeEventsTab(TabPage page)
        {
            // Log událostí
            _eventLogListView = new ListView();
            _eventLogListView.Dock = DockStyle.Fill;
            _eventLogListView.View = View.Details;
            _eventLogListView.FullRowSelect = true;
            _eventLogListView.GridLines = true;

            _eventLogListView.Columns.Add("Čas", 120);
            _eventLogListView.Columns.Add("Zdroj", 120);
            _eventLogListView.Columns.Add("Závažnost", 80);
            _eventLogListView.Columns.Add("Zpráva", 600);

            page.Controls.Add(_eventLogListView);
        }

        private void InitializeWorkflowTab(TabPage page)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 1;

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // TreeView pro fáze workflow
            _workflowTreeView = new TreeView();
            _workflowTreeView.Dock = DockStyle.Fill;

            TreeNode rootNode = new TreeNode("Fáze workflow");
            foreach (WorkflowStage stage in Enum.GetValues(typeof(WorkflowStage)))
            {
                rootNode.Nodes.Add(new TreeNode(stage.ToString()));
            }
            _workflowTreeView.Nodes.Add(rootNode);
            rootNode.Expand();

            Panel treePanel = new Panel();
            treePanel.Dock = DockStyle.Fill;
            treePanel.BorderStyle = BorderStyle.FixedSingle;

            Label treeLabel = new Label();
            treeLabel.Text = "Fáze workflow";
            treeLabel.Dock = DockStyle.Top;
            treeLabel.BackColor = Color.LightSteelBlue;
            treeLabel.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
            treeLabel.Height = 25;

            treePanel.Controls.Add(_workflowTreeView);
            treePanel.Controls.Add(treeLabel);

            layout.Controls.Add(treePanel, 0, 0);

            // Panel s detaily workflow
            Panel detailsPanel = new Panel();
            detailsPanel.Dock = DockStyle.Fill;
            detailsPanel.BorderStyle = BorderStyle.FixedSingle;

            Label detailsLabel = new Label();
            detailsLabel.Text = "Detaily workflow";
            detailsLabel.Dock = DockStyle.Top;
            detailsLabel.BackColor = Color.LightSteelBlue;
            detailsLabel.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
            detailsLabel.Height = 25;

            Chart transitionsChart = new Chart();
            transitionsChart.Dock = DockStyle.Fill;
            transitionsChart.BackColor = Color.WhiteSmoke;

            ChartArea chartArea = new ChartArea("TransitionsArea");
            transitionsChart.ChartAreas.Add(chartArea);

            Series transitionsSeries = new Series("Přechody");
            transitionsSeries.ChartType = SeriesChartType.Bar;
            transitionsChart.Series.Add(transitionsSeries);

            detailsPanel.Controls.Add(transitionsChart);
            detailsPanel.Controls.Add(detailsLabel);

            layout.Controls.Add(detailsPanel, 1, 0);

            page.Controls.Add(layout);
        }

        private void InitializeModelsTab(TabPage page)
        {
            DataGridView modelsDataGridView = new DataGridView();
            modelsDataGridView.Dock = DockStyle.Fill;
            modelsDataGridView.AllowUserToAddRows = false;
            modelsDataGridView.AllowUserToDeleteRows = false;
            modelsDataGridView.ReadOnly = true;
            modelsDataGridView.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            modelsDataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            modelsDataGridView.Columns.Add("ModelId", "Model");
            modelsDataGridView.Columns.Add("TaskType", "Typ úkolu");
            modelsDataGridView.Columns.Add("TotalAttempts", "Celkem pokusů");
            modelsDataGridView.Columns.Add("SuccessfulAttempts", "Úspěšné pokusy");
            modelsDataGridView.Columns.Add("SuccessRate", "Úspěšnost");

            page.Controls.Add(modelsDataGridView);
        }

        private Panel CreateStatusPanel()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BorderStyle = BorderStyle.FixedSingle;

            Label titleLabel = new Label();
            titleLabel.Text = "Status systému";
            titleLabel.Dock = DockStyle.Top;
            titleLabel.BackColor = Color.LightSteelBlue;
            titleLabel.Font = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
            titleLabel.Height = 25;

            TableLayoutPanel statusTable = new TableLayoutPanel();
            statusTable.Dock = DockStyle.Fill;
            statusTable.ColumnCount = 2;
            statusTable.RowCount = 5;

            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Přidání indikátorů stavu
            AddStatusRow(statusTable, 0, "Aktuální fáze:", "Idle");
            AddStatusRow(statusTable, 1, "Doba běhu:", "00:00:00");
            AddStatusRow(statusTable, 2, "Využití paměti:", "0 aplikací");
            AddStatusRow(statusTable, 3, "Úspěšnost:", "0%");
            AddStatusRow(statusTable, 4, "Stav systému:", "OK");

            panel.Controls.Add(statusTable);
            panel.Controls.Add(titleLabel);

            return panel;
        }
        private void AddGenerationControls()
        {

        }

        private void AddStatusRow(TableLayoutPanel table, int row, string label, string value)
        {
            Label labelControl = new Label();
            labelControl.Text = label;
            labelControl.Dock = DockStyle.Fill;
            labelControl.TextAlign = ContentAlignment.MiddleLeft;

            Label valueControl = new Label();
            valueControl.Text = value;
            valueControl.Dock = DockStyle.Fill;
            valueControl.TextAlign = ContentAlignment.MiddleLeft;
            valueControl.Font = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold);
            valueControl.Tag = label.Replace(":", "").Trim(); // pro pozdější identifikaci

            table.Controls.Add(labelControl, 0, row);
            table.Controls.Add(valueControl, 1, row);
        }

        private void SubscribeEvents()
        {
            _eventBus.Subscribe<SystemMessageEvent>(OnSystemMessage);
            _eventBus.Subscribe<WorkflowStateChangedEvent>(OnWorkflowStateChanged);
            _eventBus.Subscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus.Subscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
            _eventBus.Subscribe<ModelSelectionCompletedEvent>(OnModelSelectionCompleted);
        }

        private async void StartButton_Click(object sender, EventArgs e)
        {
            if (_dirigent == null)
            {
                MessageBox.Show("Dirigent není inicializován", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string applicationDescription = _applicationDescriptionTextBox.Text;
            if (string.IsNullOrWhiteSpace(applicationDescription))
            {
                MessageBox.Show("Zadejte popis aplikace", "Upozornění", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _startButton.Enabled = false;
            try
            {
                await _dirigent.StartDevelopmentSequenceAsync(applicationDescription);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chyba při generování: {ex.Message}", "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _startButton.Enabled = true;
            }
        }

        private void OnSystemMessage(SystemMessageEvent evt)
        {
            lock (_syncLock)
            {
                _systemMessages.Add(evt.Message);
                if (_systemMessages.Count > 1000)
                    _systemMessages.RemoveAt(0);
            }

            this.BeginInvoke(new Action(UpdateEventLog));
        }

        private void OnWorkflowStateChanged(WorkflowStateChangedEvent evt)
        {
            lock (_syncLock)
            {
                _currentStage = evt.State.CurrentStage;
                if (_stageTransitions.ContainsKey(_currentStage))
                    _stageTransitions[_currentStage]++;
                else
                    _stageTransitions[_currentStage] = 1;
            }

            this.BeginInvoke(new Action(UpdateWorkflowStatus));
            this.BeginInvoke(new Action(UpdateWorkflowTree));
        }

        private void OnCompilationCompleted(CompilationCompletedEvent evt)
        {
            // Bezpečně získej ModelId pomocí reflexe
            string modelId = "unknown";
            PropertyInfo prop = evt.GetType().GetProperty("ModelId");
            if (prop != null)
            {
                object value = prop.GetValue(evt, null); // starší verze C# vyžadují druhý parametr
                if (value != null)
                    modelId = value.ToString();
            }

            lock (_syncLock)
            {
                string key = string.Format("{0}-Compilation", modelId);
                _modelSuccessRates[key] = evt.Success ? 1.0 : 0.0;
            }

            this.BeginInvoke(new Action(UpdatePerformanceChart));
        }

        private void OnFormAnalysisCompleted(FormAnalysisCompletedEvent evt)
        {
            // Pro budoucí implementaci
        }

        private void OnModelSelectionCompleted(ModelSelectionCompletedEvent evt)
        {
            // Pro budoucí implementaci
        }

        private void UpdateEventLog()
        {
            _eventLogListView.BeginUpdate();
            _eventLogListView.Items.Clear();

            lock (_syncLock)
            {
                List<SystemMessage> messages = _systemMessages.OrderByDescending(m => m.Timestamp).Take(200).ToList();
                foreach (SystemMessage msg in messages)
                {
                    ListViewItem item = new ListViewItem(msg.Timestamp.ToString("HH:mm:ss"));
                    item.SubItems.Add(msg.Source);
                    item.SubItems.Add(msg.Severity.ToString());
                    item.SubItems.Add(msg.Message);

                    // Barevné zvýraznění podle závažnosti
                    switch (msg.Severity)
                    {
                        case MessageSeverity.Error:
                            item.BackColor = Color.LightPink;
                            break;
                        case MessageSeverity.Warning:
                            item.BackColor = Color.LightYellow;
                            break;
                        case MessageSeverity.Critical:
                            item.BackColor = Color.Red;
                            item.ForeColor = Color.White;
                            break;
                    }

                    _eventLogListView.Items.Add(item);
                }
            }

            _eventLogListView.EndUpdate();
        }

        private void UpdateWorkflowStatus()
        {
            _currentStageLabel.Text = string.Format("Aktuální stav: {0}", _currentStage);

            // Aktualizace v panelu stavu
            foreach (Control control in _statusPanel.Controls)
            {
                TableLayoutPanel table = control as TableLayoutPanel;
                if (table != null)
                {
                    foreach (Control c in table.Controls)
                    {
                        Label label = c as Label;
                        if (label != null && label.Tag != null)
                        {
                            if (label.Tag.ToString() == "Aktuální fáze")
                            {
                                label.Text = _currentStage.ToString();
                            }
                        }
                    }
                }
            }
        }

        private void UpdateWorkflowTree()
        {
            _workflowTreeView.BeginUpdate();

            foreach (TreeNode rootNode in _workflowTreeView.Nodes)
            {
                foreach (TreeNode stageNode in rootNode.Nodes)
                {
                    bool isCurrentStage = stageNode.Text.Equals(_currentStage.ToString(),
                                                               StringComparison.OrdinalIgnoreCase);
                    stageNode.BackColor = isCurrentStage ? Color.LightGreen : Color.White;
                    stageNode.ForeColor = isCurrentStage ? Color.DarkGreen : Color.Black;
                    stageNode.NodeFont = isCurrentStage ?
                    stageNode.NodeFont = isCurrentStage ?
                        new Font(_workflowTreeView.Font, FontStyle.Bold) : _workflowTreeView.Font;

                    int count = 0;
                    lock (_syncLock)
                    {
                        WorkflowStage stage = (WorkflowStage)Enum.Parse(typeof(WorkflowStage),
                                                                      stageNode.Text.Split('(')[0].Trim());
                        _stageTransitions.TryGetValue(stage, out count);
                    }
                    stageNode.Text = string.Format("{0} ({1})", stageNode.Text.Split('(')[0].Trim(), count);
                }
            }

            _workflowTreeView.EndUpdate();
        }

        private void UpdatePerformanceChart()
        {
            Series series = _performanceChart.Series[0];
            series.Points.Clear();

            lock (_syncLock)
            {
                foreach (KeyValuePair<string, double> kvp in _modelSuccessRates)
                {
                    series.Points.AddXY(kvp.Key, kvp.Value * 100);
                    DataPoint point = series.Points[series.Points.Count - 1];

                    // Bezpečné nastavení popisku pomocí reflexe
                    try
                    {
                        PropertyInfo labelProp = point.GetType().GetProperty("Label");
                        if (labelProp != null)
                        {
                            labelProp.SetValue(point, string.Format("{0:P0}", kvp.Value), null);
                        }
                    }
                    catch (Exception)
                    {
                        // Ignorováno, pokud Label není podporovaný
                    }
                }
            }
        }

        private void GenerateButton_Click(object sender, EventArgs e)
        {
            string description = _descriptionTextBox.Text.Trim();

            if (string.IsNullOrEmpty(description))
            {
                MessageBox.Show("Zadejte popis aplikace", "Chyba",
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Zablokování GUI během generování
            _generateButton.Enabled = false;
            _descriptionTextBox.Enabled = false;

            // Vyvolání události pro hlavní program
            if (GenerationRequested != null)
                GenerationRequested(description);
        }

        public void EnableGenerationControls()
        {
            this.BeginInvoke(new Action(EnableControls));
        }

        private void EnableControls()
        {
            _generateButton.Enabled = true;
            _descriptionTextBox.Enabled = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Odhlášení od událostí
            _eventBus?.Unsubscribe<SystemMessageEvent>(OnSystemMessage);
            _eventBus?.Unsubscribe<WorkflowStateChangedEvent>(OnWorkflowStateChanged);
            _eventBus?.Unsubscribe<CompilationCompletedEvent>(OnCompilationCompleted);
            _eventBus?.Unsubscribe<FormAnalysisCompletedEvent>(OnFormAnalysisCompleted);
            _eventBus?.Unsubscribe<ModelSelectionCompletedEvent>(OnModelSelectionCompleted);

            base.OnFormClosing(e);
        }
    }
}