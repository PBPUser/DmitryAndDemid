using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data.Archive;

/// <summary>
/// Converts a stage / spell card (<see cref="FileStageInfo"/> and its chapters, entities and dialogs) to and
/// from JSON, as a human-editable alternative to the packed binary <c>.sid</c> format the game normally loads
/// through <see cref="BitPackage"/>.
///
/// The JSON mirrors the in-memory objects one-to-one: the raw <c>Header</c> / <c>FloatingPoints</c> slots (whose
/// meanings are documented in the sibling <c>.sp</c> schema files) are written out alongside the friendlier
/// broken-out booleans, strings and <see cref="Drop"/> objects. On import the booleans and drops are the source
/// of truth: <see cref="FromJson"/> re-packs them into the <c>Header</c> bit words (via each type's
/// <c>PackFlags</c> / <c>SyncHeader</c>), so an object produced here is byte-for-byte what a binary load would
/// have produced — the runtime reads the same <c>Header</c> either way. That also means a hand author only has
/// to set the meaningful numeric slots (chapter Type, lengths, entity stats) and the booleans; the flag bits
/// and collection counts are derived.
/// </summary>
public static class StageJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,          // the File* types expose public fields, not properties
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // The game's text is largely Cyrillic (spell titles, boss names); emit it raw so the JSON stays
        // human-editable instead of a wall of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Serializes a stage / spell card to indented JSON.</summary>
    public static string ToJson(FileStageInfo info) => JsonSerializer.Serialize(info, Options);

    /// <summary>
    /// Parses a stage / spell card from JSON and normalizes it so its <c>Header</c> words agree with the
    /// broken-out flags, counts and drops — i.e. the result is exactly what a binary load would yield.
    /// </summary>
    public static FileStageInfo FromJson(string json)
    {
        FileStageInfo info = JsonSerializer.Deserialize<FileStageInfo>(json, Options)
                             ?? throw new JsonException("Stage JSON deserialized to null.");
        Normalize(info);
        return info;
    }

    /// <summary>Loads and parses a stage / spell card from a <c>.json</c> file on disk.</summary>
    public static FileStageInfo Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Writes a stage / spell card to a <c>.json</c> file on disk.</summary>
    public static void Save(FileStageInfo info, string path) => File.WriteAllText(path, ToJson(info));

    /// <summary>
    /// Fills in the array slots a JSON author is not expected to keep by hand: null collections become empty,
    /// the header counts are recomputed from the arrays, and every chapter / entity / dialog re-packs its flag
    /// bits from its booleans. Idempotent — re-normalizing an already-consistent stage is a no-op.
    /// </summary>
    private static void Normalize(FileStageInfo info)
    {
        info.Header ??= new int[16];
        info.Scripts ??= [];
        info.Backgrounds ??= [];
        info.Entities ??= [];
        info.Chapters ??= [];

        foreach (FileEntityInfo entity in info.Entities)
        {
            entity.Header ??= new int[16];
            entity.FloatingPoints ??= new float[16];
            entity.PackFlags();
        }

        foreach (FileChapterInfo chapter in info.Chapters)
        {
            chapter.Header ??= new int[8];
            chapter.Dialogs ??= [];
            foreach (FileDialogInfo dialog in chapter.Dialogs)
            {
                dialog.Header ??= new int[4];
                dialog.PackFlags();
            }
            chapter.PackFlags();
        }

        info.SyncHeader();
    }
}
