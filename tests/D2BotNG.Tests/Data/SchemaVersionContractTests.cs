using D2BotNG.Data;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace D2BotNG.Tests.Data;

/// <summary>
/// FileRepository stamps schema_version centrally, by looking the field up on the
/// container message. That lookup can only fail at runtime, and only for a repository
/// that has already opted into versioning — i.e. during someone's migration. This moves
/// the failure to build time: every container a repository is built over must carry the
/// field, so any repository can be versioned later without touching a proto.
/// </summary>
public class SchemaVersionContractTests
{
    private const string SchemaVersionField = "schema_version";

    /// <summary>
    /// Every concrete repository paired with the container message it stores — the second
    /// type argument of the FileRepository&lt;,&gt; it ultimately derives from.
    /// </summary>
    public static TheoryData<string, Type> RepositoryContainers()
    {
        var data = new TheoryData<string, Type>();
        foreach (var type in typeof(FileRepository<,>).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition) continue;

            for (var baseType = type.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (!baseType.IsGenericType
                    || baseType.GetGenericTypeDefinition() != typeof(FileRepository<,>))
                {
                    continue;
                }

                data.Add(type.Name, baseType.GetGenericArguments()[1]);
                break;
            }
        }

        return data;
    }

    [Fact]
    public void RepositoriesAreDiscovered()
    {
        // Guards the reflection above: a bad type walk would yield no cases, and the
        // theory would then pass without asserting anything.
        Assert.NotEmpty(RepositoryContainers());
    }

    [Theory]
    [MemberData(nameof(RepositoryContainers))]
    public void ContainerDeclaresSingularInt32SchemaVersion(string repositoryName, Type containerType)
    {
        var descriptor = ((IMessage)Activator.CreateInstance(containerType)!).Descriptor;
        var field = descriptor.FindFieldByName(SchemaVersionField);
        var found = field == null
            ? "no such field"
            : $"{(field.IsRepeated ? "repeated " : "")}{field.FieldType}";

        Assert.True(
            field is { IsRepeated: false, FieldType: FieldType.Int32 },
            $"{containerType.Name} (stored by {repositoryName}) must declare "
            + $"`int32 {SchemaVersionField}` but has {found}. Add it to the container "
            + "message, otherwise this repository can never opt into a schema migration.");
    }
}
