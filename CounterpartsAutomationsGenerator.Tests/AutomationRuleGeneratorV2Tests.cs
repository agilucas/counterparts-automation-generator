using CounterpartsAutomationsGenerator.Models;
using CounterpartsAutomationsGenerator.Services;
using FluentAssertions;
using NUnit.Framework;
using NSubstitute;

namespace CounterpartsAutomationsGenerator.Tests;

[TestFixture]
public class AutomationRuleGeneratorV2Tests
{
    private AutomationRuleGeneratorV2 _generator;
    private IAdaptiveThresholdService _thresholdService;

    [SetUp]
    public void SetUp()
    {
        // Create mock with permissive thresholds for most tests
        _thresholdService = Substitute.For<IAdaptiveThresholdService>();
        _thresholdService.CalculateFrequencyThreshold(Arg.Any<int>()).Returns(1); // Very permissive
        _thresholdService.CalculateMinimumCoverage(Arg.Any<int>()).Returns(1);
        
        _generator = new AutomationRuleGeneratorV2(_thresholdService);
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
        apicilRule.MinConfidence.Should().BeGreaterOrEqualTo(0.95); // High confidence for public institutions
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
            bankRule.MinConfidence.Should().BeGreaterOrEqualTo(0.85); // High confidence for banks
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

    #region Step 4: Third party codes integration tests

    [Test]
    public void GenerateRules_ExtractsThirdPartyCodes_FromExistingCounterparts()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntryWithThirdParty("VIR SEPA APICIL PREVOYANCE", "43701700", "debit", "APIC001"),
                    CreateEntryWithThirdParty("PRLV APICIL MUTUELLE", "43701700", "debit", "APIC001"),
                    CreateEntryWithThirdParty("VIR BNP PARIBAS FRAIS", "62700000", "debit", "BNP")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        // Should extract third party codes from existing counterparts
        var rulesWithThirdParty = result.Where(r => !string.IsNullOrEmpty(r.ThirdPartyCode)).ToList();
        rulesWithThirdParty.Should().HaveCountGreaterThan(0);
    }

    [Test]
    public void GenerateRules_ProposesThirdPartyCode_BasedOnFrequency()
    {
        // Arrange - APICIL with consistent third party code
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntryWithThirdParty("VIR SEPA APICIL PREVOYANCE", "43701700", "debit", "APIC001"),
                    CreateEntryWithThirdParty("PRLV APICIL MUTUELLE", "43701700", "debit", "APIC001"),
                    CreateEntryWithThirdParty("VIR APICIL RETRAITE", "43701700", "debit", "APIC001")
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
        apicilRule!.ThirdPartyCode.Should().Be("APIC001"); // Most frequent code should be proposed
    }

    [Test]
    public void GenerateRules_IncludesThirdPartyCode_AsRuleEffect()
    {
        // Arrange - Multiple entities with different third party codes
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntryWithThirdParty("PRLV SEPA CPAM BOUCHES", "43700100", "debit", "CPAM13"),
                    CreateEntryWithThirdParty("VIR SEPA URSSAF PROVENCE", "43700200", "debit", "URSSAF"),
                    CreateEntryWithThirdParty("COTISATION CAF MARSEILLE", "43700300", "debit", "CAF13")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // All public institution rules should have third party codes as effects
        var publicInstitutionRules = result.Where(r => r.RuleName.StartsWith("PublicInstitution_")).ToList();
        publicInstitutionRules.Should().HaveCountGreaterThan(0);
        
        foreach (var rule in publicInstitutionRules)
        {
            rule.ThirdPartyCode.Should().NotBeNullOrEmpty("Public institution rules should have third party codes");
        }
    }

    #endregion

    #region Step 5: Standardized operation patterns tests

