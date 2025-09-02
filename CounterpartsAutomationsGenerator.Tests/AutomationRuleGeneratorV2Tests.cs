using CounterpartsAutomationsGenerator.Models;
using CounterpartsAutomationsGenerator.Services;
using FluentAssertions;
using NUnit.Framework;

namespace CounterpartsAutomationsGenerator.Tests;

[TestFixture]
public class AutomationRuleGeneratorV2Tests
{
    private AutomationRuleGeneratorV2 _generator;

    [SetUp]
    public void SetUp()
    {
        _generator = new AutomationRuleGeneratorV2();
    }

    #region Step 1: Basic structure + entry point tests

    [Test]
    public void GenerateRules_WithEmptyData_ReturnsEmptyList()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Test]
    public void GenerateRules_WithSingleEntry_ReturnsValidRule()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    new AccountingEntry
                    {
                        BankAccountName = "51215000",
                        AccountingAccount = "51215000",
                        Label = "VIR SEPA APICIL PREVOYANCE 550720564227",
                        Direction = "debit",
                        Debit = 240.1m,
                        Counterparts = new List<Counterpart>
                        {
                            new Counterpart
                            {
                                AccountingAccount = "43701700",
                                Label = "VIR SEPA APICIL PREVOYANCE 550720564227",
                                Credit = 240.1m
                            }
                        }
                    }
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        var rule = result.First();
        rule.CreditOrDebit.Should().Be("debit");
        rule.AccountingAccount.Should().Be("43701700");
        rule.Keyword1.Should().NotBeNullOrEmpty();
    }

    [Test]
    public void GenerateRules_WithSimpleEntity_CreatesBasicRule()
    {
        // Arrange
        var request = CreateRequestWithEntity("APICIL", "43701700", 5);

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.AccountingAccount.Should().Be("43701700");
    }

    #endregion

    #region Step 2: Intelligent preliminary analysis tests

    [Test]
    public void GenerateRules_AnalyzesAllLabels_ExtractsUniquePatterns()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA APICIL PREVOYANCE 1", "43701700", "debit"),
                    CreateEntry("VIR SEPA APICIL MUTUELLE 2", "43701700", "debit"),
                    CreateEntry("PRLV SEPA CPAM PROVENCE 1", "43700100", "debit"),
                    CreateEntry("CB CARREFOUR MARSEILLE", "62600000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        // Should detect APICIL entity (appears 2 times)
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Coverage.Should().Be(2);
    }

    [Test]
    public void GenerateRules_GroupsByCounterpartAccount_CorrectStatistics()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("FRAIS BANCAIRES TRIMESTRE", "62700000", "debit"),
                    CreateEntry("COMMISSION VIREMENT", "62700000", "debit"),
                    CreateEntry("COTISATION CARTE", "62700000", "debit"),
                    CreateEntry("VIR CLIENT DUPONT", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should have different rules for different accounting accounts
        var accounts = result.Select(r => r.AccountingAccount).Distinct().ToList();
        accounts.Should().Contain("62700000"); // Bank fees account
        accounts.Should().Contain("41100000"); // Client account
    }

    [Test]
    public void GenerateRules_IdentifiesDominantEntities_ByFrequency()
    {
        // Arrange - APICIL appears more frequently than others
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"),
                    CreateEntry("VIR SEPA APICIL MUTUELLE", "43701700", "debit"),
                    CreateEntry("VIR SEPA APICIL RETRAITE", "43701700", "debit"),
                    CreateEntry("PRLV SEPA CPAM BOUCHES", "43700100", "debit"),
                    CreateEntry("VIR PARTICULIER MARTIN", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        // APICIL should be detected as dominant entity (3 occurrences)
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Coverage.Should().Be(3);
        
        // Should have higher priority than less frequent entities
        var cpamRule = result.FirstOrDefault(r => r.Keyword1?.Contains("CPAM") == true);
        if (cpamRule != null)
        {
            apicilRule.Priority.Should().BeLessOrEqualTo(cpamRule.Priority);
        }
    }

    #endregion

    #region Step 3: Priority entity rules tests

    [Test]
    public void GenerateRules_DetectsAPICIL_CreatesHighPriorityRule()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"),
                    CreateEntry("PRLV SEPA APICIL MUTUELLE", "43701700", "debit"),
                    CreateEntry("VIR PARTICULIER MARTIN", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.RuleName.Should().StartWith("PublicInstitution_");
        apicilRule.MinConfidence.Should().Be(0.95); // High confidence for public institutions
        apicilRule.Priority.Should().BeLessOrEqualTo(3); // High priority
    }

    [Test]
    public void GenerateRules_DetectsBanks_AssignsCorrectAccount()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA BNP PARIBAS FRAIS", "62700000", "debit"),
                    CreateEntry("COMMISSION CREDIT AGRICOLE", "62700000", "debit"),
                    CreateEntry("VIR CLIENT DUPONT", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        var bankRules = result.Where(r => r.RuleName.StartsWith("Bank_")).ToList();
        bankRules.Should().HaveCountGreaterThan(0);
        
        foreach (var bankRule in bankRules)
        {
            bankRule.MinConfidence.Should().Be(0.90); // High confidence for banks
            bankRule.Priority.Should().BeLessOrEqualTo(5); // Medium-high priority
        }
    }

    [Test]
    public void GenerateRules_EntityRules_HaveHighConfidence()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("PRLV SEPA CPAM BOUCHES DU RHONE", "43700100", "debit"),
                    CreateEntry("VIR SEPA URSSAF PROVENCE", "43700200", "debit"),
                    CreateEntry("COTISATION CAF MARSEILLE", "43700300", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        var publicInstitutionRules = result.Where(r => r.RuleName.StartsWith("PublicInstitution_")).ToList();
        publicInstitutionRules.Should().HaveCountGreaterThan(0);
        
        foreach (var rule in publicInstitutionRules)
        {
            rule.MinConfidence.Should().BeGreaterOrEqualTo(0.95);
            rule.Priority.Should().BeLessOrEqualTo(3); // Highest priority for public institutions
        }
    }

    [Test]
    public void GenerateRules_PrioritizesBusinessEntities_OverGeneric()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"),
                    CreateEntry("OPERATION DIVERSE CLIENT", "41100000", "debit"),
                    CreateEntry("OPERATION DIVERSE FOURNISSEUR", "40100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        var genericRules = result.Where(r => r.RuleName.StartsWith("Generic_")).ToList();
        
        if (apicilRule != null && genericRules.Any())
        {
            apicilRule.Priority.Should().BeLessOrEqualTo(genericRules.Min(r => r.Priority));
        }
    }

    [Test]
    public void GenerateRules_IncludesExamples_InGeneratedRules()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA APICIL PREVOYANCE 123", "43701700", "debit"),
                    CreateEntry("PRLV SEPA APICIL MUTUELLE 456", "43701700", "debit"),
                    CreateEntry("VIR APICIL RETRAITE COMPLEM", "43701700", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Examples.Should().NotBeEmpty();
        apicilRule.Examples.Should().HaveCount(3); // All 3 examples should be included
        apicilRule.Examples.Should().Contain("VIR SEPA APICIL PREVOYANCE 123");
        apicilRule.Examples.Should().Contain("PRLV SEPA APICIL MUTUELLE 456");
        apicilRule.Examples.Should().Contain("VIR APICIL RETRAITE COMPLEM");
    }

    #endregion

    #region Test Data Helpers

    private RuleGenerationRequest CreateRequestWithEntity(string entityName, string accountingAccount, int frequency)
    {
        var entries = new List<AccountingEntry>();
        
        for (int i = 0; i < frequency; i++)
        {
            entries.Add(new AccountingEntry
            {
                BankAccountName = "51215000",
                AccountingAccount = "51215000",
                Label = $"VIR SEPA {entityName} OPERATION {i + 1}",
                Direction = "debit",
                Debit = 100m + i,
                Counterparts = new List<Counterpart>
                {
                    new Counterpart
                    {
                        AccountingAccount = accountingAccount,
                        Label = $"VIR SEPA {entityName} OPERATION {i + 1}",
                        Credit = 100m + i
                    }
                }
            });
        }

        return new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile { Entries = entries },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };
    }

    private AccountingEntry CreateEntry(string label, string counterpartAccount, string direction)
    {
        return new AccountingEntry
        {
            BankAccountName = "51215000",
            AccountingAccount = "51215000",
            Label = label,
            Direction = direction,
            Debit = direction == "debit" ? 100m : 0m,
            Credit = direction == "credit" ? 100m : 0m,
            Counterparts = new List<Counterpart>
            {
                new Counterpart
                {
                    AccountingAccount = counterpartAccount,
                    Label = label,
                    Debit = direction == "credit" ? 100m : 0m,
                    Credit = direction == "debit" ? 100m : 0m
                }
            }
        };
    }

    #endregion
}