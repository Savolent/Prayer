using System;
using System.Globalization;
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

    public static bool TryParseCondition(
        string? token,
        out DslConditionAstNode? condition,
        out string? error)
    {
        condition = null;
        error = null;

        var expr = (token ?? string.Empty).Trim();
        if (expr.Length == 0)
        {
            error = "condition is empty";
            return false;
        }

        if (TryParseBooleanToken(expr, out var booleanToken))
        {
            condition = new DslBooleanTokenConditionAstNode(booleanToken);
            return true;
        }

        if (!TryParseComparison(expr, out var leftRaw, out var op, out var rightRaw))
        {
            error = "expected a known boolean token or comparison like FUEL() > 5";
            return false;
        }

        if (!TryParseNumericOperand(leftRaw, out var left))
        {
            error = $"unsupported left operand '{leftRaw.Trim()}'";
            return false;
        }

        if (!TryParseNumericOperand(rightRaw, out var right))
        {
            error = $"unsupported right operand '{rightRaw.Trim()}'";
            return false;
        }

        condition = new DslComparisonConditionAstNode(left, op, right);
        return true;
    }

    public static bool TryValidateCondition(DslConditionAstNode? condition, out string? error)
    {
        error = null;
        if (condition == null)
        {
            error = "condition is empty";
            return false;
        }

        switch (condition)
        {
            case DslBooleanTokenConditionAstNode booleanToken:
                if (!IsKnownBooleanToken(booleanToken.Token))
                {
                    error = $"unsupported boolean token '{booleanToken.Token}'";
                    return false;
                }
                return true;
            case DslComparisonConditionAstNode comparison:
                if (!IsSupportedComparisonOperator(comparison.Operator))
                {
                    error = $"unsupported operator '{comparison.Operator}'";
                    return false;
                }
                if (!IsValidNumericOperand(comparison.Left))
                {
                    error = "unsupported left operand";
                    return false;
                }
                if (!IsValidNumericOperand(comparison.Right))
                {
                    error = "unsupported right operand";
                    return false;
                }
                return true;
            default:
                error = "unknown condition node";
                return false;
        }
    }

    public static bool TryEvaluate(DslConditionAstNode? condition, GameState state, out bool value)
    {
        value = false;
        if (state == null || condition == null)
            return false;

        switch (condition)
        {
            case DslBooleanTokenConditionAstNode booleanToken:
                return TryEvaluateBooleanToken(booleanToken.Token, state, out value);
            case DslComparisonConditionAstNode comparison:
                if (!TryResolveNumericOperand(comparison.Left, state, out var left) ||
                    !TryResolveNumericOperand(comparison.Right, state, out var right))
                {
                    return false;
                }

                value = comparison.Operator switch
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
            default:
                return false;
        }
    }

    public static string RenderCondition(DslConditionAstNode? condition)
    {
        if (condition == null)
            return string.Empty;

        return condition switch
        {
            DslBooleanTokenConditionAstNode booleanToken =>
                booleanToken.Token.Trim().ToUpperInvariant(),
            DslComparisonConditionAstNode comparison =>
                $"{RenderNumericOperand(comparison.Left)} {comparison.Operator} {RenderNumericOperand(comparison.Right)}",
            _ => string.Empty
        };
    }

    private static bool TryParseBooleanToken(string token, out string normalizedToken)
    {
        normalizedToken = token.Trim().ToUpperInvariant();
        if (IsKnownBooleanToken(normalizedToken))
            return true;

        var fnMatch = FunctionRegex.Match(token.Trim());
        if (!fnMatch.Success)
            return false;

        normalizedToken = fnMatch.Groups["name"].Value.Trim().ToUpperInvariant();
        return IsKnownBooleanToken(normalizedToken);
    }

    private static bool IsKnownBooleanToken(string token)
        => string.Equals(token?.Trim(), "MISSION_COMPLETE", StringComparison.OrdinalIgnoreCase);

    private static bool TryEvaluateBooleanToken(string token, GameState state, out bool value)
    {
        value = false;
        var normalized = token.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "MISSION_COMPLETE":
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

    private static bool IsValidNumericOperand(DslNumericOperandAstNode operand)
    {
        return operand switch
        {
            DslIntegerOperandAstNode => true,
            DslMetricOperandAstNode metric => IsKnownMetric(metric.MetricName),
            _ => false
        };
    }

    private static bool TryParseNumericOperand(string raw, out DslNumericOperandAstNode operand)
    {
        operand = new DslIntegerOperandAstNode(0);
        var text = raw.Trim();
        if (text.Length == 0)
            return false;

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            operand = new DslIntegerOperandAstNode(numeric);
            return true;
        }

        if (!TryNormalizeMetricName(text, out var metricName))
            return false;

        if (!IsKnownMetric(metricName))
            return false;

        operand = new DslMetricOperandAstNode(metricName);
        return true;
    }

    private static string RenderNumericOperand(DslNumericOperandAstNode operand)
    {
        return operand switch
        {
            DslIntegerOperandAstNode integer => integer.Value.ToString(CultureInfo.InvariantCulture),
            DslMetricOperandAstNode metric => $"{metric.MetricName}()",
            _ => string.Empty
        };
    }

    private static bool TryResolveNumericOperand(
        DslNumericOperandAstNode operand,
        GameState state,
        out int value)
    {
        value = 0;
        switch (operand)
        {
            case DslIntegerOperandAstNode integer:
                value = integer.Value;
                return true;
            case DslMetricOperandAstNode metric:
                return TryGetMetricValue(metric.MetricName, state, out value);
            default:
                return false;
        }
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
                value = state.Ship.Fuel;
                return true;
            case "MAX_FUEL":
                value = state.Ship.MaxFuel;
                return true;
            case "CREDITS":
                value = state.Credits;
                return true;
            case "STORAGE_CREDITS":
                value = state.StorageCredits;
                return true;
            case "CARGO_USED":
                value = state.Ship.CargoUsed;
                return true;
            case "CARGO_CAPACITY":
                value = state.Ship.CargoCapacity;
                return true;
            case "HULL":
                value = state.Ship.Hull;
                return true;
            case "MAX_HULL":
                value = state.Ship.MaxHull;
                return true;
            case "SHIELD":
                value = state.Ship.Shield;
                return true;
            case "MAX_SHIELD":
                value = state.Ship.MaxShield;
                return true;
            case "CPU_USED":
                value = state.Ship.CpuUsed;
                return true;
            case "CPU_CAPACITY":
                value = state.Ship.CpuCapacity;
                return true;
            case "POWER_USED":
                value = state.Ship.PowerUsed;
                return true;
            case "POWER_CAPACITY":
                value = state.Ship.PowerCapacity;
                return true;
            case "SPEED":
                value = state.Ship.Speed;
                return true;
            case "ARMOR":
                value = state.Ship.Armor;
                return true;
            case "MODULE_COUNT":
                value = state.Ship.ModuleCount;
                return true;
            case "ACTIVE_MISSIONS":
                value = (state.ActiveMissions ?? Array.Empty<MissionInfo>()).Length;
                return true;
            default:
                return false;
        }
    }
}
