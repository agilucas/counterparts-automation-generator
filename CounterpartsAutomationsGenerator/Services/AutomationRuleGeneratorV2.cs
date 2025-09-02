using CounterpartsAutomationsGenerator.Models;
using System.Text.RegularExpressions;
using System.Text;

namespace CounterpartsAutomationsGenerator.Services;

public class AutomationRuleGeneratorV2
{
    private readonly IAdaptiveThresholdService _thresholdService;

    public AutomationRuleGeneratorV2(IAdaptiveThresholdService thresholdService)
    {
        _thresholdService = thresholdService;
    }

    // Constructor for backward compatibility and tests
    public AutomationRuleGeneratorV2() : this(new AdaptiveThresholdService())
    {
    }

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
        var adaptiveThreshold = _thresholdService.CalculateFrequencyThreshold(allEntries.Count);

        // Enhanced entity extraction with business logic
        var entityCandidates = ExtractBusinessEntities(allEntries, adaptiveThreshold);
        
        // Extract operation patterns
        var operationCandidates = ExtractOperationPatterns(allEntries, adaptiveThreshold);

        // Generate rules with improved prioritization
        foreach (var entity in entityCandidates)
        {
            var generatedRules = CreateEntityRules(entity, priority, datasetAnalysis);
            rules.AddRange(generatedRules);
            priority += generatedRules.Count;
        }
        
        // Generate operation pattern rules
        foreach (var operation in operationCandidates)
        {
            var operationRules = CreateOperationRules(operation, priority, datasetAnalysis);
            rules.AddRange(operationRules);
            priority += operationRules.Count;
        }

        // Step 7: Apply adaptive thresholds and validation
        var validatedRules = ApplyAdaptiveValidation(rules, allEntries, datasetAnalysis);

