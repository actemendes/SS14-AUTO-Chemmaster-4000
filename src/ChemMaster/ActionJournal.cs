using System;
using System.IO;
using System.Text;
using System.Text.Json;

internal interface IActionJournal : IDisposable
{
    string Path { get; }
    void Write(string eventName, ChemMasterExecutorState state, object? payload = null);
}

internal sealed class ActionJournal : IActionJournal
{
    private readonly object _sync = new();
    private readonly FileStream _stream;
    private readonly StreamWriter _writer;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; }

    public ActionJournal(string baseDirectory, string relativeDirectory)
    {
        var root = System.IO.Path.GetFullPath(baseDirectory);
        var directory = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relativeDirectory));
        if (!directory.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Папка журнала выходит за пределы каталога приложения.");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, "chemmaster-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
        _stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false));
    }

    public void Write(string eventName, ChemMasterExecutorState state, object? payload = null)
    {
        var row = new
        {
            schemaVersion = 1,
            time = DateTimeOffset.Now,
            eventName,
            state = state.ToString(),
            payload,
        };
        var line = JsonSerializer.Serialize(row, _json);
        lock (_sync)
        {
            _writer.WriteLine(line);
            _writer.Flush();
            _stream.Flush(flushToDisk: true);
        }
    }

    public void Dispose()
    {
        lock (_sync) _writer.Dispose();
    }
}
