namespace Kubernator.Core.Vulnerabilities;

// Faithful port of the FIRST.org CVSS v4.0 reference calculator (cvss_score.js,
// cvss_lookup.js, max_composed.js, max_severity.js), BSD-2-Clause. Given a v4.0
// vector string it returns the base(+threat+environmental) score, or null when
// the vector cannot be parsed.
internal static class Cvss40Calculator
{
    private static readonly string[] BaseMetrics =
        ["AV", "AC", "AT", "PR", "UI", "VC", "VI", "VA", "SC", "SI", "SA"];

    private static readonly string[] ImpactMetrics =
        ["VC", "VI", "VA", "SC", "SI", "SA"];

    public static double? TryScore(string vector)
    {
        var selected = ParseVector(vector);
        if (selected is null)
        {
            return null;
        }
        foreach (var required in BaseMetrics)
        {
            if (!selected.ContainsKey(required))
            {
                return null;
            }
        }
        try
        {
            return Score(selected);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ParseVector(string vector)
    {
        var idx = vector.IndexOf("CVSS:4.0/", StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }
        var body = vector[(idx + "CVSS:4.0/".Length)..];
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in body.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && kv[0].Length > 0 && kv[1].Length > 0)
            {
                selected[kv[0].ToUpperInvariant()] = kv[1].ToUpperInvariant();
            }
        }
        return selected;
    }

    private static string M(Dictionary<string, string> selected, string metric)
    {
        var value = selected.GetValueOrDefault(metric, "X");
        if (metric == "E" && value == "X")
        {
            return "A";
        }
        if ((metric == "CR" || metric == "IR" || metric == "AR") && value == "X")
        {
            return "H";
        }
        if (selected.TryGetValue("M" + metric, out var modified) && modified != "X")
        {
            return modified;
        }
        return value;
    }

