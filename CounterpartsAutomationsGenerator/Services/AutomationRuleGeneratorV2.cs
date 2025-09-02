using CounterpartsAutomationsGenerator.Models;
using System.Text.RegularExpressions;
using System.Text;

namespace CounterpartsAutomationsGenerator.Services;

public class AutomationRuleGeneratorV2
{
    public List<AutomationRule> GenerateRules(RuleGenerationRequest request)
    {
        // Step 1: Basic implementation - handle empty data
        if (!HasValidData(request))
        {
            return new List<AutomationRule>();
        }

        var rules = new List<AutomationRule>();
        int priority = 1;

        // Combine all entries for analysis
        var allEntries = request.DebitEntries.Entries
            .Concat(request.CreditEntries.Entries)
            .ToList();

        // Step 2: Intelligent preliminary analysis
        var datasetAnalysis = PerformDatasetAnalysis(allEntries);
        var adaptiveThreshold = CalculateAdaptiveThreshold(allEntries.Count);

        // Enhanced entity extraction with business logic
        var entityCandidates = ExtractBusinessEntities(allEntries, adaptiveThreshold);

        // Generate rules with improved prioritization
        foreach (var entity in entityCandidates)
        {
            var rule = CreateEntityRule(entity, priority++, datasetAnalysis);
            if (rule != null)
            {
                rules.Add(rule);
            }
        }

        return rules.OrderBy(r => r.Priority).ToList();
    }

    private bool HasValidData(RuleGenerationRequest request)
    {
        return request.DebitEntries?.Entries?.Any() == true || 
               request.CreditEntries?.Entries?.Any() == true;
    }


    private List<string> ExtractSignificantWords(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return new List<string>();

        // Basic word extraction - look for words with 3+ characters
        var words = Regex.Split(label, @"\W+")
            .Where(w => w.Length >= 3)
            .Where(w => !IsGenericWord(w))
            .Select(w => w.ToUpper())
            .Distinct()
            .ToList();

        return words;
    }

    private bool IsGenericWord(string word)
    {
        var genericWords = new HashSet<string>
        {
            "VIR", "SEPA", "FACT", "CB", "PRLV", "DATE", "NUM", "REF", "CIB"
        };
        
        return genericWords.Contains(word.ToUpper()) || 
               Regex.IsMatch(word, @"^\d+$"); // Exclude pure numbers
    }


