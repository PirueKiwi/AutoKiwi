using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoKiwi.Minimal;

namespace AutoKiwi.Memory
{
    /// <summary>
    /// Rozšířená paměť aplikací - ukládá a načítá informace o vygenerovaných aplikacích
    /// Implementuje sémantickou indexaci a kontextové učení podle PDF dokumentu
    /// </summary>
    public class SemanticApplicationMemory : IApplicationMemory
    {
        private readonly IEventBus _eventBus;
        private readonly List<ApplicationProfile> _applications = new List<ApplicationProfile>();
        private readonly string _memoryFolderPath;

        // Sémantický index - mapuje klíčová slova na aplikace
        private Dictionary<string, List<string>> _semanticIndex = new Dictionary<string, List<string>>();

        // Extrahované patterny a znalosti
        private List<CodePattern> _codePatterns = new List<CodePattern>();

        // Statistiky úspěšnosti a využití paměti
        private Dictionary<string, PatternUsageStatistics> _patternUsageStats = new Dictionary<string, PatternUsageStatistics>();

        public SemanticApplicationMemory(IEventBus eventBus)
        {
            _eventBus = eventBus;

            // Vytvoření složky pro ukládání paměti
            _memoryFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoKiwi",
                "Memory");

            Directory.CreateDirectory(_memoryFolderPath);

            // Načtení existujících profilů
            try
            {
                LoadProfiles();
                BuildSemanticIndex();
                ExtractCodePatterns();
                LogMessage($"Načteno {_applications.Count} profilů z paměti");
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při načítání profilů: {ex.Message}", MessageSeverity.Error);
            }

            // Přihlášení k událostem
            _eventBus.Subscribe<ApplicationSavedEvent>(OnApplicationSaved);
            _eventBus.Subscribe<PatternIdentifiedEvent>(OnPatternIdentified);
        }

