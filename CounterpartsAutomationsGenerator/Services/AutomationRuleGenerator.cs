using CounterpartsAutomationsGenerator.Models;
using System.Text.RegularExpressions;
using System.Text;

namespace CounterpartsAutomationsGenerator.Services;

public class AutomationRuleGenerator
{
    private readonly Dictionary<string, (string account, string pattern)> _operationPatterns = new()
    {
        { "FRAIS_BANCAIRES", ("62700000", @"FRAIS.*BANC|COMMISSION|COTISATION") },
        { "PRELEVEMENT", ("", @"PRLV\s+([A-Z\s]+)") },
        { "CARTE_BANCAIRE", ("", @"CB\s+(.+?)\s+FACT\s+\d+\s+(.+)") },
        { "VIREMENT", ("", @"VIR\s+(.+?)") },
        { "REMBOURSEMENT", ("", @"REMB|RBT\s+(NDF|NOTE)") }
    };

    public List<AutomationRule> GenerateRules(RuleGenerationRequest request)
    {
        var rules = new List<AutomationRule>();
        int priority = 1;

        // Calculer dynamiquement les entités à partir des données
        var dynamicEntityPatterns = ExtractDynamicEntityPatterns(request.DebitEntries.Entries, request.CreditEntries.Entries);

        // Analyse des débits
        var debitPatterns = AnalyzePatterns(request.DebitEntries.Entries, "debit", dynamicEntityPatterns);
        var debitRules = GenerateRulesFromPatterns(debitPatterns, "debit", ref priority, dynamicEntityPatterns);
        rules.AddRange(debitRules);

        // Analyse des crédits
        var creditPatterns = AnalyzePatterns(request.CreditEntries.Entries, "credit", dynamicEntityPatterns);
        var creditRules = GenerateRulesFromPatterns(creditPatterns, "credit", ref priority, dynamicEntityPatterns);
        rules.AddRange(creditRules);

        // Validation et optimisation des règles
        var validatedRules = ValidateAndOptimizeRules(rules);

        return validatedRules.OrderBy(r => r.Priority).ToList();
    }

