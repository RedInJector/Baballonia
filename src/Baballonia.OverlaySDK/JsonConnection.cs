using System.Text;

namespace OverlaySDK;

public class JsonConnection : IConnection
{
    private StringBuilder _buffer = new StringBuilder();
    private int _lastScannedIndex = 0;
    private readonly IConnection? _base;

    public JsonConnection(IConnection @base)
    {
        _base = @base;
    }


    public void Send(string data, TimeSpan timeout)
    {
        _base?.Send(data, timeout);
    }

    public string Receive(TimeSpan timeout)
    {
        var startTime = DateTime.Now;
        while (true)
        {
            if (DateTime.Now - startTime > timeout)
                throw new TimeoutException("Timeout reached");

            string content = _buffer.ToString();

            int start = -1;
            int braceDepth = 0;

            for (int i = _lastScannedIndex; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    if (braceDepth == 0)
                        start = i;
                    braceDepth++;
                }
                else if (content[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0 && start != -1)
                    {
                        int lenghh = i - start + 1;
                        string candidatestr = content.Substring(start, lenghh);

                        _buffer.Remove(0, i + 1);
                        _lastScannedIndex = 0;
                        return candidatestr;
                    }
                }
            }

            _lastScannedIndex = Math.Max(0, content.Length - 1);

            // Only read if buffer was processed and still no JSON
            string line = _base.Receive(timeout);
            if (!string.IsNullOrWhiteSpace(line))
            {
                _buffer.Append(line);
                _lastScannedIndex = Math.Max(0, _buffer.Length - line.Length);
            }
        }
    }


    public Task SendAsync(string data)
    {
        if (_base != null) return _base.SendAsync(data);

        return Task.CompletedTask;
    }

    public async Task<string> ReceiveAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string content = _buffer.ToString();

            int start = -1;
            int braceDepth = 0;

            for (int i = _lastScannedIndex; i < content.Length; i++)
            {
                if (content[i] == '{')
                {
                    if (braceDepth == 0)
                        start = i;
                    braceDepth++;
                }
                else if (content[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0 && start != -1)
                    {
                        int lenghh = i - start + 1;
                        string candidatestr = content.Substring(start, lenghh);

                        _buffer.Remove(0, i + 1);
                        _lastScannedIndex = 0;
                        return candidatestr;
                    }
                }
            }

            _lastScannedIndex = Math.Max(0, content.Length - 1);

            // Only read if buffer was processed and still no JSON
            string blob = await _base.ReceiveAsync(token);
            if (!string.IsNullOrWhiteSpace(blob))
            {
                _buffer.Append(blob);
                _lastScannedIndex = Math.Max(0, _buffer.Length - blob.Length);
            }
        }
        throw new OperationCanceledException();
    }

    public void Terminate()
    {
        _base.Terminate();
    }
}
