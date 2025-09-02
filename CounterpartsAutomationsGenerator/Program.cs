using System.Text.Json;
using CounterpartsAutomationsGenerator.Models;
using CounterpartsAutomationsGenerator.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddScoped<AutomationRuleGenerator>();
builder.Services.AddScoped<AutomationRuleGeneratorV2>();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/api/generate-automation-rules", async (IFormFile debitFile, IFormFile creditFile, AutomationRuleGenerator generator) =>
{
    try
    {
        // Validation des fichiers d'entrée
        if (debitFile == null || creditFile == null)
        {
            return Results.BadRequest("Les fichiers de débits et crédits sont requis.");
        }

        if (!debitFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            !creditFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Les fichiers doivent être au format JSON.");
        }

        // Désérialisation des fichiers JSON
        AccountingEntryFile? debitEntries;
        AccountingEntryFile? creditEntries;

        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        await using (var debitStream = debitFile.OpenReadStream())
        using (var debitReader = new StreamReader(debitStream))
        {
            var debitJson = await debitReader.ReadToEndAsync();
            debitEntries = JsonSerializer.Deserialize<AccountingEntryFile>(debitJson, jsonSerializerOptions);
        }

        await using (var creditStream = creditFile.OpenReadStream())
        using (var creditReader = new StreamReader(creditStream))
        {
            var creditJson = await creditReader.ReadToEndAsync();
            creditEntries = JsonSerializer.Deserialize<AccountingEntryFile>(creditJson, jsonSerializerOptions);
        }

        if (debitEntries?.Entries == null || creditEntries?.Entries == null)
        {
            return Results.BadRequest("Erreur lors de la désérialisation des fichiers JSON.");
        }

        // Création de la requête pour le générateur
        var request = new RuleGenerationRequest
        {
            DebitEntries = debitEntries,
            CreditEntries = creditEntries
        };

        // Génération des règles d'automatisation
        var rules = generator.GenerateRules(request);

        // Export en CSV
        var csvContent = generator.ExportToCsv(rules);

        // Statistiques de génération
        var statistics = new
        {
            TotalRules = rules.Count,
            CoverageRate = CalculateCoverageRate(rules, request),
            AveragePrecision = rules.Count > 0 ? rules.Average(r => r.Precision) : 0,
            DebitRules = rules.Count(r => r.CreditOrDebit == "debit"),
            CreditRules = rules.Count(r => r.CreditOrDebit == "credit"),
            DebitEntriesCount = debitEntries.Entries.Count,
            CreditEntriesCount = creditEntries.Entries.Count
        };

        return Results.Ok(new
        {
            Statistics = statistics,
            CsvContent = csvContent,
            Rules = rules.Take(100) // Aperçu des 10 premières règles
        });
    }
    catch (JsonException ex)
    {
        return Results.Problem($"Erreur de format JSON: {ex.Message}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erreur lors de la génération des règles: {ex.Message}");
    }
})
.WithName("GenerateAutomationRules")
.DisableAntiforgery()
.WithSummary("Génère des règles d'automatisation comptable à partir des fichiers JSON de débits et crédits");

app.MapPost("/api/v2/generate-automation-rules", async (IFormFile debitFile, IFormFile creditFile, AutomationRuleGeneratorV2 generatorV2) =>
{
    try
    {
        var (rules, request) = await ProcessFiles(debitFile, creditFile, generatorV2);

        // Statistiques de génération V2 améliorées
        var businessEntityRules = rules.Where(r => r.RuleName.StartsWith("PublicInstitution_") || r.RuleName.StartsWith("Bank_")).ToList();
        var statistics = new
        {
            TotalRules = rules.Count,
            CoverageRate = CalculateCoverageRate(rules, request),
            AveragePrecision = rules.Count > 0 ? rules.Average(r => r.Precision) : 0,
            AverageConfidence = rules.Count > 0 ? rules.Average(r => r.MinConfidence) : 0,
            DebitRules = rules.Count(r => r.CreditOrDebit == "debit"),
            CreditRules = rules.Count(r => r.CreditOrDebit == "credit"),
            DebitEntriesCount = request.DebitEntries.Entries.Count,
            CreditEntriesCount = request.CreditEntries.Entries.Count,
            BusinessEntityRules = businessEntityRules.Count,
            PublicInstitutionRules = rules.Count(r => r.RuleName.StartsWith("PublicInstitution_")),
            BankRules = rules.Count(r => r.RuleName.StartsWith("Bank_")),
            GenericRules = rules.Count(r => r.RuleName.StartsWith("Generic_")),
            Version = "V2"
        };

        return Results.Ok(new
        {
            Statistics = statistics,
            Rules = rules, // Toutes les règles pour l'analyse
            RulesByCategory = new
            {
                PublicInstitutions = rules.Where(r => r.RuleName.StartsWith("PublicInstitution_")),
                Banks = rules.Where(r => r.RuleName.StartsWith("Bank_")),
                Generic = rules.Where(r => r.RuleName.StartsWith("Generic_"))
            }
        });
    }
    catch (JsonException ex)
    {
        return Results.Problem($"Erreur de format JSON: {ex.Message}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erreur lors de la génération des règles V2: {ex.Message}");
    }
})
.WithName("GenerateAutomationRulesV2")
.DisableAntiforgery()
.WithSummary("Génère des règles d'automatisation comptable V2 avec analyse métier intelligente - Format JSON complet");