    private static double Score(Dictionary<string, string> selected)
    {
        if (ImpactMetrics.All(x => M(selected, x) == "N"))
        {
            return 0.0;
        }

        var macroVector = MacroVector(selected);
        var value = Lookup[macroVector];

        var eq1 = macroVector[0] - '0';
        var eq2 = macroVector[1] - '0';
        var eq3 = macroVector[2] - '0';
        var eq4 = macroVector[3] - '0';
        var eq5 = macroVector[4] - '0';
        var eq6 = macroVector[5] - '0';

        var eq1NextLower = $"{eq1 + 1}{eq2}{eq3}{eq4}{eq5}{eq6}";
        var eq2NextLower = $"{eq1}{eq2 + 1}{eq3}{eq4}{eq5}{eq6}";
        var eq4NextLower = $"{eq1}{eq2}{eq3}{eq4 + 1}{eq5}{eq6}";
        var eq5NextLower = $"{eq1}{eq2}{eq3}{eq4}{eq5 + 1}{eq6}";

        string? eq3eq6NextLower = null;
        string? eq3eq6NextLowerLeft = null;
        string? eq3eq6NextLowerRight = null;
        if (eq3 == 1 && eq6 == 1)
        {
            eq3eq6NextLower = $"{eq1}{eq2}{eq3 + 1}{eq4}{eq5}{eq6}";
        }
        else if (eq3 == 0 && eq6 == 1)
        {
            eq3eq6NextLower = $"{eq1}{eq2}{eq3 + 1}{eq4}{eq5}{eq6}";
        }
        else if (eq3 == 1 && eq6 == 0)
        {
            eq3eq6NextLower = $"{eq1}{eq2}{eq3}{eq4}{eq5}{eq6 + 1}";
        }
        else if (eq3 == 0 && eq6 == 0)
        {
            eq3eq6NextLowerLeft = $"{eq1}{eq2}{eq3}{eq4}{eq5}{eq6 + 1}";
            eq3eq6NextLowerRight = $"{eq1}{eq2}{eq3 + 1}{eq4}{eq5}{eq6}";
        }
        else
        {
            eq3eq6NextLower = $"{eq1}{eq2}{eq3 + 1}{eq4}{eq5}{eq6 + 1}";
        }

        var scoreEq1NextLower = LookupOrNaN(eq1NextLower);
        var scoreEq2NextLower = LookupOrNaN(eq2NextLower);
        double scoreEq3eq6NextLower;
        if (eq3 == 0 && eq6 == 0)
        {
            var left = LookupOrNaN(eq3eq6NextLowerLeft!);
            var right = LookupOrNaN(eq3eq6NextLowerRight!);
            scoreEq3eq6NextLower = left > right ? left : right;
        }
        else
        {
            scoreEq3eq6NextLower = LookupOrNaN(eq3eq6NextLower!);
        }
        var scoreEq4NextLower = LookupOrNaN(eq4NextLower);
        var scoreEq5NextLower = LookupOrNaN(eq5NextLower);

        var eq1Maxes = MaxComposedEq1[eq1];
        var eq2Maxes = MaxComposedEq2[eq2];
        var eq3eq6Maxes = MaxComposedEq3Eq6[eq3][eq6];
        var eq4Maxes = MaxComposedEq4[eq4];
        var eq5Maxes = MaxComposedEq5[eq5];

        var maxVectors = new List<string>();
        foreach (var a in eq1Maxes)
        foreach (var b in eq2Maxes)
        foreach (var c in eq3eq6Maxes)
        foreach (var d in eq4Maxes)
        foreach (var e in eq5Maxes)
        {
            maxVectors.Add(a + b + c + d + e);
        }

        double sdAv = 0, sdPr = 0, sdUi = 0, sdAc = 0, sdAt = 0,
            sdVc = 0, sdVi = 0, sdVa = 0, sdSc = 0, sdSi = 0, sdSa = 0,
            sdCr = 0, sdIr = 0, sdAr = 0;

        foreach (var maxVector in maxVectors)
        {
            sdAv = AvLevels[M(selected, "AV")] - AvLevels[Extract("AV", maxVector)];
            sdPr = PrLevels[M(selected, "PR")] - PrLevels[Extract("PR", maxVector)];
            sdUi = UiLevels[M(selected, "UI")] - UiLevels[Extract("UI", maxVector)];
            sdAc = AcLevels[M(selected, "AC")] - AcLevels[Extract("AC", maxVector)];
            sdAt = AtLevels[M(selected, "AT")] - AtLevels[Extract("AT", maxVector)];
            sdVc = VcLevels[M(selected, "VC")] - VcLevels[Extract("VC", maxVector)];
            sdVi = VcLevels[M(selected, "VI")] - VcLevels[Extract("VI", maxVector)];
            sdVa = VcLevels[M(selected, "VA")] - VcLevels[Extract("VA", maxVector)];
            sdSc = ScLevels[M(selected, "SC")] - ScLevels[Extract("SC", maxVector)];
            sdSi = SiLevels[M(selected, "SI")] - SiLevels[Extract("SI", maxVector)];
            sdSa = SiLevels[M(selected, "SA")] - SiLevels[Extract("SA", maxVector)];
            sdCr = CrLevels[M(selected, "CR")] - CrLevels[Extract("CR", maxVector)];
            sdIr = CrLevels[M(selected, "IR")] - CrLevels[Extract("IR", maxVector)];
            sdAr = CrLevels[M(selected, "AR")] - CrLevels[Extract("AR", maxVector)];

            var distances = new[] { sdAv, sdPr, sdUi, sdAc, sdAt, sdVc, sdVi, sdVa, sdSc, sdSi, sdSa, sdCr, sdIr, sdAr };
            if (distances.Any(x => x < 0))
            {
                continue;
            }
            break;
        }

        var currentEq1 = sdAv + sdPr + sdUi;
        var currentEq2 = sdAc + sdAt;
        var currentEq3eq6 = sdVc + sdVi + sdVa + sdCr + sdIr + sdAr;
        var currentEq4 = sdSc + sdSi + sdSa;

        const double step = 0.1;

        var availableEq1 = value - scoreEq1NextLower;
        var availableEq2 = value - scoreEq2NextLower;
        var availableEq3eq6 = value - scoreEq3eq6NextLower;
        var availableEq4 = value - scoreEq4NextLower;
        var availableEq5 = value - scoreEq5NextLower;

        var maxSeverityEq1 = MaxSeverityEq1[eq1] * step;
        var maxSeverityEq2 = MaxSeverityEq2[eq2] * step;
        var maxSeverityEq3eq6 = MaxSeverityEq3Eq6[eq3][eq6] * step;
        var maxSeverityEq4 = MaxSeverityEq4[eq4] * step;

        double normalizedEq1 = 0, normalizedEq2 = 0, normalizedEq3eq6 = 0, normalizedEq4 = 0, normalizedEq5 = 0;
        var nExistingLower = 0;

        if (!double.IsNaN(availableEq1))
        {
            nExistingLower++;
            normalizedEq1 = availableEq1 * (currentEq1 / maxSeverityEq1);
        }
        if (!double.IsNaN(availableEq2))
        {
            nExistingLower++;
            normalizedEq2 = availableEq2 * (currentEq2 / maxSeverityEq2);
        }
        if (!double.IsNaN(availableEq3eq6))
        {
            nExistingLower++;
            normalizedEq3eq6 = availableEq3eq6 * (currentEq3eq6 / maxSeverityEq3eq6);
        }
        if (!double.IsNaN(availableEq4))
        {
            nExistingLower++;
            normalizedEq4 = availableEq4 * (currentEq4 / maxSeverityEq4);
        }
        if (!double.IsNaN(availableEq5))
        {
            nExistingLower++;
            normalizedEq5 = 0;
        }

        var meanDistance = nExistingLower == 0
            ? 0
            : (normalizedEq1 + normalizedEq2 + normalizedEq3eq6 + normalizedEq4 + normalizedEq5) / nExistingLower;

        value -= meanDistance;
        if (value < 0)
        {
            value = 0.0;
        }
        if (value > 10)
        {
            value = 10.0;
        }
        return Math.Round(value * 10) / 10;
    }

