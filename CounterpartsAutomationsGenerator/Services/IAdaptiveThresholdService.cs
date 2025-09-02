namespace CounterpartsAutomationsGenerator.Services;

public interface IAdaptiveThresholdService
{
    /// <summary>
    /// Calculate the minimum frequency threshold based on dataset size
    /// </summary>
    /// <param name="datasetSize">Total number of entries in the dataset</param>
    /// <returns>Minimum frequency threshold for rule generation</returns>
    int CalculateFrequencyThreshold(int datasetSize);
    
    /// <summary>
    /// Calculate the minimum coverage threshold for rule validation
    /// </summary>
    /// <param name="datasetSize">Total number of entries in the dataset</param>
    /// <returns>Minimum coverage threshold for rule inclusion</returns>
    int CalculateMinimumCoverage(int datasetSize);
}