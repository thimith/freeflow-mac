using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FreeFlow;

public sealed class FFException : Exception { public FFException(string m) : base(m) { } }

static class Http
{
    public static readonly HttpClient Cloud = new() { Timeout = TimeSpan.FromSeconds(90) };

    public static Uri Url(string baseUrl, string path)
    {
        var b = (baseUrl ?? "").Trim().TrimEnd('/');
        if (!Uri.TryCreate(b + path, UriKind.Absolute, out var u)) throw new FFException($"Invalid URL: {baseUrl}");
        return u;
    }

    public static async Task<string> Send(HttpClient client, HttpRequestMessage req, CancellationToken ct = default)
    {
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode) return body;
        int code = (int)resp.StatusCode;
        if (code == 401) throw new FFException("Invalid API key (401)");
        if (code == 429) throw new FFException("Rate limited (429) — try again in a moment");
        string msg = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
                msg = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() : e.ToString();
        }
        catch { }
        throw new FFException($"HTTP {code}: {(msg.Length > 160 ? msg[..160] : msg)}");
    }
}

public record WhisperModel(string Id, string Name, string Detail)
{
    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{Id}.bin";
    public static readonly WhisperModel[] All =
    {
        new("base", "Base", "142 MB · fastest · OK for clear English"),
        new("small", "Small", "466 MB · good balance · best choice for older PCs"),
        new("large-v3-turbo-q5_0", "Large v3 Turbo (quantized)", "547 MB · best accuracy & multilingual (recommended)"),
        new("large-v3-turbo", "Large v3 Turbo (full)", "1.6 GB · marginally better, more RAM"),
    };
}

/// Runs whisper.cpp's whisper-server.exe (bundled next to FreeFlow.exe) so the model stays loaded.
sealed class LocalWhisper
{
    public static readonly LocalWhisper Shared = new();
    public string Status { get; private set; } = "Idle";
    public event Action StatusChanged;

    const int Port = 8178;
    static readonly HttpClient Local = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(120) };
    readonly object gate = new();
    Process proc;
    string runModel, runLang;
    Task startTask;

    public static string ServerExe => Path.Combine(Paths.WhisperDir, "whisper-server.exe");
    public static string ModelPath(string id) => Path.Combine(Paths.Models, $"ggml-{id}.bin");
    static int Threads => Math.Clamp(Environment.ProcessorCount, 4, 8);

    void SetStatus(string s) { Status = s; StatusChanged?.Invoke(); }

    public Task EnsureServer()
    {
        lock (gate)
        {
            var s = Settings.Current;
            if (startTask != null && !startTask.IsCompleted) return startTask;
            if (proc != null && !proc.HasExited && runModel == s.WhisperModel && runLang == s.Language) return Task.CompletedTask;
            startTask = Launch(s.WhisperModel, s.Language);
            return startTask;
        }
    }

    async Task Launch(string model, string lang)
    {
        Stop();
        var modelPath = ModelPath(model);
        if (!File.Exists(modelPath)) { SetStatus("Model not downloaded"); throw new FFException("Download a Whisper model in Settings → Voice"); }
        if (!File.Exists(ServerExe)) { SetStatus("Engine missing"); throw new FFException("whisper-server.exe is missing — reinstall FreeFlow"); }
        KillOrphans();
        SetStatus($"Loading {model}…");
        var baseArgs = $"-m \"{modelPath}\" --host 127.0.0.1 --port {Port} -l {lang} -t {Threads}";
        try { await Start(baseArgs + " -bs 1 -bo 1 -nf -fa"); }
        catch { await Start(baseArgs); }   // older/newer build without one of the speed flags
        runModel = model;
        runLang = lang;
        SetStatus($"Ready · {model}");
    }

    async Task Start(string args)
    {
        var psi = new ProcessStartInfo(ServerExe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Paths.WhisperDir,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        var p = Process.Start(psi) ?? throw new FFException("Couldn't start whisper-server");
        proc = p;
        for (int i = 0; i < 240; i++)
        {
            if (p.HasExited) { proc = null; SetStatus("Engine crashed — re-download the model?"); throw new FFException("whisper-server exited"); }
            if (await Ping()) return;
            await Task.Delay(250);
        }
        try { p.Kill(); } catch { }
        proc = null;
        throw new FFException("whisper-server didn't start in time");
    }

    static async Task<bool> Ping()
    {
        try
        {
            using var cts = new CancellationTokenSource(500);
            using var r = await Local.GetAsync($"http://127.0.0.1:{Port}/", cts.Token);
            return true;
        }
        catch { return false; }
    }

    static void KillOrphans()
    {
        foreach (var p in Process.GetProcessesByName("whisper-server"))
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }
    }

    public void Stop()
    {
        try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
        proc = null;
        runModel = runLang = null;
        SetStatus("Idle");
    }

    public async Task<string> Transcribe(string wav, string language, string prompt, CancellationToken ct)
    {
        await EnsureServer();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(wav, ct));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0.0"), "temperature");
        if (language != "auto") form.Add(new StringContent(language), "language");
        if (!string.IsNullOrEmpty(prompt)) form.Add(new StringContent(prompt), "prompt");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/inference") { Content = form };
        var body = await Http.Send(Local, req, ct);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("error", out var e)) throw new FFException(e.ToString());
        return doc.RootElement.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
    }
}

