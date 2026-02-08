using D2BotNG.Core.Protos;
using D2BotNG.Data;
using Grpc.Core;

namespace D2BotNG.Services;

public class ItemServiceImpl : ItemService.ItemServiceBase
{
    private readonly ItemRepository _itemRepository;

    public ItemServiceImpl(ItemRepository itemRepository)
    {
        _itemRepository = itemRepository;
    }

    public override Task<ListEntitiesResponse> ListEntities(ListEntitiesRequest request, ServerCallContext context)
    {
        var pathPrefix = string.IsNullOrEmpty(request.PathPrefix) ? null : request.PathPrefix;
        var entities = _itemRepository.GetEntities(pathPrefix);

        var response = new ListEntitiesResponse();
        response.Entities.AddRange(entities);

        return Task.FromResult(response);
    }

    public override Task<SearchItemsResponse> Search(SearchItemsRequest request, ServerCallContext context)
    {
        var entityPath = string.IsNullOrEmpty(request.EntityPath) ? null : request.EntityPath;
        var query = string.IsNullOrEmpty(request.Query) ? null : request.Query;

        // Only pass mode filter if any field is set
        ModeFilter? modeFilter = null;
        if (request.ModeFilter != null &&
            (request.ModeFilter.HasHardcore || request.ModeFilter.HasExpansion || request.ModeFilter.HasLadder))
        {
            modeFilter = request.ModeFilter;
        }

        var items = _itemRepository.Search(entityPath, query, modeFilter);

        var response = new SearchItemsResponse();
        response.Items.AddRange(items);

        return Task.FromResult(response);
    }
}
