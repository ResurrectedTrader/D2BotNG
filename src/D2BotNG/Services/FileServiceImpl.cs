using D2BotNG.Core.Protos;
using Grpc.Core;

namespace D2BotNG.Services;

public class FileServiceImpl : FileService.FileServiceBase
{
    public override Task<DirectoryListing> ListDirectory(ListDirectoryRequest request, ServerCallContext context)
    {
        var path = request.Path;

        // Default to drives root on Windows
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult(ListDrives());
        }

        // Validate path exists
        if (!Directory.Exists(path))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Directory not found: {path}"));
        }

        var result = new DirectoryListing
        {
            CurrentPath = Path.GetFullPath(path)
        };

        try
        {
            // List directories first
            foreach (var dir in Directory.GetDirectories(path))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    result.Entries.Add(new DirectoryEntry
                    {
                        Name = dirInfo.Name,
                        IsDirectory = true
                    });
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip directories we can't access
                }
            }

            // List all files
            foreach (var file in Directory.GetFiles(path))
            {
                result.Entries.Add(new DirectoryEntry
                {
                    Name = Path.GetFileName(file),
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Access denied: {path}"));
        }

        return Task.FromResult(result);
    }

    private static DirectoryListing ListDrives()
    {
        var result = new DirectoryListing
        {
            CurrentPath = ""
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                result.Entries.Add(new DirectoryEntry
                {
                    Name = drive.Name.TrimEnd('\\'),
                    IsDirectory = true
                });
            }
        }

        return result;
    }
}
