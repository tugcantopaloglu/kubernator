namespace Kubernator.Core.Generation.Emitters;

internal static class YamlValue
{
    public static string String(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }
        if (NeedsQuoting(value))
        {
            return $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }
        return value;
    }

    public static string Bool(bool value) => value ? "true" : "false";

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }
        var first = value[0];
        if (first is ' ' or '\t' or '*' or '&' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`' or '#' or '?' or ':' or ',' or '[' or ']' or '{' or '}')
        {
            return true;
        }
        switch (value)
        {
            case "true":
            case "false":
            case "yes":
            case "no":
            case "null":
            case "~":
            case "True":
            case "False":
            case "TRUE":
            case "FALSE":
            case "Yes":
            case "No":
            case "YES":
            case "NO":
            case "Null":
            case "NULL":
                return true;
        }
        if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return true;
        }
        foreach (var c in value)
        {
            if (c is ':' or '#' or '\n' or '\r' or '\t' or ' ' or '*' or '?' or '&' or '!' or '|' or '>')
            {
                return true;
            }
        }
        return false;
    }
}
