using Newtonsoft.Json;

namespace VRCHOTAS.Models;

public enum EulerAngleOrder
{
    PitchYawRoll = 0,
    PitchRollYaw = 1,
    YawPitchRoll = 2,
    YawRollPitch = 3,
    RollPitchYaw = 4,
    RollYawPitch = 5
}

public enum EulerAngleAxisReference
{
    Local = 0,
    World = 1
}

public sealed class EulerAnglePreferences
{
    [JsonProperty("order")]
    public EulerAngleOrder Order { get; set; } = EulerAngleOrder.PitchRollYaw;

    [JsonProperty("axisReference")]
    public EulerAngleAxisReference AxisReference { get; set; } = EulerAngleAxisReference.Local;

    public EulerAnglePreferences Clone()
    {
        return new EulerAnglePreferences
        {
            Order = Order,
            AxisReference = AxisReference
        };
    }
}