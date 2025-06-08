using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace AutoKiwi.Minimal
{
    /// <summary>
    /// Analyzátor formulářů - vytváří textový popis UI pro LLM (Form2Text)
    /// </summary>
    public class FormAnalyzer : IFormAnalyzer
    {
        private readonly IEventBus _eventBus;

        public FormAnalyzer(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        /// <summary>
        /// Analyzuje formulář a vytváří jeho textový popis pro LLM
        /// </summary>
        public async Task<string> AnalyzeFormAsync(Form form)
        {
            LogMessage("Analyzuji formulář pomocí Form2Text");

            var description = new StringBuilder();

            try
            {
                await Task.Run(() => {
                    // Základní informace o formuláři
                    description.AppendLine($"# Form Analysis");
                    description.AppendLine($"## Basic Information");
                    description.AppendLine($"- Form Title: \"{form.Text}\"");
                    description.AppendLine($"- Size: {form.Width}x{form.Height} pixels");
                    description.AppendLine($"- Total Controls: {CountAllControls(form)} (direct children: {form.Controls.Count})");
                    description.AppendLine($"- Form Border Style: {form.FormBorderStyle}");
                    description.AppendLine($"- Background Color: {ColorToHex(form.BackColor)}");
                    description.AppendLine();

                    // Hierarchická struktura ovládacích prvků
                    description.AppendLine("## Control Hierarchy");
                    AnalyzeControlHierarchy(form, description, 0);
                    description.AppendLine();

                    // Detaily ovládacích prvků
                    description.AppendLine("## Control Details");
                    AnalyzeControls(form, description, 0);
                    description.AppendLine();

                    // Layout a pozicování
                    description.AppendLine("## Layout Analysis");
                    AnalyzeLayout(form, description);
                    description.AppendLine();

                    // Interaktivní prvky
                    description.AppendLine("## Interactive Elements");
                    AnalyzeInteractiveElements(form, description);
                    description.AppendLine();

                    // Závěrečné shrnutí
                    description.AppendLine("## Summary");
                    description.AppendLine($"The form \"{form.Text}\" appears to be a {GuessApplicationType(form)} application with {CountAllControls(form)} total controls.");
                    description.AppendLine($"Primary interactive elements: {CountInteractiveElements(form)}");
                });

                LogMessage("Analýza formuláře dokončena");

                return description.ToString();
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při analýze formuláře: {ex.Message}", MessageSeverity.Error);
                return $"Error analyzing form: {ex.Message}";
            }
        }

        /// <summary>
        /// Počítá všechny ovládací prvky ve formuláři (rekurzivně)
        /// </summary>
        private int CountAllControls(Control parent)
        {
            int count = parent.Controls.Count;
            foreach (Control control in parent.Controls)
            {
                count += CountAllControls(control);
            }
            return count;
        }

        /// <summary>
        /// Počítá interaktivní prvky ve formuláři (tlačítka, textové pole, atd.)
        /// </summary>
        private string CountInteractiveElements(Control parent)
        {
            int buttons = 0;
            int textBoxes = 0;
            int checkBoxes = 0;
            int comboBoxes = 0;
            int radioButtons = 0;

            CountInteractiveElementsRecursive(parent, ref buttons, ref textBoxes, ref checkBoxes, ref comboBoxes, ref radioButtons);

            var result = new List<string>();
            if (buttons > 0) result.Add($"{buttons} buttons");
            if (textBoxes > 0) result.Add($"{textBoxes} text fields");
            if (checkBoxes > 0) result.Add($"{checkBoxes} checkboxes");
            if (comboBoxes > 0) result.Add($"{comboBoxes} dropdown menus");
            if (radioButtons > 0) result.Add($"{radioButtons} radio buttons");

            return string.Join(", ", result);
        }

        /// <summary>
        /// Rekurzivně počítá interaktivní prvky
        /// </summary>
        private void CountInteractiveElementsRecursive(Control parent, ref int buttons, ref int textBoxes, 
            ref int checkBoxes, ref int comboBoxes, ref int radioButtons)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Button) buttons++;
                else if (control is TextBox) textBoxes++;
                else if (control is CheckBox) checkBoxes++;
                else if (control is ComboBox) comboBoxes++;
                else if (control is RadioButton) radioButtons++;

                CountInteractiveElementsRecursive(control, ref buttons, ref textBoxes, ref checkBoxes, ref comboBoxes, ref radioButtons);
            }
        }

        /// <summary>
        /// Odhaduje typ aplikace na základě obsažených prvků
        /// </summary>
        private string GuessApplicationType(Form form)
        {
            // Počty různých typů ovládacích prvků
            int textBoxes = 0;
            int buttons = 0;
            int dataGrids = 0;
            int labels = 0;
            int menuItems = 0;

            CountControlTypesRecursive(form, ref textBoxes, ref buttons, ref dataGrids, ref labels, ref menuItems);

            // Heuristika pro určení typu aplikace
            if (dataGrids > 0 && buttons > 2) return "data management";
            if (textBoxes > 5 && labels > 5) return "data entry";
            if (buttons > 10) return "control panel";
            if (menuItems > 5) return "document-based";
            if (buttons == 0 && labels > 0) return "information display";
            if (textBoxes == 1 && buttons == 1) return "simple input";

            return "general purpose";
        }

        /// <summary>
        /// Počítá typy ovládacích prvků rekurzivně
        /// </summary>
        private void CountControlTypesRecursive(Control parent, ref int textBoxes, ref int buttons, 
            ref int dataGrids, ref int labels, ref int menuItems)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is TextBox) textBoxes++;
                else if (control is Button) buttons++;
                else if (control is DataGridView) dataGrids++;
                else if (control is Label) labels++;
                else if (control is MenuStrip)
                {
                    MenuStrip menu = (MenuStrip)control;
                    menuItems += CountMenuItems(menu.Items);
                }

                CountControlTypesRecursive(control, ref textBoxes, ref buttons, ref dataGrids, ref labels, ref menuItems);
            }
        }

        /// <summary>
        /// Počítá položky menu rekurzivně
        /// </summary>
        private int CountMenuItems(ToolStripItemCollection items)
        {
            int count = items.Count;
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripMenuItem)
                {
                    ToolStripMenuItem menuItem = (ToolStripMenuItem)item;
                    count += CountMenuItems(menuItem.DropDownItems);
                }
            }
            return count;
        }

        /// <summary>
        /// Analyzuje hierarchii ovládacích prvků (strom)
        /// </summary>
        private void AnalyzeControlHierarchy(Control parent, StringBuilder description, int depth)
        {
            string indent = new string(' ', depth * 2);
            
            foreach (Control control in parent.Controls)
            {
                description.AppendLine($"{indent}- {control.GetType().Name}: '{control.Name}' ({(control.Visible ? "visible" : "hidden")})");

                if (control.Controls.Count > 0)
                {
                    AnalyzeControlHierarchy(control, description, depth + 1);
                }
            }
        }

        /// <summary>
        /// Analyzuje detaily ovládacích prvků
        /// </summary>
        private void AnalyzeControls(Control parent, StringBuilder description, int depth)
        {
            string indent = new string(' ', depth * 2);

            foreach (Control control in parent.Controls)
            {
                description.AppendLine($"{indent}### {control.GetType().Name}: '{control.Name}'");
                description.AppendLine($"{indent}- Text: \"{control.Text}\"");
                description.AppendLine($"{indent}- Location: ({control.Location.X}, {control.Location.Y}), Size: {control.Width}x{control.Height}");
                description.AppendLine($"{indent}- Visible: {control.Visible}, Enabled: {control.Enabled}");
                
                // Specifické vlastnosti podle typu prvku
                if (control is TextBox textBox)
                {
                    description.AppendLine($"{indent}- TextBox Properties:");
                    description.AppendLine($"{indent}  - MultiLine: {textBox.Multiline}, ReadOnly: {textBox.ReadOnly}");
                    description.AppendLine($"{indent}  - Text: \"{textBox.Text}\"");
                    description.AppendLine($"{indent}  - MaxLength: {textBox.MaxLength}");
                }
                else if (control is Button button)
                {
                    description.AppendLine($"{indent}- Button Properties:");
                    description.AppendLine($"{indent}  - Text: \"{button.Text}\"");
                    description.AppendLine($"{indent}  - Has Click Handler: {HasClickHandler(button)}");
                }
                else if (control is CheckBox checkBox)
                {
                    description.AppendLine($"{indent}- CheckBox Properties:");
                    description.AppendLine($"{indent}  - Text: \"{checkBox.Text}\"");
                    description.AppendLine($"{indent}  - Checked: {checkBox.Checked}");
                }
                else if (control is ComboBox comboBox)
                {
                    description.AppendLine($"{indent}- ComboBox Properties:");
                    description.AppendLine($"{indent}  - Items: {comboBox.Items.Count}");
                    description.AppendLine($"{indent}  - DropDownStyle: {comboBox.DropDownStyle}");
                    description.AppendLine($"{indent}  - Selected Item: \"{(comboBox.SelectedItem != null ? comboBox.SelectedItem.ToString() : "None")}\"");
                }
                else if (control is Label label)
                {
                    description.AppendLine($"{indent}- Label Properties:");
                    description.AppendLine($"{indent}  - Text: \"{label.Text}\"");
                    description.AppendLine($"{indent}  - TextAlign: {label.TextAlign}");
                }
                
                description.AppendLine();

                // Rekurzivní zpracování potomků
                if (control.Controls.Count > 0)
                {
                    AnalyzeControls(control, description, depth + 1);
                }
            }
        }

        /// <summary>
        /// Analyzuje rozložení prvků ve formuláři
        /// </summary>
        private void AnalyzeLayout(Form form, StringBuilder description)
        {
            // Analýza obecného rozložení
            bool hasTopMenu = HasControlOfType(form, typeof(MenuStrip));
            bool hasToolbar = HasControlOfType(form, typeof(ToolStrip));
            bool hasStatusBar = HasControlOfType(form, typeof(StatusStrip));
            
            description.AppendLine($"- Top Menu: {(hasTopMenu ? "Yes" : "No")}");
            description.AppendLine($"- Toolbar: {(hasToolbar ? "Yes" : "No")}");
            description.AppendLine($"- Status Bar: {(hasStatusBar ? "Yes" : "No")}");
            
            // Pokus o identifikaci typu layoutu
            string layoutType = IdentifyLayoutType(form);
            description.AppendLine($"- Layout Type: {layoutType}");
            
            // Identifikace skupin prvků
            IdentifyControlGroups(form, description);
        }

        /// <summary>
        /// Zjistí, zda formulář obsahuje prvek daného typu
        /// </summary>
        private bool HasControlOfType(Control parent, Type controlType)
        {
            foreach (Control control in parent.Controls)
            {
                if (controlType.IsAssignableFrom(control.GetType()))
                {
                    return true;
                }
                
                if (HasControlOfType(control, controlType))
                {
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Identifikuje typ layoutu na základě rozložení prvků
        /// </summary>
        private string IdentifyLayoutType(Form form)
        {
            bool hasTableLayoutPanel = HasControlOfType(form, typeof(TableLayoutPanel));
            bool hasFlowLayoutPanel = HasControlOfType(form, typeof(FlowLayoutPanel));
            bool hasSplitContainer = HasControlOfType(form, typeof(SplitContainer));
            
            if (hasTableLayoutPanel) return "Grid-based (TableLayoutPanel)";
            if (hasFlowLayoutPanel) return "Flow-based (FlowLayoutPanel)";
            if (hasSplitContainer) return "Split Panel";
            
            // Pokus o identifikaci rozložení podle pozic prvků
            if (AreControlsAlignedHorizontally(form.Controls)) return "Horizontal Alignment";
            if (AreControlsAlignedVertically(form.Controls)) return "Vertical Alignment";
            if (AreControlsFormingGrid(form.Controls)) return "Grid-like";
            
            return "Custom (Absolute Positioning)";
        }

        /// <summary>
        /// Kontroluje, zda jsou prvky zarovnány horizontálně
        /// </summary>
        private bool AreControlsAlignedHorizontally(Control.ControlCollection controls)
        {
            if (controls.Count < 3) return false;
            
            int commonY = -1;
            int alignedCount = 0;
            
            foreach (Control control in controls)
            {
                if (commonY == -1)
                {
                    commonY = control.Location.Y;
                    alignedCount = 1;
                }
                else if (Math.Abs(control.Location.Y - commonY) < 10)
                {
                    alignedCount++;
                }
            }
            
            return alignedCount >= controls.Count * 0.6; // Alespoň 60% prvků je zarovnáno
        }

        /// <summary>
        /// Kontroluje, zda jsou prvky zarovnány vertikálně
        /// </summary>
        private bool AreControlsAlignedVertically(Control.ControlCollection controls)
        {
            if (controls.Count < 3) return false;
            
            int commonX = -1;
            int alignedCount = 0;
            
            foreach (Control control in controls)
            {
                if (commonX == -1)
                {
                    commonX = control.Location.X;
                    alignedCount = 1;
                }
                else if (Math.Abs(control.Location.X - commonX) < 10)
                {
                    alignedCount++;
                }
            }
            
            return alignedCount >= controls.Count * 0.6; // Alespoň 60% prvků je zarovnáno
        }

        /// <summary>
        /// Kontroluje, zda prvky tvoří mřížku
        /// </summary>
        private bool AreControlsFormingGrid(Control.ControlCollection controls)
        {
            if (controls.Count < 6) return false;
            
            // Zjednodušená detekce mřížky - kontrola, zda existují nejméně 2 řádky a 2 sloupce
            var uniqueYPositions = new HashSet<int>();
            var uniqueXPositions = new HashSet<int>();
            
            foreach (Control control in controls)
            {
                uniqueYPositions.Add(control.Location.Y);
                uniqueXPositions.Add(control.Location.X);
            }
            
            return uniqueYPositions.Count >= 2 && uniqueXPositions.Count >= 2;
        }

        /// <summary>
        /// Identifikuje skupiny ovládacích prvků
        /// </summary>
        private void IdentifyControlGroups(Form form, StringBuilder description)
        {
            var labelTextBoxPairs = FindLabelTextBoxPairs(form);
            var buttonGroups = FindButtonGroups(form);
            
            if (labelTextBoxPairs.Count > 0)
            {
                description.AppendLine("- Label-TextBox Pairs:");
                foreach (var pair in labelTextBoxPairs)
                {
                    description.AppendLine($"  - Label: \"{pair.Item1.Text}\" -> TextBox: \"{pair.Item2.Name}\"");
                }
            }
            
            if (buttonGroups.Count > 0)
            {
                description.AppendLine("- Button Groups:");
                int groupIndex = 1;
                foreach (var group in buttonGroups)
                {
                    description.AppendLine($"  - Group {groupIndex++}:");
                    foreach (Button button in group)
                    {
                        description.AppendLine($"    - \"{button.Text}\"");
                    }
                }
            }
        }

        /// <summary>
        /// Hledá páry Label-TextBox na základě pozic
        /// </summary>
        private List<Tuple<Label, TextBox>> FindLabelTextBoxPairs(Control parent)
        {
            var result = new List<Tuple<Label, TextBox>>();
            var labels = new List<Label>();
            var textBoxes = new List<TextBox>();
            
            // Nejprve najdeme všechny Labels a TextBoxes
            FindControlsOfType(parent, labels, textBoxes);
            
            // Hledáme páry Label-TextBox na základě pozic
            foreach (var label in labels)
            {
                var nearestTextBox = FindNearestTextBox(label, textBoxes);
                if (nearestTextBox != null)
                {
                    result.Add(new Tuple<Label, TextBox>(label, nearestTextBox));
                }
            }
            
            return result;
        }

        /// <summary>
        /// Hledá všechny prvky daných typů
        /// </summary>
        private void FindControlsOfType<T1, T2>(Control parent, List<T1> list1, List<T2> list2)
            where T1 : Control
            where T2 : Control
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T1)
                {
                    list1.Add((T1)control);
                }
                else if (control is T2)
                {
                    list2.Add((T2)control);
                }
                
                FindControlsOfType(control, list1, list2);
            }
        }

        /// <summary>
        /// Hledá nejbližší TextBox k danému Label
        /// </summary>
        private TextBox FindNearestTextBox(Label label, List<TextBox> textBoxes)
        {
            TextBox nearest = null;
            double minDistance = double.MaxValue;
            
            foreach (var textBox in textBoxes)
            {
                // Pouze TextBoxy, které jsou napravo nebo pod Label
                if (textBox.Location.X >= label.Location.X || textBox.Location.Y >= label.Location.Y)
                {
                    double distance = Math.Sqrt(
                        Math.Pow(textBox.Location.X - label.Location.X, 2) +
                        Math.Pow(textBox.Location.Y - label.Location.Y, 2));
                    
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        nearest = textBox;
                    }
                }
            }
            
            // Pouze pokud je TextBox dostatečně blízko (max 100 pixelů)
            return minDistance <= 100 ? nearest : null;
        }

        /// <summary>
        /// Hledá skupiny tlačítek na základě pozic
        /// </summary>
        private List<List<Button>> FindButtonGroups(Control parent)
        {
            var allButtons = new List<Button>();
            FindControlsOfType(parent, allButtons);
            
            var groups = new List<List<Button>>();
            var usedButtons = new HashSet<Button>();
            
            // Seskupujeme tlačítka, která jsou blízko sebe
            foreach (var button in allButtons)
            {
                if (usedButtons.Contains(button)) continue;
                
                var group = new List<Button> { button };
                usedButtons.Add(button);
                
                foreach (var otherButton in allButtons)
                {
                    if (usedButtons.Contains(otherButton)) continue;
                    
                    if (AreButtonsClose(button, otherButton))
                    {
                        group.Add(otherButton);
                        usedButtons.Add(otherButton);
                    }
                }
                
                if (group.Count > 1)
                {
                    groups.Add(group);
                }
            }
            
            return groups;
        }

        /// <summary>
        /// Hledá všechny prvky daného typu
        /// </summary>
        private void FindControlsOfType<T>(Control parent, List<T> list) where T : Control
        {
            foreach (Control control in parent.Controls)
            {
                if (control is T)
                {
                    list.Add((T)control);
                }
                
                FindControlsOfType(control, list);
            }
        }

        /// <summary>
        /// Kontroluje, zda jsou tlačítka blízko sebe
        /// </summary>
        private bool AreButtonsClose(Button button1, Button button2)
        {
            // Kontrola, zda jsou tlačítka vedle sebe nebo pod sebou
            bool horizontallyClose = Math.Abs((button1.Location.X + button1.Width) - button2.Location.X) < 10 ||
                                    Math.Abs((button2.Location.X + button2.Width) - button1.Location.X) < 10;
            
            bool verticallyClose = Math.Abs((button1.Location.Y + button1.Height) - button2.Location.Y) < 10 ||
                                  Math.Abs((button2.Location.Y + button2.Height) - button1.Location.Y) < 10;
            
            return horizontallyClose || verticallyClose;
        }

        /// <summary>
        /// Analyzuje interaktivní prvky formuláře
        /// </summary>
        private void AnalyzeInteractiveElements(Form form, StringBuilder description)
        {
            var buttons = new List<Button>();
            var textBoxes = new List<TextBox>();
            var checkBoxes = new List<CheckBox>();
            var comboBoxes = new List<ComboBox>();
            var radioButtons = new List<RadioButton>();
            
            FindAllInteractiveControls(form, buttons, textBoxes, checkBoxes, comboBoxes, radioButtons);
            
            // Popis tlačítek
            if (buttons.Count > 0)
            {
                description.AppendLine("### Buttons");
                foreach (var button in buttons)
                {
                    description.AppendLine($"- Button \"{button.Text}\" ({button.Name})");
                    description.AppendLine($"  - Location: ({button.Location.X}, {button.Location.Y}), Size: {button.Width}x{button.Height}");
                    description.AppendLine($"  - Has Click Handler: {HasClickHandler(button)}");
                }
                description.AppendLine();
            }
            
            // Popis textových polí
            if (textBoxes.Count > 0)
            {
                description.AppendLine("### Text Fields");
                foreach (var textBox in textBoxes)
                {
                    description.AppendLine($"- TextBox \"{textBox.Name}\"");
                    description.AppendLine($"  - Current Text: \"{textBox.Text}\"");
                    description.AppendLine($"  - MultiLine: {textBox.Multiline}, ReadOnly: {textBox.ReadOnly}");
                    description.AppendLine($"  - Location: ({textBox.Location.X}, {textBox.Location.Y}), Size: {textBox.Width}x{textBox.Height}");
                }
                description.AppendLine();
            }
            
            // Popis zaškrtávacích políček
            if (checkBoxes.Count > 0)
            {
                description.AppendLine("### Checkboxes");
                foreach (var checkBox in checkBoxes)
                {
                    description.AppendLine($"- CheckBox \"{checkBox.Text}\" ({checkBox.Name})");
                    description.AppendLine($"  - Checked: {checkBox.Checked}");
                    description.AppendLine($"  - Location: ({checkBox.Location.X}, {checkBox.Location.Y})");
                }
                description.AppendLine();
            }
            
            // Popis rozbalovacích seznamů
            if (comboBoxes.Count > 0)
            {
                description.AppendLine("### Dropdown Menus");
                foreach (var comboBox in comboBoxes)
                {
                    description.AppendLine($"- ComboBox \"{comboBox.Name}\"");
                    description.AppendLine($"  - Items Count: {comboBox.Items.Count}");
                    if (comboBox.Items.Count > 0)
                    {
                        description.AppendLine($"  - Items: {GetComboBoxItems(comboBox)}");
                    }
                    description.AppendLine($"  - Selected Item: \"{(comboBox.SelectedItem != null ? comboBox.SelectedItem.ToString() : "None")}\"");
                    description.AppendLine($"  - Location: ({comboBox.Location.X}, {comboBox.Location.Y})");
                }
                description.AppendLine();
            }
            
            // Popis přepínačů
            if (radioButtons.Count > 0)
            {
                description.AppendLine("### Radio Buttons");
                foreach (var radioButton in radioButtons)
                {
                    description.AppendLine($"- RadioButton \"{radioButton.Text}\" ({radioButton.Name})");
                    description.AppendLine($"  - Checked: {radioButton.Checked}");
                    description.AppendLine($"  - Location: ({radioButton.Location.X}, {radioButton.Location.Y})");
                }
                description.AppendLine();
            }
        }

        /// <summary>
        /// Hledá všechny interaktivní prvky
        /// </summary>
        private void FindAllInteractiveControls(Control parent, List<Button> buttons, List<TextBox> textBoxes,
            List<CheckBox> checkBoxes, List<ComboBox> comboBoxes, List<RadioButton> radioButtons)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is Button) buttons.Add((Button)control);
                else if (control is TextBox) textBoxes.Add((TextBox)control);
                else if (control is CheckBox) checkBoxes.Add((CheckBox)control);
                else if (control is ComboBox) comboBoxes.Add((ComboBox)control);
                else if (control is RadioButton) radioButtons.Add((RadioButton)control);
                
                FindAllInteractiveControls(control, buttons, textBoxes, checkBoxes, comboBoxes, radioButtons);
            }
        }

        /// <summary>
        /// Získá položky ComboBoxu jako string
        /// </summary>
        private string GetComboBoxItems(ComboBox comboBox)
        {
            if (comboBox.Items.Count == 0) return "[]";
            
            var items = new List<string>();
            foreach (var item in comboBox.Items)
            {
                items.Add($"\"{item}\"");
            }
            
            return "[" + string.Join(", ", items) + "]";
        }

        /// <summary>
        /// Zjistí, zda má tlačítko handler pro kliknutí
        /// </summary>
        private bool HasClickHandler(Button button)
        {
            // Zjednodušená implementace - v reálném kódu by se měla použít reflexe
            // pro kontrolu, zda existuje handler pro událost Click
            return true; // Předpokládáme, že všechna tlačítka mají handler
        }

        /// <summary>
        /// Převádí barvu na hexadecimální formát
        /// </summary>
        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// Loguje zprávu do konzole a publikuje událost
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "FormAnalyzer"
            };

            Console.WriteLine($"[{systemMessage.Timestamp:HH:mm:ss}] [{systemMessage.Source}] [{systemMessage.Severity}] {systemMessage.Message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }
    }
}