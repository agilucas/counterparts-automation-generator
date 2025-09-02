namespace CounterpartsAutomationsGenerator.Models;

public class AccountingEntryFile
{
    public List<AccountingEntry> Entries { get; set; } = new();
}

public class AccountingEntry
{
    public string BankAccountName { get; set; } = string.Empty;
    public string AccountingAccount { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? JournalCode { get; set; }
    public List<Counterpart> Counterparts { get; set; } = new();
}

public class Counterpart
{
    public string AccountingAccount { get; set; } = string.Empty;
    public string? ThirdPartyCode { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
}