    private static double LookupOrNaN(string key) =>
        Lookup.TryGetValue(key, out var v) ? v : double.NaN;

    private static string Extract(string metric, string maxVector)
    {
        var i = maxVector.IndexOf(metric, StringComparison.Ordinal);
        var sliced = maxVector[(i + metric.Length + 1)..];
        var slash = sliced.IndexOf('/');
        return slash > 0 ? sliced[..slash] : sliced;
    }

    private static string MacroVector(Dictionary<string, string> s)
    {
        int eq1;
        if (M(s, "AV") == "N" && M(s, "PR") == "N" && M(s, "UI") == "N")
        {
            eq1 = 0;
        }
        else if ((M(s, "AV") == "N" || M(s, "PR") == "N" || M(s, "UI") == "N")
                 && !(M(s, "AV") == "N" && M(s, "PR") == "N" && M(s, "UI") == "N")
                 && M(s, "AV") != "P")
        {
            eq1 = 1;
        }
        else
        {
            eq1 = 2;
        }

        var eq2 = M(s, "AC") == "L" && M(s, "AT") == "N" ? 0 : 1;

        int eq3;
        if (M(s, "VC") == "H" && M(s, "VI") == "H")
        {
            eq3 = 0;
        }
        else if (M(s, "VC") == "H" || M(s, "VI") == "H" || M(s, "VA") == "H")
        {
            eq3 = 1;
        }
        else
        {
            eq3 = 2;
        }

        int eq4;
        if (M(s, "MSI") == "S" || M(s, "MSA") == "S")
        {
            eq4 = 0;
        }
        else if (M(s, "SC") == "H" || M(s, "SI") == "H" || M(s, "SA") == "H")
        {
            eq4 = 1;
        }
        else
        {
            eq4 = 2;
        }

        var eq5 = M(s, "E") switch { "A" => 0, "P" => 1, _ => 2 };

        var eq6 = (M(s, "CR") == "H" && M(s, "VC") == "H")
                  || (M(s, "IR") == "H" && M(s, "VI") == "H")
                  || (M(s, "AR") == "H" && M(s, "VA") == "H")
            ? 0
            : 1;

        return $"{eq1}{eq2}{eq3}{eq4}{eq5}{eq6}";
    }

