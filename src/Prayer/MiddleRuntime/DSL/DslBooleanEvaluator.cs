using System;
using System.Linq;
using System.Text.RegularExpressions;

internal static class DslBooleanEvaluator
{
    private static readonly Regex ComparisonRegex =
        new(@"^\s*(?<left>.+?)\s*(?<op><=|>=|==|!=|<|>)\s*(?<right>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FunctionRegex =
        new(@"^(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(\s*\)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IdentifierRegex =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryEvaluate(string? token, GameState state, out bool value)
    {
        value = false;
        if (state == null)
            return false;

        var expr = (token ?? string.Empty).Trim();
        if (expr.Length == 0)
            return false;

        if (TryEvaluateBooleanToken(expr, state, out value))
            return true;

        if (!TryParseComparison(expr, out var leftRaw, out var op, out var rightRaw))
            return false;

        if (!TryResolveNumericOperand(leftRaw, state, out var left) ||
            !TryResolveNumericOperand(rightRaw, state, out var right))
        {
            return false;
        }

        value = op switch
        {
            ">" => left > right,
            ">=" => left >= right,
            "<" => left < right,
            "<=" => left <= right,
            "==" => left == right,
            "!=" => left != right,
            _ => false
        };
        return true;
    }

    public static bool TryValidateCondition(string? token, out string? error)
    {
        error = null;
        var expr = (token ?? string.Empty).Trim();
        if (expr.Length == 0)
        {
            error = "condition is empty";
            return false;
        }

        if (TryEvaluateBooleanToken(expr, state: null, out _))
            return true;

        if (!TryParseComparison(expr, out var leftRaw, out var op, out var rightRaw))
        {
            error = "expected a known boolean token or comparison like FUEL() > 5";
            return false;
        }

        if (!IsSupportedComparisonOperator(op))
        {
            error = $"unsupported operator '{op}'";
            return false;
        }

        if (!TryResolveNumericOperand(leftRaw, state: null, out _))
        {
            error = $"unsupported left operand '{leftRaw.Trim()}'";
            return false;
        }

        if (!TryResolveNumericOperand(rightRaw, state: null, out _))
        {
            error = $"unsupported right operand '{rightRaw.Trim()}'";
            return false;
        }

        return true;
    }

    private static bool TryEvaluateBooleanToken(string token, GameState? state, out bool value)
    {
        value = false;
        var normalized = token.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "MISSION_COMPLETE":
                if (state == null)
                {
                    value = false;
                    return true;
                }

                var missions = state.ActiveMissions ?? Array.Empty<MissionInfo>();
                value = missions.Length == 0 || missions.Any(m => m != null && m.Completed);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseComparison(
        string expr,
        out string left,
        out string op,
        out string right)
    {
        left = string.Empty;
        op = string.Empty;
        right = string.Empty;

        var match = ComparisonRegex.Match(expr);
        if (!match.Success)
            return false;

        left = match.Groups["left"].Value;
        op = match.Groups["op"].Value;
        right = match.Groups["right"].Value;
        return left.Trim().Length > 0 && right.Trim().Length > 0;
    }

    private static bool IsSupportedComparisonOperator(string op)
        => op is ">" or ">=" or "<" or "<=" or "==" or "!=";

    private static bool TryResolveNumericOperand(string raw, GameState? state, out int value)
    {
        value = 0;
        var operand = raw.Trim();
        if (operand.Length == 0)
            return false;

        if (int.TryParse(operand, out value))
            return true;

        if (!TryNormalizeMetricName(operand, out var metricName))
            return false;

        if (state == null)
            return IsKnownMetric(metricName);

        return TryGetMetricValue(metricName, state, out value);
    }

    private static bool TryNormalizeMetricName(string operand, out string metricName)
    {
        metricName = string.Empty;
        var fnMatch = FunctionRegex.Match(operand);
        if (fnMatch.Success)
        {
            metricName = fnMatch.Groups["name"].Value.Trim().ToUpperInvariant();
            return metricName.Length > 0;
        }

        if (!IdentifierRegex.IsMatch(operand))
            return false;

        metricName = operand.Trim().ToUpperInvariant();
        return metricName.Length > 0;
    }

    private static bool IsKnownMetric(string metricName)
    {
        return metricName switch
        {
            "FUEL" => true,
            "MAX_FUEL" => true,
            "CREDITS" => true,
            "STORAGE_CREDITS" => true,
            "CARGO_USED" => true,
            "CARGO_CAPACITY" => true,
            "HULL" => true,
            "MAX_HULL" => true,
            "SHIELD" => true,
            "MAX_SHIELD" => true,
            "CPU_USED" => true,
            "CPU_CAPACITY" => true,
            "POWER_USED" => true,
            "POWER_CAPACITY" => true,
            "SPEED" => true,
            "ARMOR" => true,
            "MODULE_COUNT" => true,
            "ACTIVE_MISSIONS" => true,
            _ => false
        };
    }

    private static bool TryGetMetricValue(string metricName, GameState state, out int value)
    {
        value = 0;
        switch (metricName)
        {
            case "FUEL":
                value = state.Fuel;
                return true;
            case "MAX_FUEL":
                value = state.MaxFuel;
                return true;
            case "CREDITS":
                value = state.Credits;
                return true;
            case "STORAGE_CREDITS":
                value = state.StorageCredits;
                return true;
            case "CARGO_USED":
                value = state.CargoUsed;
                return true;
            case "CARGO_CAPACITY":
                value = state.CargoCapacity;
                return true;
            case "HULL":
                value = state.Hull;
                return true;
            case "MAX_HULL":
                value = state.MaxHull;
                return true;
            case "SHIELD":
                value = state.Shield;
                return true;
            case "MAX_SHIELD":
                value = state.MaxShield;
                return true;
            case "CPU_USED":
                value = state.CpuUsed;
                return true;
            case "CPU_CAPACITY":
                value = state.CpuCapacity;
                return true;
            case "POWER_USED":
                value = state.PowerUsed;
                return true;
            case "POWER_CAPACITY":
                value = state.PowerCapacity;
                return true;
            case "SPEED":
                value = state.Speed;
                return true;
            case "ARMOR":
                value = state.Armor;
                return true;
            case "MODULE_COUNT":
                value = state.ModuleCount;
                return true;
            case "ACTIVE_MISSIONS":
                value = (state.ActiveMissions ?? Array.Empty<MissionInfo>()).Length;
                return true;
            default:
                return false;
        }
    }
}
