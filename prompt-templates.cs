using System.Collections.Generic;

namespace AutoKiwi.Minimal
{
    /// <summary>
    /// Vylepšené šablony promptů pro LLM
    /// </summary>
    public static class PromptTemplates
    {
        /// <summary>
        /// Šablony promptů pro komunikaci s LLM
        /// </summary>
        public static readonly Dictionary<string, string> Templates = new Dictionary<string, string>
        {
            // Šablona pro generování kódu
            ["GenerateCode"] = @"
# Úkol: Vygeneruj C# Windows Forms aplikaci

## Popis aplikace
{Description}

## Plán vývoje
{DevelopmentPlan}

## Kontext z paměti
{MemoryContext}

## Požadavky na výstup
- Vygeneruj POUZE zdrojový kód, bez vysvětlení nebo komentářů mimo kód
- Kód musí být úplný a spustitelný
- Používej namespace GeneratedApp
- Hlavní třída musí dědit od Form
- Používej moderní C# syntaxi
- Používej System.Windows.Forms a System.Drawing
- Implementuj všechny uvedené prvky a funkce
- Kód by měl být dobře strukturovaný a čitelný
- Zahrň všechny potřebné event handlery
- Zahrň statickou metodu Main s [STAThread] atributem

Vrať pouze zdrojový kód bez jakýchkoliv dalších vysvětlení nebo komentářů mimo kód.
",

            // Šablona pro opravy chyb
            ["RepairCode"] = @"
# Úkol: Oprav chyby v C# kódu

## Popis chyb
{Errors}

## Původní kód
```csharp
{Code}
```

## Požadavky na výstup
- Vrať POUZE opravený zdrojový kód, bez vysvětlení nebo komentářů mimo kód
- Zachovej původní strukturu kódu, pokud není příčinou chyb
- Zaměř se na opravy těchto konkrétních chyb
- Ujisti se, že kód je kompletní a spustitelný
- Zkontroluj, že všechny potřebné using direktivy jsou zahrnuty
- Zkontroluj správné pojmenování proměnných a metod

Vrať pouze opravený zdrojový kód bez jakýchkoliv dalších vysvětlení.
",

            // Šablona pro testování aplikace
            ["TestApplication"] = @"
# Úkol: Otestuj Windows Forms aplikaci

## Původní zadání aplikace
{Description}

## Popis UI a chování aplikace
{FormDescription}

## Tvůj úkol jako testera
1. Zkontroluj, zda UI aplikace odpovídá zadání
2. Zkontroluj, zda všechny požadované prvky jsou přítomny
3. Zkontroluj, zda všechny prvky mají správné vlastnosti a umístění
4. Zkontroluj, zda všechny požadované funkce jsou implementovány
5. Porovnej aplikaci s původním zadáním

## Požadavky na výstup
Vrať výsledek testování v jednom z těchto formátů:
- Pokud aplikace vyhovuje zadání: 'TEST PASS: [důvody]'
- Pokud aplikace nevyhovuje zadání: 'TEST FAIL: [důvody a co chybí nebo nefunguje]'

Buď velmi konkrétní ve své analýze a uveď přesné důvody pro tvé hodnocení.
",

            // Šablona pro opravy problémů z testování
            ["RepairTestIssues"] = @"
# Úkol: Oprav problémy nalezené při testování aplikace

## Výsledky testování
{TestIssues}

## Původní kód
```csharp
{Code}
```

## Požadavky na výstup
- Vrať POUZE opravený zdrojový kód, bez vysvětlení nebo komentářů mimo kód
- Zaměř se na vyřešení problémů uvedených v testovacích výsledcích
- Ujisti se, že opravená aplikace splňuje původní zadání
- Zachovej všechny funkční části kódu
- Ujisti se, že kód je kompletní a spustitelný

Vrať pouze opravený zdrojový kód bez jakýchkoliv dalších vysvětlení.
"
        };
    }
}