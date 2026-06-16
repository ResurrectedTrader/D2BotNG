using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// File-backed store of the latest live character state per profile (characters.json).
/// Keyed by owning profile name.
/// </summary>
public class CharacterRepository : FileRepository<Character, CharacterList>
{
    public CharacterRepository(Paths paths) : base(paths, "characters.json") { }

    protected override string GetKey(Character c) => c.Profile;

    protected override IList<Character> GetItems(CharacterList list) => list.Characters;

    protected override CharacterList CreateList(IEnumerable<Character> items)
    {
        var list = new CharacterList();
        list.Characters.AddRange(items);
        return list;
    }
}