        return validatedRules.OrderBy(r => r.Priority).ToList();
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
                        ExampleLabels = new List<string>(),
                        ThirdPartyCodes = new Dictionary<string, int>()
                    };
                }

                entityCandidates[entity].Frequency++;
                
                // Add example labels (limit to 5 examples)
                if (entityCandidates[entity].ExampleLabels.Count < 5 && 
                    !entityCandidates[entity].ExampleLabels.Contains(entry.Label))
                {
                    entityCandidates[entity].ExampleLabels.Add(entry.Label);
                }
                
                // Add counterpart accounting accounts and third party codes
                foreach (var counterpart in entry.Counterparts)
                {
                    if (!entityCandidates[entity].AccountingAccounts.Contains(counterpart.AccountingAccount))
                    {
                        entityCandidates[entity].AccountingAccounts.Add(counterpart.AccountingAccount);
                    }
                    
                    // Track bank account patterns for restrictions
                    var bankAccountName = entry.BankAccountName ?? string.Empty;
                    if (!string.IsNullOrEmpty(bankAccountName))
                    {
                        if (!entityCandidates[entity].BankAccountPatterns.ContainsKey(bankAccountName))
                        {
                            entityCandidates[entity].BankAccountPatterns[bankAccountName] = new BankAccountPattern
                            {
                                BankAccount = bankAccountName,
                                CounterpartAccounts = new List<string>(),
                                Frequency = 0,
                                ThirdPartyCodes = new Dictionary<string, int>()
                            };
                        }

                        var pattern = entityCandidates[entity].BankAccountPatterns[bankAccountName];
                        pattern.Frequency++;

                        if (!pattern.CounterpartAccounts.Contains(counterpart.AccountingAccount))
                        {
                            pattern.CounterpartAccounts.Add(counterpart.AccountingAccount);
                        }

                        // Track third party codes per bank account
                        if (!string.IsNullOrEmpty(counterpart.ThirdPartyCode))
                        {
                            if (pattern.ThirdPartyCodes.ContainsKey(counterpart.ThirdPartyCode))
                            {
                                pattern.ThirdPartyCodes[counterpart.ThirdPartyCode]++;
                            }
                            else
                            {
                                pattern.ThirdPartyCodes[counterpart.ThirdPartyCode] = 1;
                            }
                        }
                    }
                    
                    // Collect global third party codes
                    if (!string.IsNullOrEmpty(counterpart.ThirdPartyCode))
                    {
                        if (entityCandidates[entity].ThirdPartyCodes.ContainsKey(counterpart.ThirdPartyCode))
                        {
                            entityCandidates[entity].ThirdPartyCodes[counterpart.ThirdPartyCode]++;
                        }
                        else
                        {
                            entityCandidates[entity].ThirdPartyCodes[counterpart.ThirdPartyCode] = 1;
                        }
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

    private List<OperationCandidate> ExtractOperationPatterns(List<AccountingEntry> entries, int threshold)
    {
        var operationCandidates = new Dictionary<string, OperationCandidate>();

        foreach (var entry in entries)
        {
            var operationPatterns = DetectOperationPatterns(entry.Label);
            
            foreach (var pattern in operationPatterns)
            {
                var key = $"{pattern.Type}";
                
                if (!operationCandidates.ContainsKey(key))
                {
                    operationCandidates[key] = new OperationCandidate
                    {
                        OperationType = pattern.Type,
                        Frequency = 0,
                        AccountingAccounts = new List<string>(),
                        Direction = entry.Direction,
                        Keywords = new List<string>(),
                        ExampleLabels = new List<string>(),
                        BankAccountPatterns = new Dictionary<string, BankAccountPattern>()
                    };
                }

                operationCandidates[key].Frequency++;
                
                // Add accounting accounts from actual data instead of generic ones
                foreach (var counterpart in entry.Counterparts)
                {
                    if (!operationCandidates[key].AccountingAccounts.Contains(counterpart.AccountingAccount))
                    {
                        operationCandidates[key].AccountingAccounts.Add(counterpart.AccountingAccount);
                    }
                }

                if (pattern.ExtractedKeyword != null && !operationCandidates[key].Keywords.Contains(pattern.ExtractedKeyword))
                {
                    operationCandidates[key].Keywords.Add(pattern.ExtractedKeyword);
                }
                
                if (operationCandidates[key].ExampleLabels.Count < 3 && !operationCandidates[key].ExampleLabels.Contains(entry.Label))
                {
                    operationCandidates[key].ExampleLabels.Add(entry.Label);
                }

                // Track bank account patterns for operations too
                var bankAccountName = entry.BankAccountName ?? string.Empty;
                if (!string.IsNullOrEmpty(bankAccountName))
                {
                    if (!operationCandidates[key].BankAccountPatterns.ContainsKey(bankAccountName))
                    {
                        operationCandidates[key].BankAccountPatterns[bankAccountName] = new BankAccountPattern
                        {
                            BankAccount = bankAccountName,
                            CounterpartAccounts = new List<string>(),
                            Frequency = 0,
                            ThirdPartyCodes = new Dictionary<string, int>()
                        };
                    }

                    var bankPattern = operationCandidates[key].BankAccountPatterns[bankAccountName];
                    bankPattern.Frequency++;

                    foreach (var counterpart in entry.Counterparts)
                    {
                        if (!bankPattern.CounterpartAccounts.Contains(counterpart.AccountingAccount))
                        {
                            bankPattern.CounterpartAccounts.Add(counterpart.AccountingAccount);
                        }
                    }
                }
            }
        }

        return operationCandidates.Values
            .Where(o => o.Frequency >= threshold)
            .OrderByDescending(o => o.Frequency)
            .ToList();
    }

    private List<OperationPattern> DetectOperationPatterns(string label)
    {
        var patterns = new List<OperationPattern>();

        // Bank fees pattern
        if (Regex.IsMatch(label, @"FRAIS.*BANC|COMMISSION|COTISATION", RegexOptions.IgnoreCase))
        {
            patterns.Add(new OperationPattern
            {
                Type = OperationType.BankFees,
                AccountingAccount = string.Empty, // No generic account
                ExtractedKeyword = "FRAIS"
            });
        }

        // Credit card pattern
        var cbMatch = Regex.Match(label, @"CB\s+(.+?)(\s+FACT\s+\d+)?", RegexOptions.IgnoreCase);
        if (cbMatch.Success)
        {
            patterns.Add(new OperationPattern
            {
                Type = OperationType.CreditCard,
                AccountingAccount = string.Empty, // No generic account
                ExtractedKeyword = "CB"
            });
        }

        // Transfer pattern
        var virMatch = Regex.Match(label, @"VIR\s+(.+)", RegexOptions.IgnoreCase);
        if (virMatch.Success)
        {
            patterns.Add(new OperationPattern
            {
                Type = OperationType.Transfer,
                AccountingAccount = string.Empty, // No generic account
                ExtractedKeyword = "VIR"
            });
        }

        // Direct debit pattern
        var prlvMatch = Regex.Match(label, @"PRLV", RegexOptions.IgnoreCase);
        if (prlvMatch.Success)
        {
            patterns.Add(new OperationPattern
            {
                Type = OperationType.DirectDebit,
                AccountingAccount = string.Empty, // No generic account
                ExtractedKeyword = "PRLV"
            });
        }

        return patterns;
    }


    private List<AutomationRule> CreateOperationRules(OperationCandidate operation, int priority, DatasetAnalysis analysis)
    {
        var rules = new List<AutomationRule>();
        
        if (operation.AccountingAccounts.Count == 0)
            return rules;

        // For operations, priority is lower than entities but still important
        var operationPriority = priority + 50; // Lower priority than entities

        // Only create rules for operations that have actual accounting accounts from data
        if (operation.AccountingAccounts.Count == 0)
            return rules;

        // Create basic operation rule
        var primaryAccount = operation.AccountingAccounts.First();
        var mainKeyword = operation.Keywords.FirstOrDefault() ?? operation.OperationType.ToString();

        var rule = new AutomationRule
        {
            Priority = operationPriority,
            CreditOrDebit = operation.Direction,
            AccountingAccount = primaryAccount,
            RuleName = $"Operation_{operation.OperationType}_{mainKeyword}",
            Keyword1 = mainKeyword,
            MinConfidence = GetOperationConfidence(operation.OperationType),
            Coverage = operation.Frequency,
            Precision = 0.85, // Operations have good but not perfect precision
            Examples = operation.ExampleLabels
        };

        rules.Add(rule);
        return rules;
    }

    private double GetOperationConfidence(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.BankFees => 0.95,      // Very reliable
            OperationType.DirectDebit => 0.90,   // Reliable
            OperationType.CreditCard => 0.85,    // Good
            OperationType.Transfer => 0.80,      // Good but can vary
            _ => 0.80
        };
    }

    private List<AutomationRule> CreateEntityRules(EntityCandidate entity, int priority, DatasetAnalysis analysis)
    {
        var rules = new List<AutomationRule>();
        
        if (entity.AccountingAccounts.Count == 0)
            return rules;

        // Check if entity has different patterns per bank account
        var bankAccountPatterns = entity.BankAccountPatterns;
        
        if (bankAccountPatterns.Count > 1)
        {
            // Multiple bank accounts with potentially different counterpart accounts
            var distinctPatternsByAccount = bankAccountPatterns.Values
                .Where(p => p.CounterpartAccounts.Count > 0)
                .GroupBy(p => p.CounterpartAccounts.First()) // Group by first counterpart account
                .ToList();
                
            if (distinctPatternsByAccount.Count > 1)
            {
                // Create bank-account-specific rules
                int currentPriority = priority;
                foreach (var patternGroup in distinctPatternsByAccount)
                {
                    var pattern = patternGroup.First();
                    var bankAccountsForThisPattern = patternGroup
                        .Select(p => p.BankAccount)
                        .ToList();
                    
                    var rule = CreateEntityRuleWithBankRestriction(entity, currentPriority, analysis, pattern, bankAccountsForThisPattern);
                    if (rule != null)
                    {
                        rules.Add(rule);
                        currentPriority++;
                    }
                }
                return rules;
            }
        }
        
        // Default behavior: single rule without bank account restrictions
        var defaultRule = CreateEntityRule(entity, priority, analysis);
        if (defaultRule != null)
        {
            rules.Add(defaultRule);
        }
        
        return rules;
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

        // Get the most frequent third party code as rule effect
        var mostFrequentThirdPartyCode = GetMostFrequentThirdPartyCode(entity);

        return new AutomationRule
        {
            Priority = Math.Max(1, adjustedPriority),
            CreditOrDebit = entity.Direction,
            AccountingAccount = primaryAccount,
            ThirdPartyCode = mostFrequentThirdPartyCode,
            RuleName = $"{entity.BusinessCategory}_{entity.Name}",
            Keyword1 = entity.Name,
            MinConfidence = GetConfidenceByCategory(entity.BusinessCategory),
            Coverage = entity.Frequency,
            Precision = CalculatePrecisionEstimate(entity, analysis),
            Examples = entity.ExampleLabels
        };
    }

    private AutomationRule? CreateEntityRuleWithBankRestriction(EntityCandidate entity, int priority, DatasetAnalysis analysis, BankAccountPattern pattern, List<string> restrictedBankAccounts)
    {
        if (pattern.CounterpartAccounts.Count == 0)
            return null;

        // Use the primary counterpart account for this pattern
        var primaryAccount = pattern.CounterpartAccounts.First();

        // Adjust priority based on business category
        var adjustedPriority = priority - GetBusinessPriority(entity.BusinessCategory);

        // Get the most frequent third party code from this pattern
        var mostFrequentThirdPartyCode = GetMostFrequentThirdPartyCodeFromPattern(pattern);

        return new AutomationRule
        {
            Priority = Math.Max(1, adjustedPriority),
            CreditOrDebit = entity.Direction,
            AccountingAccount = primaryAccount,
            ThirdPartyCode = mostFrequentThirdPartyCode,
            RuleName = $"{entity.BusinessCategory}_{entity.Name}_{primaryAccount}",
            Keyword1 = entity.Name,
            MinConfidence = GetConfidenceByCategory(entity.BusinessCategory),
            Coverage = pattern.Frequency,
            Precision = CalculatePrecisionEstimate(entity, analysis),
            Examples = entity.ExampleLabels.Take(3).ToList(), // Limit examples for restricted rules
            RestrictedBankAccounts = string.Join(",", restrictedBankAccounts)
        };
    }

    private string? GetMostFrequentThirdPartyCodeFromPattern(BankAccountPattern pattern)
    {
        if (!pattern.ThirdPartyCodes.Any())
            return null;

        var mostFrequent = pattern.ThirdPartyCodes
            .OrderByDescending(kv => kv.Value)
            .First();

        // Only return if it appears in at least 70% of cases for consistency
        var consistencyThreshold = pattern.Frequency * 0.7;
        if (mostFrequent.Value >= consistencyThreshold)
        {
            return mostFrequent.Key;
        }

        return null;
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

    private string? GetMostFrequentThirdPartyCode(EntityCandidate entity)
    {
        if (!entity.ThirdPartyCodes.Any())
            return null;

        // Get the most frequent third party code
        var mostFrequent = entity.ThirdPartyCodes
            .OrderByDescending(kv => kv.Value)
            .First();

        // Only return if it appears in at least 70% of cases for consistency
        var consistencyThreshold = entity.Frequency * 0.7;
        if (mostFrequent.Value >= consistencyThreshold)
        {
            return mostFrequent.Key;
        }

        return null;
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
        public Dictionary<string, int> ThirdPartyCodes { get; set; } = new();
        public Dictionary<string, BankAccountPattern> BankAccountPatterns { get; set; } = new();
    }

    private class BankAccountPattern
    {
        public string BankAccount { get; set; } = string.Empty;
        public List<string> CounterpartAccounts { get; set; } = new();
        public int Frequency { get; set; }
        public Dictionary<string, int> ThirdPartyCodes { get; set; } = new();
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

    private class OperationCandidate
    {
        public OperationType OperationType { get; set; }
        public int Frequency { get; set; }
        public List<string> AccountingAccounts { get; set; } = new();
        public string Direction { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();
        public List<string> ExampleLabels { get; set; } = new();
        public Dictionary<string, BankAccountPattern> BankAccountPatterns { get; set; } = new();
    }

    private class OperationPattern
    {
        public OperationType Type { get; set; }
        public string AccountingAccount { get; set; } = string.Empty;
        public string? ExtractedKeyword { get; set; }
    }

    private enum OperationType
    {
        BankFees,
        CreditCard,
        Transfer,
        DirectDebit
    }

    // Step 7: Adaptive validation and quality control
    private List<AutomationRule> ApplyAdaptiveValidation(List<AutomationRule> rules, List<AccountingEntry> allEntries, DatasetAnalysis analysis)
    {
        var validatedRules = new List<AutomationRule>();
        var datasetSize = allEntries.Count;

        foreach (var rule in rules)
        {
            // Calculate realistic confidence score based on coverage and precision
            var updatedConfidence = CalculateRealisticConfidence(rule, datasetSize);
            rule.MinConfidence = updatedConfidence;

            // Cross-validate rule precision
            var crossValidationScore = PerformCrossValidation(rule, allEntries);
            rule.Precision = crossValidationScore;

            // Apply quality thresholds - eliminate rules below 80% precision
            // Operation rules have different validation criteria
            bool shouldIncludeRule = false;
            
            if (rule.RuleName.Contains("Operation_"))
            {
                // Operation rules: more lenient precision threshold and coverage requirement
                shouldIncludeRule = rule.Precision >= 0.60 && rule.Coverage >= 1;
            }
            else
            {
                // Entity rules: standard validation
                shouldIncludeRule = rule.Precision >= 0.80 && rule.Coverage >= _thresholdService.CalculateMinimumCoverage(datasetSize);
            }
            
            if (shouldIncludeRule)
            {
                validatedRules.Add(rule);
            }
        }

        // Resolve conflicts and optimize rules
        return OptimizeRulesForConflicts(validatedRules);
    }

    private double CalculateRealisticConfidence(AutomationRule rule, int datasetSize)
    {
        // Base confidence adjusted by coverage and dataset characteristics
        var baseConfidence = rule.MinConfidence;
        var coverageRatio = (double)rule.Coverage / datasetSize;
        
        // Higher coverage in larger datasets reduces confidence (more likely to have edge cases)
        // Lower coverage in smaller datasets is more reliable
        var datasetAdjustment = datasetSize switch
        {
            < 100 => coverageRatio > 0.1 ? 0.05 : -0.02,  // Small: boost high coverage
            < 500 => coverageRatio > 0.05 ? 0.02 : -0.05, // Medium: modest boost
            _ => coverageRatio > 0.02 ? -0.02 : -0.08      // Large: penalize high coverage
        };

        var adjustedConfidence = baseConfidence + datasetAdjustment;
        return Math.Max(0.70, Math.Min(0.99, adjustedConfidence));
    }

    private double PerformCrossValidation(AutomationRule rule, List<AccountingEntry> allEntries)
    {
        if (string.IsNullOrEmpty(rule.Keyword1))
            return rule.Precision; // Keep existing precision if no keyword

        // Find all entries that would match this rule
        var matchingEntries = allEntries.Where(entry => 
            EntryMatchesRule(entry, rule)).ToList();
        
        if (!matchingEntries.Any())
            return 0.5; // Low precision if no matches found

        // Calculate precision: how many matching entries have the correct accounting account
        var correctMatches = matchingEntries.Count(entry => 
            entry.Counterparts.Any(c => c.AccountingAccount == rule.AccountingAccount));
        
        var precision = (double)correctMatches / matchingEntries.Count;
        
        // Boost precision for high-confidence categories
        if (rule.RuleName.Contains("PublicInstitution") || rule.RuleName.Contains("Bank"))
        {
            precision = Math.Min(0.95, precision + 0.05);
        }
        
        return Math.Max(0.60, precision);
    }

    private bool EntryMatchesRule(AccountingEntry entry, AutomationRule rule)
    {
        // Check direction
        if (entry.Direction != rule.CreditOrDebit)
            return false;

        // Check bank account restrictions
        if (!string.IsNullOrEmpty(rule.RestrictedBankAccounts))
        {
            var restrictedAccounts = rule.RestrictedBankAccounts.Split(',').Select(a => a.Trim()).ToList();
            if (!restrictedAccounts.Contains(entry.BankAccountName ?? ""))
                return false;
        }

        // Check keyword match
        if (!string.IsNullOrEmpty(rule.Keyword1))
        {
            return entry.Label.Contains(rule.Keyword1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }


    private List<AutomationRule> OptimizeRulesForConflicts(List<AutomationRule> rules)
    {
        var optimizedRules = new List<AutomationRule>();
        var processedKeywords = new HashSet<string>();

        // Group rules by keyword to detect conflicts
        var rulesByKeyword = rules
            .Where(r => !string.IsNullOrEmpty(r.Keyword1))
            .GroupBy(r => r.Keyword1!.ToUpper())
            .ToList();

        foreach (var keywordGroup in rulesByKeyword)
        {
            var keywordRules = keywordGroup.OrderByDescending(r => r.Precision).ToList();
            
            if (keywordRules.Count == 1)
            {
                // No conflict, add the rule
                optimizedRules.Add(keywordRules[0]);
            }
            else
            {
                // Resolve conflicts - prefer higher precision and business priority
                var bestRule = keywordRules
                    .OrderByDescending(r => r.Priority <= 20 ? 1 : 0) // Business entities first
                    .ThenByDescending(r => r.Precision)
                    .ThenByDescending(r => r.Coverage)
                    .First();
                
                // Only add if precision is significantly better or has business priority
                if (bestRule.Precision >= 0.85 || bestRule.Priority <= 20)
                {
                    optimizedRules.Add(bestRule);
                }
                
                // Check if other rules in the group can be differentiated
                var remainingRules = keywordRules.Where(r => r != bestRule && r.Precision >= 0.80).ToList();
                foreach (var remaining in remainingRules)
                {
                    // Add rule with different accounting account if precision is high enough
                    if (remaining.AccountingAccount != bestRule.AccountingAccount && remaining.Precision >= 0.85)
                    {
                        remaining.Priority = bestRule.Priority + 1; // Lower priority
                        optimizedRules.Add(remaining);
                    }
                }
            }
        }

        // Add rules without keywords (operation patterns)
        var rulesWithoutKeywords = rules.Where(r => string.IsNullOrEmpty(r.Keyword1)).ToList();
        optimizedRules.AddRange(rulesWithoutKeywords);

        // Final reordering by priority
        return optimizedRules
            .OrderBy(r => r.Priority)
            .ToList();
    }
}