namespace CounterpartsAutomationsGenerator.Services;

public class AdaptiveThresholdService : IAdaptiveThresholdService
{
    public int CalculateFrequencyThreshold(int datasetSize)
    {
        // Adaptive thresholds based on dataset size - Step 7 enhanced
        return datasetSize switch
        {
            < 100 => 3,     // Small dataset: minimum 3 occurrences
            < 500 => 5,     // Medium dataset: minimum 5 occurrences  
            _ => 10         // Large dataset: minimum 10 occurrences
        };
    }

    public int CalculateMinimumCoverage(int datasetSize)
    {
        // Minimum coverage required based on dataset size
        return datasetSize switch
        {
            < 100 => 2,     // At least 2 occurrences for small datasets
            < 500 => 3,     // At least 3 occurrences for medium datasets
            _ => 5          // At least 5 occurrences for large datasets
        };
    }
}