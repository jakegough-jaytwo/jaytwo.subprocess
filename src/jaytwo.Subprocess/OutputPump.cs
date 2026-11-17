using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace jaytwo.Subprocess;

/// <summary>
/// Reads a stream to end-of-file on a background task, appending decoded chunks verbatim so no text is reconstructed.
/// The text collected so far is available at any time, which is what lets a bounded drain still return partial output.
/// </summary>
internal sealed class OutputPump
{
    private readonly TextReader _reader;
    private readonly int _maxLength;
    private readonly StringBuilder _buffer = new StringBuilder();

    public OutputPump(TextReader reader, int maxLength = 0)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _maxLength = maxLength;
    }

    public Task Completion { get; private set; } = Task.CompletedTask;

    public bool Failed { get; private set; }

    public void Start() => Completion = Task.Run(RunAsync);

    public string GetText()
    {
        lock (_buffer)
        {
            return _buffer.ToString();
        }
    }

    private async Task RunAsync()
    {
        var chunk = new char[4096];

        try
        {
            int read;
            while ((read = await _reader.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
            {
                lock (_buffer)
                {
                    _buffer.Append(chunk, 0, read);

                    if (_maxLength > 0 && _buffer.Length > _maxLength)
                    {
                        _buffer.Remove(0, _buffer.Length - _maxLength);
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // reader closed because the drain gave up or the owner was disposed
        }
        catch (IOException)
        {
        }
        catch (Exception)
        {
            Failed = true;
        }
    }
}
