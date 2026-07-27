using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// File-backed store of the latest live character state per profile (characters.json).
/// Keyed by owning profile name.
/// </summary>
public class CharacterRepository : FileRepository<Character, CharacterCollection>
{
    public CharacterRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "characters.json") { }

    protected override string GetKey(Character c) => c.Profile;

    protected override IList<Character> GetItems(CharacterCollection list) => list.Characters;

    protected override CharacterCollection CreateList(IEnumerable<Character> items)
    {
        var list = new CharacterCollection();
        list.Characters.AddRange(items);
        return list;
    }
}
