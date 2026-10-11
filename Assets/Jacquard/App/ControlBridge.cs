#if UNITY_EDITOR || UNITY_STANDALONE
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Jacquard.App {

// The network thread carries text only. All model, UI and transport access
// occurs in Pump on the app's main thread. Opt-in and loopback keep the control
// seam out of ordinary launches, mobile players and the browser.
public sealed class ControlBridge : IDisposable
{
    readonly JacquardApp _app;
    readonly string _token;
    readonly TcpListener _listener;
    readonly ConcurrentQueue<Pending> _queue = new();
    readonly Thread _thread;
    volatile bool _stopping;

    sealed class Pending
    {
        public string Text, Response;
        public readonly object Gate = new();
        public bool Cancelled;
        public DateTime Deadline = DateTime.UtcNow.AddSeconds(5);
    }

    [Serializable] sealed class Request
    {
        public string token, operation, preset;
        public int x, y, x2, y2, channel, note;
        public float value, length;
    }
    [Serializable] sealed class Response
    {
        public bool ok;
        public string message, score;
        public bool playing;
        public int selected, x, y;
        public string[] presets;
    }

    public static ControlBridge TryStart(JacquardApp app)
    {
        var portText = Environment.GetEnvironmentVariable("JACQUARD_CONTROL_PORT");
        if (string.IsNullOrEmpty(portText)) return null;
        var token = Environment.GetEnvironmentVariable("JACQUARD_CONTROL_TOKEN");
        if (!int.TryParse(portText, out var port) || port < 1024 || port > 65535 ||
            string.IsNullOrEmpty(token) || token.Length < 32)
        {
            Debug.LogError("Jacquard control requires a port in 1024..65535 and a token of at least 32 characters.");
            return null;
        }
        try { return new ControlBridge(app, port, token); }
        catch (Exception ex) { Debug.LogError("Jacquard control could not start: " + ex.Message); return null; }
    }

    ControlBridge(JacquardApp app, int port, string token)
    {
        _app = app;
        _token = token;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(8);
        _thread = new Thread(Listen) { IsBackground = true, Name = "Jacquard control" };
        _thread.Start();
    }

    void Listen()
    {
        while (!_stopping)
        {
            try
            {
                using var client = _listener.AcceptTcpClient();
                client.ReceiveTimeout = client.SendTimeout = 6000;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 1024, true);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                var text = new StringBuilder();
                int c;
                while ((c = reader.Read()) != -1 && c != '\n')
                {
                    if (text.Length >= 65536) throw new IOException("Request too large");
                    text.Append((char)c);
                }
                if (c == -1) continue;
                if (_queue.Count >= 32)
                {
                    writer.WriteLine("{\"ok\":false,\"message\":\"Control queue is full\"}");
                    continue;
                }
                var pending = new Pending { Text = text.ToString() };
                _queue.Enqueue(pending);
                lock (pending.Gate)
                {
                    while (pending.Response == null && !_stopping)
                    {
                        var remaining = pending.Deadline - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero) break;
                        Monitor.Wait(pending.Gate, remaining);
                    }
                    if (pending.Response == null) pending.Cancelled = true;
                    writer.WriteLine(pending.Response ?? "{\"ok\":false,\"message\":\"Control timed out; inspect state before retrying\"}");
                }
            }
            catch (Exception) { if (_stopping) return; }
        }
    }

    public void Pump()
    {
        for (var i = 0; i < 8 && _queue.TryDequeue(out var pending); i++)
            lock (pending.Gate)
            {
                if (pending.Cancelled || DateTime.UtcNow > pending.Deadline) continue;
                try { pending.Response = JsonUtility.ToJson(Execute(JsonUtility.FromJson<Request>(pending.Text))); }
                catch (Exception ex) { pending.Response = JsonUtility.ToJson(new Response { message = ex.Message }); }
                Monitor.PulseAll(pending.Gate);
            }
    }

    Response Execute(Request r)
    {
        if (r == null || !SameToken(r.token, _token)) throw new ArgumentException("Unauthorized control request");
        var editor = _app.Editor;
        var view = _app.View;
        if (r.operation != "state" && r.operation != "presets" &&
            r.operation != "play" && r.operation != "stop" && editor.Locked)
            throw new InvalidOperationException("Score is locked while a load is pending");
        switch (r.operation)
        {
            case "state": break;
            case "presets": break;
            case "play": if (!_app.Sequencer.IsPlaying) _app.TogglePlay(); break;
            case "stop": if (_app.Sequencer.IsPlaying) _app.TogglePlay(); break;
            case "tempo":
                if (float.IsNaN(r.value) || float.IsInfinity(r.value) || r.value < 20 || r.value > 300)
                    throw new ArgumentException("Tempo must be in 20..300");
                _app.Project.Tempo = r.value; editor.Commit(); break;
            case "preset":
                if (r.channel < 1 || r.channel > PatchBank.Channels) throw new ArgumentException("Channel must be in 1..8");
                _app.Project.Patches[r.channel] = TimbrePresets.Create(r.preset); editor.Commit(); break;
            case "cursor": SetCursor(r.x, r.y); break;
            case "select":
                CheckPoint(r.x, r.y); CheckPoint(r.x2, r.y2);
                view.SelectRectangle(new GridPoint(r.x, r.y), new GridPoint(r.x2, r.y2)); break;
            case "select_all": view.SelectAll(); break;
            case "clear_selection": view.ClearSelection(); break;
            case "copy": editor.CopySelection(); break;
            case "paste":
                SetCursor(r.x, r.y);
                if (!editor.PasteSelection()) throw new InvalidOperationException("Clipboard is empty or the group does not fit");
                break;
            case "delete": editor.Delete(); break;
            case "fold": view.ToggleStack(); break;
            case "note":
                CheckPoint(r.x, r.y);
                if (r.note < 0 || r.note > 127 || float.IsNaN(r.length) ||
                    float.IsInfinity(r.length) || r.length <= 0 || r.length > 64)
                    throw new ArgumentException("Note must be in 0..127 and length in (0,64]");
                if (!_app.Project.Score.PlaceStack(new GridPoint(r.x, r.y),
                    new[] { new NoteTile { Note = r.note, Length = r.length } }))
                    throw new InvalidOperationException("The cell cannot accept a note");
                editor.Commit(); break;
            case "save":
                _app.Save();
                if (_app.Message.StartsWith("could not save", StringComparison.Ordinal))
                    throw new IOException(_app.Message);
                break;
            default: throw new ArgumentException("Unknown operation");
        }
        return new Response
        {
            ok = true, message = r.operation == "save" ? _app.Message : "OK",
            score = r.operation == "state" ? ProjectFormat.Write(_app.Project) : null,
            presets = r.operation == "presets" ? new System.Collections.Generic.List<string>(TimbrePresets.Names).ToArray() : null,
            playing = _app.Sequencer.IsPlaying, selected = view.Selection.Count,
            x = view.Cursor.X, y = view.Cursor.Y
        };
    }

    static void CheckPoint(int x, int y)
    {
        if (x < 0 || y < 0 || x > 4096 || y > 4096) throw new ArgumentException("Coordinates must be in 0..4096");
    }
    void SetCursor(int x, int y)
    {
        CheckPoint(x, y);
        _app.View.SetCursor(new GridPoint(x, y));
        if (_app.View.Cursor != new GridPoint(x, y)) throw new ArgumentException("Cursor is outside the visible plane");
    }
    static bool SameToken(string a, string b)
    {
        if (a == null) return false;
        var mismatch = a.Length ^ b.Length;
        for (var i = 0; i < b.Length; i++) mismatch |= b[i] ^ (i < a.Length ? a[i] : 0);
        return mismatch == 0;
    }
    public void Dispose()
    {
        _stopping = true;
        _listener.Stop();
        while (_queue.TryDequeue(out var pending))
            lock (pending.Gate) { pending.Cancelled = true; Monitor.PulseAll(pending.Gate); }
        _thread.Join(100);
    }
}

} // namespace Jacquard.App
#endif
