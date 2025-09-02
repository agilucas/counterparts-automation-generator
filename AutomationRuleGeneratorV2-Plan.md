# AutomationRuleGeneratorV2 - Plan détaillé d'implémentation

## Vision et Objectifs

### Problèmes de la V1
- **Complexité excessive** : 850+ lignes, logique entremêlée
- **Seuils rigides** : Pas d'adaptabilité aux différents datasets
- **Gestion des conflits opaque** : Difficile à comprendre et déboguer
- **Absence de tests** : Impossible de valider les améliorations
- **Performance imprévisible** : Résultats variables selon les données
- **⚠️ PROBLÈME MAJEUR : Résultats non pertinents** : Les règles générées ne correspondent pas aux attentes métier

### Objectifs V2
- **🎯 PERTINENCE MÉTIER** : Règles qui correspondent réellement aux besoins comptables
- **Coverage rate > 85%** : Couvrir la majorité des transactions
- **Accuracy > 95%** : Règles précises et fiables
- **Maintenabilité** : Code clair, testé, évolutif
- **Adaptabilité** : Seuils dynamiques selon le volume de données
- **Traçabilité** : Décisions explicites et auditables

### 🔄 Changement d'approche fondamental
**La V2 ne cherche PAS à reproduire les résultats de la V1**. Elle vise à générer des règles **véritablement utiles** selon les spécifications expertes du CLAUDE.md :

1. **Focus sur les entités métier** : APICIL, BNP, CPAM, etc. avec leurs comptes spécifiques
2. **Patterns d'opérations standards** : CB, VIR, PRLV avec extraction intelligente
3. **Règles discriminantes** : Éliminer les ambiguïtés, privilégier la spécificité
4. **Validation métier** : Chaque règle doit avoir un sens comptable

## Architecture V2 - Approche fonctionnelle

### Principe de base
- **Un seul service** : `AutomationRuleGeneratorV2`
- **Tests unitaires comportementaux** : Valider les fonctionnalités end-to-end
- **Évolution progressive** : Chaque étape améliore les métriques
- **Spécifications expertes** : Respecter les règles comptables du CLAUDE.md

## Étapes d'implémentation

### Étape 1 : Structure de base + Tests du point d'entrée
**Objectif** : Poser les fondations avec validation immédiate

**Comportement attendu :**
- Accepte `RuleGenerationRequest` (debit + credit entries)
- Retourne `List<AutomationRule>` basique
- Génère au minimum 1 règle pour 1 entité simple

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_WithSimpleEntity_CreatesBasicRule()
[Test] 
public void GenerateRules_WithEmptyData_ReturnsEmptyList()
[Test]
public void GenerateRules_WithSingleEntry_ReturnsValidRule()
```

**Implémentation minimale :**
- Structure de classe
- Méthode `GenerateRules()` fonctionnelle
- Extraction basique d'1 entité fréquente

### Étape 2 : Analyse préliminaire intelligente
**Objectif** : Comprendre complètement les données d'entrée

**Comportement attendu :**
- Extrait TOUS les libellés uniques
- Groupe par compte de contrepartie
- Calcule fréquences et statistiques
- Identifie entités dominantes par compte

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_AnalyzesAllLabels_ExtractsUniquePatterns()
[Test]
public void GenerateRules_GroupsByCounterpartAccount_CorrectStatistics()
[Test]
public void GenerateRules_IdentifiesDominantEntities_ByFrequency()
```

**Améliorations :**
- Analyse exhaustive des données
- Statistiques de fréquence
- Détection d'entités par seuils adaptatifs

### Étape 3 : Règles d'entités prioritaires
**Objectif** : Créer des règles pour les entités métier importantes