    [Test]
    public void GenerateRules_BankFees_AssignsCorrectAccount()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("FRAIS BANCAIRES TRIMESTRE", "62700000", "debit"),
                    CreateEntry("COMMISSION VIREMENT SEPA", "62700000", "debit"),
                    CreateEntry("COTISATION CARTE BLEUE", "62700000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should detect bank fee operations and assign correct account
        var bankFeeRules = result.Where(r => r.AccountingAccount == "62700000").ToList();
        bankFeeRules.Should().HaveCountGreaterThan(0);
        
        var bankFeeRule = bankFeeRules.FirstOrDefault(r => 
            r.Keyword1?.Contains("FRAIS", StringComparison.OrdinalIgnoreCase) == true ||
            r.RuleName.Contains("BankFees", StringComparison.OrdinalIgnoreCase));
        bankFeeRule.Should().NotBeNull();
    }

    [Test]
    public void GenerateRules_CreditCards_ExtractsKeywords()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("CB CARREFOUR MARSEILLE FACT 123 M. MARTIN", "62600000", "debit"),
                    CreateEntry("CB LECLERC LYON FACT 456 MME DURAND", "45100300", "debit"),
                    CreateEntry("CB AMAZON FACT 789 MONSIEUR BERNARD", "62600000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should detect credit card operations
        var cbRules = result.Where(r => 
            r.Keyword1?.Contains("CB", StringComparison.OrdinalIgnoreCase) == true ||
            r.RuleName.Contains("CreditCard", StringComparison.OrdinalIgnoreCase)).ToList();
        cbRules.Should().HaveCountGreaterThan(0);
    }