    private static readonly Dictionary<string, double> AvLevels = new() { ["N"] = 0.0, ["A"] = 0.1, ["L"] = 0.2, ["P"] = 0.3 };
    private static readonly Dictionary<string, double> PrLevels = new() { ["N"] = 0.0, ["L"] = 0.1, ["H"] = 0.2 };
    private static readonly Dictionary<string, double> UiLevels = new() { ["N"] = 0.0, ["P"] = 0.1, ["A"] = 0.2 };
    private static readonly Dictionary<string, double> AcLevels = new() { ["L"] = 0.0, ["H"] = 0.1 };
    private static readonly Dictionary<string, double> AtLevels = new() { ["N"] = 0.0, ["P"] = 0.1 };
    private static readonly Dictionary<string, double> VcLevels = new() { ["H"] = 0.0, ["L"] = 0.1, ["N"] = 0.2 };
    private static readonly Dictionary<string, double> ScLevels = new() { ["H"] = 0.1, ["L"] = 0.2, ["N"] = 0.3 };
    private static readonly Dictionary<string, double> SiLevels = new() { ["S"] = 0.0, ["H"] = 0.1, ["L"] = 0.2, ["N"] = 0.3 };
    private static readonly Dictionary<string, double> CrLevels = new() { ["H"] = 0.0, ["M"] = 0.1, ["L"] = 0.2 };

    private static readonly Dictionary<int, string[]> MaxComposedEq1 = new()
    {
        [0] = ["AV:N/PR:N/UI:N/"],
        [1] = ["AV:A/PR:N/UI:N/", "AV:N/PR:L/UI:N/", "AV:N/PR:N/UI:P/"],
        [2] = ["AV:P/PR:N/UI:N/", "AV:A/PR:L/UI:P/"]
    };
    private static readonly Dictionary<int, string[]> MaxComposedEq2 = new()
    {
        [0] = ["AC:L/AT:N/"],
        [1] = ["AC:H/AT:N/", "AC:L/AT:P/"]
    };
    private static readonly Dictionary<int, Dictionary<int, string[]>> MaxComposedEq3Eq6 = new()
    {
        [0] = new()
        {
            [0] = ["VC:H/VI:H/VA:H/CR:H/IR:H/AR:H/"],
            [1] = ["VC:H/VI:H/VA:L/CR:M/IR:M/AR:H/", "VC:H/VI:H/VA:H/CR:M/IR:M/AR:M/"]
        },
        [1] = new()
        {
            [0] = ["VC:L/VI:H/VA:H/CR:H/IR:H/AR:H/", "VC:H/VI:L/VA:H/CR:H/IR:H/AR:H/"],
            [1] = ["VC:L/VI:H/VA:L/CR:H/IR:M/AR:H/", "VC:L/VI:H/VA:H/CR:H/IR:M/AR:M/", "VC:H/VI:L/VA:H/CR:M/IR:H/AR:M/", "VC:H/VI:L/VA:L/CR:M/IR:H/AR:H/", "VC:L/VI:L/VA:H/CR:H/IR:H/AR:M/"]
        },
        [2] = new()
        {
            [1] = ["VC:L/VI:L/VA:L/CR:H/IR:H/AR:H/"]
        }
    };
    private static readonly Dictionary<int, string[]> MaxComposedEq4 = new()
    {
        [0] = ["SC:H/SI:S/SA:S/"],
        [1] = ["SC:H/SI:H/SA:H/"],
        [2] = ["SC:L/SI:L/SA:L/"]
    };
    private static readonly Dictionary<int, string[]> MaxComposedEq5 = new()
    {
        [0] = ["E:A/"],
        [1] = ["E:P/"],
        [2] = ["E:U/"]
    };

    private static readonly Dictionary<int, int> MaxSeverityEq1 = new() { [0] = 1, [1] = 4, [2] = 5 };
    private static readonly Dictionary<int, int> MaxSeverityEq2 = new() { [0] = 1, [1] = 2 };
    private static readonly Dictionary<int, Dictionary<int, int>> MaxSeverityEq3Eq6 = new()
    {
        [0] = new() { [0] = 7, [1] = 6 },
        [1] = new() { [0] = 8, [1] = 8 },
        [2] = new() { [1] = 10 }
    };
    private static readonly Dictionary<int, int> MaxSeverityEq4 = new() { [0] = 6, [1] = 5, [2] = 4 };

