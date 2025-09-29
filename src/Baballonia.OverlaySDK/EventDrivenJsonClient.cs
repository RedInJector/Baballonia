using System.Text;
using System.Text.Json;

namespace OverlaySDK;

public class EventDrivenJsonClient :IEventDrivenConnection<object, JsonDocument>
{
    private IEventDrivenConnection<string, string> _client;
    private StringBuilder _buffer = new StringBuilder();

    private int _braceDepth = 0;
    private bool _inString = false;
    private bool _escape = false;

    public bool Connected() => _client?.Connected() ?? false;

    public event Action<JsonDocument>? DataReceived;
    public event Action? OnDisconnect;

    public EventDrivenJsonClient(EventDrivenTcpClient client)
    {
        _client = client;
        _client.DataReceived += ProcessData;
        _client.OnDisconnect += () => OnDisconnect?.Invoke();
    }

    private void ProcessData(string chunk)
    {
        foreach (char c in chunk)
        {
            _buffer.Append(c);
            if (_inString)
            {
                if (_escape)
                {
                    _escape = false;
                }
                else if (c == '\\')
                {
                    _escape = true;
                }
                else if (c == '"')
                {
                    _inString = false;
                }
            }
            else
            {
                if (c == '"')
                {
                    _inString = true;
                }
                else if (c == '{')
                {
                    _braceDepth++;
                }
                else if (c == '}')
                {
                    _braceDepth--;
                    if (_braceDepth == 0)
                    {
                        // Possible complete JSON
                        TryDispatchJson();
                        _buffer.Clear();
                    }
                }
            }
        }
    }
    private void TryDispatchJson()
    {
        try
        {
            string json = _buffer.ToString().Trim();
            if (json.Length > 0)
            {
                var doc = JsonDocument.Parse(json);
                DataReceived?.Invoke(doc);
            }
        }
        catch (JsonException)
        {
            // Incomplete or malformed JSON, ignore
        }
    }

    public void Send(object obj)
    {
        string json = JsonSerializer.Serialize(obj);
        _client.Send(json);
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
