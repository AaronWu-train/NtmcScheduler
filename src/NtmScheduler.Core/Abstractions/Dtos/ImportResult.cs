namespace NtmScheduler.Core.Abstractions.Dtos;

public sealed record ImportError(int? Row, string Message, string? EmployeeId = null);

public sealed record ImportResult(int SuccessCount, IReadOnlyList<ImportError> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static ImportResult Ok(int count) => new(count, Array.Empty<ImportError>());

    public static ImportResult Fail(params ImportError[] errors) => new(0, errors);
}
