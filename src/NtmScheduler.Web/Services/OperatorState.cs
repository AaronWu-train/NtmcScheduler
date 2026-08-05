namespace NtmScheduler.Web.Services;

/// <summary>Cascading「目前操作者」state (persisted via OperatorBox + localStorage).</summary>
public sealed class OperatorState
{
    public string Name { get; private set; } = "未指定";

    public event Action? Changed;

    public void Set(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "未指定" : name.Trim();
        Changed?.Invoke();
    }
}
