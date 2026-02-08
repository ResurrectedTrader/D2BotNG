using System.Text.Json.Serialization;
using D2BotNG.Core.Protos;
using JetBrains.Annotations;

namespace D2BotNG.Data.LegacyModels;

public class LegacySchedule
{
    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Times")]
    public List<LegacyPeriod> Times { get; init; } = [];

    public Schedule ToModern()
    {
        var schedule = new Schedule { Name = Name };

        // Legacy stores pairs: [startPeriod, endPeriod, startPeriod, endPeriod, ...]
        for (int i = 0; i + 1 < Times.Count; i += 2)
        {
            var start = Times[i];
            var end = Times[i + 1];
            schedule.Periods.Add(new TimePeriod
            {
                StartHour = (uint)Math.Max(0, start.Hour),
                StartMinute = (uint)Math.Max(0, start.Minute),
                EndHour = (uint)Math.Max(0, end.Hour),
                EndMinute = (uint)Math.Max(0, end.Minute)
            });
        }
        return schedule;
    }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class LegacyPeriod
{
    [JsonPropertyName("Hour")]
    public int Hour { get; init; }

    [JsonPropertyName("Minute")]
    public int Minute { get; init; }
}