        /// <summary>
        /// Ukládá informace o aplikaci do paměti
        /// </summary>
        public void SaveApplication(ApplicationProfile profile)
        {
            LogMessage($"Ukládám aplikaci '{profile.Name}' do paměti");

            // Nastavení času vytvoření, pokud není nastaven
            if (profile.CreatedDate == default)
            {
                profile.CreatedDate = DateTime.Now;
            }

            // Generování ID, pokud není nastaveno
            if (string.IsNullOrEmpty(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString();
            }

            // Doplnění dalších metadat
            EnrichApplicationProfile(profile);

            // Přidání profilu do paměti
            _applications.Add(profile);

            // Aktualizace sémantického indexu
            IndexApplication(profile);

            // Extrakce patternů z nové aplikace
            ExtractPatternsFromApplication(profile);

            // Uložení profilu do souboru
            try
            {
                SaveProfileToFile(profile);
                LogMessage($"Profil '{profile.Name}' byl úspěšně uložen do souboru");
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při ukládání profilu do souboru: {ex.Message}", MessageSeverity.Error);
            }

            // Publikování události
            _eventBus.Publish(new ApplicationSavedEvent(profile));
        }

        /// <summary>
        /// Získá relevantní kontext pro novou aplikaci
        /// </summary>
        public async Task<string> GetRelevantContext(string applicationDescription)
        {
            LogMessage("Hledám relevantní kontext pro novou aplikaci");

            if (_applications.Count == 0)
            {
                LogMessage("Žádné aplikace v paměti");
                return string.Empty;
            }

            try
            {
                // Pro větší realističnost a plynulost provádíme operaci asynchronně
                return await Task.Run(() =>
                {
                    // Sémantické vyhledávání - vyhledáme aplikace podle klíčových slov
                    var matchingApps = FindSimilarApplications(applicationDescription, 5);

                    if (matchingApps.Count == 0)
                    {
                        LogMessage("Nenalezeny žádné podobné aplikace");
                        return string.Empty;
                    }

                    // Vytvoření kontextu ze základních informací o podobných aplikacích a nalezených patternech
                    var contextBuilder = new StringBuilder();
                    contextBuilder.AppendLine("## Podobné aplikace v paměti");

                    foreach (var app in matchingApps)
                    {
                        contextBuilder.AppendLine($"### Aplikace: {app.Name} (podobnost: {app.Similarity:P0})");
                        contextBuilder.AppendLine($"Popis: {app.Description}");
                        contextBuilder.AppendLine($"Typ: {app.ApplicationType}");

                        if (app.Features != null && app.Features.Count > 0)
                        {
                            contextBuilder.AppendLine("Funkce:");
                            foreach (var feature in app.Features)
                            {
                                contextBuilder.AppendLine($"- {feature}");
                            }
                        }

                        contextBuilder.AppendLine();
                    }

                    // Přidání nalezených relevantních patternů
                    var relevantPatterns = FindRelevantPatterns(applicationDescription, 3);

                    if (relevantPatterns.Count > 0)
                    {
                        contextBuilder.AppendLine("## Relevantní vzory");

                        foreach (var pattern in relevantPatterns)
                        {
                            contextBuilder.AppendLine($"### {pattern.Name}");
                            contextBuilder.AppendLine($"Popis: {pattern.Description}");
                            contextBuilder.AppendLine("Ukázka implementace:");
                            contextBuilder.AppendLine("```csharp");
                            contextBuilder.AppendLine(pattern.CodeExample);
                            contextBuilder.AppendLine("```");
                            contextBuilder.AppendLine();

                            // Aktualizace statistik využití patternu
                            UpdatePatternUsage(pattern.Id);
                        }
                    }

                    string context = contextBuilder.ToString();
                    LogMessage($"Nalezen kontext, {context.Length} znaků");

                    return context;
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Chyba při hledání kontextu: {ex.Message}", MessageSeverity.Error);
                return string.Empty;
            }
        }

        /// <summary>
        /// Získá statistiky paměti
        /// </summary>
        public MemoryStatistics GetStatistics()
        {
            return new MemoryStatistics
            {
                TotalApplications = _applications.Count,
                ApplicationTypes = _applications
                    .GroupBy(a => a.ApplicationType)
                    .Select(g => new TypeCount { Type = g.Key.ToString(), Count = g.Count() })
                    .ToList(),
                TotalPatterns = _codePatterns.Count,
                IndexedKeywords = _semanticIndex.Count,
                TopPatterns = _patternUsageStats
                    .OrderByDescending(p => p.Value.UsageCount)
                    .Take(5)
                    .Select(p => new PatternUsage
                    {
                        PatternId = p.Key,
                        PatternName = GetPatternName(p.Key),
                        UsageCount = p.Value.UsageCount,
                        SuccessRate = p.Value.SuccessRate
                    })
                    .ToList()
            };
        }

        /// <summary>
        /// Hledá podobné aplikace na základě popisu
        /// </summary>
        public List<SimilarApplication> FindSimilarApplications(string description, int maxCount = 3)
        {
            // Extrakce klíčových slov z popisu
            var keywords = ExtractKeywords(description);

            // Hledání aplikací podle klíčových slov
            var matchingAppIds = new Dictionary<string, int>();

            foreach (var keyword in keywords)
            {
                if (_semanticIndex.TryGetValue(keyword, out var appIds))
                {
                    foreach (var appId in appIds)
                    {
                        if (matchingAppIds.ContainsKey(appId))
                        {
                            matchingAppIds[appId]++;
                        }
                        else
                        {
                            matchingAppIds[appId] = 1;
                        }
                    }
                }
            }

            // Seřazení aplikací podle počtu shod a výpočet podobnosti
            return matchingAppIds
                .OrderByDescending(m => m.Value)
                .Take(maxCount)
                .Select(m =>
                {
                    var app = _applications.First(a => a.Id == m.Key);
                    double similarity = (double)m.Value / keywords.Count;

                    return new SimilarApplication
                    {
                        Id = app.Id,
                        Name = app.Name,
                        Description = app.Description,
                        ApplicationType = app.ApplicationType,
                        Features = app.Features,
                        CreatedDate = app.CreatedDate,
                        Similarity = similarity
                    };
                })
                .ToList();
        }

        /// <summary>
        /// Hledá relevantní patterny pro daný popis
        /// </summary>
        public List<CodePattern> FindRelevantPatterns(string description, int maxCount = 3)
        {
            // Extrakce klíčových slov z popisu
            var keywords = ExtractKeywords(description);

            // Skórování patternů podle shody klíčových slov
            var patternScores = new Dictionary<string, int>();

            foreach (var pattern in _codePatterns)
            {
                int score = 0;

                foreach (var keyword in keywords)
                {
                    if (pattern.Tags.Contains(keyword, StringComparer.OrdinalIgnoreCase) ||
                        pattern.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                        pattern.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        score++;
                    }
                }

                if (score > 0)
                {
                    patternScores[pattern.Id] = score;
                }
            }

            // Seřazení patternů podle skóre a úspěšnosti
            return patternScores
                .OrderByDescending(p => p.Value)
                .ThenByDescending(p => GetPatternSuccessRate(p.Key))
                .Take(maxCount)
                .Select(p => _codePatterns.First(cp => cp.Id == p.Key))
                .ToList();
        }

        /// <summary>
        /// Extrahuje klíčová slova z textu
        /// </summary>
        private List<string> ExtractKeywords(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new List<string>();
            }

            // Převod na malá písmena a rozdělení na slova
            var words = text.ToLower()
                .Split(new[] { ' ', ',', '.', ';', ':', '!', '?', '\r', '\n', '\t', '(', ')', '[', ']', '{', '}' },
                    StringSplitOptions.RemoveEmptyEntries);

            // Odstranění běžných stop slov
            var stopWords = new HashSet<string>
            {
                "a", "an", "the", "and", "or", "but", "is", "are", "was", "were",
                "be", "been", "being", "have", "has", "had", "do", "does", "did",
                "to", "from", "in", "out", "on", "off", "over", "under", "again",
                "s", "t", "m", "ve", "ll", "d", "pro", "na", "v", "k", "o", "u", "do"
            };

            // Filtrace slov - pouze slova delší než 2 znaky a která nejsou stop slovy
            return words
                .Where(w => w.Length > 2 && !stopWords.Contains(w))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Obohacuje profil aplikace o další metadata
        /// </summary>
        private void EnrichApplicationProfile(ApplicationProfile profile)
        {
            // Generování tagů z popisu, pokud ještě nejsou definovány
            if (profile.Tags == null || profile.Tags.Count == 0)
            {
                profile.Tags = ExtractKeywords(profile.Description);
            }

            // Extrakce métriky kódu
            profile.Metrics = CalculateCodeMetrics(profile.SourceCode);

            // Identifikace použitých komponent
            profile.UsedComponents = IdentifyUsedComponents(profile.SourceCode);
        }

        /// <summary>
        /// Načte všechny profily z paměti
        /// </summary>
        private void LoadProfiles()
        {
            _applications.Clear();

            var profileFiles = Directory.GetFiles(_memoryFolderPath, "*.json");
            foreach (var file in profileFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<ApplicationProfile>(json);

                    if (profile != null)
                    {
                        _applications.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Chyba při načítání profilu {file}: {ex.Message}", MessageSeverity.Warning);
                }
            }
        }

        /// <summary>
        /// Vytváří sémantický index pro rychlé vyhledávání
        /// </summary>
        private void BuildSemanticIndex()
        {
            _semanticIndex.Clear();

            foreach (var app in _applications)
            {
                IndexApplication(app);
            }
        }

        /// <summary>
        /// Indexuje aplikaci do sémantického indexu
        /// </summary>
        private void IndexApplication(ApplicationProfile app)
        {
            // Získání všech klíčových slov z aplikace
            var keywords = new HashSet<string>();

            // Z popisu
            keywords.UnionWith(ExtractKeywords(app.Description));

            // Z tagů
            if (app.Tags != null)
            {
                keywords.UnionWith(app.Tags);
            }

            // Z funkcí
            if (app.Features != null)
            {
                keywords.UnionWith(app.Features.SelectMany(f => ExtractKeywords(f)));
            }

            // Z názvu
            keywords.UnionWith(ExtractKeywords(app.Name));

            // Přidání do indexu
            foreach (var keyword in keywords)
            {
                if (!_semanticIndex.ContainsKey(keyword))
                {
                    _semanticIndex[keyword] = new List<string>();
                }

                if (!_semanticIndex[keyword].Contains(app.Id))
                {
                    _semanticIndex[keyword].Add(app.Id);
                }
            }
        }

        /// <summary>
        /// Extrahuje kódové patterny ze všech aplikací
        /// </summary>
        private void ExtractCodePatterns()
        {
            _codePatterns.Clear();

            foreach (var app in _applications)
            {
                ExtractPatternsFromApplication(app);
            }
        }

        /// <summary>
        /// Extrahuje patterny z jedné aplikace
        /// </summary>
        private void ExtractPatternsFromApplication(ApplicationProfile app)
        {
            if (string.IsNullOrEmpty(app.SourceCode))
            {
                return;
            }

            // Pokus o identifikaci známých patternů
            IdentifyKnownPatterns(app);

            // Extrakce metod jako potenciálních patternů
            ExtractMethodsAsPatterns(app);

            // Identifikace UI komponent a jejich vzorů
            IdentifyUiPatterns(app);
        }

        /// <summary>
        /// Identifikuje známé patterny v kódu
        /// </summary>
        private void IdentifyKnownPatterns(ApplicationProfile app)
        {
            // Seznam známých patternů k identifikaci
            var knownPatterns = new List<(string Name, string Regex, string Tag)>
            {
                ("Event Handler Pattern", @"private\s+void\s+\w+_(\w+)\s*\(\s*object\s+\w+\s*,\s*\w+\s+\w+\s*\)", "EventHandling"),
                ("Data Binding Pattern", @"dataGridView\d+\.DataSource\s*=", "DataBinding"),
                ("File Dialog Pattern", @"(Open|Save)FileDialog\s+\w+\s*=\s*new\s+(Open|Save)FileDialog", "FileOperations"),
                ("Message Box Pattern", @"MessageBox\.Show\s*\(", "UserInteraction"),
                ("Database Connection Pattern", @"SqlConnection|OleDbConnection|SQLiteConnection", "Database"),
                ("Thread Start Pattern", @"Thread\s+\w+\s*=\s*new\s+Thread|Task\.Run|Task\.Factory\.StartNew", "Threading"),
                ("Singleton Pattern", @"private\s+static\s+\w+\s+\w+Instance\s*;|private\s+\w+\s*\(\s*\)\s*{", "Singleton"),
                ("Factory Pattern", @"class\s+\w+Factory|Create\w+\s*\(", "Factory"),
                ("Observer Pattern", @"event\s+\w+|EventHandler<\w+>", "Observer")
            };

            foreach (var pattern in knownPatterns)
            {
                if (Regex.IsMatch(app.SourceCode, pattern.Regex))
                {
                    // Identifikace patternu
                    string patternId = $"{pattern.Name}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    string patternName = pattern.Name;
                    string patternExample = ExtractPatternExample(app.SourceCode, pattern.Regex);

                    // Vytvoření nového patternu, pokud ještě neexistuje podobný
                    if (!_codePatterns.Any(p => p.Name == patternName && p.CodeExample == patternExample))
                    {
                        var codePattern = new CodePattern
                        {
                            Id = patternId,
                            Name = patternName,
                            Description = $"Pattern identified in application {app.Name}",
                            CodeExample = patternExample,
                            Tags = new List<string> { pattern.Tag },
                            ApplicationIds = new List<string> { app.Id }
                        };

                        _codePatterns.Add(codePattern);

                        // Publikujeme událost o identifikaci patternu
                        _eventBus.Publish(new PatternIdentifiedEvent(codePattern, app.Id));
                    }
                    else
                    {
                        // Přidání aplikace k existujícímu patternu
                        var existingPattern = _codePatterns.First(p => p.Name == patternName && p.CodeExample == patternExample);
                        if (!existingPattern.ApplicationIds.Contains(app.Id))
                        {
                            existingPattern.ApplicationIds.Add(app.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extrahuje metody jako potenciální patterny
        /// </summary>
        private void ExtractMethodsAsPatterns(ApplicationProfile app)
        {
            var methodMatches = Regex.Matches(app.SourceCode,
                @"(public|private|protected|internal)\s+(static\s+)?\w+\s+(\w+)\s*\(([^)]*)\)\s*{([^{}]|{[^{}]*})*}",
                RegexOptions.Singleline);

            foreach (Match match in methodMatches)
            {
                if (match.Groups.Count >= 4)
                {
                    string methodName = match.Groups[3].Value;
                    string methodCode = match.Value;

                    // Ignorování běžných metod jako InitializeComponent
                    if (methodName == "InitializeComponent" || methodName.StartsWith("get_") || methodName.StartsWith("set_"))
                    {
                        continue;
                    }

                    // Identifikace patternu
                    string patternId = $"Method_{methodName}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    string patternName = $"Method Pattern: {methodName}";

                    // Vytvoření nového patternu, pokud ještě neexistuje podobný
                    if (!_codePatterns.Any(p => p.Name == patternName || StringSimilarity(p.CodeExample, methodCode) > 0.8))
                    {
                        var codePattern = new CodePattern
                        {
                            Id = patternId,
                            Name = patternName,
                            Description = $"Method pattern from {app.Name}",
                            CodeExample = methodCode,
                            Tags = new List<string> { "Method", methodName },
                            ApplicationIds = new List<string> { app.Id }
                        };

                        _codePatterns.Add(codePattern);
                    }
                    else
                    {
                        // Přidání aplikace k existujícímu patternu
                        var existingPattern = _codePatterns.First(p => p.Name == patternName || StringSimilarity(p.CodeExample, methodCode) > 0.8);
                        if (!existingPattern.ApplicationIds.Contains(app.Id))
                        {
                            existingPattern.ApplicationIds.Add(app.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Identifikuje UI patterny v kódu
        /// </summary>
        private void IdentifyUiPatterns(ApplicationProfile app)
        {
            if (app.ApplicationType != ApplicationType.WinForms)
            {
                return;
            }

            // Hledání vzorů uspořádání komponent
            var layoutPatterns = new List<(string Name, string Regex, string Tag)>
            {
                ("Grid Layout Pattern", @"TableLayoutPanel\s+\w+\s*=\s*new\s+TableLayoutPanel", "Layout"),
                ("Flow Layout Pattern", @"FlowLayoutPanel\s+\w+\s*=\s*new\s+FlowLayoutPanel", "Layout"),
                ("Dock Fill Pattern", @"Dock\s*=\s*DockStyle\.Fill", "Layout"),
                ("Anchor Pattern", @"Anchor\s*=\s*AnchorStyles\.", "Layout"),
                ("Tab Control Pattern", @"TabControl\s+\w+\s*=\s*new\s+TabControl", "Layout"),
                ("Split Container Pattern", @"SplitContainer\s+\w+\s*=\s*new\s+SplitContainer", "Layout")
            };

            foreach (var pattern in layoutPatterns)
            {
                if (Regex.IsMatch(app.SourceCode, pattern.Regex))
                {
                    // Identifikace patternu
                    string patternId = $"{pattern.Name}_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                    string patternName = pattern.Name;
                    string patternExample = ExtractPatternExample(app.SourceCode, pattern.Regex);

                    // Vytvoření nového patternu, pokud ještě neexistuje podobný
                    if (!_codePatterns.Any(p => p.Name == patternName && StringSimilarity(p.CodeExample, patternExample) > 0.7))
                    {
                        var codePattern = new CodePattern
                        {
                            Id = patternId,
                            Name = patternName,
                            Description = $"UI layout pattern from {app.Name}",
                            CodeExample = patternExample,
                            Tags = new List<string> { pattern.Tag, "UI" },
                            ApplicationIds = new List<string> { app.Id }
                        };

                        _codePatterns.Add(codePattern);

                        // Publikujeme událost o identifikaci patternu
                        _eventBus.Publish(new PatternIdentifiedEvent(codePattern, app.Id));
                    }
                    else
                    {
                        // Přidání aplikace k existujícímu patternu
                        var existingPattern = _codePatterns.First(p => p.Name == patternName && StringSimilarity(p.CodeExample, patternExample) > 0.7);
                        if (!existingPattern.ApplicationIds.Contains(app.Id))
                        {
                            existingPattern.ApplicationIds.Add(app.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Extrahuje příklad patternu z kódu
        /// </summary>
        private string ExtractPatternExample(string code, string patternRegex)
        {
            var match = Regex.Match(code, patternRegex);
            if (match.Success)
            {
                // Extrakce kontextu - několik řádků před a po
                int start = Math.Max(0, match.Index - 200);
                int end = Math.Min(code.Length, match.Index + match.Length + 200);

                // Najdeme začátek řádku
                while (start > 0 && code[start] != '\n')
                {
                    start--;
                }

                // Najdeme konec řádku
                while (end < code.Length && code[end] != '\n')
                {
                    end++;
                }

                return code.Substring(start, end - start).Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Uloží profil do souboru
        /// </summary>
        private void SaveProfileToFile(ApplicationProfile profile)
        {
            string filePath = Path.Combine(_memoryFolderPath, $"{profile.Id}.json");

            // Serializace profilu do JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(profile, options);

            // Uložení do souboru
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Vypočítá metriky kódu
        /// </summary>
        private CodeMetrics CalculateCodeMetrics(string sourceCode)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                return new CodeMetrics();
            }

            var metrics = new CodeMetrics();

            // Počet řádků kódu
            metrics.LinesOfCode = sourceCode.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            // Počet tříd
            metrics.ClassCount = Regex.Matches(sourceCode, @"class\s+\w+").Count;

            // Počet metod
            metrics.MethodCount = Regex.Matches(sourceCode, @"(public|private|protected|internal)\s+(static\s+)?\w+\s+\w+\s*\(").Count;

            // Počet proměnných
            metrics.VariableCount = Regex.Matches(sourceCode, @"(var|int|string|bool|double|float|decimal|char|byte|object|Form)\s+\w+").Count;

            // Počet řádků komentářů
            metrics.CommentLines = Regex.Matches(sourceCode, @"^\s*//.*$", RegexOptions.Multiline).Count;

            return metrics;
        }

        /// <summary>
        /// Identifikuje použité komponenty v kódu
        /// </summary>
        private List<string> IdentifyUsedComponents(string sourceCode)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                return new List<string>();
            }

            var components = new HashSet<string>();

            // Hledání Windows Forms komponent
            var controlMatches = Regex.Matches(sourceCode, @"(Button|TextBox|Label|ComboBox|CheckBox|RadioButton|DataGridView|TreeView|ListView|MenuStrip|ToolStrip|StatusStrip|TabControl|Panel|GroupBox)\s+\w+");

            foreach (Match match in controlMatches)
            {
                if (match.Groups.Count > 1)
                {
                    components.Add(match.Groups[1].Value);
                }
            }

            return components.ToList();
        }

        /// <summary>
        /// Výpočet podobnosti dvou řetězců (Jaccard index)
        /// </summary>
        private double StringSimilarity(string str1, string str2)
        {
            if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            {
                return 0;
            }

            // Rozdělení na slova
            var words1 = new HashSet<string>(str1.Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            var words2 = new HashSet<string>(str2.Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));

            // Výpočet průniku a sjednocení
            int intersection = words1.Intersect(words2).Count();
            int union = words1.Union(words2).Count();

            // Jaccard index
            return union > 0 ? (double)intersection / union : 0;
        }

        /// <summary>
        /// Aktualizuje statistiky využití patternu
        /// </summary>
        private void UpdatePatternUsage(string patternId)
        {
            if (!_patternUsageStats.ContainsKey(patternId))
            {
                _patternUsageStats[patternId] = new PatternUsageStatistics
                {
                    UsageCount = 0,
                    SuccessCount = 0,
                    SuccessRate = 0.5 // Výchozí hodnota
                };
            }

            _patternUsageStats[patternId].UsageCount++;
        }

        /// <summary>
        /// Zaznamenává úspěšnost použití patternu
        /// </summary>
        public void RecordPatternSuccess(string patternId, bool success)
        {
            if (!_patternUsageStats.ContainsKey(patternId))
            {
                _patternUsageStats[patternId] = new PatternUsageStatistics
                {
                    UsageCount = 1,
                    SuccessCount = success ? 1 : 0,
                    SuccessRate = success ? 1.0 : 0.0
                };
            }
            else
            {
                var stats = _patternUsageStats[patternId];
                stats.UsageCount++;
                if (success)
                {
                    stats.SuccessCount++;
                }

                stats.SuccessRate = (double)stats.SuccessCount / stats.UsageCount;
            }
        }

        /// <summary>
        /// Získá název patternu podle ID
        /// </summary>
        private string GetPatternName(string patternId)
        {
            var pattern = _codePatterns.FirstOrDefault(p => p.Id == patternId);
            return pattern != null ? pattern.Name : patternId;
        }

        /// <summary>
        /// Získá úspěšnost patternu podle ID
        /// </summary>
        private double GetPatternSuccessRate(string patternId)
        {
            if (_patternUsageStats.TryGetValue(patternId, out var stats))
            {
                return stats.SuccessRate;
            }

            return 0.5; // Výchozí hodnota
        }

        /// <summary>
        /// Event handler pro událost ApplicationSavedEvent
        /// </summary>
        private void OnApplicationSaved(ApplicationSavedEvent evt)
        {
            // Aktualizace indexu a patternů
            ExtractPatternsFromApplication(evt.Application);
        }

        /// <summary>
        /// Event handler pro událost PatternIdentifiedEvent
        /// </summary>
        private void OnPatternIdentified(PatternIdentifiedEvent evt)
        {
            // Zde bychom mohli implementovat další logiku, například aktualizaci statistik
        }

        /// <summary>
        /// Loguje zprávu
        /// </summary>
        private void LogMessage(string message, MessageSeverity severity = MessageSeverity.Info)
        {
            var systemMessage = new SystemMessage
            {
                Message = message,
                Severity = severity,
                Timestamp = DateTime.Now,
                Source = "SemanticMemory"
            };

            Console.WriteLine($"[{systemMessage.Timestamp:HH:mm:ss}] [{systemMessage.Source}] [{systemMessage.Severity}] {systemMessage.Message}");

            // Publikování události
            _eventBus.Publish(new SystemMessageEvent(systemMessage));
        }

        public void SaveApplication(Minimal.ApplicationProfile profile)
        {
            throw new NotImplementedException();
        }
    }

    #region Podpůrné třídy

    /// <summary>
    /// Pattern identifikovaný v kódu
    /// </summary>
    public class CodePattern
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CodeExample { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> ApplicationIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Statistiky využití patternu
    /// </summary>
    public class PatternUsageStatistics
    {
        public int UsageCount { get; set; }
        public int SuccessCount { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Podobná aplikace pro výstup
    /// </summary>
    public class SimilarApplication
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ApplicationType ApplicationType { get; set; }
        public List<string> Features { get; set; }
        public DateTime CreatedDate { get; set; }
        public double Similarity { get; set; }
    }

    /// <summary>
    /// Metriky kódu
    /// </summary>
    public class CodeMetrics
    {
        public int LinesOfCode { get; set; }
        public int ClassCount { get; set; }
        public int MethodCount { get; set; }
        public int VariableCount { get; set; }
        public int CommentLines { get; set; }
    }

    /// <summary>
    /// Statistiky paměti
    /// </summary>
    public class MemoryStatistics
    {
        public int TotalApplications { get; set; }
        public List<TypeCount> ApplicationTypes { get; set; } = new List<TypeCount>();
        public int TotalPatterns { get; set; }
        public int IndexedKeywords { get; set; }
        public List<PatternUsage> TopPatterns { get; set; } = new List<PatternUsage>();
    }

    /// <summary>
    /// Počet typů aplikací
    /// </summary>
    public class TypeCount
    {
        public string Type { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// Využití patternu
    /// </summary>
    public class PatternUsage
    {
        public string PatternId { get; set; }
        public string PatternName { get; set; }
        public int UsageCount { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Událost publikovaná při identifikaci patternu
    /// </summary>
    public class PatternIdentifiedEvent
    {
        public CodePattern Pattern { get; }
        public string ApplicationId { get; }

        public PatternIdentifiedEvent(CodePattern pattern, string applicationId)
        {
            Pattern = pattern;
            ApplicationId = applicationId;
        }
    }

    #endregion

    #region Rozšíření třídy ApplicationProfile

    /// <summary>
    /// Rozšíření třídy ApplicationProfile o další vlastnosti
    /// </summary>
    public class ApplicationProfile
    {

        public string Id { get; set; }
        public string Name { get; set; }
        public ApplicationType ApplicationType { get; set; }
        public string Description { get; set; }
        public string SourceCode { get; set; }
        public DateTime CreatedDate { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> Features { get; set; } = new List<string>();

        public CodeMetrics Metrics { get; set; } = new CodeMetrics();
        public List<string> UsedComponents { get; set; } = new List<string>();
    }

    #endregion
}