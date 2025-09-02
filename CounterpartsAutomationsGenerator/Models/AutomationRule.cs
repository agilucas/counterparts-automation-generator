namespace CounterpartsAutomationsGenerator.Models;

public enum KeywordMatchingMode
{
    All,    // Tous les keywords doivent être présents
    OneOf   // Au moins un des keywords doit être présent
}

public class AutomationRule
{
    public int Priority { get; set; }
    public string CreditOrDebit { get; set; } = string.Empty;
    public string AccountingAccount { get; set; } = string.Empty;
    public string? ThirdPartyCode { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string? Keyword1 { get; set; }
    public string? Keyword2 { get; set; }
    public string? Keyword3 { get; set; }
    public KeywordMatchingMode KeywordMatching { get; set; } = KeywordMatchingMode.OneOf;
    public string? RestrictedBankAccounts { get; set; }
    public double MinConfidence { get; set; }
    public int Coverage { get; set; } // Nombre d'occurrences couvertes
    public double Precision { get; set; } // Précision calculée
    public List<string> Examples { get; set; } = new(); // Stocker les exemples pour la résolution de conflits
}

public class RuleGenerationRequest
{
    public AccountingEntryFile DebitEntries { get; set; } = new();
    public AccountingEntryFile CreditEntries { get; set; } = new();
}

public class PatternAnalysis
{
    public string Pattern { get; set; } = string.Empty;
    public string AccountingAccount { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public List<string> Examples { get; set; } = new();
    public string Direction { get; set; } = string.Empty;
}

public class EntityInfo
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public HashSet<string> AccountingAccounts { get; set; } = new();
}