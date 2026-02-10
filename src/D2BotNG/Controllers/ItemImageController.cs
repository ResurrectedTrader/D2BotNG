using D2BotNG.Core.Protos;
using D2BotNG.Rendering;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;

namespace D2BotNG.Controllers;

/// <summary>
/// HTTP endpoints for item image rendering
/// </summary>
[ApiController]
[Route("api/items/images")]
public class ItemImageController : ControllerBase
{
    private readonly ILogger<ItemImageController> _logger;
    private readonly ItemRenderer _itemRenderer;

    public ItemImageController(ILogger<ItemImageController> logger, ItemRenderer itemRenderer)
    {
        _logger = logger;
        _itemRenderer = itemRenderer;
    }

    /// <summary>
    /// Render a single item image by code and color
    /// </summary>
    [HttpGet("{code}")]
    public IActionResult GetImage(string code, [FromQuery] int color = -1)
    {
        try
        {
            var item = new Item
            {
                Code = code,
                ItemColor = (uint)(color < 0 ? 0 : color),
                Description = ""
            };

            var pngData = _itemRenderer.RenderItem(item);
            return File(pngData, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render image for code {Code}", code);
            return NotFound();
        }
    }

    /// <summary>
    /// Render an item with sockets from POST body
    /// </summary>
    [HttpPost("render")]
    public IActionResult RenderItem([FromBody] ItemRenderRequest request)
    {
        try
        {
            var item = ConvertRequestToItem(request);
            var pngData = _itemRenderer.RenderItemWithSockets(item);
            return File(pngData, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render item");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Render a full item tooltip from POST body
    /// </summary>
    [HttpPost("tooltip")]
    public IActionResult RenderTooltip([FromBody] ItemRenderRequest request)
    {
        try
        {
            var item = ConvertRequestToItem(request);
            var pngData = _itemRenderer.RenderItemTooltip(item);
            return File(pngData, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render tooltip");
            return BadRequest(new { error = ex.Message });
        }
    }

    private static Item ConvertRequestToItem(ItemRenderRequest request)
    {
        var item = new Item
        {
            Code = request.Code,
            Name = request.Name ?? "",
            Description = request.Description ?? "",
            ItemColor = (uint)request.Color,
            TextColor = (uint)request.TextColor
        };

        if (request.Sockets != null)
        {
            foreach (var socket in request.Sockets)
            {
                item.Sockets.Add(new Item
                {
                    Code = socket.Code,
                    ItemColor = (uint)socket.Color,
                    Description = ""
                });
            }
        }

        return item;
    }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ItemRenderRequest
{
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int Color { get; set; }
    public int TextColor { get; set; }
    public List<SocketInfo>? Sockets { get; set; }
}

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class SocketInfo
{
    public string Code { get; set; } = "";
    public int Color { get; set; }
}
