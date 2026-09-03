using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mew.Workbench;

/// <summary>
/// Persists the dock layout as JSON under the per-user Mew application data folder.
/// </summary>
internal sealed class WorkbenchLayoutStore
{
    public WorkbenchLayoutStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        FilePath = Path.Combine(appData, "Mew", "layout.json");
    }

    public string FilePath { get; }
    private string PresentationFilePath => Path.Combine(Path.GetDirectoryName(FilePath)!, "presentation.json");

    public string? TryLoad()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, json);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public WorkbenchPresentationState? TryLoadPresentation()
    {
        try
        {
            return File.Exists(PresentationFilePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(PresentationFilePath), WorkbenchJsonContext.Default.WorkbenchPresentationState)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void SavePresentation(WorkbenchPresentationState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PresentationFilePath)!);
            File.WriteAllText(PresentationFilePath, JsonSerializer.Serialize(state, WorkbenchJsonContext.Default.WorkbenchPresentationState));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// View 菜单控制的区域显隐状态。可空字段用于识别旧 presentation.json 中不存在的新区域字段。
/// </summary>
internal sealed record WorkbenchPresentationState
{
    public string? ActiveActivityId { get; init; }
    public bool? IsActivityBarVisible { get; init; }
    public bool? IsSideBarVisible { get; init; }
    public bool? IsPanelVisible { get; init; }
    public bool? IsStatusBarVisible { get; init; }
}

[JsonSerializable(typeof(WorkbenchPresentationState))]
internal sealed partial class WorkbenchJsonContext : JsonSerializerContext;
