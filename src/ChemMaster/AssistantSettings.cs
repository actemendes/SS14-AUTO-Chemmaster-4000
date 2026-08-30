using System;
using System.IO;
using System.Text.Json;

internal sealed class AssistantSettings
{
    public int SchemaVersion { get; set; } = 1;
    public int SnapshotTimeoutMilliseconds { get; set; } = 20000;
    public int MaximumSnapshotAgeMilliseconds { get; set; } = 5000;
    public int StateChangeTimeoutMilliseconds { get; set; } = 15000;
    public int StableScrollTimeoutMilliseconds { get; set; } = 12000;
    public int PollIntervalMilliseconds { get; set; } = 125;
    public int MaximumActions { get; set; } = 10000;
    public int ExpectedTransferMode { get; set; } = 0;
    public bool ActivateGameOnStart { get; set; } = true;
    public bool TurboMode { get; set; }
    public bool TwoPhaseHotBeaker { get; set; } = true;
    public string EmergencyHotkey { get; set; } = "F12";
    public string LogDirectory { get; set; } = "logs";

    public static AssistantSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AssistantSettings();
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidDataException("settings.json слишком велик.");
        var settings = JsonSerializer.Deserialize<AssistantSettings>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException("settings.json пуст или повреждён.");
        settings.Validate();
        return settings;
    }

    public void Save(string path)
    {
        Validate();
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        File.Move(temporary, path, overwrite: true);
    }

    public void Validate()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("Неподдерживаемая версия settings.json.");
        if (SnapshotTimeoutMilliseconds is < 1000 or > 120000 ||
            StateChangeTimeoutMilliseconds is < 1000 or > 120000 ||
            StableScrollTimeoutMilliseconds is < 1000 or > 120000 ||
            PollIntervalMilliseconds is < 25 or > 5000)
            throw new InvalidDataException("Некорректные timeout/poll настройки.");
        if (MaximumSnapshotAgeMilliseconds is < 2000 or > 10000)
            throw new InvalidDataException("maximumSnapshotAgeMilliseconds должен быть в диапазоне 2000..10000.");
        if (MaximumActions is < 1 or > 10000) throw new InvalidDataException("Некорректный лимит действий.");
        if (ExpectedTransferMode != 0) throw new InvalidDataException("Поддерживается только ChemMasterMode.Transfer=0.");
        if (string.IsNullOrWhiteSpace(LogDirectory) || Path.IsPathRooted(LogDirectory) || LogDirectory.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("logDirectory должен быть безопасным относительным путём.");
        if (!EmergencyHotkey.Equals("F12", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("В этой версии безопасная глобальная клавиша фиксирована: F12.");
    }
}
