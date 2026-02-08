using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

public class ScheduleRepository : FileRepository<Schedule, ScheduleList>
{
    public ScheduleRepository(Paths paths) : base(paths, "schedules.json") { }

    protected override string GetKey(Schedule s) => s.Name;

    protected override IList<Schedule> GetItems(ScheduleList list) => list.Schedules;

    protected override ScheduleList CreateList(IEnumerable<Schedule> items)
    {
        var list = new ScheduleList();
        list.Schedules.AddRange(items);
        return list;
    }
}
