using System.Reflection;
using NEngineFormat.Core.Data;


namespace NEngineFormat.Core.Utils;

/// <summary>
/// Maps block ids to the classes that implement them, and builds a block from an id and its bytes.
/// </summary>
/// <remarks>
/// Block types in this assembly are picked up automatically from their
/// <see cref="BlockIdAttribute"/>. A format living in another assembly registers its own with
/// <see cref="RegisterAssembly"/> or <see cref="Register{T}"/>.
/// </remarks>
public static class BlockRegistry
{
#if NET9_0_OR_GREATER
    private static readonly Lock Gate = new();
#else
    // System.Threading.Lock arrived in .NET 9, and this library is also compiled into a plugin
    // for a host that carries .NET 7.
    private static readonly object Gate = new();
#endif
    private static readonly Dictionary<uint, Registration> ById = [];
    private static readonly Dictionary<Type, uint> IdByType = [];

    static BlockRegistry() => RegisterAssembly(typeof(BlockRegistry).Assembly);

    /// <summary>
    /// Builds the block registered for <paramref name="id"/> and lets it read its own payload.
    /// </summary>
    /// <param name="id">The block id, as stored in the file.</param>
    /// <param name="data">The block's payload bytes.</param>
    /// <returns>
    /// An instance of the class registered for <paramref name="id"/>, or a plain
    /// <see cref="BlockBase"/> when the id is unknown. Either way <see cref="BlockBase.Type"/>,
    /// <see cref="BlockBase.Size"/> and <see cref="BlockBase.Data"/> carry what was passed in,
    /// so an unrecognised block still survives a round-trip intact.
    /// </returns>
    public static BlockBase Create(uint id, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        BlockBase block = Instantiate(id);
        block.Type = id;
        block.Size = (uint)data.Length;
        block.Data = data;
        block.Read(new BitPackage(data));
        return block;
    }

    /// <summary>
    /// Builds a block and returns it already typed, for when the caller knows what to expect.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The id resolves to a different block class than <typeparamref name="T"/>.
    /// </exception>
    public static T Create<T>(uint id, byte[] data) where T : BlockBase
    {
        BlockBase block = Create(id, data);
        if (block is not T typed)
        {
            throw new InvalidOperationException(
                $"Block id {id} maps to {block.GetType().Name}, not {typeof(T).Name}.");
        }

        return typed;
    }

    /// <summary>Registers every <see cref="BlockIdAttribute"/>-marked block type in an assembly.</summary>
    public static void RegisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (Type type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<BlockIdAttribute>() is { } attribute && !type.IsAbstract)
            {
                Register(attribute.Id, type);
            }
        }
    }

    /// <summary>Registers a block type under the id from its <see cref="BlockIdAttribute"/>.</summary>
    public static void Register<T>() where T : BlockBase
    {
        BlockIdAttribute attribute = typeof(T).GetCustomAttribute<BlockIdAttribute>()
            ?? throw new InvalidOperationException($"{typeof(T).Name} has no [BlockId]; pass an id explicitly.");

        Register(attribute.Id, typeof(T));
    }

    /// <summary>Registers a block type under an explicit id.</summary>
    public static void Register<T>(uint id) where T : BlockBase => Register(id, typeof(T));

    /// <summary>The class registered for an id, or <see langword="null"/> when it is unknown.</summary>
    public static Type? GetBlockType(uint id)
    {
        lock (Gate)
        {
            return ById.TryGetValue(id, out Registration registration) ? registration.Type : null;
        }
    }

    /// <summary>The id a block class is registered under.</summary>
    /// <exception cref="InvalidOperationException">The class is not registered.</exception>
    public static uint GetId<T>() where T : BlockBase
    {
        lock (Gate)
        {
            return IdByType.TryGetValue(typeof(T), out uint id)
                ? id
                : throw new InvalidOperationException($"{typeof(T).Name} is not registered.");
        }
    }

    /// <summary>Whether any block class is registered for an id.</summary>
    public static bool IsRegistered(uint id)
    {
        lock (Gate)
        {
            return ById.ContainsKey(id);
        }
    }

    /// <summary>Every registered id, ascending.</summary>
    public static IReadOnlyList<uint> RegisteredIds()
    {
        lock (Gate)
        {
            return [.. ById.Keys.Order()];
        }
    }

    private static void Register(uint id, Type type)
    {
        if (!typeof(BlockBase).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"{type.Name} does not derive from {nameof(BlockBase)}.");
        }

        // The factory constructs the block before its payload is known, so a parameterless
        // constructor is required. Caught here, at registration, rather than on the first
        // file that happens to contain this block.
        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"{type.Name} needs a public parameterless constructor to be created from an id.");
        }

        lock (Gate)
        {
            if (ById.TryGetValue(id, out Registration existing))
            {
                if (existing.Type == type)
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Block id {id} is already registered to {existing.Type.Name}; {type.Name} cannot claim it too.");
            }

            // The map runs both ways, so one class means one id. Allowing a second would
            // leave GetId picking arbitrarily between them.
            if (IdByType.TryGetValue(type, out uint takenId))
            {
                throw new InvalidOperationException(
                    $"{type.Name} is already registered under id {takenId}; it cannot also use {id}.");
            }

            ById[id] = new Registration(type);
            IdByType[type] = id;
        }
    }

    private static BlockBase Instantiate(uint id)
    {
        Registration registration;
        lock (Gate)
        {
            if (!ById.TryGetValue(id, out registration))
            {
                // An unknown id is not an error here: the bytes are kept as-is so a tool that
                // does not know this block cannot silently drop it.
                return new BlockBase();
            }
        }

        return (BlockBase)Activator.CreateInstance(registration.Type)!;
    }

    private readonly struct Registration(Type type)
    {
        public Type Type { get; } = type;
    }
}