**Comportement attendu :**
- Détecte organismes : APICIL, CPAM, URSSAF, BNP...
- Associe au bon compte comptable
- Priorité élevée (1-20)
- Confiance élevée (>0.95)

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_DetectsAPICIL_CreatesHighPriorityRule()
[Test]
public void GenerateRules_DetectsBanks_AssignsCorrectAccount()
[Test]
public void GenerateRules_EntityRules_HaveHighConfidence()
```

**Patterns spécifiques :**
- Organismes publics : `CPAM|URSSAF|CAF|POLE\s+EMPLOI`
- Banques : `BNP|CREDIT|SOCIETE\s+GENERALE`
- Entreprises : Noms en majuscules récurrents

### Étape 4 : Patterns d'opérations standardisés
**Objectif** : Reconnaître les types d'opérations bancaires

**Comportement attendu :**
- Frais bancaires → compte 62700000
- Cartes CB → extraction commerçant + porteur
- Virements → extraction bénéficiaire
- Prélèvements → extraction organisme

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_BankFees_AssignsCorrectAccount()
[Test]
public void GenerateRules_CreditCards_ExtractsKeywords()
[Test]
public void GenerateRules_Transfers_CapturesBeneficiary()
```

**Patterns selon CLAUDE.md :**
- `FRAIS.*BANC|COMMISSION|COTISATION`
- `CB\s+(.+?)\s+FACT\s+\d+\s+(.+)`
- `VIR\s+(.+?)`
- `PRLV\s+([A-Z\s]+)`

### Étape 5 : Seuils adaptatifs et validation
**Objectif** : Adapter les critères selon le volume de données

**Comportement attendu :**
- Seuils minimum selon taille dataset
- Validation croisée des règles
- Élimination règles <80% précision
- Score de confiance calculé

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_SmallDataset_LowersThresholds()
[Test]
public void GenerateRules_ValidatesRules_EliminatesLowPrecision()
[Test]
public void GenerateRules_CalculatesConfidence_RealisticScores()
```

**Seuils dynamiques :**
- Dataset <100 : seuil 3 occurrences
- Dataset 100-500 : seuil 5 occurrences  
- Dataset >500 : seuil 10 occurrences

### Étape 6 : Optimisation et résolution des conflits
**Objectif** : Règles finales cohérentes et optimales

**Comportement attendu :**
- Merge règles similaires intelligemment
- Résout conflits par priorité métier
- Ordonne par priorité décroissante
- Génère règles discriminantes si besoin

**Tests unitaires :**
```csharp
[Test]
public void GenerateRules_MergesSimilarRules_IntelligentConsolidation()
[Test]
public void GenerateRules_ResolvesConflicts_ByBusinessPriority()
[Test]
public void GenerateRules_FinalOutput_OrderedByPriority()
```

**Logique d'optimisation :**
- Entités > Opérations > Mots-clés
- Fréquence élevée > Spécificité
- Règles discriminantes si comptes différents

## Métriques de validation

### Par étape
1. **Base** : ≥1 règle générée
2. **Analyse** : 100% labels extraits
3. **Entités** : ≥70% entités métier détectées (APICIL, BNP, CPAM...)
4. **Opérations** : ≥80% patterns d'opérations reconnus (CB, VIR, PRLV...)
5. **Adaptatifs** : Seuils corrects selon dataset
6. **Optimisés** : ≥85% couverture, ≥95% précision

### 🎯 Critères de pertinence métier
- **Entités reconnues** : Organismes réels (pas de mots génériques)
- **Comptes cohérents** : Association logique entité → compte comptable
- **Patterns utiles** : Règles exploitables en production
- **Pas de bruit** : Élimination des règles trop génériques ou ambiguës

### Données de test
- Extraits de `debit_test.json` et `credit_test.json`
- Scénarios : dataset petit, moyen, grand
- Cas limites : données vides, entité unique, conflits multiples

## Livrables attendus

1. **AutomationRuleGeneratorV2.cs** : Service principal
2. **AutomationRuleGeneratorV2Tests.cs** : Tests unitaires complets
3. **Validation** : Métriques >85% couverture, >95% précision
4. **Documentation** : Décisions d'architecture et points d'amélioration

## Timeline

- **Étapes 1-2** : Structure et analyse → ~2-3 heures
- **Étapes 3-4** : Règles métier → ~3-4 heures  
- **Étapes 5-6** : Optimisation → ~2-3 heures
- **Tests et validation** : Transversal à chaque étape

**Total estimé : 7-10 heures de développement**