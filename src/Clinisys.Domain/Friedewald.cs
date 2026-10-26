namespace Clinisys.Domain;

// Units are exact catalogue snapshots. No implicit conversion or medical defaults.
public sealed record FriedewaldOutcome(string Status, decimal? Value);

public static class Friedewald
{
    public static FriedewaldOutcome Calculate(decimal? total, decimal? hdl, decimal? triglycerides,
        string totalUnit, string hdlUnit, string triglycerideUnit, string ldlUnit)
    {
        if (total is null || hdl is null || triglycerides is null) return new("MissingSource", null);
        if (totalUnit is not ("mg/dL" or "mmol/L") || hdlUnit != totalUnit ||
            triglycerideUnit != totalUnit || ldlUnit != totalUnit)
            return new("IncompatibleUnits", null);
        if (total < 0 || hdl < 0 || triglycerides < 0) return new("InvalidSource", null);
        if (triglycerides >= (totalUnit == "mg/dL" ? 400m : 4.5m))
            return new("TriglyceridesTooHigh", null);
        var value = total.Value - hdl.Value - triglycerides.Value / (totalUnit == "mg/dL" ? 5m : 2.2m);
        if (value < 0 || value > 999999999999.999999m) return new("InvalidCalculatedValue", null);
        return new("Calculated", decimal.Round(value, 6, MidpointRounding.AwayFromZero));
    }
}