/// Downloads ggml models with progress. Call from the UI thread (progress events arrive there).
sealed class ModelDownloader
{
    public static readonly ModelDownloader Shared = new();
    public readonly Dictionary<string, double> Progress = new();
    public string LastError;
    public event Action Changed;
    readonly Dictionary<string, CancellationTokenSource> running = new();
    static readonly HttpClient Dl = new() { Timeout = Timeout.InfiniteTimeSpan };

    public bool IsDownloaded(WhisperModel m) => File.Exists(LocalWhisper.ModelPath(m.Id));

    public async void Download(WhisperModel m)
    {
        if (running.ContainsKey(m.Id)) return;
        var cts = new CancellationTokenSource();
        running[m.Id] = cts;
        LastError = null;
        Progress[m.Id] = 0;
        Changed?.Invoke();
        var dest = LocalWhisper.ModelPath(m.Id);
        var tmp = dest + ".part";
        try
        {
            using var resp = await Dl.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using (var src = await resp.Content.ReadAsStreamAsync(cts.Token))
            await using (var dst = File.Create(tmp))
            {
                var buf = new byte[1 << 20];
                long done = 0;
                int lastPct = -1, n;
                while ((n = await src.ReadAsync(buf, cts.Token)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), cts.Token);
                    done += n;
                    if (total > 0)
                    {
                        int pct = (int)(done * 100 / total);
                        if (pct != lastPct) { lastPct = pct; Progress[m.Id] = pct / 100.0; Changed?.Invoke(); }
                    }
                }
            }
            File.Move(tmp, dest, true);
            if (m.Id == Settings.Current.WhisperModel && Settings.Current.Stt == SttProvider.LocalWhisper)
                _ = LocalWhisper.Shared.EnsureServer().ContinueWith(_ => { });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastError = "Download failed: " + ex.Message; }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            running.Remove(m.Id);
            Progress.Remove(m.Id);
            Changed?.Invoke();
        }
    }

    public void Cancel(string id) { if (running.TryGetValue(id, out var c)) c.Cancel(); }

    public void Delete(WhisperModel m)
    {
        try { File.Delete(LocalWhisper.ModelPath(m.Id)); } catch { }
        Changed?.Invoke();
    }
}

static class Transcriber
{
    public static async Task<string> Transcribe(string wav, CancellationToken ct)
    {
        var s = Settings.Current;
        var prompt = Vocabulary.WhisperPrompt();
        switch (s.Stt)
        {
            case SttProvider.LocalWhisper:
                return await LocalWhisper.Shared.Transcribe(wav, s.Language, prompt, ct);
            case SttProvider.Groq:
                if (string.IsNullOrWhiteSpace(s.GroqKey)) throw new FFException("Add your free Groq API key in Settings → Voice");
                return await OpenAI("https://api.groq.com/openai/v1", s.GroqKey, s.GroqWhisperModel, wav, s.Language, prompt, ct);
            default:
                return await OpenAI(s.CustomBase, s.CustomKey, s.CustomSttModel, wav, s.Language, prompt, ct);
        }
    }

    static async Task<string> OpenAI(string baseUrl, string key, string model, string wav, string lang, string prompt, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(wav, ct));
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "audio.wav");
        form.Add(new StringContent(model), "model");
        if (lang != "auto") form.Add(new StringContent(lang), "language");
        if (!string.IsNullOrEmpty(prompt)) form.Add(new StringContent(prompt), "prompt");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0"), "temperature");
        using var req = new HttpRequestMessage(HttpMethod.Post, Http.Url(baseUrl, "/audio/transcriptions")) { Content = form };
        if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        var body = await Http.Send(Http.Cloud, req, ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var t) ? (t.GetString() ?? "").Trim() : "";
    }
}
