using System.Text;

namespace Denon.Serial.Test;

/// <summary>
/// Reads command lines from the console with a Linux-shell style history: Up/Down step through
/// earlier commands, Left/Right/Home/End/Backspace/Delete edit the line (Ctrl+A/Ctrl+E/Ctrl+U as
/// in bash), Esc clears it. The last commands are kept in a history file so they survive restarts.
/// When input or output is redirected (piped, scripted), it falls back to Console.ReadLine.
/// Uses only System.Console; no external libraries.
/// </summary>
public sealed class CommandLineReader
{
    private readonly List<string> _history = new();
    private readonly int _maxHistory;
    private readonly string? _historyFile;
    private readonly HashSet<string> _excluded;
    private readonly object _consoleLock = new();

    // The line being edited (shared with WriteAbove, which can run on another thread).
    private readonly StringBuilder _buffer = new();
    private string _prompt = "";
    private bool _reading;
    private int _cursor;          // position in _buffer
    private int _startLeft;       // console position where the input starts (just after the prompt)
    private int _startTop;
    private int _drawnLength;     // characters currently drawn, so a shorter line can blank the rest

    /// <param name="historyFile">File to load and save history in; null keeps it in memory only.</param>
    /// <param name="maxHistory">How many commands to keep.</param>
    /// <param name="excludeFromHistory">Lines never recorded, e.g. "q" to quit (case-insensitive).</param>
    public CommandLineReader(string? historyFile, int maxHistory = 100, IEnumerable<string>? excludeFromHistory = null)
    {
        _historyFile = historyFile;
        _maxHistory = maxHistory;
        _excluded = new HashSet<string>(excludeFromHistory ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        try
        {
            if (_historyFile is not null && File.Exists(_historyFile))
            {
                _history.AddRange(File.ReadAllLines(_historyFile).Where(l => l.Length > 0 && !_excluded.Contains(l.Trim())).TakeLast(_maxHistory));
            }
        }
        catch (Exception)
        {
            // An unreadable history file just means starting with an empty history.
        }
    }

    /// <summary>A history file in the user's home folder, e.g. ~/.denon.serial.test_history.</summary>
    public static string DefaultHistoryFile(string appName) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".{appName}_history");

    public IReadOnlyList<string> History => _history;

    /// <summary>Shows the prompt and returns the line typed (null at end of redirected input).</summary>
    public string? ReadLine(string prompt)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Write(prompt);
            string? piped = Console.ReadLine();
            if (piped is not null) AddToHistory(piped);
            return piped;
        }

        lock (_consoleLock)
        {
            _prompt = prompt;
            _buffer.Clear();
            _cursor = 0;
            _drawnLength = 0;
            Console.Write(prompt);
            _startLeft = Console.CursorLeft;
            _startTop = Console.CursorTop;
            _reading = true;
        }

        int historyIndex = _history.Count;   // _history.Count = the new line being typed
        string draft = "";                   // what was typed before stepping into the history

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

            lock (_consoleLock)
            {
                if (ctrl && key.Key == ConsoleKey.A) { MoveCursorTo(0); continue; }
                if (ctrl && key.Key == ConsoleKey.E) { MoveCursorTo(_buffer.Length); continue; }
                if (ctrl && key.Key == ConsoleKey.U) { SetBuffer(""); historyIndex = _history.Count; continue; }

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        MoveCursorTo(_buffer.Length);
                        Console.WriteLine();
                        _reading = false;
                        string line = _buffer.ToString();
                        AddToHistory(line);
                        return line;

                    case ConsoleKey.UpArrow:
                        if (historyIndex > 0)
                        {
                            if (historyIndex == _history.Count) draft = _buffer.ToString();
                            historyIndex--;
                            SetBuffer(_history[historyIndex]);
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (historyIndex < _history.Count)
                        {
                            historyIndex++;
                            SetBuffer(historyIndex == _history.Count ? draft : _history[historyIndex]);
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (_cursor > 0) MoveCursorTo(_cursor - 1);
                        break;

                    case ConsoleKey.RightArrow:
                        if (_cursor < _buffer.Length) MoveCursorTo(_cursor + 1);
                        break;

                    case ConsoleKey.Home:
                        MoveCursorTo(0);
                        break;

                    case ConsoleKey.End:
                        MoveCursorTo(_buffer.Length);
                        break;

                    case ConsoleKey.Backspace:
                        if (_cursor > 0)
                        {
                            _buffer.Remove(_cursor - 1, 1);
                            _cursor--;
                            Redraw();
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (_cursor < _buffer.Length)
                        {
                            _buffer.Remove(_cursor, 1);
                            Redraw();
                        }
                        break;

                    case ConsoleKey.Escape:
                        SetBuffer("");
                        historyIndex = _history.Count;
                        break;

                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            _buffer.Insert(_cursor, key.KeyChar);
                            _cursor++;
                            Redraw();
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Prints text (e.g. a message from the receiver) above the line being typed, then redraws the
    /// prompt and the partly typed command below it. Safe to call from another thread.
    /// </summary>
    public void WriteAbove(string text)
    {
        lock (_consoleLock)
        {
            if (!_reading || Console.IsOutputRedirected)
            {
                Console.WriteLine(text);
                return;
            }

            // Blank the prompt line(s), print the text there, then start the prompt again below it.
            Console.SetCursorPosition(0, _startTop);
            Console.Write(new string(' ', _startLeft + _drawnLength));
            Console.SetCursorPosition(0, _startTop);
            Console.WriteLine(text);
            Console.Write(_prompt);
            _startLeft = Console.CursorLeft;
            _startTop = Console.CursorTop;
            _drawnLength = 0;
            Redraw();
        }
    }

    private void AddToHistory(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (_excluded.Contains(line.Trim())) return;
        if (_history.Count > 0 && _history[^1] == line) return;   // no immediate repeats

        _history.Add(line);
        if (_history.Count > _maxHistory) _history.RemoveRange(0, _history.Count - _maxHistory);

        try
        {
            if (_historyFile is not null) File.WriteAllLines(_historyFile, _history);
        }
        catch (Exception)
        {
            // Not being able to save history isn't worth interrupting the session for.
        }
    }

    private void SetBuffer(string text)
    {
        _buffer.Clear().Append(text);
        _cursor = text.Length;
        Redraw();
    }

    // Rewrites the input after the prompt, blanking anything left over from a longer line.
    private void Redraw()
    {
        int width = Math.Max(1, Console.BufferWidth);
        string text = _buffer.ToString();
        int pad = Math.Max(0, _drawnLength - text.Length);

        Console.SetCursorPosition(_startLeft, _startTop);
        Console.Write(text + new string(' ', pad));

        // Writing past the bottom of the window scrolls it; move the start row up to match.
        int expectedTop = _startTop + (_startLeft + text.Length + pad) / width;
        if (Console.CursorTop < expectedTop) _startTop -= expectedTop - Console.CursorTop;

        _drawnLength = text.Length;
        MoveCursorTo(_cursor);
    }

    private void MoveCursorTo(int index)
    {
        int width = Math.Max(1, Console.BufferWidth);
        _cursor = index;
        int position = _startLeft + index;
        Console.SetCursorPosition(position % width, _startTop + position / width);
    }
}