    private Dictionary<string, string> ExtractDynamicEntityPatterns(List<AccountingEntry> debitEntries, List<AccountingEntry> creditEntries)
    {
        var entityPatterns = new Dictionary<string, string>();
        var allEntries = debitEntries.Concat(creditEntries).ToList();

        // Extraire tous les libellés
        var allLabels = allEntries.SelectMany(e => new[] { e.Label }.Concat(e.Counterparts.Select(c => c.Label))).ToList();

        // Patterns pour détecter des entités organisationnelles
        var organizationPatterns = new[]
        {
            @"\b([A-Z]{2,}(?:\s+[A-Z]{2,}){0,3})\b", // Mots en majuscules (2+ lettres)
            @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+){1,3})\b", // Noms propres
            @"\b(SARL|SAS|SA|EURL|SNC|GIE)\s+([A-Z\s]+)\b", // Formes juridiques
            @"\b([A-Z]+\s+(?:BANK|BANQUE|CREDIT|ASSURANCE|MUTUELLE))\b", // Institutions financières
            @"\b(CPAM|URSSAF|CAF|POLE\s+EMPLOI|AGENCE\s+[A-Z]+)\b" // Organismes publics
        };

        var entityCandidates = new Dictionary<string, EntityInfo>();

        foreach (var label in allLabels)
        {
            foreach (var pattern in organizationPatterns)
            {
                var matches = Regex.Matches(label, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    var entity = match.Groups[match.Groups.Count - 1].Value.Trim().ToUpper();

                    // Filtrer les entités trop courtes ou trop génériques
                    if (entity.Length < 4 || IsGenericWord(entity))
                        continue;

                    if (!entityCandidates.ContainsKey(entity))
                    {
                        entityCandidates[entity] = new EntityInfo { Name = entity, Count = 0, AccountingAccounts = new HashSet<string>() };
                    }

                    entityCandidates[entity].Count++;

                    // Associer avec le compte comptable de contrepartie
                    var entry = allEntries.FirstOrDefault(e => e.Label.Contains(entity, StringComparison.OrdinalIgnoreCase));
                    if (entry?.Counterparts.Any() == true)
                    {
                        foreach (var counterpart in entry.Counterparts)
                        {
                            entityCandidates[entity].AccountingAccounts.Add(counterpart.AccountingAccount);
                        }
                    }
                }
            }
        }

        // Sélectionner les entités avec une fréquence significative et cohérence de compte
        foreach (var candidate in entityCandidates.Where(c => c.Value.Count >= 3))
        {
            // Prendre le compte comptable le plus fréquent pour cette entité
            var accountGroups = candidate.Value.AccountingAccounts.GroupBy(a => a).ToList();
            if (accountGroups.Any())
            {
                var mostFrequentAccount = accountGroups
                    .OrderByDescending(g => g.Count())
                    .First().Key;

                // Ne garder que si le compte principal représente au moins 70% des occurrences
                var dominance = (double)accountGroups.First().Count() / candidate.Value.Count;
                if (dominance >= 0.7)
                {
                    entityPatterns[candidate.Key] = mostFrequentAccount;
                }
            }
        }

        return entityPatterns;
    }

    private bool IsGenericWord(string word)
    {
        var genericWords = new HashSet<string>
        {
            "FRANCE", "PARIS", "LYON", "MARSEILLE", "BANK", "BANQUE", "CREDIT", "CARTE", "COMPTE",
            "VIREMENT", "PRELEVEMENT", "FACTURE", "PAIEMENT", "REMBOURSEMENT", "FRAIS", "COMMISSION",
            "SEPA", "FACT", "DATE", "MONTANT", "NUMERO", "REF", "REFERENCE", "TOTAL", "SOLDE",
            "AVOIR", "COMPTABILITE", "ECHEANCE", "VALIDATION", "TRAITEMENT", "OPERATION",
            "CLIENT", "SOCIETE", "ENTREPRISE", "GROUPE", "HOLDING", "FINANCE", "FINANCIAL",
            "SERVICES", "SERVICE", "GESTION", "MANAGEMENT", "CONSEIL", "CONSULTING",
            "JANVIER", "FEVRIER", "MARS", "AVRIL", "MAI", "JUIN", "JUILLET", "AOUT",
            "SEPTEMBRE", "OCTOBRE", "NOVEMBRE", "DECEMBRE", "MATIN", "SOIR", "JOUR", "NUIT",
            "LUNDI", "MARDI", "MERCREDI", "JEUDI", "VENDREDI", "SAMEDI", "DIMANCHE",
            "AVEC", "SANS", "DANS", "POUR", "DEPUIS", "VERS", "CHEZ"
        };

        return genericWords.Contains(word) || word.Length <= 3 || IsNumericPattern(word);
    }

    private bool IsNumericPattern(string word)
    {
        // Exclure les mots qui sont principalement des chiffres ou des codes génériques
        return Regex.IsMatch(word, @"^\d+$|^[A-Z]{1,2}\d+$|^\d{2,}[A-Z]*$");
    }

    private List<PatternAnalysis> AnalyzePatterns(List<AccountingEntry> entries, string direction, Dictionary<string, string> dynamicEntityPatterns)
    {
        var patterns = new List<PatternAnalysis>();

        // Grouper par compte de contrepartie
        var groupedByAccount = entries
            .SelectMany(e => e.Counterparts.Select(c => new { Entry = e, Counterpart = c }))
            .GroupBy(x => x.Counterpart.AccountingAccount);

        foreach (var group in groupedByAccount)
        {
            var labels = group.Select(x => x.Entry.Label).ToList();

            // Analyser les patterns pour ce compte
            patterns.AddRange(ExtractPatternsFromLabels(labels, group.Key, direction, dynamicEntityPatterns));
        }

        return patterns.Where(p => p.Frequency >= 5 || IsSpecificEntity(p.Pattern, dynamicEntityPatterns)).ToList();
    }

    private List<PatternAnalysis> ExtractPatternsFromLabels(List<string> labels, string accountingAccount, string direction, Dictionary<string, string> dynamicEntityPatterns)
    {
        var patterns = new List<PatternAnalysis>();

        // 1. Recherche d'entités dynamiques (priorité haute)
        foreach (var entity in dynamicEntityPatterns.Keys)
        {
            var matchingLabels = labels.Where(l => l.Contains(entity, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchingLabels.Any())
            {
                patterns.Add(new PatternAnalysis
                {
                    Pattern = entity,
                    AccountingAccount = accountingAccount,
                    Frequency = matchingLabels.Count,
                    Examples = matchingLabels.Take(5).ToList(),
                    Direction = direction
                });
            }
        }

        // 2. Recherche de patterns d'opérations (priorité moyenne)
        foreach (var operation in _operationPatterns)
        {
            var regex = new Regex(operation.Value.pattern, RegexOptions.IgnoreCase);
            var matchingLabels = labels.Where(l => regex.IsMatch(l)).ToList();
            if (matchingLabels.Any())
            {
                patterns.Add(new PatternAnalysis
                {
                    Pattern = operation.Key,
                    AccountingAccount = accountingAccount,
                    Frequency = matchingLabels.Count,
                    Examples = matchingLabels.Take(5).ToList(),
                    Direction = direction
                });
            }
        }

        // 3. Extraction de mots-clés récurrents MAIS avec critères très stricts
        var wordFrequency = ExtractKeywords(labels);

        // Filtrer les mots-clés pour ne garder que les plus discriminants
        var filteredKeywords = wordFrequency
            .Where(kv => kv.Value >= 10) // Fréquence minimale élevée
            .Where(kv => !IsGenericWord(kv.Key)) // Filtrer les mots génériques
            .Where(kv => CalculateKeywordSpecificity(kv.Key, labels) > 0.7) // Seuil de spécificité élevé
            .OrderByDescending(kv => kv.Value)
            .Take(2); // Limiter à 2 mots-clés maximum par compte

        foreach (var keyword in filteredKeywords)
        {
            var matchingLabels = labels.Where(l => l.Contains(keyword.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            patterns.Add(new PatternAnalysis
            {
                Pattern = keyword.Key,
                AccountingAccount = accountingAccount,
                Frequency = matchingLabels.Count,
                Examples = matchingLabels.Take(5).ToList(),
                Direction = direction
            });
        }

        return patterns;
    }

    private double CalculateKeywordSpecificity(string keyword, List<string> labels)
    {
        // Calcule la spécificité d'un mot-clé : ratio de labels contenant ce mot vs tous les labels
        var containingLabels = labels.Count(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        var specificity = (double)containingLabels / labels.Count;

        // Pénaliser les mots très courts ou très longs
        if (keyword.Length < 4 || keyword.Length > 20)
            specificity *= 0.5;

        return specificity;
    }

    private Dictionary<string, int> ExtractKeywords(List<string> labels)
    {
        var keywords = new Dictionary<string, int>();
        var excludeWords = new HashSet<string> { "DE", "LA", "LE", "DU", "ET", "EN", "POUR", "PAR", "SUR", "AVEC", "SANS" };

        foreach (var label in labels)
        {
            var words = Regex.Split(label, @"\W+")
                .Where(w => w.Length > 3 && !excludeWords.Contains(w.ToUpper()))
                .Select(w => w.ToUpper());

            foreach (var word in words)
            {
                keywords[word] = keywords.GetValueOrDefault(word, 0) + 1;
            }
        }

        return keywords;
    }

    private List<AutomationRule> GenerateRulesFromPatterns(List<PatternAnalysis> patterns, string direction, ref int priority, Dictionary<string, string> dynamicEntityPatterns)
    {
        var rules = new List<AutomationRule>();
        var existingRules = new List<AutomationRule>(); // Pour tracker les règles déjà créées

        // Trier par fréquence décroissante et priorité (entités > opérations > mots-clés)
        var sortedPatterns = patterns
            .OrderByDescending(p => GetPatternPriority(p, dynamicEntityPatterns))
            .ThenByDescending(p => p.Frequency)
            .ToList();

        foreach (var pattern in sortedPatterns)
        {
            // Vérifier si une règle similaire existe déjà
            var existingSimilarRule = FindSimilarExistingRule(pattern, existingRules, direction);

            if (existingSimilarRule != null)
            {
                // Une règle similaire existe, essayer de l'améliorer ou la fusionner
                var improvedRule = TryImproveSimilarRule(existingSimilarRule, pattern, existingRules);
                if (improvedRule != null)
                {
                    // Remplacer l'ancienne règle par la nouvelle améliorée
                    var index = existingRules.IndexOf(existingSimilarRule);
                    existingRules[index] = improvedRule;
                    rules[rules.IndexOf(existingSimilarRule)] = improvedRule;
                }
                // Sinon, ignorer ce pattern car il est déjà couvert
                continue;
            }

            // Aucune règle similaire trouvée, créer une nouvelle règle
            var rule = CreateRuleFromPattern(pattern, priority++, direction, dynamicEntityPatterns);
            if (rule != null)
            {
                rules.Add(rule);
                existingRules.Add(rule);
            }
        }

        return rules;
    }

    private int GetPatternPriority(PatternAnalysis pattern, Dictionary<string, string> dynamicEntityPatterns)
    {
        // Priorité : Entités dynamiques > Opérations > Mots-clés génériques
        if (dynamicEntityPatterns.ContainsKey(pattern.Pattern))
            return 3; // Priorité haute
        if (_operationPatterns.ContainsKey(pattern.Pattern))
            return 2; // Priorité moyenne
        return 1; // Priorité basse
    }

    private AutomationRule? FindSimilarExistingRule(PatternAnalysis newPattern, List<AutomationRule> existingRules, string direction)
    {
        return existingRules.FirstOrDefault(rule =>
            rule.CreditOrDebit == direction &&
            (
                // Même mot-clé exact
                rule.Keyword1 == newPattern.Pattern ||
                // Mots-clés qui se chevauchent significativement
                CalculateKeywordSimilarity(rule.Keyword1, newPattern.Pattern) > 0.8 ||
                // Même compte comptable avec des mots-clés liés
                (rule.AccountingAccount == newPattern.AccountingAccount &&
                 CalculateKeywordSimilarity(rule.Keyword1, newPattern.Pattern) > 0.5)
            )
        );
    }

    private double CalculateKeywordSimilarity(string? keyword1, string keyword2)
    {
        if (string.IsNullOrEmpty(keyword1) || string.IsNullOrEmpty(keyword2))
            return 0;

        // Similarité basée sur la distance de Levenshtein normalisée
        var maxLength = Math.Max(keyword1.Length, keyword2.Length);
        if (maxLength == 0) return 1;

        var distance = LevenshteinDistance(keyword1.ToUpper(), keyword2.ToUpper());
        return 1.0 - (double)distance / maxLength;
    }

    private int LevenshteinDistance(string s1, string s2)
    {
        if (s1 == s2) return 0;
        if (s1.Length == 0) return s2.Length;
        if (s2.Length == 0) return s1.Length;

        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost
                );
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    private AutomationRule? TryImproveSimilarRule(AutomationRule existingRule, PatternAnalysis newPattern, List<AutomationRule> allRules)
    {
        // Si la nouvelle analyse a une fréquence beaucoup plus élevée, considérer la remplacer
        if (newPattern.Frequency > existingRule.Coverage * 1.5)
        {
            // Créer une règle améliorée qui combine les deux
            var improvedRule = new AutomationRule
            {
                Priority = existingRule.Priority,
                CreditOrDebit = existingRule.CreditOrDebit,
                AccountingAccount = DetermineBestAccount(existingRule, newPattern),
                Keyword1 = ChooseBestKeyword(existingRule.Keyword1, newPattern.Pattern, newPattern.Frequency, existingRule.Coverage),
                RuleName = $"Amélioré_{existingRule.Keyword1}_{newPattern.Pattern}",
                Coverage = existingRule.Coverage + newPattern.Frequency,
                MinConfidence = Math.Min(0.95, (existingRule.MinConfidence + 0.85) / 2),
                Precision = existingRule.Precision,
                Examples = existingRule.Examples.Concat(newPattern.Examples).Distinct().ToList(),
                KeywordMatching = KeywordMatchingMode.OneOf
            };

            // Essayer d'ajouter un mot-clé discriminant si nécessaire
            if (existingRule.AccountingAccount != newPattern.AccountingAccount)
            {
                var discriminants = FindDiscriminantKeywords(
                    newPattern.Examples,
                    allRules.Where(r => r.Keyword1 == existingRule.Keyword1).ToList(),
                    newPattern.AccountingAccount
                );

                if (discriminants.Any())
                {
                    improvedRule.Keyword2 = discriminants.First();
                    improvedRule.KeywordMatching = KeywordMatchingMode.All;
                    improvedRule.RuleName = $"Spécialisé_{improvedRule.Keyword1}_{discriminants.First()}";
                }
            }

            return improvedRule;
        }

        return null; // Aucune amélioration possible
    }

    private string DetermineBestAccount(AutomationRule existingRule, PatternAnalysis newPattern)
    {
        // Choisir le compte comptable basé sur la fréquence
        if (newPattern.Frequency > existingRule.Coverage)
            return newPattern.AccountingAccount;
        return existingRule.AccountingAccount;
    }

    private string ChooseBestKeyword(string? existingKeyword, string newKeyword, int newFrequency, int existingFrequency)
    {
        // Choisir le mot-clé le plus spécifique ou le plus fréquent
        if (string.IsNullOrEmpty(existingKeyword))
            return newKeyword;

        // Préférer les mots-clés plus longs (plus spécifiques)
        if (newKeyword.Length > existingKeyword.Length && newFrequency >= existingFrequency * 0.7)
            return newKeyword;

        // Préférer les mots-clés plus fréquents
        if (newFrequency > existingFrequency * 1.3)
            return newKeyword;

        return existingKeyword;
    }

    private AutomationRule? CreateRuleFromPattern(PatternAnalysis pattern, int priority, string direction, Dictionary<string, string> dynamicEntityPatterns)
    {
        var rule = new AutomationRule
        {
            Priority = priority,
            CreditOrDebit = direction,
            AccountingAccount = pattern.AccountingAccount,
            Coverage = pattern.Frequency,
            MinConfidence = 0.85,
            Examples = pattern.Examples.ToList()
        };

        // Règles spécifiques pour les entités dynamiques
        if (dynamicEntityPatterns.ContainsKey(pattern.Pattern))
        {
            rule.RuleName = $"Entité_{pattern.Pattern}";
            rule.Keyword1 = pattern.Pattern;
            rule.Priority = priority <= 20 ? priority : 20; // Priorité haute pour les entités
            rule.MinConfidence = 0.95;

            // Utiliser le compte comptable déterminé dynamiquement
            if (!string.IsNullOrEmpty(dynamicEntityPatterns[pattern.Pattern]))
            {
                rule.AccountingAccount = dynamicEntityPatterns[pattern.Pattern];
            }

            return rule;
        }

        // Règles pour les types d'opérations
        if (_operationPatterns.ContainsKey(pattern.Pattern))
        {
            var operationInfo = _operationPatterns[pattern.Pattern];
            rule.RuleName = $"Opération_{pattern.Pattern}";
            rule.Keyword1 = ExtractKeywordFromPattern(pattern.Pattern);

            if (!string.IsNullOrEmpty(operationInfo.account))
            {
                rule.AccountingAccount = operationInfo.account;
            }
            return rule;
        }

        // Règles génériques par mots-clés (critères très stricts)
        if (pattern.Frequency >= 15) // Seuil encore plus élevé
        {
            rule.RuleName = $"MotClé_{pattern.Pattern}";
            rule.Keyword1 = pattern.Pattern;
            rule.MinConfidence = 0.80;
            return rule;
        }

        return null;
    }

    private string ExtractKeywordFromPattern(string patternName)
    {
        return patternName switch
        {
            "FRAIS_BANCAIRES" => "FRAIS",
            "PRELEVEMENT" => "PRLV",
            "CARTE_BANCAIRE" => "CB",
            "VIREMENT" => "VIR",
            "REMBOURSEMENT" => "REMB",
            _ => patternName
        };
    }

    private bool IsSpecificEntity(string pattern, Dictionary<string, string> dynamicEntityPatterns)
    {
        return dynamicEntityPatterns.ContainsKey(pattern) ||
               Regex.IsMatch(pattern, @"^[A-Z]{2,}\s*[A-Z]*$");
    }

    private List<AutomationRule> ValidateAndOptimizeRules(List<AutomationRule> rules)
    {
        var validatedRules = new List<AutomationRule>();

        foreach (var rule in rules)
        {
            // Valider la cohérence entre mots-clés et exemples
            var validatedRule = ValidateRuleConsistency(rule);
            if (validatedRule != null)
            {
                var precision = CalculateRulePrecision(validatedRule);
                validatedRule.Precision = precision;

                if (precision >= 0.80) // Seuil minimum de précision
                {
                    validatedRules.Add(validatedRule);
                }
            }
        }

        // Éliminer les règles redondantes avec résolution intelligente des conflits
        return EliminateRedundantRules(validatedRules);
    }

    private AutomationRule? ValidateRuleConsistency(AutomationRule rule)
    {
        var keywords = new[] { rule.Keyword1, rule.Keyword2, rule.Keyword3 }
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        if (keywords.Count <= 1)
            return rule; // Pas de validation nécessaire pour un seul mot-clé

        // Vérifier si les exemples correspondent au mode de correspondance défini
        var validExamples = rule.KeywordMatching == KeywordMatchingMode.All
            ? rule.Examples.Where(example =>
                keywords.All(keyword => example.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList()
            : rule.Examples.Where(example =>
                keywords.Any(keyword => example.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();

        // Si moins de 50% des exemples correspondent, recalculer le mode optimal
        if (validExamples.Count < rule.Examples.Count * 0.5)
        {
            var (optimalMode, optimalExamples, coverage) = DetermineOptimalKeywordMatching(keywords, rule.Examples);
            
            // Si même avec le mode optimal on a trop peu d'exemples, rejeter la règle
            if (optimalExamples.Count < 3)
                return null;

            return new AutomationRule
            {
                Priority = rule.Priority,
                CreditOrDebit = rule.CreditOrDebit,
                AccountingAccount = rule.AccountingAccount,
                ThirdPartyCode = rule.ThirdPartyCode,
                RuleName = rule.RuleName,
                Keyword1 = rule.Keyword1,
                Keyword2 = rule.Keyword2,
                Keyword3 = rule.Keyword3,
                KeywordMatching = optimalMode,
                RestrictedBankAccounts = rule.RestrictedBankAccounts,
                MinConfidence = rule.MinConfidence,
                Coverage = coverage,
                Precision = rule.Precision,
                Examples = optimalExamples
            };
        }

        // Mettre à jour la couverture avec les exemples valides
        rule.Coverage = validExamples.Count;
        rule.Examples = validExamples;
        return rule;
    }

    private double CalculateRulePrecision(AutomationRule rule)
    {
        if (rule.RuleName.StartsWith("Entité_"))
            return 0.95;
        if (rule.RuleName.StartsWith("Opération_"))
            return 0.90;

        return Math.Min(0.95, 0.70 + (rule.Coverage * 0.01));
    }

    private List<AutomationRule> EliminateRedundantRules(List<AutomationRule> rules)
    {
        var optimizedRules = new List<AutomationRule>();

        // Grouper les règles par keyword et direction
        var ruleGroups = rules
            .Where(r => !string.IsNullOrEmpty(r.Keyword1))
            .GroupBy(r => new { r.Keyword1, r.CreditOrDebit })
            .ToList();

        foreach (var group in ruleGroups)
        {
            if (group.Count() == 1)
            {
                optimizedRules.Add(group.First());
            }
            else
            {
                var resolvedRules = ResolveConflictingRules(group.ToList());
                optimizedRules.AddRange(resolvedRules);
            }
        }

        var rulesWithoutKeyword = rules.Where(r => string.IsNullOrEmpty(r.Keyword1)).ToList();
        optimizedRules.AddRange(rulesWithoutKeyword);

        return optimizedRules.OrderBy(r => r.Priority).ToList();
    }

    private List<AutomationRule> ResolveConflictingRules(List<AutomationRule> conflictingRules)
    {
        var resolvedRules = new List<AutomationRule>();
        var accountGroups = conflictingRules.GroupBy(r => r.AccountingAccount).ToList();

        foreach (var accountGroup in accountGroups)
        {
            var rulesForAccount = accountGroup.ToList();
            var combinedExamples = rulesForAccount
                .SelectMany(r => r.Examples)
                .Distinct()
                .ToList();

            var discriminantKeywords = FindDiscriminantKeywords(combinedExamples, conflictingRules, accountGroup.Key);

            if (discriminantKeywords.Any())
            {
                var enhancedRule = CreateEnhancedRule(rulesForAccount.First(), discriminantKeywords, combinedExamples);
                if (enhancedRule != null)
                {
                    resolvedRules.Add(enhancedRule);
                }
            }
            else
            {
                var consolidatedRule = ConsolidateRules(rulesForAccount);
                resolvedRules.Add(consolidatedRule);
            }
        }

        return resolvedRules;
    }

    private List<string> FindDiscriminantKeywords(List<string> examples, List<AutomationRule> allConflictingRules, string targetAccount)
    {
        var otherAccountsExamples = allConflictingRules
            .Where(r => r.AccountingAccount != targetAccount)
            .SelectMany(r => r.Examples)
            .ToList();

        var wordsInTarget = ExtractWordsFromExamples(examples);
        var wordsInOthers = ExtractWordsFromExamples(otherAccountsExamples);

        // Chercher les mots les plus discriminants et spécifiques
        var discriminants = wordsInTarget
            .Where(word => !wordsInOthers.Contains(word) && !IsGenericWord(word) && !IsOperationKeyword(word))
            .OrderByDescending(word => CalculateWordSpecificity(word, examples))
            .Take(2)
            .ToList();

        return discriminants;
    }

    private bool IsOperationKeyword(string word)
    {
        // Mots-clés d'opérations trop génériques pour être discriminants
        var operationKeywords = new HashSet<string>
        {
            "VIR", "VIREMENT", "CB", "CARTE", "PRLV", "PRELEVEMENT", "REMB", "REMBOURSEMENT",
            "SEPA", "FACT", "FACTURE", "RGLT", "REGLEMENT", "SOLDE", "ACOMPTE", "AVOIR"
        };
        return operationKeywords.Contains(word.ToUpper());
    }

    private double CalculateWordSpecificity(string word, List<string> examples)
    {
        // Calculer la spécificité d'un mot : longueur + fréquence relative
        var frequency = examples.Count(e => e.Contains(word, StringComparison.OrdinalIgnoreCase));
        var relativeFrequency = (double)frequency / examples.Count;
        
        // Privilégier les mots plus longs (plus spécifiques) avec une bonne fréquence
        return word.Length * relativeFrequency;
    }

    private HashSet<string> ExtractWordsFromExamples(List<string> examples)
    {
        var words = new HashSet<string>();
        var excludeWords = new HashSet<string> { "DE", "LA", "LE", "DU", "ET", "EN", "POUR", "PAR", "SUR", "AVEC", "SANS" };

        foreach (var example in examples)
        {
            var wordsInExample = Regex.Split(example, @"\W+")
                .Where(w => w.Length > 3 && !excludeWords.Contains(w.ToUpper()))
                .Select(w => w.ToUpper());

            foreach (var word in wordsInExample)
            {
                words.Add(word);
            }
        }
        return words;
    }

    private (KeywordMatchingMode mode, List<string> examples, int coverage) DetermineOptimalKeywordMatching(List<string> keywords, List<string> examples)
    {
        if (keywords.Count <= 1)
            return (KeywordMatchingMode.OneOf, examples, examples.Count);

        // Pour les règles avec plusieurs mots-clés, on ne veut que le mode "All" pour éviter les conflits
        // Tester le mode "All" : tous les mots-clés doivent être présents
        var examplesWithAllKeywords = examples.Where(example =>
            keywords.All(keyword => example.Contains(keyword, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Si on a au moins 3 exemples avec tous les mots-clés, c'est valable
        if (examplesWithAllKeywords.Count >= 3)
        {
            return (KeywordMatchingMode.All, examplesWithAllKeywords, examplesWithAllKeywords.Count);
        }
        
        // Sinon, la règle avec plusieurs mots-clés n'est pas assez spécifique
        // Retourner une règle invalide (sera filtrée plus tard)
        return (KeywordMatchingMode.All, new List<string>(), 0);
    }

    private AutomationRule? CreateEnhancedRule(AutomationRule baseRule, List<string> discriminantKeywords, List<string> examples)
    {
        var candidateKeywords = new List<string> { baseRule.Keyword1 }
            .Concat(discriminantKeywords.Where(k => !string.IsNullOrEmpty(k)))
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();

        // Réorganiser les mots-clés pour mettre le plus spécifique en premier
        var orderedKeywords = ReorderKeywordsBySpecificity(candidateKeywords, examples);
        
        // Filtrer les exemples pour ne garder que ceux qui correspondent au mode de correspondance choisi
        var (keywordMatching, filteredExamples, coverage) = DetermineOptimalKeywordMatching(orderedKeywords, examples);

        // Si pas assez d'exemples valides, ne pas créer la règle
        if (coverage < 3)
            return null;

        return new AutomationRule
        {
            Priority = baseRule.Priority,
            CreditOrDebit = baseRule.CreditOrDebit,
            AccountingAccount = baseRule.AccountingAccount,
            Keyword1 = orderedKeywords.ElementAtOrDefault(0),
            Keyword2 = orderedKeywords.ElementAtOrDefault(1),
            Keyword3 = orderedKeywords.ElementAtOrDefault(2),
            KeywordMatching = keywordMatching,
            RuleName = $"Spécialisé_{orderedKeywords.First()}_{baseRule.AccountingAccount}",
            Coverage = coverage,
            MinConfidence = Math.Min(0.95, baseRule.MinConfidence + 0.05),
            Precision = baseRule.Precision,
            Examples = filteredExamples
        };
    }

    private List<string> ReorderKeywordsBySpecificity(List<string> keywords, List<string> examples)
    {
        // Trier les mots-clés par spécificité décroissante (plus spécifique = meilleur mot-clé principal)
        return keywords
            .OrderByDescending(keyword => IsOperationKeyword(keyword) ? 0 : 1) // Les mots d'opération en dernier
            .ThenByDescending(keyword => CalculateWordSpecificity(keyword, examples))
            .ToList();
    }

    private AutomationRule ConsolidateRules(List<AutomationRule> conflictingRules)
    {
        var primaryRule = conflictingRules.OrderBy(r => r.Priority).First();
        var totalCoverage = conflictingRules.Sum(r => r.Coverage);

        var accountFrequency = conflictingRules
            .GroupBy(r => r.AccountingAccount)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Coverage));

        var bestAccount = accountFrequency
            .OrderByDescending(kv => kv.Value)
            .First().Key;

        var dominanceRatio = (double)accountFrequency[bestAccount] / totalCoverage;
        var adjustedConfidence = Math.Max(0.70, primaryRule.MinConfidence * dominanceRatio);

        // Fusionner les exemples et appliquer la logique de correspondance optimale
        var allExamples = conflictingRules
            .SelectMany(r => r.Examples)
            .Distinct()
            .ToList();

        // Créer une liste de tous les mots-clés disponibles
        var allKeywords = conflictingRules
            .SelectMany(r => new[] { r.Keyword1, r.Keyword2, r.Keyword3 })
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct()
            .ToList();

        // Déterminer le mode de correspondance optimal
        var (optimalMode, validExamples, coverage) = DetermineOptimalKeywordMatching(allKeywords, allExamples);

        return new AutomationRule
        {
            Priority = primaryRule.Priority,
            CreditOrDebit = primaryRule.CreditOrDebit,
            AccountingAccount = bestAccount,
            Keyword1 = primaryRule.Keyword1,
            Keyword2 = allKeywords.Skip(1).FirstOrDefault(),
            Keyword3 = allKeywords.Skip(2).FirstOrDefault(),
            KeywordMatching = optimalMode,
            RuleName = $"Consolidé_{primaryRule.Keyword1}",
            Coverage = coverage,
            MinConfidence = adjustedConfidence,
            Precision = conflictingRules.Average(r => r.Precision),
            Examples = validExamples
        };
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
}