    private static readonly Dictionary<string, double> Lookup = new()
    {
        ["000000"] = 10, ["000001"] = 9.9, ["000010"] = 9.8, ["000011"] = 9.5, ["000020"] = 9.5, ["000021"] = 9.2,
        ["000100"] = 10, ["000101"] = 9.6, ["000110"] = 9.3, ["000111"] = 8.7, ["000120"] = 9.1, ["000121"] = 8.1,
        ["000200"] = 9.3, ["000201"] = 9, ["000210"] = 8.9, ["000211"] = 8, ["000220"] = 8.1, ["000221"] = 6.8,
        ["001000"] = 9.8, ["001001"] = 9.5, ["001010"] = 9.5, ["001011"] = 9.2, ["001020"] = 9, ["001021"] = 8.4,
        ["001100"] = 9.3, ["001101"] = 9.2, ["001110"] = 8.9, ["001111"] = 8.1, ["001120"] = 8.1, ["001121"] = 6.5,
        ["001200"] = 8.8, ["001201"] = 8, ["001210"] = 7.8, ["001211"] = 7, ["001220"] = 6.9, ["001221"] = 4.8,
        ["002001"] = 9.2, ["002011"] = 8.2, ["002021"] = 7.2, ["002101"] = 7.9, ["002111"] = 6.9, ["002121"] = 5,
        ["002201"] = 6.9, ["002211"] = 5.5, ["002221"] = 2.7, ["010000"] = 9.9, ["010001"] = 9.7, ["010010"] = 9.5,
        ["010011"] = 9.2, ["010020"] = 9.2, ["010021"] = 8.5, ["010100"] = 9.5, ["010101"] = 9.1, ["010110"] = 9,
        ["010111"] = 8.3, ["010120"] = 8.4, ["010121"] = 7.1, ["010200"] = 9.2, ["010201"] = 8.1, ["010210"] = 8.2,
        ["010211"] = 7.1, ["010220"] = 7.2, ["010221"] = 5.3, ["011000"] = 9.5, ["011001"] = 9.3, ["011010"] = 9.2,
        ["011011"] = 8.5, ["011020"] = 8.5, ["011021"] = 7.3, ["011100"] = 9.2, ["011101"] = 8.2, ["011110"] = 8,
        ["011111"] = 7.2, ["011120"] = 7, ["011121"] = 5.9, ["011200"] = 8.4, ["011201"] = 7, ["011210"] = 7.1,
        ["011211"] = 5.2, ["011220"] = 5, ["011221"] = 3, ["012001"] = 8.6, ["012011"] = 7.5, ["012021"] = 5.2,
        ["012101"] = 7.1, ["012111"] = 5.2, ["012121"] = 2.9, ["012201"] = 6.3, ["012211"] = 2.9, ["012221"] = 1.7,
        ["100000"] = 9.8, ["100001"] = 9.5, ["100010"] = 9.4, ["100011"] = 8.7, ["100020"] = 9.1, ["100021"] = 8.1,
        ["100100"] = 9.4, ["100101"] = 8.9, ["100110"] = 8.6, ["100111"] = 7.4, ["100120"] = 7.7, ["100121"] = 6.4,
        ["100200"] = 8.7, ["100201"] = 7.5, ["100210"] = 7.4, ["100211"] = 6.3, ["100220"] = 6.3, ["100221"] = 4.9,
        ["101000"] = 9.4, ["101001"] = 8.9, ["101010"] = 8.8, ["101011"] = 7.7, ["101020"] = 7.6, ["101021"] = 6.7,
        ["101100"] = 8.6, ["101101"] = 7.6, ["101110"] = 7.4, ["101111"] = 5.8, ["101120"] = 5.9, ["101121"] = 5,
        ["101200"] = 7.2, ["101201"] = 5.7, ["101210"] = 5.7, ["101211"] = 5.2, ["101220"] = 5.2, ["101221"] = 2.5,
        ["102001"] = 8.3, ["102011"] = 7, ["102021"] = 5.4, ["102101"] = 6.5, ["102111"] = 5.8, ["102121"] = 2.6,
        ["102201"] = 5.3, ["102211"] = 2.1, ["102221"] = 1.3, ["110000"] = 9.5, ["110001"] = 9, ["110010"] = 8.8,
        ["110011"] = 7.6, ["110020"] = 7.6, ["110021"] = 7, ["110100"] = 9, ["110101"] = 7.7, ["110110"] = 7.5,
        ["110111"] = 6.2, ["110120"] = 6.1, ["110121"] = 5.3, ["110200"] = 7.7, ["110201"] = 6.6, ["110210"] = 6.8,
        ["110211"] = 5.9, ["110220"] = 5.2, ["110221"] = 3, ["111000"] = 8.9, ["111001"] = 7.8, ["111010"] = 7.6,
        ["111011"] = 6.7, ["111020"] = 6.2, ["111021"] = 5.8, ["111100"] = 7.4, ["111101"] = 5.9, ["111110"] = 5.7,
        ["111111"] = 5.7, ["111120"] = 4.7, ["111121"] = 2.3, ["111200"] = 6.1, ["111201"] = 5.2, ["111210"] = 5.7,
        ["111211"] = 2.9, ["111220"] = 2.4, ["111221"] = 1.6, ["112001"] = 7.1, ["112011"] = 5.9, ["112021"] = 3,
        ["112101"] = 5.8, ["112111"] = 2.6, ["112121"] = 1.5, ["112201"] = 2.3, ["112211"] = 1.3, ["112221"] = 0.6,
        ["200000"] = 9.3, ["200001"] = 8.7, ["200010"] = 8.6, ["200011"] = 7.2, ["200020"] = 7.5, ["200021"] = 5.8,
        ["200100"] = 8.6, ["200101"] = 7.4, ["200110"] = 7.4, ["200111"] = 6.1, ["200120"] = 5.6, ["200121"] = 3.4,
        ["200200"] = 7, ["200201"] = 5.4, ["200210"] = 5.2, ["200211"] = 4, ["200220"] = 4, ["200221"] = 2.2,
        ["201000"] = 8.5, ["201001"] = 7.5, ["201010"] = 7.4, ["201011"] = 5.5, ["201020"] = 6.2, ["201021"] = 5.1,
        ["201100"] = 7.2, ["201101"] = 5.7, ["201110"] = 5.5, ["201111"] = 4.1, ["201120"] = 4.6, ["201121"] = 1.9,
        ["201200"] = 5.3, ["201201"] = 3.6, ["201210"] = 3.4, ["201211"] = 1.9, ["201220"] = 1.9, ["201221"] = 0.8,
        ["202001"] = 6.4, ["202011"] = 5.1, ["202021"] = 2, ["202101"] = 4.7, ["202111"] = 2.1, ["202121"] = 1.1,
        ["202201"] = 2.4, ["202211"] = 0.9, ["202221"] = 0.4, ["210000"] = 8.8, ["210001"] = 7.5, ["210010"] = 7.3,
        ["210011"] = 5.3, ["210020"] = 6, ["210021"] = 5, ["210100"] = 7.3, ["210101"] = 5.5, ["210110"] = 5.9,
        ["210111"] = 4, ["210120"] = 4.1, ["210121"] = 2, ["210200"] = 5.4, ["210201"] = 4.3, ["210210"] = 4.5,
        ["210211"] = 2.2, ["210220"] = 2, ["210221"] = 1.1, ["211000"] = 7.5, ["211001"] = 5.5, ["211010"] = 5.8,
        ["211011"] = 4.5, ["211020"] = 4, ["211021"] = 2.1, ["211100"] = 6.1, ["211101"] = 5.1, ["211110"] = 4.8,
        ["211111"] = 1.8, ["211120"] = 2, ["211121"] = 0.9, ["211200"] = 4.6, ["211201"] = 1.8, ["211210"] = 1.7,
        ["211211"] = 0.7, ["211220"] = 0.8, ["211221"] = 0.2, ["212001"] = 5.3, ["212011"] = 2.4, ["212021"] = 1.4,
        ["212101"] = 2.4, ["212111"] = 1.2, ["212121"] = 0.5, ["212201"] = 1, ["212211"] = 0.3, ["212221"] = 0.1,
    };
}
