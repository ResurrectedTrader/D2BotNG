using D2ItemToolkit;
using Unit = D2BotNG.Core.Protos.Captures.Unit;

namespace D2BotNG.Capture;

/// <summary>
/// A captured unit seen through the tooltip library's own interface, so the game tables can be
/// queried about it without a second copy of the item existing.
///
/// Forwards rather than copies, and that matters for more than allocation: implementing
/// <see cref="IUnit" /> is checked by the compiler, so a member added to it breaks the build here
/// instead of silently reaching the engine as a default — which is exactly what a field-by-field
/// copy would have done.
///
/// The two shapes line up because both describe the same producer document, and the library reads
/// placement (<see cref="Location" />, <see cref="X" />) and <see cref="ItemLevel" /> directly off
/// the document's own fields rather than a normalised copy of them.
/// </summary>
internal sealed class CapturedUnit(Unit unit) : IUnit
{
    private IReadOnlyList<IUnitStatList>? _statsLists;
    private IReadOnlyList<IUnitStat>? _stats;
    private IReadOnlyList<IUnit>? _items;
    private IReadOnlyList<IUnitSkill>? _skills;

    public int UnitType => unit.UnitType;
    public int ClassId => unit.ClassId;
    public string Code => unit.Code;
    public int Quality => unit.Quality;
    public ItemRecordFlags ItemFlags => (ItemRecordFlags)unit.ItemFlags;
    public int FileIndex => unit.FileIndex;
    public int RarePrefix => unit.RarePrefix;
    public int RareSuffix => unit.RareSuffix;
    public int AutoAffix => unit.AutoAffix;
    public int Format => unit.Format;
    public IReadOnlyList<int> MagicPrefix => unit.MagicPrefix;
    public IReadOnlyList<int> MagicSuffix => unit.MagicSuffix;
    public int EarLevel => unit.EarLevel;
    public string PlayerName => unit.PlayerName;
    public int GfxIndex => unit.GfxIndex;
    public uint FlagsEx => unit.FlagsEx;

    public int ItemLevel => unit.ItemLevel;

    // Where it sits. The library's set rules read both: Location to find the equipped pieces, and
    // X — the equip location for anything equipped — to tell the alternate weapon set apart, which
    // counts as OWNED but lights no worn bit.
    public int Location => unit.Location;
    public int X => unit.X;

    // Projected once and kept: the engine walks these more than once per call.
    public IReadOnlyList<IUnitStatList> StatsLists =>
        _statsLists ??= unit.StatsLists.Select(IUnitStatList (l) => new CapturedStatList(l)).ToList();

    public IReadOnlyList<IUnitStat> Stats =>
        _stats ??= unit.Stats.Select(IUnitStat (s) => new CapturedStat(s)).ToList();

    /// <summary>
    /// What this unit contains, which is how the library reaches an item's socket fillers.
    ///
    /// Only ITEMS are ever wrapped — the store wraps a container's item, and this wraps that item's
    /// sockets — so the library's other reading of the same member, a WEARER's carried gear, has no
    /// producer here and the containers on a wearer document never travel through this adapter.
    /// </summary>
    public IReadOnlyList<IUnit> Items =>
        _items ??= unit.Sockets.Select(IUnit (u) => new CapturedUnit(u)).ToList();

    public IReadOnlyList<IUnitSkill> Skills =>
        _skills ??= unit.Skills.Select(IUnitSkill (s) => new CapturedSkill(s)).ToList();

    private sealed class CapturedStatList(Core.Protos.Captures.StatList list) : IUnitStatList
    {
        private IReadOnlyList<IUnitStat>? _stats;

        public int StateNo => list.StateNo;
        public uint Flags => list.Flags;

        public IReadOnlyList<IUnitStat> Stats =>
            _stats ??= list.Stats.Select(IUnitStat (s) => new CapturedStat(s)).ToList();
    }

    private sealed class CapturedStat(Core.Protos.Captures.Stat stat) : IUnitStat
    {
        public int Id => stat.Id;

        /// <summary>
        /// Narrowed deliberately. The capture widens an unsigned stat so JSON never carries a
        /// negative — experience at level 99 is ~3.52 billion — but the game holds int32, and an
        /// unchecked narrowing restores exactly those bits. Same convention the library's own
        /// JSON reader uses, so a value round-trips identically whichever way it arrived.
        /// </summary>
        public int Value => unchecked((int)stat.Value);

        public int Layer => stat.Layer;
    }

    private sealed class CapturedSkill(Core.Protos.Captures.Skill skill) : IUnitSkill
    {
        public int Skill => skill.SkillId;
        public int Level => skill.Level;
    }
}