    private DatasetAnalysis PerformDatasetAnalysis(List<AccountingEntry> entries)
    {
        var allLabels = entries.SelectMany(e => new[] { e.Label }).ToList();
        var uniqueLabels = allLabels.Distinct().ToList();
        var counterpartAccounts = entries
            .SelectMany(e => e.Counterparts.Select(c => c.AccountingAccount))
            .Distinct()
            .ToList();

        return new DatasetAnalysis
        {
            TotalEntries = entries.Count,
            UniqueLabels = uniqueLabels,
            UniqueAccounts = counterpartAccounts,
            AccountFrequency = entries
                .SelectMany(e => e.Counterparts.Select(c => c.AccountingAccount))
                .GroupBy(a => a)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private int CalculateAdaptiveThreshold(int datasetSize)
    {
        // Adaptive thresholds based on dataset size
        return datasetSize switch
        {
            < 50 => 1,      // Very permissive for small datasets
            < 200 => 3,     // Moderate for medium datasets
            _ => 5          // Strict for large datasets
        };
    }

    private List<EntityCandidate> ExtractBusinessEntities(List<AccountingEntry> entries, int threshold)
    {
        var entityCandidates = new Dictionary<string, EntityCandidate>();

        foreach (var entry in entries)
        {
            // Extract business entities with improved patterns
            var entities = ExtractBusinessEntityNames(entry.Label);
            
            foreach (var entity in entities)
            {
                if (!entityCandidates.ContainsKey(entity))
                {
                    entityCandidates[entity] = new EntityCandidate
                    {
                        Name = entity,
                        Frequency = 0,
                        AccountingAccounts = new List<string>(),
                        Direction = entry.Direction,
                        BusinessCategory = DetermineBusinessCategory(entity),
                        ExampleLabels = new List<string>()
                    };
                }

                entityCandidates[entity].Frequency++;
                
                // Add example labels (limit to 5 examples)
                if (entityCandidates[entity].ExampleLabels.Count < 5 && 
                    !entityCandidates[entity].ExampleLabels.Contains(entry.Label))
                {
                    entityCandidates[entity].ExampleLabels.Add(entry.Label);
                }
                
                // Add counterpart accounting accounts
                foreach (var counterpart in entry.Counterparts)
                {
                    if (!entityCandidates[entity].AccountingAccounts.Contains(counterpart.AccountingAccount))
                    {
                        entityCandidates[entity].AccountingAccounts.Add(counterpart.AccountingAccount);
                    }
                }
            }
        }

        // Filter with adaptive threshold and prioritize by business relevance
        return entityCandidates.Values
            .Where(e => e.Frequency >= threshold)
            .OrderByDescending(e => GetBusinessPriority(e.BusinessCategory))
            .ThenByDescending(e => e.Frequency)
            .ToList();
    }

    private List<string> ExtractBusinessEntityNames(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return new List<string>();

        var entities = new List<string>();

        // Public institutions patterns (high priority)
        var publicInstitutions = new[] { "APICIL", "CPAM", "URSSAF", "CAF", "POLE EMPLOI" };
        foreach (var institution in publicInstitutions)
        {
            if (label.Contains(institution, StringComparison.OrdinalIgnoreCase))
            {
                entities.Add(institution);
            }
        }

        // Bank patterns
        var banks = new[] { "BNP", "CREDIT AGRICOLE", "SOCIETE GENERALE", "CIC", "BANQUE POPULAIRE" };
        foreach (var bank in banks)
        {
            if (label.Contains(bank, StringComparison.OrdinalIgnoreCase))
            {
                entities.Add(bank);
            }
        }

        // If no specific entities found, extract significant words (but deprioritize them)
        if (entities.Count == 0)
        {
            var significantWords = ExtractSignificantWords(label);
            entities.AddRange(significantWords.Take(1)); // Only take the most significant
        }

        return entities;
    }

    private BusinessCategory DetermineBusinessCategory(string entityName)
    {
        var publicInstitutions = new[] { "APICIL", "CPAM", "URSSAF", "CAF", "POLE EMPLOI" };
        var banks = new[] { "BNP", "CREDIT AGRICOLE", "SOCIETE GENERALE", "CIC" };

        if (publicInstitutions.Any(p => entityName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return BusinessCategory.PublicInstitution;
        
        if (banks.Any(b => entityName.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return BusinessCategory.Bank;
        
        return BusinessCategory.Generic;
    }

    private int GetBusinessPriority(BusinessCategory category)
    {
        return category switch
        {
            BusinessCategory.PublicInstitution => 3,
            BusinessCategory.Bank => 2,
            BusinessCategory.Generic => 1,
            _ => 1
        };
    }

    private AutomationRule? CreateEntityRule(EntityCandidate entity, int priority, DatasetAnalysis analysis)
    {
        if (entity.AccountingAccounts.Count == 0)
            return null;

        // Use the most frequent accounting account
        var primaryAccount = entity.AccountingAccounts
            .GroupBy(a => a)
            .OrderByDescending(g => g.Count())
            .First().Key;

        // Adjust priority based on business category
        var adjustedPriority = priority - GetBusinessPriority(entity.BusinessCategory);

        return new AutomationRule
        {
            Priority = Math.Max(1, adjustedPriority),
            CreditOrDebit = entity.Direction,
            AccountingAccount = primaryAccount,
            RuleName = $"{entity.BusinessCategory}_{entity.Name}",
            Keyword1 = entity.Name,
            MinConfidence = GetConfidenceByCategory(entity.BusinessCategory),
            Coverage = entity.Frequency,
            Precision = CalculatePrecisionEstimate(entity, analysis),
            Examples = entity.ExampleLabels
        };
    }

    private double GetConfidenceByCategory(BusinessCategory category)
    {
        return category switch
        {
            BusinessCategory.PublicInstitution => 0.95,
            BusinessCategory.Bank => 0.90,
            BusinessCategory.Generic => 0.80,
            _ => 0.80
        };
    }

    private double CalculatePrecisionEstimate(EntityCandidate entity, DatasetAnalysis analysis)
    {
        // Higher precision for entities with consistent accounting accounts
        var accountConsistency = entity.AccountingAccounts.Count == 1 ? 1.0 : 0.8;
        var categoryBonus = GetBusinessPriority(entity.BusinessCategory) * 0.05;
        
        return Math.Min(0.95, 0.75 + accountConsistency * 0.15 + categoryBonus);
    }

    private enum BusinessCategory
    {
        PublicInstitution,
        Bank,
        Generic
    }

    private class EntityCandidate
    {
        public string Name { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public List<string> AccountingAccounts { get; set; } = new();
        public string Direction { get; set; } = string.Empty;
        public BusinessCategory BusinessCategory { get; set; }
        public List<string> ExampleLabels { get; set; } = new();
    }

    public string ExportToCsv(List<AutomationRule> rules)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Priority,CreditOrDebit,AccountingAccount,ThirdPartyCode,RuleName,Keyword1,Keyword2,Keyword3,KeywordMatching,RestrictedBankAccounts,MinConfidence");

        foreach (var rule in rules)
        {
            csv.AppendLine($"{rule.Priority},{rule.CreditOrDebit},{rule.AccountingAccount}," +
                          $"{rule.ThirdPartyCode ?? ""}," +
                          $"\"{rule.RuleName}\"," +
                          $"\"{rule.Keyword1 ?? ""}\"," +
                          $"\"{rule.Keyword2 ?? ""}\"," +
                          $"\"{rule.Keyword3 ?? ""}\"," +
                          $"{rule.KeywordMatching.ToString().ToLower()}," +
                          $"\"{rule.RestrictedBankAccounts ?? ""}\"," +
                          $"{rule.MinConfidence:F2}");
        }

        return csv.ToString();
    }

    private class DatasetAnalysis
    {
        public int TotalEntries { get; set; }
        public List<string> UniqueLabels { get; set; } = new();
        public List<string> UniqueAccounts { get; set; } = new();
        public Dictionary<string, int> AccountFrequency { get; set; } = new();
    }
}