app.MapPost("/api/v2/generate-automation-rules/csv", async (IFormFile debitFile, IFormFile creditFile, AutomationRuleGeneratorV2 generatorV2) =>
{
    try
    {
        var (rules, request) = await ProcessFiles(debitFile, creditFile, generatorV2);

        // Export en CSV avec la méthode V2
        var csvContent = generatorV2.ExportToCsv(rules);

        var response = Results.Stream(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent)),
            contentType: "text/csv",
            fileDownloadName: $"automation-rules-v2-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        );

        return response;
    }
    catch (JsonException ex)
    {
        return Results.Problem($"Erreur de format JSON: {ex.Message}");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erreur lors de la génération des règles V2 CSV: {ex.Message}");
    }
})
.WithName("GenerateAutomationRulesV2Csv")
.DisableAntiforgery()
.WithSummary("Génère des règles d'automatisation comptable V2 et retourne directement le fichier CSV");

app.MapGet("/api/health", () => Results.Ok(new { Status = "OK", Timestamp = DateTime.UtcNow }))
.WithName("HealthCheck");

app.Run();

// Fonction helper pour traiter les fichiers et générer les règles
static async Task<(List<AutomationRule> rules, RuleGenerationRequest request)> ProcessFiles(
    IFormFile debitFile, 
    IFormFile creditFile, 
    AutomationRuleGeneratorV2 generatorV2)
{
    // Validation des fichiers d'entrée
    if (debitFile == null || creditFile == null)
    {
        throw new ArgumentException("Les fichiers de débits et crédits sont requis.");
    }

    if (!debitFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        !creditFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("Les fichiers doivent être au format JSON.");
    }

    // Désérialisation des fichiers JSON
    AccountingEntryFile? debitEntries;
    AccountingEntryFile? creditEntries;

    var jsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    await using (var debitStream = debitFile.OpenReadStream())
    using (var debitReader = new StreamReader(debitStream))
    {
        var debitJson = await debitReader.ReadToEndAsync();
        debitEntries = JsonSerializer.Deserialize<AccountingEntryFile>(debitJson, jsonSerializerOptions);
    }

    await using (var creditStream = creditFile.OpenReadStream())
    using (var creditReader = new StreamReader(creditStream))
    {
        var creditJson = await creditReader.ReadToEndAsync();
        creditEntries = JsonSerializer.Deserialize<AccountingEntryFile>(creditJson, jsonSerializerOptions);
    }

    if (debitEntries?.Entries == null || creditEntries?.Entries == null)
    {
        throw new InvalidOperationException("Erreur lors de la désérialisation des fichiers JSON.");
    }

    // Création de la requête pour le générateur V2
    var request = new RuleGenerationRequest
    {
        DebitEntries = debitEntries,
        CreditEntries = creditEntries
    };

    // Génération des règles d'automatisation avec V2
    var rules = generatorV2.GenerateRules(request);

    return (rules, request);
}

// Fonction helper pour calculer le taux de couverture
static double CalculateCoverageRate(List<AutomationRule> rules, RuleGenerationRequest request)
{
    var totalEntries = request.DebitEntries.Entries.Count + request.CreditEntries.Entries.Count;
    var coveredEntries = rules.Sum(r => r.Coverage);
    return totalEntries > 0 ? Math.Min(100.0, (double)coveredEntries / totalEntries * 100) : 0;
}