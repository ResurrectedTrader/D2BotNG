using D2BotNG.Capture;
using D2BotNG.Core.Protos.Captures;
using D2BotNG.Data;
using D2BotNG.Utilities;
using TooltipEngine = D2ItemToolkit.TooltipEngine;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace D2BotNG.Tests.Capture;

/// <summary>
/// Round-trips real snapshots through the capture store.
///
/// Unlike the other tests here this is not a contract check — it exists because the store is
/// hand-written SQL over a decomposed schema, and the things most likely to be wrong (socket
/// nesting rebuilt from parent_id, a partial snapshot leaving untouched sections alone, kill
/// deltas accumulating rather than replacing) cannot be caught by the compiler and would
/// otherwise only surface as a wrong inventory in front of a user.
/// </summary>
public class CaptureStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly CaptureStore _store;
    /// <summary>
    /// The real tables, embedded in the library — so these tests assert against the game's own
    /// values rather than a fixture's idea of them. There is nothing to stub: an install is never
    /// consulted, so there is no "tables missing" state to simulate either.
    /// </summary>
    private readonly TooltipEngine _tooltip = TooltipEngine.Embedded;

    public CaptureStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "d2botng-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new CaptureStore(NullLogger<CaptureStore>.Instance, new Paths(_directory), _tooltip);
        _store.Open();
    }

    public void Dispose()
    {
        _store.Dispose();
        // No catch here on purpose. This used to swallow an IOException, blamed on the OS holding
        // the WAL; it was actually connection pooling keeping the file open past Dispose — the
        // same thing that broke the recreate paths. If it ever comes back, that has regressed.
        Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    /// <summary>Parses exactly the way ingest does — straight into the wire proto.</summary>
    private static Snapshot Parse(string json) => ProtobufJsonConfig.Parser.Parse<Snapshot>(json);

    private void Apply(string json) => _store.Apply("Bot1", Parse(json));

    [Fact]
    public void KeyframeStoresIdentityStatsAndSkills()
    {
        Apply(Keyframe());

        var character = _store.GetCharacter("Bot1")!;
        Assert.Equal("Bot1", character.Profile);
        Assert.Equal("Acct", character.Identity.Account);
        Assert.Equal("USWest", character.Identity.Realm);
        Assert.Equal("Sorc", character.Player.Name);
        Assert.Equal(1, character.Player.ClassId);
        Assert.Equal(2, character.Identity.Difficulty);
        Assert.Equal(40, character.Player.Area);

        // charFlags 0x24 = hardcore (0x04) + expansion (0x20). Served raw, not pre-derived: a
        // stored copy of a bit could only ever disagree with the bit.
        Assert.Equal(36u, character.Identity.CharFlags);
        Assert.True(character.Identity.Ladder);

        // Level is not a field either — it is stat 12 off the player's merged stats.
        Assert.Equal(90, character.Player.Stats.Single(s => s.Id == 12).Value);
        Assert.Equal(123456, character.Player.Stats.Single(s => s.Id == 14).Value);

        var skill = Assert.Single(character.Player.Skills);
        Assert.Equal(48, skill.SkillId);
        Assert.Equal(20, skill.HardPoints);
        Assert.Equal(28, skill.Level); // bonused; the gear contribution is level - hard

        var progression = Assert.Single(character.Progression);
        Assert.Equal(2, progression.Difficulty);
        Assert.Equal([1, 2], progression.Quests);
        Assert.Equal([0, 3], progression.Waypoints);
    }

    [Fact]
    public void TheListIsMetadataOnly()
    {
        Apply(Keyframe());

        // A selector needs a label and a freshness stamp, not an inventory. The summary is its
        // own message precisely so "no gear here" is a statement about the endpoint rather than
        // about the character — there is no containers field to misread.
        var summary = Assert.Single(_store.ListCharacters());
        Assert.Equal("Bot1", summary.Profile);
        Assert.Equal("Sorc", summary.Name);
        Assert.Equal(1, summary.ClassId);
        Assert.Equal(90, summary.Level); // denormalised stat 12, so the list need not carry stats
        Assert.Equal("Acct", summary.Identity.Account);
        Assert.Equal(2, summary.Identity.Difficulty);
        Assert.NotNull(summary.UpdatedAt);
    }

    [Fact]
    public void ItemsKeepTheirStatListsAndSocketNesting()
    {
        Apply(Keyframe());

        var detail = _store.GetCharacter("Bot1")!;
        var inventory = detail.Player.Containers.Inventory;
        Assert.Equal(10, inventory.Width);

        var helm = Assert.Single(inventory.Items);
        Assert.Equal("cap", helm.Code);
        Assert.Equal(1001u, helm.Gid);
        // Both arrays in full, so a prefix/suffix swap or a lost slot shows up: the position IS
        // the slot, and 812 sits in suffix slot 0.
        Assert.Equal([0, 0, 0], helm.MagicPrefix);
        Assert.Equal([812, 0, 0], helm.MagicSuffix);
        Assert.Equal(-1, helm.FileIndex);

        // The rest of the row, by ordinal. These are read positionally out of a hand-written
        // SELECT, so editing that list silently repoints every column after the edit — which is
        // the one failure the compiler cannot catch here.
        //
        // The fixture still sends a `description`, and reaching this line at all is the assertion
        // about it: the field is gone from the contract, the producer that has not stopped sending
        // one must keep working, and nothing is stored or read back.
        Assert.Equal("Cap of the Whale", helm.Title);
        Assert.Equal(3, helm.Location);
        Assert.Equal((2, 1), (helm.X, helm.Y));
        Assert.Equal((2, 2), (helm.Width, helm.Height));

        // uint round-trip: 0x80000000 exceeds int32 and must survive the INTEGER column.
        var statList = Assert.Single(helm.StatsLists);
        Assert.Equal(2147483648u, statList.Flags);
        Assert.Equal(2, statList.Stats.Count);
        Assert.Equal(39, statList.Stats[0].Id);
        Assert.Equal(30, statList.Stats[0].Value);

        // A socket filler is a whole nested unit, positional by socket index.
        var gem = Assert.Single(helm.Sockets);
        Assert.Equal("gpr", gem.Code);
        Assert.Equal(1002u, gem.Gid);
        Assert.Equal(48, Assert.Single(Assert.Single(gem.StatsLists).Stats).Id);
    }

    [Fact]
    public void BeltSlotIndexBecomesAGridCell()
    {
        Apply(Keyframe());

        var belt = _store.GetCharacter("Bot1")!.Player.Containers.Belt;
        var potion = Assert.Single(belt.Items);
        // Slot 5 in a 4-wide belt is column 1, and row 0 is the BOTTOM row in game, so on a
        // top-down grid it lands at y = 4 - 1 - 1 = 2.
        Assert.Equal(1, potion.X);
        Assert.Equal(2, potion.Y);
    }

    [Fact]
    public void TableDerivedColumnsAreResolvedAtIngestAndFilterable()
    {
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"inventory":{"width":10,"height":4,"items":[
               {"unitType":4,"classId":225,"code":"7cr","quality":2,"gid":3001,"title":"Phase Blade",
                "location":3,"x":0,"y":0,"w":2,"h":3},
               {"unitType":4,"classId":29,"code":"crs","quality":2,"gid":3002,"title":"Crystal Sword",
                "location":3,"x":4,"y":0,"w":2,"h":3}
             ]}}}}
            """);

        Unit Only(SearchItemsRequest request) => Assert.Single(_store.SearchItems(request).Results).Item;

        // Every value below is the game's own, read from the embedded tables rather than from a
        // fixture's idea of them: Phase Blade (classId 225) is elite, str 25 / dex 136 / level 54;
        // Crystal Sword (29) is the normal member of that family, str 43 with no dex or level
        // requirement. Both are type `swor` (row 30), which sits under `weap` (45).
        //
        // The class id is what resolution keys off, NOT the code — an item carries both and only
        // the id indexes the table, so a fixture with a plausible code and the wrong id resolves
        // to a different item entirely and silently.

        // Tier separates two items of the SAME base family and the same quality — which is the
        // whole point of it being a separate axis from `quality`.
        Assert.Equal("7cr", Only(new SearchItemsRequest { Tiers = { Tier.Elite } }).Code);
        Assert.Equal("crs", Only(new SearchItemsRequest { Tiers = { Tier.Normal } }).Code);

        // A broad category matches, though the items store only the leaf `swor` — this is the
        // descendant expansion, and the case ResurrectedTrade gets wrong. Asking for the leaf
        // directly must work too, or the expansion has swallowed the exact case.
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest { ItemTypes = { 45 } }).Total);
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest { ItemTypes = { 30 } }).Total);
        Assert.Empty(_store.SearchItems(new SearchItemsRequest { ItemTypes = { 37 } }).Results);

        // Requirements are a property of the ITEM; the character is the bound, supplied here.
        Assert.Equal("crs", Only(new SearchItemsRequest
        {
            RequiredDexterity = new Int32Range { Max = 50 },
        }).Code);
        Assert.Equal("crs", Only(new SearchItemsRequest
        {
            RequiredLevel = new Int32Range { Max = 40 },
        }).Code);
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest
        {
            RequiredStrength = new Int32Range { Min = 20, Max = 50 },
        }).Total);

        // An inverted range is a caller error like every other one, not an empty result. This can
        // only be reached with tables loaded — without them the availability check fires first,
        // which is why it cannot live in the malformed-request theory above.
        Assert.Throws<InvalidSearchRequestException>(() => _store.SearchItems(
            new SearchItemsRequest { RequiredLevel = new Int32Range { Min = 40, Max = 10 } }));
        Assert.Throws<InvalidSearchRequestException>(() => _store.SearchItems(
            new SearchItemsRequest { RequiredDexterity = new Int32Range { Min = 40, Max = 10 } }));

        // An open end is not a bound: max alone must not be read as a range starting at zero.
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest
        {
            RequiredStrength = new Int32Range { Min = 20 },
        }).Total);
    }

    [Fact]
    public void ARunewordSearchDoesNotMatchAMagicItemSharingItsPrefixSlot()
    {
        // Both items carry 812 in magic prefix slot 0, which is the whole hazard: on the runeword
        // it is a runes.txt id, on the magic sword an ordinary affix index. Only the runeword
        // flag (0x4000000) tells them apart, so the filter applies it rather than trusting the
        // number alone — the same over-matching direction a caller could never spot in results.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"inventory":{"width":10,"height":4,"items":[
               {"unitType":4,"code":"crs","quality":2,"itemFlags":67108864,"gid":2001,
                "magicPrefix":[812,0,0],"title":"Spirit","location":3,"x":0,"y":0,"w":2,"h":3},
               {"unitType":4,"code":"lsd","quality":4,"itemFlags":0,"gid":2002,
                "magicPrefix":[812,0,0],"title":"Bronze Long Sword","location":3,"x":4,"y":0,"w":1,"h":3}
             ]}}}}
            """);

        var found = _store.SearchItems(new SearchItemsRequest { Runewords = { 812 } });
        Assert.Equal("crs", Assert.Single(found.Results).Item.Code);

        // Sanity: without the pairing both would match, so the fixture really does exercise it.
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest()).Total);
    }

    [Fact]
    public void TheMergedSurfaceTotalsWhatTheRawSurfaceOnlySplits()
    {
        // The case this surface exists for. A Tal Rasha's Horadric Crest with an Um rune shows
        // All Resistances +30, and a Death Mask's Defense line is its base plus its own +45 — but
        // raw holds 15 and 15 on separate lists and 76 and 45 on separate lists, so a bound of 30
        // or 100 matches no single row. The rune carries no stats of its own at all.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"stash":{"pages":[{"index":0,"width":6,"height":8,"items":[
               {"unitType":4,"classId":358,"code":"xsk","quality":5,"itemFlags":2064,"fileIndex":80,
                "gid":4001,"title":"Tal Rasha's Horadric Crest","location":7,"x":0,"y":0,"w":2,"h":2,
                "statsLists":[
                  {"stateNo":0,"flags":64,"stats":[
                    {"id":31,"value":45},{"id":39,"value":15},{"id":41,"value":15},
                    {"id":43,"value":15},{"id":45,"value":15}]},
                  {"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":76},{"id":194,"value":1}]}],
                "sockets":[
                  {"unitType":4,"classId":631,"code":"r22","quality":2,"itemFlags":0,"fileIndex":-1,
                   "gid":4002,"title":"Um Rune","location":7,"x":0,"y":0,"w":1,"h":1}]}
             ]}]}}}}
            """);

        var merged = (int statId, int min) => new SearchItemsRequest
        {
            Conditions =
            {
                new StatCondition
                {
                    StatIds = { statId }, MinValue = min, Surface = StatSurface.Merged,
                },
            },
        };

        // Raw sees only the biggest single source, which is the whole problem.
        Assert.Equal(0, Search(Stat(39, min: 30)).Total);
        Assert.Equal(1, Search(Stat(39, min: 15)).Total);

        // Merged sees the total — the rune's 15 folded in, which is nowhere in the capture.
        Assert.Equal(1, _store.SearchItems(merged(39, 30)).Total);
        Assert.Equal(0, _store.SearchItems(merged(39, 31)).Total);

        // And op 13 applied: 76 base + 45 flat is the 121 the game draws.
        Assert.Equal(1, _store.SearchItems(merged(31, 121)).Total);
        Assert.Equal(0, _store.SearchItems(merged(31, 122)).Total);
        // Raw's best single source is the 76 base, so it cannot answer that at all.
        Assert.Equal(0, Search(Stat(31, min: 100)).Total);

        // The item's OWN total is a separate column, so "30 of its own" excludes a helm that only
        // reaches 30 with a rune in it — while its own 15 still matches at 15.
        StatCondition Own(int min) => new()
        {
            StatIds = { 39 }, MinValue = min,
            Surface = StatSurface.Merged, Sockets = SocketScope.HostOnly,
        };
        Assert.Equal(0, _store.SearchItems(
            new SearchItemsRequest { Conditions = { Own(30) } }).Total);
        Assert.Equal(1, _store.SearchItems(
            new SearchItemsRequest { Conditions = { Own(15) } }).Total);
    }

    [Fact]
    public void ARunesGrantIsStoredAgainstTheRuneSoTheSocketScopesMeanSomething()
    {
        // A rune arrives with an empty stat chain — gems.txt holds its mods, keyed by the host's
        // gemapplytype — so without synthesising them at ingest the raw surface's socket axis did
        // nothing at all for runes and gems: WITH_FILLERS added no rows and FILLERS_ONLY could
        // never match. The Um in this Crest grants All Resistances +15 in a helm.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"stash":{"pages":[{"index":0,"width":6,"height":8,"items":[
               {"unitType":4,"classId":358,"code":"xsk","quality":5,"itemFlags":2064,"fileIndex":80,
                "gid":6001,"title":"Tal Rasha's Horadric Crest","location":7,"x":0,"y":0,"w":2,"h":2,
                "statsLists":[{"stateNo":0,"flags":64,"stats":[{"id":39,"value":15}]}],
                "sockets":[
                  {"unitType":4,"classId":631,"code":"r22","quality":2,"itemFlags":0,"fileIndex":-1,
                   "gid":6002,"title":"Um Rune","location":7,"x":0,"y":0,"w":1,"h":1}]}
             ]}]}}}}
            """);

        // The rune's grant is a source in its own right, reachable through the host.
        Assert.Equal(1, Search(Stat(39, min: 15, sockets: SocketScope.FillersOnly)).Total);
        Assert.Equal(0, Search(Stat(39, min: 16, sockets: SocketScope.FillersOnly)).Total);

        // The item's own stats alone still know nothing of it.
        Assert.Equal(1, Search(Stat(39, min: 15, sockets: SocketScope.HostOnly)).Total);

        // And the two together are still SOURCES, not a total: 15 and 15 is no source of 30. That
        // is what the merged surface is for, and it is why the two are separate questions.
        Assert.Equal(1, Search(Stat(39, min: 15, sockets: SocketScope.WithFillers)).Total);
        Assert.Equal(0, Search(Stat(39, min: 16, sockets: SocketScope.WithFillers)).Total);
        Assert.Equal(1, _store.SearchItems(new SearchItemsRequest
        {
            Conditions =
            {
                new StatCondition { StatIds = { 39 }, MinValue = 30, Surface = StatSurface.Merged },
            },
        }).Total);
    }

    [Fact]
    public void AConditionNamingSeveralStatsCountsOnceTowardsAGroup()
    {
        // `merged_stat` is keyed (item_id, stat_id, layer), so a condition naming four stat ids
        // selects four ROWS for one item. A group counts branches with UNION ALL + COUNT(*), so
        // without DISTINCT that single condition satisfies "at least 2" on its own — and the UI
        // reaches this on any counted group holding an all-resistances row, because a counted row
        // pools every stat id sharing its scale into ONE condition.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"stash":{"pages":[{"index":0,"width":6,"height":8,"items":[
               {"unitType":4,"classId":358,"code":"xsk","quality":4,"itemFlags":16,"fileIndex":-1,
                "gid":7001,"title":"Resists Only","location":7,"x":0,"y":0,"w":2,"h":2,
                "statsLists":[{"stateNo":0,"flags":64,"stats":[
                  {"id":39,"value":20},{"id":41,"value":20},
                  {"id":43,"value":20},{"id":45,"value":20}]}]},
               {"unitType":4,"classId":358,"code":"xsk","quality":4,"itemFlags":16,"fileIndex":-1,
                "gid":7002,"title":"Resist And Cast","location":7,"x":2,"y":0,"w":2,"h":2,
                "statsLists":[{"stateNo":0,"flags":64,"stats":[
                  {"id":39,"value":20},{"id":105,"value":10}]}]}
             ]}]}}}}
            """);

        StatCondition Merged(params int[] statIds) => new()
        {
            StatIds = { statIds }, MinValue = 1, Surface = StatSurface.Merged,
        };

        var atLeastTwo = new SearchItemsRequest
        {
            Groups =
            {
                new StatConditionGroup
                {
                    Conditions = { Merged(39, 41, 43, 45), Merged(105) },
                    MinMatches = 2,
                },
            },
        };

        // Only the item that genuinely satisfies BOTH conditions.
        var match = Assert.Single(_store.SearchItems(atLeastTwo).Results);
        Assert.Equal(7002u, match.Item.Gid);

        // The premise: the four-stat condition really does match the other item, so this is about
        // how its rows are counted rather than about it failing to match at all.
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest
        {
            Conditions = { Merged(39, 41, 43, 45) },
        }).Total);

        // The same on the RAW surface, which over-counts for a different reason: the four stats sit
        // on ONE stat list, so the condition selects four rows of the same list rather than four
        // per-stat rows. Both branches build the group's members, so both have to collapse them.
        StatCondition Raw(params int[] statIds) => new() { StatIds = { statIds }, MinValue = 1 };

        var rawAtLeastTwo = Assert.Single(_store.SearchItems(new SearchItemsRequest
        {
            Groups =
            {
                new StatConditionGroup
                {
                    Conditions = { Raw(39, 41, 43, 45), Raw(105) }, MinMatches = 2,
                },
            },
        }).Results);
        Assert.Equal(7002u, rawAtLeastTwo.Item.Gid);
    }

    [Fact]
    public void TheMergedSurfaceLeavesOutEarnedSetTiers()
    {
        // A Tal Rasha's belt carries +60 Defense on a set-TIER list (state 165), which exists only
        // while the wearer holds the other pieces. The game draws 98; the belt on its own is 38.
        // Indexing 98 would make the same query answer differently depending on what a bot was
        // wearing when it last reported, so a tier is not part of what this item is worth.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"stash":{"pages":[{"index":0,"width":6,"height":8,"items":[
               {"unitType":4,"classId":392,"code":"zmb","quality":5,"itemFlags":16,"fileIndex":73,
                "gid":5001,"title":"Tal Rasha's Fine-Spun Cloth","location":7,"x":0,"y":0,"w":2,"h":1,
                "statsLists":[
                  {"stateNo":165,"flags":64,"stats":[{"id":31,"value":60}]},
                  {"stateNo":0,"flags":2147483648,"stats":[{"id":31,"value":38}]}]}
             ]}]}}}}
            """);

        var merged = (int min) => new SearchItemsRequest
        {
            Conditions =
            {
                new StatCondition { StatIds = { 31 }, MinValue = min, Surface = StatSurface.Merged },
            },
        };

        // The item's own 38, not the 98 the game currently draws.
        Assert.Equal(1, _store.SearchItems(merged(38)).Total);
        Assert.Equal(0, _store.SearchItems(merged(39)).Total);

        // And the raw surface agrees, because the UI excludes the tier states there too — the two
        // must not disagree about the same item. Asked WITHOUT that exclusion, raw still sees the
        // stored row, which is what makes this assertion about the exclusion rather than the data.
        var rawExcludingTiers = new StatCondition
        {
            StatIds = { 31 },
            MinValue = 60,
            Lists = new StatListScope { ExcludeStates = { 165, 166, 167, 168, 169, 170 } },
        };
        Assert.Equal(0, _store.SearchItems(
            new SearchItemsRequest { Conditions = { rawExcludingTiers } }).Total);
        Assert.Equal(1, Search(Stat(31, min: 60)).Total);
    }

    [Theory]
    [InlineData("sockets on merged")]
    [InlineData("lists on merged")]
    public void TheMergedSurfaceRefusesScopesItCannotHonour(string shape)
    {
        Apply(Keyframe());

        var condition = new StatCondition { StatIds = { 39 }, Surface = StatSurface.Merged };
        // FILLERS_ONLY has no total to read, and `lists` selects among sources a total does not
        // have. Dropping either quietly would answer a different question than was asked.
        if (shape == "sockets on merged") condition.Sockets = SocketScope.FillersOnly;
        else condition.Lists = new StatListScope { FlagsAll = 0x40 };

        Assert.Throws<InvalidSearchRequestException>(
            () => _store.SearchItems(new SearchItemsRequest { Conditions = { condition } }));
    }

    [Fact]
    public void IdentityFieldsOrWithinThemselvesAndNarrowEachOther()
    {
        // Values within one field are alternatives; the fields AND, like every other filter. That
        // is what makes "this runeword, on this base" askable — and it also means naming a base and
        // an unrelated unique is a contradiction that answers with nothing. The store answers
        // literally because it cannot tell a deliberate pairing from a mistaken one; warning about
        // the mistaken one is the UI's job, and this pins which half is whose.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"inventory":{"width":10,"height":4,"items":[
               {"unitType":4,"classId":358,"code":"xsk","quality":2,"itemFlags":0,"fileIndex":-1,
                "gid":3001,"title":"Death Mask","location":3,"x":0,"y":0,"w":2,"h":2},
               {"unitType":4,"classId":428,"code":"usk","quality":7,"itemFlags":0,"fileIndex":42,
                "gid":3002,"title":"Andariel's Visage","location":3,"x":4,"y":0,"w":2,"h":2},
               {"unitType":4,"classId":31,"code":"lsd","quality":2,"itemFlags":0,"fileIndex":-1,
                "gid":3003,"title":"Long Sword","location":3,"x":7,"y":0,"w":1,"h":3}
             ]}}}}
            """);

        var baseOnly = new SearchItemsRequest { ClassIds = { 358 } };
        var uniqueOnly = new SearchItemsRequest
        {
            SpecificItems = { new SpecificItem { Quality = 7, FileIndex = 42 } },
        };
        Assert.Equal("xsk", Assert.Single(_store.SearchItems(baseOnly).Results).Item.Code);
        Assert.Equal("usk", Assert.Single(_store.SearchItems(uniqueOnly).Results).Item.Code);

        // Within one field, alternatives: both bases match.
        Assert.Equal(2, _store.SearchItems(
            new SearchItemsRequest { ClassIds = { 358, 31 } }).Total);

        // Across fields, a narrowing — and here a contradiction, since Andariel's Visage is not a
        // Death Mask. Nothing is the literal answer, not a bug.
        Assert.Equal(0, _store.SearchItems(new SearchItemsRequest
        {
            ClassIds = { 358 },
            SpecificItems = { new SpecificItem { Quality = 7, FileIndex = 42 } },
        }).Total);

        // The same pairing on the unique's OWN base is the useful case, and it holds.
        Assert.Equal("usk", Assert.Single(_store.SearchItems(new SearchItemsRequest
        {
            ClassIds = { 428 },
            SpecificItems = { new SpecificItem { Quality = 7, FileIndex = 42 } },
        }).Results).Item.Code);

        Assert.Equal(3, _store.SearchItems(new SearchItemsRequest()).Total);
    }

    [Fact]
    public void AStatTheWearerStopsReportingIsRemoved()
    {
        Apply(Keyframe());
        Assert.Equal(123456, _store.GetCharacter("Bot1")!.Player.Stats.Single(s => s.Id == 14).Value);

        // Stats are UPSERTED now rather than deleted and reinserted, and an upsert cannot retract.
        // The producer sends a fixed curated set so this should never happen — but if it ever
        // sends fewer, the missing ones must go rather than linger at their last seen value.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000010000,
               "player":{"name":"Sorc","stats":[{"id":12,"value":91}]}}
              """);

        var stats = _store.GetCharacter("Bot1")!.Player.Stats;
        Assert.Equal(91, Assert.Single(stats).Value);
        Assert.Equal(12, stats[0].Id);
    }

    [Fact]
    public void AGearGrantedSkillDisappearsWhenTheGearComesOff()
    {
        Apply(Keyframe());
        Assert.Equal(48, Assert.Single(_store.GetCharacter("Bot1")!.Player.Skills).SkillId);

        // The load-bearing half of the same rule. A skill list is NOT fixed — GetAllSkills
        // includes what gear grants — so unequipping the item that granted one has to remove it.
        // Upserting alone would leave a phantom skill on the character for good.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000020000,
               "player":{"name":"Sorc","skills":[{"skill":36,"hard":1,"level":3}]}}
              """);

        var skill = Assert.Single(_store.GetCharacter("Bot1")!.Player.Skills);
        Assert.Equal(36, skill.SkillId);
        Assert.Equal(3, skill.Level);
    }

    [Fact]
    public void AnUpsertUpdatesInPlaceRatherThanDuplicating()
    {
        Apply(Keyframe());

        // Experience moving is the common case and the reason for the upsert. The same ids come
        // back with new values: they must be REPLACED, not accumulated alongside the old rows —
        // which a unique constraint would hide, since a duplicate would throw rather than double.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000030000,
               "player":{"name":"Sorc","stats":[{"id":12,"value":93},{"id":14,"value":999999}],
                         "skills":[{"skill":48,"hard":20,"level":31}]}}
              """);

        var player = _store.GetCharacter("Bot1")!.Player;
        Assert.Equal(2, player.Stats.Count);
        Assert.Equal(93, player.Stats.Single(s => s.Id == 12).Value);
        Assert.Equal(999999, player.Stats.Single(s => s.Id == 14).Value);

        // Level is denormalised onto the character row, so it has to track the upsert too.
        Assert.Equal(93, Assert.Single(_store.ListCharacters()).Level);

        var skill = Assert.Single(player.Skills);
        Assert.Equal(31, skill.Level);
        Assert.Equal(20, skill.HardPoints);
    }

    [Fact]
    public void AWearerDocumentWithAnEmptyNameIsStillAppliedInFull()
    {
        Apply(Keyframe());

        // The producer sends an empty name whenever the client has not resolved one — the block
        // is present, its fingerprint changed, and it carries a real area and hand. Reading the
        // VALUE as "no document arrived" discarded all of it, including the area, which then
        // credited the next tick's time to wherever the character used to be.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000010000,
             "player":{"unitType":0,"classId":1,"flagsEx":32,"name":"","area":83,"hand":1,
                       "skills":[{"skill":36,"hard":1,"level":1}]}}
            """);

        var player = _store.GetCharacter("Bot1")!.Player;
        Assert.Equal(83, player.Area);
        Assert.Equal(1, player.Hand);
        Assert.Equal(36, Assert.Single(player.Skills).SkillId);
    }

    [Fact]
    public void MercStatsWithoutAMercRowDoNotConjureAMercenary()
    {
        // The merc's unit document and its stats, skills and gear are fingerprinted separately, so
        // a merc SECTION can arrive carrying all three and no document — no `name` key, so there is
        // nothing to write a merc row from. Rebuilding a wearer out of the owner-1 rows served a
        // mercenary that was never captured, nameless and with a class id of zero.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "identity":{"account":"Acct","realm":"USWest","difficulty":2},
             "player":{"unitType":0,"classId":1,"name":"Sorc","stats":[{"id":12,"value":90}]},
             "merc":{"unitType":1,"classId":271,"stats":[{"id":12,"value":85}],
                     "skills":[{"skill":36,"hard":1,"level":1}],
                     "containers":{"equipped":{"items":[
                       {"unitType":4,"classId":29,"code":"crs","quality":2,"gid":9001,
                        "title":"Crystal Sword","location":1,"x":4,"y":0,"w":2,"h":3}]}}}}
            """);

        Assert.Null(_store.GetCharacter("Bot1")!.Merc);

        // The premise, so this cannot pass by the section having been ignored wholesale: the
        // owner-1 rows really did land, and only the read path declines to build a wearer for them.
        var orphan = Assert.Single(_store.SearchItems(new SearchItemsRequest { ClassIds = { 29 } }).Results);
        Assert.Equal(Owner.Merc, orphan.Owner);
    }

    [Fact]
    public void ABeltWithNoDimensionsIsServedWithTheGridItsCellsWereDerivedFrom()
    {
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"belt":{"items":[
               {"unitType":4,"code":"hp5","gid":1003,"location":2,"x":5,"y":0,"w":1,"h":1}
             ]}}}}
            """);

        var belt = _store.GetCharacter("Bot1")!.Player.Containers.Belt;

        // The slot index is decomposed against the default 4x4, so those are the dimensions the
        // cells mean. Serving the reported 0x0 alongside them would leave a renderer with a
        // coordinate it cannot place and no way to discover the grid it came from.
        Assert.Equal(4, belt.Width);
        Assert.Equal(4, belt.Height);
        Assert.Equal(1, Assert.Single(belt.Items).X);
        Assert.Equal(2, belt.Items[0].Y);
    }

    [Fact]
    public void StashComesBackAsPagesTheWayItWasSent()
    {
        Apply(Keyframe());

        // Storage keeps one row per page, and the read folds them back under the holder.
        // The stash is a Stash, not a Container: it has no items or grid of its own, only pages.
        var stash = _store.GetCharacter("Bot1")!.Player.Containers.Stash;
        var page = Assert.Single(stash.Pages);
        Assert.Equal("Personal", page.Name);
        Assert.Equal(0, page.Index);
        Assert.Equal(6, page.Width);
        Assert.Equal(8, page.Height);
        Assert.Equal("rin", Assert.Single(page.Items).Code);
    }

    [Fact]
    public void MercIsStoredAndDismissalClearsIt()
    {
        Apply(Keyframe());

        var detail = _store.GetCharacter("Bot1")!;
        Assert.Equal("Rogue", detail.Merc.Name);
        Assert.Equal(85, detail.Merc.Stats.Single(s => s.Id == 12).Value);
        Assert.NotNull(detail.Merc.Containers.Equipped);

        // Mid-game the merc going quiet means "unchanged", not "gone" — the engine sends null
        // when it cannot resolve one for a single sample, and throwing the merc away on that
        // would blink it out of the UI every time.
        Apply("""{"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000002000,"merc":null}""");
        Assert.NotNull(_store.GetCharacter("Bot1")!.Merc);

        // A keyframe is authoritative: the engine emits merc on every one, so its absence there
        // means there genuinely is no merc.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#2","keyframe":true,"updatedAt":1717000003000,"merc":null}
              """);

        // The whole wearer goes, gear included — there is no longer a separate place for its
        // containers to survive in, which is part of the point of folding them onto the Unit.
        detail = _store.GetCharacter("Bot1")!;
        Assert.Null(detail.Merc);
    }

    [Fact]
    public void AKeyframeCarryingAMercKeepsIt()
    {
        Apply(Keyframe());
        // The keyframe rule must not fire when the merc IS present on it.
        Apply(Keyframe().Replace("Game#1", "Game#2"));

        Assert.Equal("Rogue", _store.GetCharacter("Bot1")!.Merc.Name);
    }

    [Fact]
    public void AbsentSectionsLeaveStoredStateAlone()
    {
        Apply(Keyframe());

        // A partial snapshot carrying only kills must not blank the identity or the inventory.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000002000,
               "kills":{"byClass":[{"id":58,"spec":2,"count":4}]}}
              """);

        var detail = _store.GetCharacter("Bot1")!;
        Assert.Equal("Sorc", detail.Player.Name);
        // Level is not a stored field any more — it is stat 12 off the player's merged stats.
        Assert.Equal(90, detail.Player.Stats.Single(s => s.Id == 12).Value);
        Assert.Single(detail.Player.Containers.Inventory.Items);

        // Kills are a delta since the engine's last send, so 3 + 4 rather than 4.
        var classKill = detail.Kills.Single(k => !k.SuperUnique);
        Assert.Equal(58, classKill.Id);
        Assert.Equal(2, classKill.Spec);
        Assert.Equal(7, classKill.Count);
        Assert.Equal(1, detail.Kills.Single(k => k.SuperUnique).Count);
    }

    [Fact]
    public void KillsArriveBeforeAnyDifficultyIsKnownAreDroppedNotMisfiled()
    {
        // A fresh database while a bot is already mid-game — a base-path change, or a recreate.
        // The first thing to arrive can be a partial carrying kills but no identity, and the
        // character is on Hell. Filing those under Normal would be permanent: kill deltas are
        // added to a lifetime total that never self-corrects.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000000000,
               "kills":{"byClass":[{"id":58,"spec":2,"count":3}]}}
              """);

        Assert.Empty(_store.GetCharacter("Bot1")!.Kills);

        // Once identity names the difficulty, counting resumes under the right one.
        Apply(Keyframe()); // difficulty 2 (Hell)
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000002000,
               "kills":{"byClass":[{"id":58,"spec":2,"count":4}]}}
              """);

        var kill = Assert.Single(_store.GetCharacter("Bot1")!.Kills, k => !k.SuperUnique);
        Assert.Equal(2, kill.Difficulty);
        Assert.Equal(7, kill.Count); // 3 from the keyframe + 4, and nothing under Normal
    }

    [Fact]
    public void ANewGameDropsThePreviousGamesItems()
    {
        Apply(Keyframe());
        Assert.NotNull(_store.GetCharacter("Bot1")!.Player.Containers.Inventory);

        // Item gids are only valid within one game, so a game change clears them — but the
        // accumulated analytics have to survive it.
        Apply("""{"schemaVersion":2,"gameId":"Game#2","keyframe":true,"updatedAt":1717000003000}""");

        var detail = _store.GetCharacter("Bot1")!;
        Assert.Null(detail.Player.Containers.Inventory);
        Assert.Equal("Sorc", detail.Player.Name);
        Assert.NotEmpty(detail.Kills);
    }

    [Fact]
    public void OneProfilesWritesAndReadsStopAtItsOwnRows()
    {
        // Every row in this schema is keyed by profile, and a manager runs hundreds of them into
        // one database. Nothing else here uses two, so a dropped `profile` predicate is invisible:
        // on a write it is one bot entering a new game wiping another's gear, on a read it is a
        // character's inventory answering with everybody's.
        Apply(Keyframe());
        _store.Apply("Bot2", Parse(Keyframe().Replace("Sorc", "Barb")));

        var scoped = _store.SearchItems(new SearchItemsRequest { Profiles = { "Bot1" } });
        Assert.Equal(3, scoped.Total);
        Assert.All(scoped.Results, match => Assert.Equal("Bot1", match.Profile));
        Assert.Equal(6, _store.SearchItems(new SearchItemsRequest()).Total);

        // Bot1 rebuilds a container, reports a shrinking stat set (which prunes), then changes
        // game (which clears every container and dismisses the merc). Bot2 reports nothing at all
        // across the three, so anything of its own that moves is a predicate that was not applied.
        Apply("""
              {"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000010000,
               "player":{"name":"Sorc","stats":[{"id":12,"value":91}],
                         "containers":{"inventory":{"width":10,"height":4,"items":[]}}}}
              """);
        Apply("""
              {"schemaVersion":2,"gameId":"Game#2","keyframe":true,"updatedAt":1717000020000}
              """);

        Assert.Null(_store.GetCharacter("Bot1")!.Player.Containers.Inventory);
        Assert.Empty(_store.SearchItems(new SearchItemsRequest { Profiles = { "Bot1" } }).Results);

        var other = _store.GetCharacter("Bot2")!;
        Assert.Equal("Barb", other.Player.Name);
        Assert.Single(other.Player.Containers.Inventory.Items);
        Assert.Equal(123456, other.Player.Stats.Single(s => s.Id == 14).Value);
        Assert.Equal("Rogue", other.Merc.Name);
        Assert.Equal(3, _store.SearchItems(new SearchItemsRequest { Profiles = { "Bot2" } }).Total);
    }

    [Fact]
    public void NamingSeveralProfilesSearchesThoseAndOnlyThose()
    {
        // The filter is a list rather than one name or all of them, and the middle is the whole
        // point: comparing what a few mules hold is the question, and neither end expresses it.
        // Three profiles, so "the ones asked for" and "not all of them" are different answers.
        Apply(Keyframe());
        _store.Apply("Bot2", Parse(Keyframe().Replace("Sorc", "Barb")));
        _store.Apply("Bot3", Parse(Keyframe().Replace("Sorc", "Pala")));

        var pair = _store.SearchItems(new SearchItemsRequest { Profiles = { "Bot1", "Bot3" } });

        Assert.Equal(6, pair.Total);
        Assert.Equal(["Bot1", "Bot3"], pair.Results.Select(m => m.Profile).Distinct().Order());
        Assert.Equal(9, _store.SearchItems(new SearchItemsRequest()).Total);
    }

    /// <summary>The common case, in one expression — which is the point of the flattened model.</summary>
    private static StatCondition Stat(int statId, int? min = null, int? max = null,
        SocketScope sockets = SocketScope.HostOnly)
    {
        var condition = new StatCondition { StatIds = { statId }, Sockets = sockets };
        if (min.HasValue) condition.MinValue = min.Value;
        if (max.HasValue) condition.MaxValue = max.Value;
        return condition;
    }

    private SearchItemsResponse Search(params StatCondition[] conditions) =>
        _store.SearchItems(new SearchItemsRequest { Conditions = { conditions } });

    [Fact]
    public void SearchFindsItemsByStat()
    {
        Apply(Keyframe());

        // Fire resist (39) >= 30 is on the helm itself.
        var found = Search(Stat(39, min: 30));
        Assert.Equal(1, found.Total);
        Assert.Equal("cap", Assert.Single(found.Results).Item.Code);
        Assert.Equal("inventory", found.Results[0].Container);

        // Above the item's value: no match.
        Assert.Empty(Search(Stat(39, min: 31)).Results);

        // Bare conditions are AND-ed, and may be satisfied by different stats on one item.
        Assert.Single(Search(Stat(39, min: 30), Stat(7)).Results);
        Assert.Empty(Search(Stat(39, min: 30), Stat(9999)).Results);
    }

    [Fact]
    public void SocketScopeDecidesWhoseStatsCount()
    {
        Apply(Keyframe());

        // Stat 48 is on the socketed gem, not the helm. Per-condition scope is what makes this
        // expressible — and the match is still the HOST item, with the gem nested inside it.
        Assert.Empty(Search(Stat(48)).Results);

        var viaSocket = Assert.Single(Search(Stat(48, sockets: SocketScope.WithFillers)).Results);
        Assert.Equal("cap", viaSocket.Item.Code);
        Assert.Equal("gpr", Assert.Single(viaSocket.Item.Sockets).Code);

        // FILLERS_ONLY excludes the host's own stats: 39 is the helm's, so it finds nothing.
        Assert.Empty(Search(Stat(39, min: 30, sockets: SocketScope.FillersOnly)).Results);
        Assert.Single(Search(Stat(48, sockets: SocketScope.FillersOnly)).Results);
    }

    [Fact]
    public void AGroupCountsHowManyOfItsConditionsMatched()
    {
        Apply(Keyframe());

        // min_matches = 1 is OR: the helm has 39 but not 9999.
        Assert.Single(SearchGroups(new StatConditionGroup
        {
            Conditions = { Stat(39, 30), Stat(9999) },
            MinMatches = 1,
        }).Results);

        // Requiring both fails — and absent min_matches means exactly that.
        Assert.Empty(SearchGroups(new StatConditionGroup
        {
            Conditions = { Stat(39, 30), Stat(9999) },
        }).Results);
    }

    [Fact]
    public void ANegatedGroupExcludes()
    {
        Apply(Keyframe());

        // The ring has stat 7 and no 39; the helm has both. Negating 39 leaves the ring.
        var found = _store.SearchItems(new SearchItemsRequest
        {
            Conditions = { Stat(7) },
            Groups = { new StatConditionGroup { Conditions = { Stat(39) }, Negate = true } },
        });

        Assert.Equal("rin", Assert.Single(found.Results).Item.Code);
    }

    /// <summary>
    /// Every one of these was previously "repaired" in silence, and the repair changed the
    /// answer — three of the four towards matching MORE than was asked for, which a caller
    /// reading results has no way to notice. They are rejected now, so this pins the contract
    /// rather than the old behaviour.
    /// </summary>
    [Theory]
    // A lone layer_max was dropped entirely, so a search bounded to a layer range silently
    // matched every layer.
    [InlineData("layer_max without layer")]
    [InlineData("layer_max below layer")]
    // Clamping min_matches into range moves the other side of the complement when negated:
    // "exclude items matching 2 of these 1" means exclude nothing, but excluded the matchers.
    [InlineData("min_matches above the condition count")]
    [InlineData("min_matches of zero")]
    // A condition naming no stats filtered nothing bare, but was dropped from a group and left
    // the count applying to whatever remained.
    [InlineData("condition with no stat ids")]
    [InlineData("empty group")]
    // Unknown enum values folded to their zero value: a newer client's socket scope became
    // HOST_ONLY, quietly answering a different question.
    [InlineData("unknown socket scope")]
    [InlineData("unknown container name")]
    // An ItemTypes.txt row that does not exist expands to nothing, and an empty IN list is legal
    // SQL — so this answered "no such items" to a filter naming no category at all.
    [InlineData("unknown item type row")]
    // SQLite's structural limits (500 compound-SELECT arms, 1000 expression depth) are reached
    // one to two orders of magnitude below its bound-parameter ceiling, so a parameter cap alone
    // let these through to fail as an unmappable engine error.
    [InlineData("too many conditions")]
    [InlineData("too many conditions in a group")]
    [InlineData("too many specific items")]
    // Clamping an oversized limit silently drops every item past the ceiling from a page the
    // caller believes is whole, and breaks the paging arithmetic that depends on the size asked for.
    [InlineData("limit above the ceiling")]
    [InlineData("negative limit")]
    [InlineData("negative offset")]
    public void AnUnanswerableRequestIsRejectedRatherThanRepaired(string kind)
    {
        Apply(Keyframe());

        var request = new SearchItemsRequest();
        switch (kind)
        {
            case "layer_max without layer":
                request.Conditions.Add(new StatCondition { StatIds = { 39 }, LayerMax = 54 });
                break;
            case "layer_max below layer":
                request.Conditions.Add(new StatCondition { StatIds = { 39 }, Layer = 60, LayerMax = 54 });
                break;
            case "min_matches above the condition count":
                request.Groups.Add(new StatConditionGroup { Conditions = { Stat(39) }, MinMatches = 2 });
                break;
            case "min_matches of zero":
                request.Groups.Add(new StatConditionGroup { Conditions = { Stat(39) }, MinMatches = 0 });
                break;
            case "condition with no stat ids":
                request.Conditions.Add(new StatCondition());
                break;
            case "empty group":
                request.Groups.Add(new StatConditionGroup { Negate = true });
                break;
            case "unknown socket scope":
                request.Conditions.Add(new StatCondition { StatIds = { 39 }, Sockets = (SocketScope)5 });
                break;
            case "unknown container name":
                request.Containers.Add("stashh");
                break;
            case "unknown item type row":
                request.ItemTypes.Add(99999);
                break;
            case "too many conditions":
                for (var i = 0; i < 200; i++) request.Conditions.Add(Stat(39));
                break;
            case "too many conditions in a group":
                var group = new StatConditionGroup { MinMatches = 1 };
                for (var i = 0; i < 600; i++) group.Conditions.Add(Stat(39));
                request.Groups.Add(group);
                break;
            case "too many specific items":
                for (var i = 0; i < 800; i++)
                    request.SpecificItems.Add(new SpecificItem { Quality = 7, FileIndex = i });
                break;
            case "limit above the ceiling":
                request.Limit = 5000;
                break;
            case "negative limit":
                request.Limit = -3;
                break;
            case "negative offset":
                request.Offset = -5;
                break;
        }

        Assert.Throws<InvalidSearchRequestException>(() => _store.SearchItems(request));
    }

    [Fact]
    public void AFilterTooLargeToBindIsRejected()
    {
        Apply(Keyframe());

        // SQLite binds one parameter per element and gives up past 32766, with an error no caller
        // could act on. 500 base items is a legitimate sweep; 40k is a caller that needs telling.
        var request = new SearchItemsRequest();
        for (var i = 0; i < 40_000; i++) request.ClassIds.Add(i);

        Assert.Throws<InvalidSearchRequestException>(() => _store.SearchItems(request));
    }

    [Fact]
    public void ListScopeSeparatesBaseStatsFromGrantedOnes()
    {
        Apply(Keyframe());

        // An item's base stats and its affix stats are BOTH state_no 0 in a real capture; only
        // STATLIST_MAGIC (0x40) tells them apart, and it is the GRANTED list that carries it — the
        // base array is marked by STATLIST_EXTENDED (0x80000000) instead. The helm's list is the
        // base one, so it is reachable by excluding 0x40 and not by requiring it.
        Assert.Single(Search(new StatCondition
        {
            StatIds = { 39 },
            Lists = new StatListScope { FlagsNone = 0x40 },
        }).Results);

        Assert.Empty(Search(new StatCondition
        {
            StatIds = { 39 },
            Lists = new StatListScope { FlagsAll = 0x40 },
        }).Results);

        // Excluding the state the stat lives on removes it; excluding an unrelated one does not.
        Assert.Empty(Search(new StatCondition
        {
            StatIds = { 39 },
            Lists = new StatListScope { ExcludeStates = { 0 } },
        }).Results);

        Assert.Single(Search(new StatCondition
        {
            StatIds = { 39 },
            Lists = new StatListScope { ExcludeStates = { 165, 166, 167, 168, 169, 170 } },
        }).Results);
    }

    [Fact]
    public void IdentityAndLocationFiltersNarrowWithoutAnyStatCondition()
    {
        Apply(Keyframe());

        // The most common operator query is not a stat query at all: "where is my X".
        Assert.Equal(3, _store.SearchItems(new SearchItemsRequest()).Total);

        Assert.Equal("cap", Assert.Single(_store.SearchItems(
            new SearchItemsRequest { ClassIds = { 306 } }).Results).Item.Code);

        // Excluding what is being worn is a real filter — equipped items are not mule candidates.
        var stashOnly = _store.SearchItems(new SearchItemsRequest { Containers = { "stash" } });
        Assert.Equal("rin", Assert.Single(stashOnly.Results).Item.Code);

        // Quality, class id and graphic all narrow on their own. The potion is the only normal
        // item, so asking for magic alone has to leave it out — naming every quality the fixture
        // holds would answer the same as no filter at all.
        Assert.Equal(["cap", "rin"], _store.SearchItems(
            new SearchItemsRequest { Qualities = { 4 } }).Results.Select(r => r.Item.Code));
        Assert.Single(_store.SearchItems(new SearchItemsRequest { ClassIds = { 306 } }).Results);
        Assert.Single(_store.SearchItems(new SearchItemsRequest { GfxIndexes = { 2 } }).Results);

        // Flag masks: the helm is the only socketed item (0x800).
        Assert.Equal("cap", Assert.Single(_store.SearchItems(
            new SearchItemsRequest { ItemFlagsAll = 0x800 }).Results).Item.Code);
        Assert.Equal(2, _store.SearchItems(new SearchItemsRequest { ItemFlagsNone = 0x800 }).Total);
    }

    private SearchItemsResponse SearchGroups(params StatConditionGroup[] groups) =>
        _store.SearchItems(new SearchItemsRequest { Groups = { groups } });

    [Fact]
    public void EveryMatchGetsItsOwnItem()
    {
        Apply(Keyframe());

        // Stat 7 is on the helm (inventory) and the ring (stash). The page is rebuilt in ONE
        // pass and the trees mapped back by root id, so this is what would catch that mapping
        // going wrong — every single-match test would still pass with the items swapped.
        var found = Search(Stat(7));

        Assert.Equal(2, found.Total);
        Assert.Equal(2, found.Results.Count);

        var helm = found.Results.Single(r => r.Container == "inventory");
        Assert.Equal("cap", helm.Item.Code);
        Assert.Equal(1001u, helm.Item.Gid);
        Assert.Single(helm.Item.Sockets); // its gem came along, on the right item

        var ring = found.Results.Single(r => r.Container == "stash");
        Assert.Equal("rin", ring.Item.Code);
        Assert.Equal(1004u, ring.Item.Gid);
        Assert.Empty(ring.Item.Sockets);
    }

    [Fact]
    public void AreaTimeAccruesBetweenInGameUpdates()
    {
        Apply(Keyframe());
        // Same game, 30s later, still in area 40: the gap is credited to where it was spent.
        Apply("""{"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000030000}""");

        var areaTime = Assert.Single(_store.GetCharacter("Bot1")!.AreaTime);
        Assert.Equal(40, areaTime.Area);
        Assert.Equal(2, areaTime.Difficulty);
        Assert.Equal(30000, areaTime.Milliseconds);

        _store.ResetAreaTime("Bot1");
        Assert.Empty(_store.GetCharacter("Bot1")!.AreaTime);
    }

    /// <summary>Where the store puts the database, for the recovery tests below.</summary>
    private string DatabasePath => Path.Combine(_directory, "data", "ng", "captures.db");

    [Fact]
    public void ACorruptDatabaseIsRecreatedAndKeepsWorking()
    {
        Apply(Keyframe());
        _store.Dispose();
        File.WriteAllBytes(DatabasePath, "this is not a database"u8.ToArray());

        using var reopened = new CaptureStore(NullLogger<CaptureStore>.Instance, new Paths(_directory), _tooltip);
        reopened.Open();
        reopened.Apply("Bot1", Parse(Keyframe()));

        // The old contents are gone, which is fine — this store is derived state — but it must
        // come back up rather than staying disabled until someone deletes the file by hand.
        Assert.Equal("Sorc", Assert.Single(reopened.ListCharacters()).Name);
    }

    /// <summary>
    /// The rule is "any version that is not this one", not "any newer one" — deleting the
    /// migration chain made those the same code path. Both cases are above
    /// <see cref="CaptureSchema.Version" /> because at version 1 nothing can be below it: 0 is
    /// reserved for a file with no schema yet, which is created rather than discarded. The
    /// adjacent value is the one that carries the test — a version check gone off by one would
    /// pass 999 and fail here.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(999)]
    public void ADatabaseAtAnotherSchemaVersionIsRecreatedAndKeepsWorking(int version)
    {
        Apply(Keyframe());
        _store.Dispose();
        SetUserVersion(DatabasePath, version);

        using var reopened = new CaptureStore(NullLogger<CaptureStore>.Instance, new Paths(_directory), _tooltip);
        reopened.Open();
        reopened.Apply("Bot1", Parse(Keyframe()));

        Assert.Equal("Sorc", Assert.Single(reopened.ListCharacters()).Name);
    }

    /// <summary>
    /// Stamps a schema version other than this build's. Pooling is off so this helper cannot be
    /// the thing holding the file open when the store tries to replace it.
    /// </summary>
    private static void SetUserVersion(string path, int version)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version}";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void ReopeningReadsBackWhatWasStored()
    {
        Apply(Keyframe());
        _store.Dispose();

        using var reopened = new CaptureStore(NullLogger<CaptureStore>.Instance, new Paths(_directory), _tooltip);
        reopened.Open();

        var character = Assert.Single(reopened.ListCharacters());
        Assert.Equal("Sorc", character.Name);
        Assert.Single(reopened.GetCharacter("Bot1")!.Player.Containers.Inventory.Items);
    }

    [Fact]
    public void TheGapBetweenSessionsIsNotCreditedAsAreaTime()
    {
        Apply(Keyframe());
        _store.Dispose();

        using var reopened = new CaptureStore(NullLogger<CaptureStore>.Instance, new Paths(_directory), _tooltip);
        reopened.Open();

        // Four minutes after the stored snapshot — inside the five-minute tick cap, so the cap is
        // not what saves us here. The manager was down for that gap, and only the in-memory
        // "reported this session" gate stops it being credited to the area the character was last
        // standing in. Nothing persists that gate: a column would survive the restart and so
        // could never distinguish this case from an ordinary update.
        reopened.Apply("Bot1", Parse(
            """{"schemaVersion":2,"gameId":"Game#1","updatedAt":1717000240000}"""));

        Assert.Empty(reopened.GetCharacter("Bot1")!.AreaTime);
    }

    [Fact]
    public void ItemLevelRoundTripsAndFilters()
    {
        Apply(Keyframe());

        // Stored and read back on the item itself — not a requirement, and not the character's.
        var cap = Assert.Single(Search(Stat(39, min: 30)).Results);
        Assert.Equal(87, cap.Item.ItemLevel);

        SearchItemsResponse ByLevel(int? min, int? max)
        {
            var range = new Int32Range();
            if (min.HasValue) range.Min = min.Value;
            if (max.HasValue) range.Max = max.Value;
            return _store.SearchItems(new SearchItemsRequest { ItemLevel = range });
        }

        // The fixture spans 20 / 50 / 87, so each bound has to discriminate rather than sweep the
        // lot — an upper bound above every item would pass whatever the filter did.
        Assert.Equal("cap", Assert.Single(ByLevel(87, null).Results).Item.Code);
        Assert.Equal("hp5", Assert.Single(ByLevel(null, 30).Results).Item.Code);
        Assert.Equal("rin", Assert.Single(ByLevel(40, 60).Results).Item.Code);
        Assert.Empty(ByLevel(88, null).Results);

        // And it sorts, which is the crafting question: what is the highest-level base I have?
        var byLevel = _store.SearchItems(new SearchItemsRequest
        {
            Ordering = new Ordering { Column = ItemColumn.ItemLevel, Descending = true },
        });
        Assert.Equal("cap", byLevel.Results[0].Item.Code);
    }

    [Fact]
    public void NamingSeveralContainersLooksInAllOfThem()
    {
        Apply(Keyframe());

        var all = _store.SearchItems(new SearchItemsRequest());
        Assert.Equal(3, all.Total); // cap (inventory), hp5 (belt), rin (stash)

        // Everything not currently worn, which with a closed set of five is the other four named.
        // There is no exclusion list to say it the other way round — two fields able to contradict
        // each other is a shape the store could only reject.
        var notWorn = _store.SearchItems(new SearchItemsRequest
        {
            Containers =
            {
                CaptureSchema.ContainerBelt, CaptureSchema.ContainerStash,
                CaptureSchema.ContainerCube, CaptureSchema.ContainerEquipped,
            },
        });
        Assert.Equal(["hp5", "rin"], notWorn.Results.Select(r => r.Item.Code));
    }

    // -----------------------------------------------------------------------
    // Ordering
    // -----------------------------------------------------------------------

    private List<string> SearchOrdered(Ordering ordering, int limit = 0, int offset = 0)
    {
        var response = _store.SearchItems(new SearchItemsRequest
        {
            Ordering = ordering,
            Limit = limit,
            Offset = offset,
        });
        return response.Results.Select(r => r.Item.Code).ToList();
    }

    private static Ordering ByStat(int statId, bool descending) => new()
    {
        Stat = new StatOrder { StatIds = { statId } },
        Descending = descending,
    };

    [Fact]
    public void OrderByColumnSortsOnThatColumn()
    {
        Apply(OrderingFixture());

        // Quality comes straight off the capture: axe 6, cap 4, whm 3, hp5 2.
        Assert.Equal(["hp5", "whm", "cap", "axe"],
            SearchOrdered(new Ordering { Column = ItemColumn.Quality }));
        Assert.Equal(["axe", "cap", "whm", "hp5"],
            SearchOrdered(new Ordering { Column = ItemColumn.Quality, Descending = true }));
    }

    [Fact]
    public void OrderByColumnPutsUnknownsLastInBothDirections()
    {
        Apply(OrderingFixture());

        // The fixture's premise, asserted rather than assumed: `tier` is resolved from the game
        // tables at ingest, and exactly one of these four has a class id naming no row. A tier
        // filter cannot match a NULL, so this is what proves the other three DID resolve — without
        // it, all four being NULL would leave the position tiebreaker to put the potion last on
        // its own and the assertions below would hold for the wrong reason.
        var resolvable = _store.SearchItems(new SearchItemsRequest
        {
            Tiers = { Tier.Normal, Tier.Exceptional, Tier.Elite },
        });
        Assert.Equal(["axe", "cap", "whm"], resolvable.Results.Select(r => r.Item.Code));

        // SQLite sorts NULLs FIRST ascending, so an unmodified ORDER BY would lead with the item
        // nothing is known about.
        Assert.Equal("hp5", SearchOrdered(new Ordering { Column = ItemColumn.Tier }).Last());
        Assert.Equal("hp5",
            SearchOrdered(new Ordering { Column = ItemColumn.Tier, Descending = true }).Last());
    }

    [Fact]
    public void DamageIsStoredPerLineSoOrderingAsksAboutTheLineThatWasClicked()
    {
        // A one-handed sword and a two-handed thresher, each carrying only its own damage pair —
        // 21/22 for the sword, 23/24 for the thresher, which is how the game stores them.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"inventory":{"width":10,"height":4,"items":[
               {"unitType":4,"classId":29,"code":"crs","quality":2,"itemFlags":0,"fileIndex":-1,
                "title":"Sword","gid":4001,"location":3,"x":0,"y":0,"w":1,"h":3,
                "statsLists":[{"stateNo":0,"flags":64,
                  "stats":[{"id":21,"value":10},{"id":22,"value":40}]}]},
               {"unitType":4,"classId":255,"code":"7s8","quality":2,"itemFlags":0,"fileIndex":-1,
                "title":"Thresher","gid":4002,"location":3,"x":2,"y":0,"w":2,"h":4,
                "statsLists":[{"stateNo":0,"flags":64,
                  "stats":[{"id":23,"value":30},{"id":24,"value":150}]}]}
             ]}}}}
            """);

        string[] Ordered(ItemColumn column) => _store.SearchItems(new SearchItemsRequest
        {
            Ordering = new Ordering { Column = column, Descending = true },
        }).Results.Select(r => r.Item.Title).ToArray();

        // Each ranks on ITS OWN line and the other is absent, which is the point of storing them
        // apart: the thresher hits harder, but it draws no one-hand line to compare, so ranking by
        // one-hand damage must not fold its two-handed number in — it has to sort last instead.
        Assert.Equal(["Sword", "Thresher"], Ordered(ItemColumn.DamageOneHandMax));
        Assert.Equal(["Thresher", "Sword"], Ordered(ItemColumn.DamageTwoHandMax));
    }

    [Fact]
    public void OrderByDamageRanksByTheLineTheTooltipDraws()
    {
        // Two of the same base at different damage, and a helm that draws no damage line at all.
        Apply(
            """
            {"schemaVersion":2,"gameId":"Game#1","keyframe":true,"updatedAt":1717000000000,
             "player":{"name":"Sorc","containers":{"inventory":{"width":10,"height":4,"items":[
               {"unitType":4,"classId":29,"code":"crs","quality":2,"itemFlags":0,"fileIndex":-1,
                "title":"Duller","gid":3001,"location":3,"x":0,"y":0,"w":1,"h":3,
                "statsLists":[{"stateNo":0,"flags":64,
                  "stats":[{"id":21,"value":10},{"id":22,"value":40}]}]},
               {"unitType":4,"classId":29,"code":"crs","quality":2,"itemFlags":0,"fileIndex":-1,
                "title":"Sharper","gid":3002,"location":3,"x":2,"y":0,"w":1,"h":3,
                "statsLists":[{"stateNo":0,"flags":64,
                  "stats":[{"id":21,"value":20},{"id":22,"value":90}]}]},
               {"unitType":4,"classId":358,"code":"xsk","quality":2,"itemFlags":0,"fileIndex":-1,
                "title":"Helm","gid":3003,"location":3,"x":4,"y":0,"w":2,"h":2}
             ]}}}}
            """);

        // Resolved at ingest through the toolkit, which is what makes this a COLUMN rather than a
        // stat order: the line the game DRAWS picks the right stat pair for the item (a two-hander
        // is 23/24, not 21/22) and applies the game's own arithmetic to it, where ordering by stat
        // 22 alone ranks by one end of one pair and silently omits every weapon using the other.
        string[] Ordered(bool descending) => _store.SearchItems(new SearchItemsRequest
        {
            Ordering = new Ordering { Column = ItemColumn.DamageOneHandMax, Descending = descending },
        }).Results.Select(r => r.Item.Title).ToArray();

        // The helm has no damage line, so it is absent rather than zero — and absent sorts last in
        // BOTH directions, which is why it does not lead the ascending pass.
        Assert.Equal(["Sharper", "Duller", "Helm"], Ordered(descending: true));
        Assert.Equal(["Duller", "Sharper", "Helm"], Ordered(descending: false));
    }

    [Fact]
    public void ConditionsGroupsAndAnOrderingCoexistInOneRequest()
    {
        Apply(OrderingFixture());

        // The three families each generate CTEs and bound parameters from their own prefixes, and
        // nothing else exercises them together — a name reused across two of them would bind one
        // clause's value into another's slot, which is a wrong ANSWER rather than an error.
        var response = _store.SearchItems(new SearchItemsRequest
        {
            // Every item carrying stat 7 at all.
            Conditions = { Stat(7, min: 50) },
            Groups =
            {
                // Satisfied by the axe and the hammer through their 100+ and by the cap through its
                // 60, so the group narrows nothing on its own; what it tests is that it does not
                // narrow wrongly.
                new StatConditionGroup
                {
                    Conditions = { Stat(7, min: 100), Stat(7, max: 60) }, MinMatches = 1,
                },
            },
            Ordering = ByStat(7, descending: true),
        });

        Assert.Equal(3, response.Total);
        Assert.Equal(["axe", "cap", "whm"], response.Results.Select(r => r.Item.Code));
    }

    [Fact]
    public void OrderByStatRanksByTheBestSourceOnRawAndByTheTotalOnMerged()
    {
        Apply(OrderingFixture());

        // One key, one set of data, two answers — which is the whole reason the surface is named on
        // the ordering rather than inherited. The cap carries stat 7 on two lists, 60 and 100; the
        // axe carries a single 120. Raw ranks by the BEST SOURCE, so the axe leads: a condition
        // means "has a source of at least N", and an ordering that summed would put the cap above
        // the item the filter actually selected. Merged ranks by the TOTAL, so the cap's 160 leads —
        // which is what a reader comparing two items means, and the branch the UI orders through
        // whenever the panel matched on totals.
        //
        // The axe sits FIRST in the container on purpose: with the cap first, an ordering that
        // ranked by anything constant across these rows would come back cap-then-axe on the position
        // tiebreaker alone and the merged assertion would hold without ranking anything.
        Assert.Equal(["axe", "cap"], SearchOrdered(ByStat(7, descending: true)).Take(2));
        Assert.Equal(["cap", "axe"], SearchOrdered(new Ordering
        {
            Stat = new StatOrder { StatIds = { 7 }, Surface = StatSurface.Merged },
            Descending = true,
        }).Take(2));
    }

    [Fact]
    public void OrderByStatPutsItemsWithoutItLastInBothDirections()
    {
        Apply(OrderingFixture());

        // The potion has no stat 7 at all. Ascending is the case that matters: "no such modifier"
        // is not the smallest value, it is the absence of one, and leading with it would bury
        // every real answer.
        Assert.Equal(["cap", "whm", "axe", "hp5"], SearchOrdered(ByStat(7, descending: false)));
        Assert.Equal(["axe", "cap", "whm", "hp5"], SearchOrdered(ByStat(7, descending: true)));
    }

    [Fact]
    public void OrderingDoesNotChangeTheTotal()
    {
        Apply(OrderingFixture());

        var unordered = _store.SearchItems(new SearchItemsRequest());
        var ordered = _store.SearchItems(new SearchItemsRequest { Ordering = ByStat(7, true) });

        // Also covers a structural detail: the ordering CTE is declared in front of the COUNT
        // query too, which never joins it. An unused CTE has to be legal there, or every ordered
        // search would fail on the count rather than the page.
        Assert.Equal(unordered.Total, ordered.Total);
        Assert.Equal(4, ordered.Total);
    }

    [Fact]
    public void PagingAnOrderedSearchVisitsEachItemExactlyOnce()
    {
        Apply(OrderingFixture());

        // No sort key offered is unique, so the position tiebreakers have to survive alongside the
        // requested one. Without them an OFFSET page can repeat a row and skip another — and the
        // cap and the hammer both rank at 100, which is the tie that makes the pages depend on
        // them rather than on the key alone.
        var pages = Enumerable.Range(0, 4)
            .SelectMany(page => SearchOrdered(ByStat(7, descending: true), limit: 1, offset: page))
            .ToList();

        Assert.Equal(4, pages.Distinct().Count());
        Assert.Equal(["axe", "cap", "whm", "hp5"], pages);
    }

    [Theory]
    // A column the enum does not define: answering it as the default order would look sorted.
    [InlineData(99, 0, "unknown column")]
    // A stat ordering naming nothing to order by.
    [InlineData(-1, 0, "no stat_ids")]
    public void OrderingRejectsWhatItCannotAnswer(int column, int statId, string expected)
    {
        Apply(OrderingFixture());

        var ordering = new Ordering();
        if (column >= 0)
        {
            ordering.Column = (ItemColumn)column;
        }
        else
        {
            var stat = new StatOrder();
            if (statId > 0) stat.StatIds.Add(statId);
            ordering.Stat = stat;
        }

        var error = Assert.Throws<InvalidSearchRequestException>(
            () => _store.SearchItems(new SearchItemsRequest { Ordering = ordering }));
        Assert.Contains(expected, error.Message);
    }

    /// <summary>
    /// Four items whose ordering is unambiguous on every axis under test.
    ///
    /// The cap and the axe deliberately disagree between MAX and SUM on stat 7, and the flags say
    /// which list is which the way a real capture does: the base array carries STATLIST_EXTENDED
    /// (0x80000000) and the granted mods STATLIST_MAGIC (0x40). That is not decoration — the
    /// toolkit's merged view skips a group carrying neither, so a fixture flagged 0 stores no
    /// merged row at all and the merged ordering would then be ranking absences.
    ///
    /// The hammer ties the cap on stat 7, so the position tiebreakers are load-bearing rather than
    /// theoretical; the potion has neither a stat 7 nor a resolvable class id, so it is the
    /// "unknown" row for both the stat and the column cases.
    /// </summary>
    private static string OrderingFixture() =>
        """
        {
          "schemaVersion": 2,
          "gameId": "Game#1",
          "keyframe": true,
          "updatedAt": 1717000000000,
          "identity": {"account":"Acct","realm":"USWest","difficulty":2,"charFlags":36},
          "player": {
            "unitType":0,"classId":1,"flagsEx":32,"name":"Sorc","skills":[],
            "stats":[{"id":12,"value":90}],
            "containers":{
              "inventory":{"width":10,"height":4,"items":[
                {"unitType":4,"classId":1,"code":"axe","quality":6,"itemFlags":0,"format":0,
                 "fileIndex":-1,"magicPrefix":[0,0,0],"magicSuffix":[0,0,0],
                 "title":"Axe",
                 "statsLists":[{"stateNo":0,"flags":64,"stats":[{"id":7,"value":120}]}],
                 "gid":2001,"location":3,"x":0,"y":0,"w":2,"h":3},
                {"unitType":4,"classId":306,"code":"cap","quality":4,"itemFlags":0,"format":0,
                 "fileIndex":-1,"magicPrefix":[0,0,0],"magicSuffix":[0,0,0],
                 "title":"Cap",
                 "statsLists":[
                   {"stateNo":0,"flags":2147483648,"stats":[{"id":7,"value":60}]},
                   {"stateNo":0,"flags":64,"stats":[{"id":7,"value":100}]}
                 ],
                 "gid":2002,"location":3,"x":3,"y":0,"w":2,"h":2},
                {"unitType":4,"classId":99999,"code":"hp5","quality":2,"itemFlags":0,"format":0,
                 "fileIndex":-1,"itemLevel":20,"magicPrefix":[0,0,0],"magicSuffix":[0,0,0],
                 "title":"Potion",
                 "gid":2003,"location":3,"x":6,"y":0,"w":1,"h":1},
                {"unitType":4,"classId":22,"code":"whm","quality":3,"itemFlags":0,"format":0,
                 "fileIndex":-1,"magicPrefix":[0,0,0],"magicSuffix":[0,0,0],
                 "title":"Hammer",
                 "statsLists":[{"stateNo":0,"flags":64,"stats":[{"id":7,"value":100}]}],
                 "gid":2004,"location":3,"x":8,"y":0,"w":2,"h":3}
              ]}
            }
          }
        }
        """;

    /// <summary>A full keyframe, shaped exactly as d2bsng's UnitJson.cpp emits it.</summary>
    private static string Keyframe() =>
        """
        {
          "schemaVersion": 2,
          "gameId": "Game#1",
          "keyframe": true,
          "updatedAt": 1717000000000,
          "identity": {"account":"Acct","realm":"USWest","difficulty":2,"charFlags":36,"ladder":true},
          "progression": {"quests":[1,2],"waypoints":[0,3]},
          "player": {
            "unitType":0,"classId":1,"flagsEx":32,"name":"Sorc",
            "skills":[{"skill":48,"hard":20,"level":28}],
            "area":40,"hand":0,
            "stats":[{"id":12,"value":90},{"id":14,"value":123456}],
            "containers":{
              "inventory":{"width":10,"height":4,"items":[
                {"unitType":4,"classId":306,"code":"cap","quality":4,"itemFlags":2048,"format":0,
                 "fileIndex":-1,"itemLevel":87,"rarePrefix":0,"rareSuffix":0,"autoAffix":0,
                 "magicPrefix":[0,0,0],"magicSuffix":[812,0,0],"earLevel":0,"playerName":"","gfxIndex":0,
                 "title":"Cap of the Whale","description":"a tooltip",
                 "statsLists":[{"stateNo":0,"flags":2147483648,
                   "stats":[{"id":39,"value":30},{"id":7,"value":100}]}],
                 "gid":1001,"location":3,"x":2,"y":1,"w":2,"h":2,
                 "sockets":[
                   {"unitType":4,"classId":581,"code":"gpr","quality":2,"itemFlags":0,"format":0,
                    "fileIndex":-1,"rarePrefix":0,"rareSuffix":0,"autoAffix":0,
                    "magicPrefix":[0,0,0],"magicSuffix":[0,0,0],"earLevel":0,"playerName":"","gfxIndex":0,
                    "title":"Perfect Ruby",
                    "statsLists":[{"stateNo":0,"flags":1,"stats":[{"id":48,"value":5}]}],
                    "gid":1002,"location":3,"x":0,"y":0,"w":1,"h":1}
                 ]}
              ]},
              "belt":{"width":4,"height":4,"items":[
                {"unitType":4,"classId":591,"code":"hp5","quality":2,"itemFlags":0,"format":0,
                 "fileIndex":-1,"rarePrefix":0,"rareSuffix":0,"autoAffix":0,
                 "magicPrefix":[0,0,0],"magicSuffix":[0,0,0],"earLevel":0,"playerName":"","gfxIndex":0,
                 "title":"Greater Healing Potion",
                 "gid":1003,"location":2,"x":5,"y":0,"w":1,"h":1}
              ]},
              "stash":{"pages":[{"index":0,"name":"Personal","width":6,"height":8,"items":[
                {"unitType":4,"classId":522,"code":"rin","quality":4,"itemFlags":0,"format":0,
                 "fileIndex":-1,"itemLevel":50,"rarePrefix":0,"rareSuffix":0,"autoAffix":0,
                 "magicPrefix":[0,0,0],"magicSuffix":[0,0,0],"earLevel":0,"playerName":"","gfxIndex":2,
                 "title":"Ring of the Apprentice",
                 "statsLists":[{"stateNo":0,"flags":1,"stats":[{"id":7,"value":50}]}],
                 "gid":1004,"location":7,"x":1,"y":2,"w":1,"h":1}
              ]}]}
            }
          },
          "merc": {
            "unitType":1,"classId":271,"flagsEx":32,"name":"Rogue","skills":[],
            "stats":[{"id":12,"value":85}],
            "containers":{"equipped":{"items":[]}}
          },
          "kills": {"byClass":[{"id":58,"spec":2,"count":3}],"bySuperUnique":[{"id":66,"count":1}]}
        }
        """;

}
