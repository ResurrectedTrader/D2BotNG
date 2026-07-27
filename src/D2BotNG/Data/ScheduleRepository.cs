using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

public class ScheduleRepository : FileRepository<Schedule, ScheduleCollection>
{
    public ScheduleRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "schedules.json") { }

    protected override string GetKey(Schedule s) => s.Name;

    protected override IList<Schedule> GetItems(ScheduleCollection list) => list.Schedules;

    protected override ScheduleCollection CreateList(IEnumerable<Schedule> items)
    {
        var list = new ScheduleCollection();
        list.Schedules.AddRange(items);
        return list;
    }
}
