# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

CounterpartsAutomationsGenerator is a .NET 9.0 Web API application that generates accounting automation rules from JSON files containing debit and credit entries. The system analyzes accounting patterns to create intelligent rules for automatic counterpart matching.

## Development Commands

### Build and Run
- `dotnet build` - Build the solution
- `dotnet run --project CounterpartsAutomationsGenerator` - Run the application locally
- Application runs on https://localhost:7178 and http://localhost:5146

### Testing
- `dotnet test` - Run unit tests
- Follow Test-Driven Development (TDD) approach

**Testing Strategy:**
- **Tests First**: Write unit tests before implementing features
- **Red-Green-Refactor**: Follow TDD cycle (failing test → implementation → refactoring)
- **Test Coverage**: Aim for comprehensive coverage of business logic
- **Test Categories**:
  - Unit tests for AutomationRuleGenerator service logic
- **Continuous Testing**: Run tests frequently during development

### Development Environment
- Target Framework: .NET 9.0
- Development URL: https://localhost:7178
- Swagger UI available at `/swagger` in development mode

## Architecture Overview

### Core Components

**Models (`Models/`)**:
- `AccountingEntry` - Represents banking transactions with debits, credits, and counterparts
- `AutomationRule` - Generated rules with keywords, priorities, and matching logic
- `PatternAnalysis` - Internal analysis of recurring patterns in accounting data

**Services (`Services/`)**:
- `AutomationRuleGenerator` - Core service that analyzes accounting entries and generates automation rules using pattern recognition, entity extraction, and keyword analysis

**Controllers (`Controllers/`)**:
- `AutomationRulesController` - REST API endpoints for rule generation

### Key Features

1. **Dynamic Entity Recognition**: Extracts organization names, bank names, and public institutions from transaction labels
2. **Pattern Analysis**: Identifies operation types (bank fees, card payments, transfers, etc.)
3. **Rule Optimization**: Eliminates redundant rules and resolves conflicts through intelligent merging
4. **CSV Export**: Generates rules in CSV format for import into accounting systems

### API Endpoints

- `POST /api/generate-automation-rules` - Main endpoint accepting debit and credit JSON files
- `GET /api/health` - Health check endpoint

### Data Flow

1. Upload debit/credit JSON files via form data
2. Deserialize accounting entries with case-insensitive options
3. Extract dynamic entity patterns from labels
4. Analyze patterns by accounting account and direction
5. Generate rules with priority, keywords, and confidence scores
6. Validate and optimize rules for conflicts
7. Export to CSV format with statistics

### Rule Generation Logic

- **Priority 1**: Dynamic entities (organizations, banks) - high confidence (0.95)
- **Priority 2**: Operation patterns (fees, transfers, cards) - medium confidence (0.90)  
- **Priority 3**: Generic keywords - variable confidence based on frequency

### Configuration Files

- `appsettings.json` / `appsettings.Development.json` - Application configuration
- `launchSettings.json` - Development server settings
- `.csproj` - Project dependencies including Swagger/OpenAPI packages

### Test Data

- `debit_test.json` - Sample debit entries for testing
- `credit_test.json` - Sample credit entries for testing

## Code Conventions

- Use C# 9.0+ features (records, nullable reference types enabled)
- Services registered via dependency injection
- Pattern matching for operation type detection
- LINQ extensively used for data analysis
- Regex patterns for entity and keyword extraction

## Expert Accounting Automation Specifications

You are an accounting expert specialized in automation of accounting entries. From JSON files containing accounting entries, generate a comprehensive set of automation rules.

### Objectives:
- Coverage rate > 85%
- Accuracy > 95%
- Robust and maintainable rules

### Methodology:

#### 1. Preliminary Analysis
- Extract ALL unique labels from the JSON file
- Group by counterpart accounting account (AccountingAccount of Counterparts)
- Identify recurring patterns by frequency of occurrence
- Analyze variations and synonyms for each pattern

#### 2. Rule Creation Strategy
**Priority Order:**
1. **Specific rules** (exact keywords, proper names)
2. **Structure rules** (regex patterns for standardized formats)
3. **Generic rules** (broad categories)

**Quality Criteria:**
- A rule must cover ≥ 10 occurrences OR be very specific (proper name)
- Avoid overlaps between rules (test priority)
- Use discriminating keywords (avoid overly generic words)

#### 3. Types of Rules to Create

**A. Entity/Supplier Rules:**
- Company names (FRANFINANCE, GRENKE, BNP PARIBAS...)
- Public organizations (CPAM, URSSAF, APICIL...)
- Recurring service providers

**B. Operation Type Rules:**
- Bank fees: `FRAIS.*BANC|COMMISSION|COTISATION`
- Direct debits: `PRLV\s+([A-Z\s]+)` (capture name)
- Bank cards: `CB\s+(.+?)\s+FACT\s+\d+\s+(.+)` (capture merchant + cardholder)
- Transfers: `VIR\s+(.+?)` (capture beneficiary)
- Refunds: `REMB|RBT\s+(NDF|NOTE)`

**C. Account Restriction Rules:**
- Personal expenses on specific accounts
- Operations by subsidiary/department

#### 4. Enhanced CSV Output Format
```csv
Priority,AccountingAccount,ThirdPartyCode,RuleName,Keyword1,Keyword2,Keyword3,RestrictedBankAccounts,MinConfidence
```

**Additional Columns:**
- Priority: 1-100 (1 = maximum priority)
- RuleName: explicit rule name
- MinConfidence: confidence threshold (0.8-1.0)

#### 5. Validation Rules
- Test each rule on historical data
- Eliminate rules with <80% accuracy
- Merge redundant rules
- Order by descending priority

#### 6. Special Cases Management

**Bank Cards:**
- Pattern: `CB\s+(.+?)\s+FACT.*?(M\.|MME|MADAME|MONSIEUR)\s+(.+)`
- Extract: merchant + cardholder
- Rule: if cardholder = employee → 45100300, else → 40110000

**Internal Transfers:**
- Detect transfers between company accounts
- Specific account based on movement type

**Exceptional Amounts:**
- Different rules based on amount (e.g., >€10,000)

#### 7. Expected Deliverables
1. CSV file with all rules
2. Analysis report with:
   - Coverage rate per rule
   - Identified uncovered patterns
   - Improvement suggestions
3. Rule validation script