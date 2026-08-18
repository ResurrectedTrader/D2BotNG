using D2BotNG.Core.Protos;

namespace D2BotNG.Data;

/// <summary>
/// Flat, file-backed collection of proxies (proxies.json). Unlike keys there is no
/// rotation, in-use exclusivity, or held state — a profile references one proxy by its
/// address, which also serves as the proxy's identity.
/// </summary>
public class ProxyRepository : FileRepository<Proxy, ProxyCollection>
{
    public ProxyRepository(Paths paths, DataWriteGate writeGate) : base(paths, writeGate, "proxies.json") { }

    protected override string GetKey(Proxy p) => p.Address;

    protected override IList<Proxy> GetItems(ProxyCollection list) => list.Proxies;

    protected override ProxyCollection CreateList(IEnumerable<Proxy> items)
    {
        // Persist sorted by address so the on-disk file and every snapshot are ordered.
        var list = new ProxyCollection();
        list.Proxies.AddRange(items.OrderBy(p => p.Address, StringComparer.OrdinalIgnoreCase));
        return list;
    }
}