    [Test]
    public void GenerateRules_Transfers_CapturesBeneficiary()
    {
        // Arrange
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("VIR SEPA FOURNISSEUR DUPONT", "40110000", "debit"),
                    CreateEntry("VIR SALAIRE EMPLOYE MARTIN", "42100000", "debit"),
                    CreateEntry("VIR REMBOURSEMENT CLIENT", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should detect transfer operations
        var transferRules = result.Where(r => 
            r.Keyword1?.Contains("VIR", StringComparison.OrdinalIgnoreCase) == true ||
            r.RuleName.Contains("Transfer", StringComparison.OrdinalIgnoreCase)).ToList();
        transferRules.Should().HaveCountGreaterThan(0);
    }

    [Test]
    public void GenerateRules_DirectDebits_ExtractsOrganization()
    {
        // Arrange - Add more PRLV entries to ensure pattern is detected
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    CreateEntry("PRLV SEPA EDF ENERGIE", "60611000", "debit"),
                    CreateEntry("PRLV ORANGE TELECOM", "62600000", "debit"),
                    CreateEntry("PRLV ASSURANCE MAAF", "61610000", "debit"),
                    CreateEntry("PRLV SEPA EDF FACTURE", "60611000", "debit"),
                    CreateEntry("PRLV SFR TELECOM", "62600000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should detect direct debit operations
        var directDebitRules = result.Where(r => 
            r.Keyword1?.Contains("PRLV", StringComparison.OrdinalIgnoreCase) == true ||
            r.RuleName.Contains("DirectDebit", StringComparison.OrdinalIgnoreCase)).ToList();
        directDebitRules.Should().HaveCountGreaterThan(0);
    }

    #endregion

    #region Step 6: Bank account restrictions tests

    [Test]
    public void GenerateRules_RestrictsByBankAccount_ForSpecificPatterns()
    {
        // Arrange - Same transaction type on different bank accounts
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // CB fees on dirigeant account → should go to compte courant associé
                    CreateEntryWithBankAccount("FRAIS CB MENSUEL", "45100300", "debit", "512100"),
                    CreateEntryWithBankAccount("FRAIS CB MENSUEL", "45100300", "debit", "512100"),
                    // CB fees on société account → should go to frais généraux  
                    CreateEntryWithBankAccount("FRAIS CB MENSUEL", "62600000", "debit", "512000"),
                    CreateEntryWithBankAccount("FRAIS CB MENSUEL", "62600000", "debit", "512000")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should create bank account specific rules
        var restrictedRules = result.Where(r => !string.IsNullOrEmpty(r.RestrictedBankAccounts)).ToList();
        restrictedRules.Should().HaveCountGreaterThan(0);
        
        // Should have separate rules for different bank accounts
        var dirigeantRule = restrictedRules.FirstOrDefault(r => r.RestrictedBankAccounts?.Contains("512100") == true);
        var societeRule = restrictedRules.FirstOrDefault(r => r.RestrictedBankAccounts?.Contains("512000") == true);
        
        dirigeantRule.Should().NotBeNull();
        societeRule.Should().NotBeNull();
        
        // Should have different accounting accounts
        dirigeantRule.AccountingAccount.Should().Be("45100300");
        societeRule.AccountingAccount.Should().Be("62600000");
    }

    [Test]
    public void GenerateRules_CreatesAccountSpecificRules_ForSameEntity()
    {
        // Arrange - Same entity but different treatment based on bank account
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // URSSAF on dirigeant account
                    CreateEntryWithBankAccount("PRLV SEPA URSSAF PROVENCE", "45100300", "debit", "512100"),
                    CreateEntryWithBankAccount("COTISATION URSSAF TRIMESTRE", "45100300", "debit", "512100"),
                    // URSSAF on société account 
                    CreateEntryWithBankAccount("PRLV SEPA URSSAF PROVENCE", "40110000", "debit", "512000"),
                    CreateEntryWithBankAccount("COTISATION URSSAF TRIMESTRE", "40110000", "debit", "512000")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should create different rules for same entity on different bank accounts
        var urssafRules = result.Where(r => r.Keyword1?.Contains("URSSAF") == true).ToList();
        urssafRules.Should().HaveCountGreaterThan(1);
        
        // Should have at least one rule with bank account restriction
        var restrictedRule = urssafRules.FirstOrDefault(r => !string.IsNullOrEmpty(r.RestrictedBankAccounts));
        restrictedRule.Should().NotBeNull();
    }

    [Test]
    public void GenerateRules_LimitsRuleApplication_ToBankAccounts()
    {
        // Arrange - Pattern that should only apply to specific bank accounts
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // CB pattern only on specific account
                    CreateEntryWithBankAccount("CB CARREFOUR MARSEILLE", "62600000", "debit", "512100"),
                    CreateEntryWithBankAccount("CB LECLERC PROVENCE", "62600000", "debit", "512100"),
                    CreateEntryWithBankAccount("CB AMAZON ACHAT", "62600000", "debit", "512100"),
                    // Other entries on different accounts
                    CreateEntryWithBankAccount("VIR SEPA FOURNISSEUR DUPONT", "40110000", "debit", "512000")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should create rules with restricted bank accounts when pattern is account-specific
        var cbRules = result.Where(r => 
            r.Keyword1?.Contains("CB", StringComparison.OrdinalIgnoreCase) == true).ToList();
        
        if (cbRules.Any())
        {
            var restrictedCbRule = cbRules.FirstOrDefault(r => !string.IsNullOrEmpty(r.RestrictedBankAccounts));
            restrictedCbRule?.RestrictedBankAccounts.Should().Contain("512100");
        }
    }

    #endregion

    #region Step 7: Adaptive thresholds and validation tests

    [Test]
    public void GenerateRules_SmallDataset_LowersThresholds()
    {
        // Arrange - Use real threshold service for this test
        var realThresholdService = new AdaptiveThresholdService();
        var generatorWithRealService = new AutomationRuleGeneratorV2(realThresholdService);
        
        // Very small dataset (under 100 entries) with entities meeting the threshold
        var entries = new List<AccountingEntry>();
        
        // Add 3 APICIL entries to meet small dataset threshold (3 occurrences)
        entries.Add(CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"));
        entries.Add(CreateEntry("PRLV APICIL MUTUELLE", "43701700", "debit"));
        entries.Add(CreateEntry("COTISATION APICIL RETRAITE", "43701700", "debit"));
        
        // Add some other entries to reach small dataset size (around 50-80 total)
        for (int i = 0; i < 47; i++)
        {
            entries.Add(CreateEntry($"OPERATION DIVERSE {i}", "40100000", "debit"));
        }
        
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile { Entries = entries },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = generatorWithRealService.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should accept entities with 3+ occurrences in small datasets (threshold = 3 for <100 entries)
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull("Small dataset should accept patterns with 3+ occurrences");
        apicilRule!.Coverage.Should().Be(3);
        
        // Should have realistic confidence adjusted for small dataset
        apicilRule.MinConfidence.Should().BeGreaterThan(0.70);
        apicilRule.MinConfidence.Should().BeLessOrEqualTo(0.99);
    }

    [Test]
    public void GenerateRules_ValidatesRules_EliminatesLowPrecision()
    {
        // Arrange - Mixed quality patterns
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // High precision pattern - consistent accounting account
                    CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"),
                    CreateEntry("PRLV APICIL MUTUELLE", "43701700", "debit"),
                    CreateEntry("COTISATION APICIL RETRAITE", "43701700", "debit"),
                    
                    // Low precision pattern - inconsistent accounting accounts
                    CreateEntry("OPERATION DIVERS CLIENT A", "41100000", "debit"),
                    CreateEntry("OPERATION DIVERS CLIENT B", "40100000", "debit"), // Different account
                    CreateEntry("OPERATION DIVERS CLIENT C", "62600000", "debit")  // Different account
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // High precision rules should be kept
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Precision.Should().BeGreaterOrEqualTo(0.80);
        
        // All rules should meet minimum precision threshold
        foreach (var rule in result)
        {
            rule.Precision.Should().BeGreaterOrEqualTo(0.60, 
                $"Rule {rule.RuleName} should meet minimum precision threshold");
        }
    }

    [Test]
    public void GenerateRules_CalculatesConfidence_RealisticScores()
    {
        // Arrange - Various entity types and coverage levels
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // Public institution - should have high confidence
                    CreateEntry("PRLV SEPA CPAM BOUCHES DU RHONE", "43700100", "debit"),
                    CreateEntry("COTISATION CPAM TRIMESTRE", "43700100", "debit"),
                    
                    // Bank - should have medium-high confidence  
                    CreateEntry("FRAIS BANCAIRES BNP PARIBAS", "62700000", "debit"),
                    CreateEntry("COMMISSION BNP VIREMENT", "62700000", "debit"),
                    
                    // Generic pattern - should have lower confidence
                    CreateEntry("ACHAT FOURNITURE BUREAU", "60600000", "debit"),
                    CreateEntry("ACHAT MATERIEL DIVERS", "60600000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
        
        // Public institution rules should have highest confidence
        var publicRules = result.Where(r => r.RuleName.StartsWith("PublicInstitution_")).ToList();
        if (publicRules.Any())
        {
            foreach (var rule in publicRules)
            {
                rule.MinConfidence.Should().BeGreaterOrEqualTo(0.90);
            }
        }
        
        // Bank rules should have high confidence
        var bankRules = result.Where(r => r.RuleName.StartsWith("Bank_")).ToList();
        if (bankRules.Any())
        {
            foreach (var rule in bankRules)
            {
                rule.MinConfidence.Should().BeGreaterOrEqualTo(0.85);
            }
        }
        
        // All confidence scores should be realistic (between 0.70 and 0.99)
        foreach (var rule in result)
        {
            rule.MinConfidence.Should().BeGreaterOrEqualTo(0.70);
            rule.MinConfidence.Should().BeLessOrEqualTo(0.99);
        }
    }

    [Test]
    public void GenerateRules_LargeDataset_AppliesStricterThresholds()
    {
        // Arrange - Use real threshold service for this test
        var realThresholdService = new AdaptiveThresholdService();
        var generatorWithRealService = new AutomationRuleGeneratorV2(realThresholdService);
        
        // Large dataset simulation with many entries
        var entries = new List<AccountingEntry>();
        
        // Create 600 entries to simulate large dataset
        for (int i = 0; i < 200; i++)
        {
            entries.Add(CreateEntry($"VIR SEPA APICIL PREVOYANCE {i}", "43701700", "debit"));
        }
        for (int i = 0; i < 150; i++)
        {
            entries.Add(CreateEntry($"PRLV CPAM MARSEILLE {i}", "43700100", "debit"));
        }
        for (int i = 0; i < 100; i++)
        {
            entries.Add(CreateEntry($"FRAIS BANCAIRES DIVERS {i}", "62700000", "debit"));
        }
        // Add some low-frequency patterns (should be filtered out)
        for (int i = 0; i < 8; i++) // Below threshold of 10 for large datasets
        {
            entries.Add(CreateEntry($"OPERATION RARE {i}", "40100000", "debit"));
        }

        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile { Entries = entries },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = generatorWithRealService.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // High frequency entities should be included
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Coverage.Should().Be(200);
        
        var cpamRule = result.FirstOrDefault(r => r.Keyword1?.Contains("CPAM") == true);
        cpamRule.Should().NotBeNull();
        cpamRule!.Coverage.Should().Be(150);
        
        // Low frequency patterns should be filtered out in large datasets
        var rareRule = result.FirstOrDefault(r => r.Keyword1?.Contains("RARE") == true);
        rareRule.Should().BeNull("Low frequency patterns should be filtered out in large datasets");
        
        // Confidence should be adjusted for large dataset
        foreach (var rule in result)
        {
            rule.MinConfidence.Should().BeGreaterOrEqualTo(0.70);
            rule.Coverage.Should().BeGreaterOrEqualTo(5); // Minimum coverage for large datasets
        }
    }

    [Test]
    public void GenerateRules_CrossValidation_CalculatesAccuratePrecision()
    {
        // Arrange - Pattern with mixed precision
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // APICIL with consistent account (high precision)
                    CreateEntry("VIR SEPA APICIL PREVOYANCE", "43701700", "debit"),
                    CreateEntry("PRLV APICIL MUTUELLE", "43701700", "debit"),
                    CreateEntry("COTISATION APICIL RETRAITE", "43701700", "debit"),
                    
                    // Generic pattern with mixed accounts (lower precision)
                    CreateEntry("VIR FOURNISSEUR A", "40110000", "debit"),
                    CreateEntry("VIR FOURNISSEUR B", "40110000", "debit"),
                    CreateEntry("VIR FOURNISSEUR C", "62600000", "debit") // Different account - reduces precision
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // APICIL rule should have high precision (consistent account)
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Precision.Should().BeGreaterOrEqualTo(0.90);
        
        // Generic VIR rule (if created) should have moderate precision due to mixed accounts
        var virRule = result.FirstOrDefault(r => 
            r.Keyword1?.Contains("VIR") == true && 
            !r.Keyword1.Contains("APICIL") && 
            r.RuleName.StartsWith("Generic_"));
        if (virRule != null)
        {
            virRule.Precision.Should().BeLessOrEqualTo(0.85);
        }
    }

    [Test]
    public void GenerateRules_OptimizesConflicts_ResolvesByBusinessPriority()
    {
        // Arrange - Conflicting rules with different business priorities
        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile
            {
                Entries = new List<AccountingEntry>
                {
                    // "PARIBAS" appears in both BNP PARIBAS (bank) and generic operations
                    CreateEntry("FRAIS BNP PARIBAS MENSUEL", "62700000", "debit"),
                    CreateEntry("COMMISSION BNP PARIBAS", "62700000", "debit"),
                    CreateEntry("VIREMENT BNP PARIBAS CLIENT", "62700000", "debit"),
                    
                    // Generic "PARIBAS" references (should be deprioritized)
                    CreateEntry("OPERATION PARIBAS DIVERSE", "40100000", "debit"),
                    CreateEntry("TRANSACTION PARIBAS AUTRE", "41100000", "debit")
                }
            },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = _generator.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // Should resolve conflicts in favor of business entities
        var paribasRules = result.Where(r => r.Keyword1?.Contains("BNP") == true).ToList();
        paribasRules.Should().HaveCountLessOrEqualTo(2); // Should consolidate conflicting rules
        
        if (paribasRules.Any())
        {
            var bestRule = paribasRules.OrderBy(r => r.Priority).First();
            bestRule.RuleName.Should().StartWith("Bank_"); // Business entity should win
            bestRule.Precision.Should().BeGreaterOrEqualTo(0.85);
        }
        
        // Final rules should be ordered by priority
        var priorities = result.Select(r => r.Priority).ToList();
        priorities.Should().BeInAscendingOrder();
    }

    [Test]
    public void GenerateRules_MaintainsMinimumCoverage_BasedOnDatasetSize()
    {
        // Arrange - Use real threshold service for this test
        var realThresholdService = new AdaptiveThresholdService();
        var generatorWithRealService = new AutomationRuleGeneratorV2(realThresholdService);
        
        // Medium dataset with various coverage levels
        var entries = new List<AccountingEntry>();
        
        // High coverage entity (15 occurrences)
        for (int i = 0; i < 15; i++)
        {
            entries.Add(CreateEntry($"VIR SEPA APICIL OPERATION {i}", "43701700", "debit"));
        }
        
        // Medium coverage entity (8 occurrences)
        for (int i = 0; i < 8; i++)
        {
            entries.Add(CreateEntry($"PRLV CPAM MARSEILLE {i}", "43700100", "debit"));
        }
        
        // Low coverage entity (2 occurrences - below threshold for medium dataset)
        for (int i = 0; i < 2; i++)
        {
            entries.Add(CreateEntry($"OPERATION RARE {i}", "40100000", "debit"));
        }
        
        // Fill to make it a medium-sized dataset (~200 entries)
        for (int i = 0; i < 175; i++)
        {
            entries.Add(CreateEntry($"VIR DIVERS OPERATION {i}", "40110000", "debit"));
        }

        var request = new RuleGenerationRequest
        {
            DebitEntries = new AccountingEntryFile { Entries = entries },
            CreditEntries = new AccountingEntryFile { Entries = new List<AccountingEntry>() }
        };

        // Act
        var result = generatorWithRealService.GenerateRules(request);

        // Assert
        result.Should().NotBeNull();
        
        // High coverage entities should be included
        var apicilRule = result.FirstOrDefault(r => r.Keyword1?.Contains("APICIL") == true);
        apicilRule.Should().NotBeNull();
        apicilRule!.Coverage.Should().Be(15);
        
        // Medium coverage should be included for medium dataset
        var cpamRule = result.FirstOrDefault(r => r.Keyword1?.Contains("CPAM") == true);
        cpamRule.Should().NotBeNull();
        cpamRule!.Coverage.Should().Be(8);
        
        // All included rules should meet minimum coverage threshold
        foreach (var rule in result)
        {
            rule.Coverage.Should().BeGreaterOrEqualTo(3); // Medium dataset threshold
        }
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

    private AccountingEntry CreateEntryWithThirdParty(string label, string counterpartAccount, string direction, string thirdPartyCode)
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
                    ThirdPartyCode = thirdPartyCode,
                    Label = label,
                    Debit = direction == "credit" ? 100m : 0m,
                    Credit = direction == "debit" ? 100m : 0m
                }
            }
        };
    }

    private AccountingEntry CreateEntryWithBankAccount(string label, string counterpartAccount, string direction, string bankAccountName)
    {
        return new AccountingEntry
        {
            BankAccountName = bankAccountName,
            AccountingAccount = bankAccountName,
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