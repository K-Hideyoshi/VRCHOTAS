using Newtonsoft.Json;

namespace VRCHOTAS.Models;

/// <summary>
/// Root document for per-configuration saved anchor points.
/// Stored as a single file keyed by configuration file name.
/// </summary>
public sealed class AnchorPointsDocument
{
    [JsonProperty("configs")]
    public Dictionary<string, AnchorPointsPerConfig> Configs { get; set; } = new();
}

/// <summary>
/// Left and right hand anchor data for a single configuration.
/// </summary>
public sealed class AnchorPointsPerConfig
{
    [JsonProperty("left")]
    public HandAnchorData Left { get; set; } = new();

    [JsonProperty("right")]
    public HandAnchorData Right { get; set; } = new();
}

/// <summary>
/// Snapshot of a single hand's position and rotation anchor values.
/// </summary>
public sealed class HandAnchorData
{
    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }

    [JsonProperty("z")]
    public double Z { get; set; }

    [JsonProperty("pitchDeg")]
    public double PitchDeg { get; set; }

    [JsonProperty("yawDeg")]
    public double YawDeg { get; set; }

    [JsonProperty("rollDeg")]
    public double RollDeg { get; set; }

    public bool EqualsAnchor(HandAnchorData other)
    {
        return Math.Abs(X - other.X) < 0.0001
               && Math.Abs(Y - other.Y) < 0.0001
               && Math.Abs(Z - other.Z) < 0.0001
               && Math.Abs(PitchDeg - other.PitchDeg) < 0.001
               && Math.Abs(YawDeg - other.YawDeg) < 0.001
               && Math.Abs(RollDeg - other.RollDeg) < 0.001;
    }

    public HandAnchorData Clone()
    {
        return new HandAnchorData
        {
            X = X,
            Y = Y,
            Z = Z,
            PitchDeg = PitchDeg,
            YawDeg = YawDeg,
            RollDeg = RollDeg
        };
    }
}
