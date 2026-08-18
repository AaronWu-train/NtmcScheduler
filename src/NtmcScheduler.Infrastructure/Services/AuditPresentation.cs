using System.Globalization;
using System.Text.Json;
using NtmcScheduler.Contracts;
using NtmcScheduler.Infrastructure.Data;

namespace NtmcScheduler.Infrastructure.Services;

public sealed record AuditAssignmentContext(
    string EmployeeCode,
    string EmployeeName,
    DateOnly Date,
    DateOnly Month,
    string ScheduleName);

public static class AuditPresentation
{
    private static readonly IReadOnlyDictionary<string, string> ActionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LoginSucceeded"] = "登入成功",
        ["LoginFailed"] = "登入失敗",
        ["LoginLockedOut"] = "帳號鎖定",
        ["LoginRateLimited"] = "登入限速",
        ["LogoutSucceeded"] = "登出",
        ["PasswordChanged"] = "修改密碼",
        ["PasswordReset"] = "重設密碼",
        ["InitialAdministratorCreated"] = "建立初始管理者",
        ["UserCreated"] = "建立帳號",
        ["UserPermissionsChanged"] = "變更權限",
        ["EmployeeCreated"] = "新增員工",
        ["EmployeeUpdated"] = "修改員工",
        ["EmployeeDeleted"] = "刪除員工",
        ["EmployeeCsvImported"] = "匯入員工 CSV",
        ["DemandCreated"] = "建立月需求",
        ["DemandDeleted"] = "刪除月需求",
        ["DemandEmployeeUpdated"] = "修改需求員工",
        ["DemandAssignmentUpdated"] = "修改需求日格",
        ["EmployeeDemandSubmissionUpdated"] = "修改員工填報",
        ["EmployeeDemandSubmissionAssignmentUpdated"] = "修改員工填報日格",
        ["DemandSubmissionImported"] = "匯入員工填報",
        ["DemandCsvImported"] = "匯入需求 CSV",
        ["PreviousScheduleUploaded"] = "上傳上月班表",
        ["PreviousScheduleSelected"] = "選擇上月班表",
        ["UploadedPreviousScheduleSelected"] = "選擇已上傳上月班表",
        ["DemandPreviousInheritedFieldsRestored"] = "統計上月 R/R1 與萬年班表",
        ["PerpetualScheduleUploaded"] = "上傳萬年班表",
        ["DemandPerpetualScheduleCleared"] = "清除需求萬年班表",
        ["MPerpetualScheduleUploaded"] = "上傳 M 萬年班表",
        ["MPerpetualPatternCreated"] = "新增 M 萬年模板",
        ["MPerpetualPatternUpdated"] = "修改 M 萬年模板",
        ["MPerpetualPatternDeleted"] = "刪除 M 萬年模板",
        ["ConfigurationRevisionCreated"] = "建立設定版本",
        ["ScheduleRunQueued"] = "排程求解",
        ["ScheduleRunCompleted"] = "求解完成",
        ["ScheduleRunFailed"] = "求解失敗",
        ["ScheduleAssignmentUpdated"] = "修改班表日格",
        ["ScheduleEmployeeMonthlyShiftUpdated"] = "修改 T 月班別",
        ["ScheduleAdopted"] = "採用班表",
        ["ScheduleUnadopted"] = "取消採用",
        ["ScheduleArchived"] = "封存班表",
        ["ScheduleRenamed"] = "重新命名班表",
        ["ScheduleVersionImported"] = "匯入班表版本",
        ["ScheduleCsvDownloaded"] = "下載班表 CSV",
        ["ExternalScheduleCsvDownloaded"] = "下載外派班表 CSV"
    };

    private static readonly IReadOnlyDictionary<string, string> ResourceTypeLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Employee"] = "員工",
        ["EmployeeList"] = "員工清單",
        ["DemandDraft"] = "月需求",
        ["DemandEmployee"] = "需求員工",
        ["EmployeeDemandSubmission"] = "員工填報",
        ["ScheduleVersion"] = "班表版本",
        ["ScheduleAssignment"] = "班表日格",
        ["ScheduleEmployeeSnapshot"] = "班表員工",
        ["ScheduleRun"] = "求解工作",
        ["User"] = "帳號",
        ["ConfigurationRevision"] = "共同設定",
        ["MPerpetualScheduleTemplate"] = "M 萬年班表",
        ["Authentication"] = "登入"
    };

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["employeeCode"] = "員工 ID",
        ["name"] = "姓名",
        ["userName"] = "帳號",
        ["month"] = "月份",
        ["demandMonth"] = "需求月份",
        ["scheduleName"] = "班表名稱",
        ["date"] = "日期",
        ["kind"] = "日格",
        ["shift"] = "班別",
        ["station"] = "車站",
        ["requestedRest"] = "指定 R休",
        ["monthlyShift"] = "月班別",
        ["openingRest"] = "月初 R",
        ["openingSpecialRest"] = "月初 R1",
        ["requestedLeaveRestCount"] = "R休上限",
        ["perpetualScheduleId"] = "萬年班表代號",
        ["employmentStartDate"] = "到職日",
        ["affiliation"] = "所屬",
        ["ability"] = "能力",
        ["fileName"] = "檔名",
        ["patternCount"] = "模板數",
        ["employeeCount"] = "員工數",
        ["bytes"] = "位元組",
        ["status"] = "狀態",
        ["candidateCount"] = "候選數",
        ["isDisabled"] = "停用",
        ["isAdministrator"] = "管理者",
        ["workspaces"] = "工作區",
        ["mustChangePassword"] = "須改密碼",
        ["scheduleVersionId"] = "班表版本",
        ["employees"] = "員工數",
        ["assignments"] = "日格數",
        ["eventStart"] = "公務開始",
        ["eventEnd"] = "公務結束",
        ["eventDescription"] = "公務說明"
    };

    public static IReadOnlyList<(string Action, string Label)> ActionOptions() =>
        ActionLabels.OrderBy(x => x.Value).Select(x => (x.Key, x.Value)).ToArray();

    public static AuditLogDto Format(AuditLog row, IReadOnlyDictionary<Guid, AuditAssignmentContext>? assignments = null)
    {
        var actionLabel = ActionLabels.GetValueOrDefault(row.Action, row.Action);
        AuditAssignmentContext? assignment = null;
        if (assignments is not null && row.ResourceType == "ScheduleAssignment" && Guid.TryParse(row.ResourceId, out var assignmentId))
            assignments.TryGetValue(assignmentId, out assignment);
        var changes = BuildChanges(row.BeforeJson, row.AfterJson);
        var targetSummary = BuildTargetSummary(row.Action, row.ResourceType, row.ResourceId, row.BeforeJson, row.AfterJson, assignment);
        var readableSummary = BuildReadableSummary(actionLabel, targetSummary, changes, row.Action);
        return new(
            row.Id,
            row.AtUtc,
            row.ActorName,
            row.ActorUserId,
            row.SessionId,
            row.IpAddress,
            row.UserAgent,
            row.Action,
            actionLabel,
            row.Workspace,
            targetSummary,
            readableSummary,
            row.Succeeded,
            row.CorrelationId,
            changes,
            new AuditTechnicalDetailsDto(
                row.ActorUserId,
                row.SessionId,
                row.Action,
                row.ResourceType,
                row.ResourceId,
                row.BeforeJson,
                row.AfterJson,
                row.IpAddress,
                row.UserAgent,
                row.CorrelationId));
    }

    private static string BuildReadableSummary(string actionLabel, string targetSummary, IReadOnlyList<AuditFieldChangeDto> changes, string action)
    {
        if (changes.Count == 0)
            return action is "LoginSucceeded" or "LogoutSucceeded" ? actionLabel : $"{actionLabel}：{targetSummary}";

        if (action is "ScheduleAssignmentUpdated" or "DemandAssignmentUpdated" or "EmployeeDemandSubmissionAssignmentUpdated")
        {
            var cellBefore = DescribeCell(changes);
            var cellAfter = DescribeCell(changes, after: true);
            if (cellBefore is not null || cellAfter is not null)
                return $"{actionLabel}：{targetSummary}，{cellBefore ?? "（無）"} → {cellAfter ?? "（無）"}";
        }

        if (changes.Count == 1)
            return $"{actionLabel}：{targetSummary}，{changes[0].Label} {changes[0].Before} → {changes[0].After}";

        return $"{actionLabel}：{targetSummary}（{changes.Count} 項變更）";
    }

    private static string? DescribeCell(IReadOnlyList<AuditFieldChangeDto> changes, bool after = false)
    {
        var station = changes.FirstOrDefault(x => x.Label == "車站");
        var shift = changes.FirstOrDefault(x => x.Label == "班別");
        var kind = changes.FirstOrDefault(x => x.Label == "日格");
        var parts = new List<string>();
        var stationValue = after ? station?.After : station?.Before;
        var shiftValue = after ? shift?.After : shift?.Before;
        var kindValue = after ? kind?.After : kind?.Before;
        if (!string.IsNullOrWhiteSpace(stationValue) && stationValue != "（無）") parts.Add(stationValue);
        if (!string.IsNullOrWhiteSpace(shiftValue) && shiftValue != "（無）") parts.Add(shiftValue);
        if (!string.IsNullOrWhiteSpace(kindValue) && kindValue != "（無）" &&
            (parts.Count == 0 || kindValue is not ("上班" or "休假")))
            parts.Add(kindValue);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static string BuildTargetSummary(
        string action,
        string resourceType,
        string resourceId,
        string? beforeJson,
        string? afterJson,
        AuditAssignmentContext? assignment)
    {
        var after = ParseObject(afterJson);
        var before = ParseObject(beforeJson);
        var data = after ?? before;

        var month = assignment?.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ?? FormatMonth(TryGet(data, "month", "demandMonth"));
        var scheduleName = assignment?.ScheduleName ?? TryGetString(data, "scheduleName");
        var employeeCode = assignment?.EmployeeCode ?? TryGetString(data, "employeeCode");
        var employeeName = assignment?.EmployeeName
            ?? (employeeCode is not null ? TryGetString(data, "name") : TryGetString(data, "userName"));
        var date = assignment?.Date.ToString("M/d", CultureInfo.InvariantCulture)
            ?? FormatDateShort(TryGet(data, "date"));
        var fileName = TryGetString(data, "fileName");

        if (action is "ScheduleAssignmentUpdated" or "DemandAssignmentUpdated" or "EmployeeDemandSubmissionAssignmentUpdated" &&
            employeeCode is not null && date is not null)
            return Join("／", MonthLabel(month), ScheduleLabel(scheduleName), Join(" ", EmployeeLabel(employeeCode, employeeName), date));

        if (employeeCode is not null)
            return Join("／", MonthLabel(month), ScheduleLabel(scheduleName), EmployeeLabel(employeeCode, employeeName));

        if (month is not null && scheduleName is not null)
            return $"{MonthLabel(month)} 班表「{scheduleName}」";

        if (month is not null)
            return $"{MonthLabel(month)} {ResourceTypeLabels.GetValueOrDefault(resourceType, resourceType)}";

        if (fileName is not null)
            return fileName;

        if (action.StartsWith("Login", StringComparison.Ordinal) || action is "LogoutSucceeded" or "PasswordChanged")
            return action is "LoginSucceeded" or "LogoutSucceeded" ? "系統登入" : ResourceTypeLabels.GetValueOrDefault(resourceType, resourceType);

        if (assignment is not null)
            return Join(" ", EmployeeLabel(assignment.EmployeeCode, assignment.EmployeeName), assignment.Date.ToString("M/d", CultureInfo.InvariantCulture));

        return $"{ResourceTypeLabels.GetValueOrDefault(resourceType, resourceType)} · {ShortId(resourceId)}";
    }

    private static IReadOnlyList<AuditFieldChangeDto> BuildChanges(string? beforeJson, string? afterJson)
    {
        var before = ParseObject(beforeJson);
        var after = ParseObject(afterJson);
        if (before is null && after is null) return [];

        var skipContext = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "month", "demandMonth", "scheduleName", "employeeCode", "name", "userName", "date"
        };

        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (before is not null) foreach (var property in before.Value.EnumerateObject()) keys.Add(property.Name);
        if (after is not null) foreach (var property in after.Value.EnumerateObject()) keys.Add(property.Name);

        var changes = new List<AuditFieldChangeDto>();
        foreach (var key in keys)
        {
            if (skipContext.Contains(key)) continue;
            var beforeValue = before is null || !TryGetProperty(before.Value, key, out var beforeElement) ? null : FormatValue(key, beforeElement);
            var afterValue = after is null || !TryGetProperty(after.Value, key, out var afterElement) ? null : FormatValue(key, afterElement);
            if (string.Equals(beforeValue, afterValue, StringComparison.Ordinal)) continue;
            changes.Add(new(FieldLabels.GetValueOrDefault(key, key), beforeValue ?? "（無）", afterValue ?? "（無）"));
        }

        if (changes.Count == 0 && before is null && after is not null)
            changes.Add(new("內容", "（無）", "已建立"));
        if (changes.Count == 0 && before is not null && after is null)
            changes.Add(new("內容", "已刪除", "（無）"));

        return changes;
    }

    private static JsonElement? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static JsonElement? TryGet(JsonElement? element, params string[] names)
    {
        if (element is null) return null;
        foreach (var name in names)
            if (TryGetProperty(element.Value, name, out var value)) return value;
        return null;
    }

    private static string? TryGetString(JsonElement? element, params string[] names)
    {
        var value = TryGet(element, names);
        return value is null ? null : FormatScalar(value.Value);
    }

    private static string? FormatValue(string key, JsonElement value) => key switch
    {
        "kind" => FormatKind(value),
        "shift" or "monthlyShift" => FormatShift(value),
        "requestedRest" or "isDisabled" or "isAdministrator" or "mustChangePassword" => FormatBool(value),
        "workspaces" => FormatWorkspaces(value),
        _ => FormatScalar(value)
    };

    private static string FormatScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "（無）",
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        JsonValueKind.String => FormatString(value.GetString() ?? ""),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => string.Join("、", value.EnumerateArray().Select(x => FormatScalar(x))),
        JsonValueKind.Object => value.GetRawText(),
        _ => value.GetRawText()
    };

    private static string FormatString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "（無）";
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly.ToString("M/d", CultureInfo.InvariantCulture);
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
            return offset.ToOffset(TimeSpan.FromHours(8)).ToString("M/d HH:mm", CultureInfo.InvariantCulture);
        return KindOrShiftLabel(raw);
    }

    private static string KindOrShiftLabel(string raw) => raw switch
    {
        "Work" => "上班",
        "Rest" => "R",
        "SpecialRest" => "R1",
        "LeaveRest" => "R休",
        "WorkEvent" => "公務",
        "Unresolved" => "待定",
        "Early" => "早班",
        "Afternoon" => "午班",
        "Night" => "夜班",
        _ => raw
    };

    private static string FormatKind(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "（無）";
        return value.ValueKind == JsonValueKind.String
            ? FormatString(value.GetString() ?? "")
            : FormatScalar(value);
    }

    private static string FormatShift(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "（無）";
        return value.ValueKind == JsonValueKind.String
            ? FormatString(value.GetString() ?? "")
            : FormatScalar(value);
    }

    private static string FormatBool(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        JsonValueKind.Null or JsonValueKind.Undefined => "（無）",
        _ => FormatScalar(value)
    };

    private static string FormatWorkspaces(JsonElement value) =>
        value.ValueKind != JsonValueKind.Array
            ? FormatScalar(value)
            : string.Join("、", value.EnumerateArray().Select(x => FormatScalar(x)));

    private static string? FormatMonth(JsonElement? value)
    {
        if (value is null) return null;
        if (value.Value.ValueKind == JsonValueKind.String)
        {
            var raw = value.Value.GetString() ?? "";
            return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("yyyy-MM", CultureInfo.InvariantCulture)
                : raw;
        }
        return FormatScalar(value.Value);
    }

    private static string? FormatDateShort(JsonElement? value)
    {
        if (value is null) return null;
        if (value.Value.ValueKind == JsonValueKind.String &&
            DateOnly.TryParse(value.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("M/d", CultureInfo.InvariantCulture);
        return FormatScalar(value.Value);
    }

    private static string MonthLabel(string? month) => month is null ? "" : month.Length >= 7 ? month[..7] : month;
    private static string ScheduleLabel(string? name) => string.IsNullOrWhiteSpace(name) ? "" : $"班表「{name}」";
    private static string EmployeeLabel(string code, string? name) => string.IsNullOrWhiteSpace(name) ? code : $"{code} {name}";
    private static string ShortId(string resourceId) => resourceId.Length > 8 ? resourceId[..8] : resourceId;

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(x => !string.IsNullOrWhiteSpace(x)));
}
