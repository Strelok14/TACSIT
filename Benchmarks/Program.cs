using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

try
{
var argsList = Environment.GetCommandLineArgs().Skip(1).ToArray();
var cliMode = argsList.Length > 0 ? argsList[0] : null;
var cliBaseUrl = argsList.Length > 1 ? argsList[1] : null;

var baseUrl = cliBaseUrl ?? Environment.GetEnvironmentVariable("TACID_BENCH_URL") ?? "http://localhost:5000";
var login = Environment.GetEnvironmentVariable("TACID_BENCH_LOGIN") ?? "admin";
var password = Environment.GetEnvironmentVariable("TACID_BENCH_PASSWORD") ?? "admin123";
var keyBase64 = Environment.GetEnvironmentVariable("TACID_BENCH_KEY_B64") ?? "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
var keyBytes = Convert.FromBase64String(keyBase64);
var runSeed = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000);
var beaconBase = 100000 + runSeed;
var mode = (cliMode ?? Environment.GetEnvironmentVariable("TACID_BENCH_MODE") ?? "all").ToLowerInvariant();

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var tracePath = Path.Combine(AppContext.BaseDirectory, "artifacts", "benchmark_trace.log");
Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
await File.AppendAllTextAsync(tracePath, $"START mode={mode} baseUrl={baseUrl} utc={DateTime.UtcNow:O}{Environment.NewLine}");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

var token = await LoginAsync(http, login, password, options);
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

await EnsureAnchorsAsync(http, options);

var result = new BenchmarkResult
{
    TimestampUtc = DateTime.UtcNow,
    BaseUrl = baseUrl
};

if (mode is "all" or "performance")
{
    var perfProfiles = new[]
    {
        new PerfProfile("P03", 3, 10, 6),
        new PerfProfile("P05", 5, 10, 6)
    };

    foreach (var p in perfProfiles)
    {
        try
        {
            await File.AppendAllTextAsync(tracePath, $"PROFILE_START {p.Name} utc={DateTime.UtcNow:O}{Environment.NewLine}");
            var profileResult = await RunPerformanceProfileAsync(http, p, keyBytes, options, beaconBase + p.Beacons * 1000);
            result.Performance.Add(profileResult);
            await File.AppendAllTextAsync(tracePath, $"PROFILE_DONE {p.Name} ok={profileResult.Success} fail={profileResult.Fail} utc={DateTime.UtcNow:O}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            await File.AppendAllTextAsync(tracePath, $"PROFILE_FAIL {p.Name} ex={ex.GetType().Name} msg={ex.Message} utc={DateTime.UtcNow:O}{Environment.NewLine}");
            result.Performance.Add(new PerformanceResult
            {
                Profile = p.Name,
                Beacons = p.Beacons,
                FrequencyHz = p.FrequencyHz,
                DurationSec = p.DurationSec,
                RequestsTotal = 0,
                Success = 0,
                Fail = 0,
                ThroughputRps = 0,
                AvgMs = 0,
                P50Ms = 0,
                P95Ms = 0,
                P99Ms = 0,
                Error = ex.GetType().Name
            });
        }
    }
}

if (mode is "all" or "accuracy")
{
    result.Accuracy.Add(await RunAccuracyScenarioAsync(http, "LOS_STATIC", beaconBase + 1, keyBytes, options, moving: false, nlosLike: false));
    result.Accuracy.Add(await RunAccuracyScenarioAsync(http, "LOS_MOVING", beaconBase + 2, keyBytes, options, moving: true, nlosLike: false));
    result.Accuracy.Add(await RunAccuracyScenarioAsync(http, "NLOS_STATIC_SYN", beaconBase + 3, keyBytes, options, moving: false, nlosLike: true));
    result.Accuracy.Add(await RunAccuracyScenarioAsync(http, "NLOS_MOVING_SYN", beaconBase + 4, keyBytes, options, moving: true, nlosLike: true));
}

if (mode is "all" or "security")
{
    result.Security = await RunSecurityChecksAsync(http, beaconBase + 100, keyBytes, options);
    result.MixedTest = await RunMixedTestAsync(http, beaconBase + 200, keyBytes, options);
}

if (mode is "all" or "recovery")
{
    result.Recovery = await RunRecoveryTestAsync(http, beaconBase + 300, keyBytes, options);
}

var outDir = Path.Combine(AppContext.BaseDirectory, "artifacts");
Directory.CreateDirectory(outDir);

var suffix = mode switch
{
    "accuracy" => "accuracy",
    "performance" => "performance",
    "security" => "security",
    "recovery" => "recovery",
    _ => "all"
};

var jsonPath = Path.Combine(outDir, $"benchmark_results_{suffix}.json");
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(result, options));

var perfCsv = Path.Combine(outDir, $"performance_{suffix}.csv");
var perfLines = new List<string> { "Profile,Beacons,FrequencyHz,DurationSec,RequestsTotal,Success,Fail,ThroughputRps,P50ms,P95ms,P99ms,AvgMs,Error" };
perfLines.AddRange(result.Performance.Select(x =>
    $"{x.Profile},{x.Beacons},{x.FrequencyHz},{x.DurationSec},{x.RequestsTotal},{x.Success},{x.Fail},{x.ThroughputRps:F2},{x.P50Ms:F2},{x.P95Ms:F2},{x.P99Ms:F2},{x.AvgMs:F2},{x.Error}"));
await File.WriteAllLinesAsync(perfCsv, perfLines);

var accCsv = Path.Combine(outDir, $"accuracy_{suffix}.csv");
var accLines = new List<string> { "Scenario,Samples,RMSE,MAE,MaxError" };
accLines.AddRange(result.Accuracy.Select(x =>
    $"{x.Scenario},{x.Samples},{x.Rmse:F3},{x.Mae:F3},{x.MaxError:F3}"));
await File.WriteAllLinesAsync(accCsv, accLines);

if (result.MixedTest != null)
{
    var mixedCsv = Path.Combine(outDir, $"mixed_{suffix}.csv");
    var mixedLines = new List<string> { "LegitTotal,LegitBlocked429,FalsePositiveRatePercent,FloodTotal,FloodBlocked429,FloodBlockRatePercent" };
    var mt = result.MixedTest;
    mixedLines.Add($"{mt.LegitTotal},{mt.LegitBlocked429},{mt.FalsePositiveRatePercent:F2},{mt.FloodTotal},{mt.FloodBlocked429},{mt.FloodBlockRatePercent:F2}");
    await File.WriteAllLinesAsync(mixedCsv, mixedLines);
    Console.WriteLine($"MIXED CSV: {mixedCsv}");
}

if (result.Recovery != null)
{
    var recCsv = Path.Combine(outDir, $"recovery_{suffix}.csv");
    var recLines = new List<string> { "BaselineP95Ms,BaselineAvgMs,BaselineSamples,TRecover200Sec,TRecoverP95Sec,TotalProbeTimeSec" };
    var rv = result.Recovery;
    recLines.Add($"{rv.BaselineP95Ms:F2},{rv.BaselineAvgMs:F2},{rv.BaselineSamples},{rv.TRecover200Sec:F3},{rv.TRecoverP95Sec:F3},{rv.TotalProbeTimeSec:F3}");
    await File.WriteAllLinesAsync(recCsv, recLines);
    Console.WriteLine($"RECOVERY CSV: {recCsv}");
}

Console.WriteLine("=== BENCHMARK COMPLETE ===");
Console.WriteLine($"JSON: {jsonPath}");
Console.WriteLine($"PERF CSV: {perfCsv}");
Console.WriteLine($"ACC CSV: {accCsv}");
await File.AppendAllTextAsync(tracePath, $"DONE mode={mode} utc={DateTime.UtcNow:O}{Environment.NewLine}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("=== BENCHMARK FAILED ===");
    Console.Error.WriteLine(ex.ToString());
    var tracePath = Path.Combine(AppContext.BaseDirectory, "artifacts", "benchmark_trace.log");
    Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
    await File.AppendAllTextAsync(tracePath, $"FATAL ex={ex.GetType().Name} msg={ex.Message} utc={DateTime.UtcNow:O}{Environment.NewLine}");
    Environment.ExitCode = 1;
}

static async Task<string> LoginAsync(HttpClient http, string login, string password, JsonSerializerOptions options)
{
    var req = new { login, password };
    var json = JsonSerializer.Serialize(req, options);
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    var resp = await http.PostAsync("/api/auth/login", content);
    if (!resp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Login failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
    }

    var body = await resp.Content.ReadAsStringAsync();
    var doc = JsonDocument.Parse(body);
    if (!doc.RootElement.TryGetProperty("token", out var tokenEl))
    {
        throw new InvalidOperationException("Login response missing token");
    }

    return tokenEl.GetString() ?? throw new InvalidOperationException("Token is null");
}

static async Task EnsureAnchorsAsync(HttpClient http, JsonSerializerOptions options)
{
    var anchors = new[]
    {
        new { id = 1, name = "A1", x = 0.0, y = 0.0, z = 2.0, calibrationOffset = 0.0, status = 1 },
        new { id = 2, name = "A2", x = 50.0, y = 0.0, z = 2.0, calibrationOffset = 0.0, status = 1 },
        new { id = 3, name = "A3", x = 50.0, y = 50.0, z = 2.0, calibrationOffset = 0.0, status = 1 },
        new { id = 4, name = "A4", x = 0.0, y = 50.0, z = 4.0, calibrationOffset = 0.0, status = 1 }
    };

    foreach (var anchor in anchors)
    {
        var getResp = await http.GetAsync($"/api/anchors/{anchor.id}");
        using var body = new StringContent(JsonSerializer.Serialize(anchor, options), Encoding.UTF8, "application/json");
        if ((int)getResp.StatusCode == 404)
        {
            var createResp = await http.PostAsync("/api/anchors", body);
            if (!createResp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Create anchor failed for {anchor.id}: {(int)createResp.StatusCode} {await createResp.Content.ReadAsStringAsync()}");
            }
        }
        else if (getResp.IsSuccessStatusCode)
        {
            using var putBody = new StringContent(JsonSerializer.Serialize(anchor, options), Encoding.UTF8, "application/json");
            var putResp = await http.PutAsync($"/api/anchors/{anchor.id}", putBody);
            if (!putResp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Update anchor failed for {anchor.id}: {(int)putResp.StatusCode} {await putResp.Content.ReadAsStringAsync()}");
            }
        }
    }
}

static async Task<PerformanceResult> RunPerformanceProfileAsync(HttpClient http, PerfProfile profile, byte[] keyBytes, JsonSerializerOptions options, int beaconStart)
{
    var beaconIds = Enumerable.Range(1, profile.Beacons).Select(i => beaconStart + i).ToArray();
    foreach (var beaconId in beaconIds)
    {
        await ProvisionKeyAsync(http, beaconId, keyBytes, options);
    }

    var seqMap = beaconIds.ToDictionary(x => x, _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    var latencies = new List<double>(capacity: profile.Beacons * profile.FrequencyHz * profile.DurationSec);

    var ok = 0;
    var fail = 0;
    var swAll = Stopwatch.StartNew();

    var tickMs = 1000 / profile.FrequencyHz;
    var ticks = profile.DurationSec * profile.FrequencyHz;

    for (var t = 0; t < ticks; t++)
    {
        var tickSw = Stopwatch.StartNew();
        var tasks = new List<Task<(bool Ok, double Ms)>>();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var beaconId in beaconIds)
        {
            seqMap[beaconId]++;
            tasks.Add(PostTelemetryTimedAsync(http, beaconId, seqMap[beaconId], nowMs, keyBytes, options));
        }

        var tickResults = await Task.WhenAll(tasks);
        foreach (var r in tickResults)
        {
            if (r.Ok)
            {
                ok++;
                latencies.Add(r.Ms);
            }
            else
            {
                fail++;
            }
        }

        var remaining = tickMs - (int)tickSw.ElapsedMilliseconds;
        if (remaining > 0)
        {
            await Task.Delay(remaining);
        }
    }

    swAll.Stop();

    return new PerformanceResult
    {
        Profile = profile.Name,
        Beacons = profile.Beacons,
        FrequencyHz = profile.FrequencyHz,
        DurationSec = profile.DurationSec,
        RequestsTotal = ok + fail,
        Success = ok,
        Fail = fail,
        ThroughputRps = (ok + fail) / Math.Max(0.001, swAll.Elapsed.TotalSeconds),
        AvgMs = Percentile(latencies, 50, average: true),
        P50Ms = Percentile(latencies, 50),
        P95Ms = Percentile(latencies, 95),
        P99Ms = Percentile(latencies, 99)
    };
}

static async Task<AccuracyResult> RunAccuracyScenarioAsync(HttpClient http, string scenario, int beaconId, byte[] keyBytes, JsonSerializerOptions options, bool moving, bool nlosLike)
{
    await ProvisionKeyAsync(http, beaconId, keyBytes, options);

    var rnd = new Random(42 + beaconId);
    long seq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    var samples = 80;
    var errors = new List<double>(samples);

    for (var i = 0; i < samples; i++)
    {
        var t = moving ? i / (double)(samples - 1) : 0.5;
        var trueX = moving ? 10 + 30 * t : 25;
        var trueY = moving ? 10 + 20 * t : 25;
        var trueZ = 1.5;

        var anchors = new[]
        {
            (id: 1, x: 0.0, y: 0.0, z: 2.0),
            (id: 2, x: 50.0, y: 0.0, z: 2.0),
            (id: 3, x: 50.0, y: 50.0, z: 2.0),
            (id: 4, x: 0.0, y: 50.0, z: 4.0)
        };

        var distances = new List<AnchorDistance>();
        foreach (var a in anchors)
        {
            var d = Math.Sqrt(Math.Pow(trueX - a.x, 2) + Math.Pow(trueY - a.y, 2) + Math.Pow(trueZ - a.z, 2));
            var noiseSigma = nlosLike ? 0.6 : 0.15;
            var bias = nlosLike ? 0.8 : 0.0;
            var noisy = Math.Max(0.1, d + NextGaussian(rnd) * noiseSigma + bias);
            distances.Add(new AnchorDistance(a.id, noisy, -60));
        }

        seq++;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var packet = BuildPacket(beaconId, seq, ts, distances, keyBytes);

        using var content = new StringContent(JsonSerializer.Serialize(packet, options), Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        var resp = await http.PostAsync("/api/telemetry/measurement", content);
        sw.Stop();

        if (!resp.IsSuccessStatusCode)
        {
            continue;
        }

        var body = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("position", out var pos))
        {
            continue;
        }

        var x = pos.GetProperty("x").GetDouble();
        var y = pos.GetProperty("y").GetDouble();
        var z = pos.GetProperty("z").GetDouble();
        var err = Math.Sqrt(Math.Pow(x - trueX, 2) + Math.Pow(y - trueY, 2) + Math.Pow(z - trueZ, 2));
        errors.Add(err);

        await Task.Delay(120);
    }

    if (errors.Count == 0)
    {
        return new AccuracyResult { Scenario = scenario, Samples = 0, Rmse = 0, Mae = 0, MaxError = 0 };
    }

    var rmse = Math.Sqrt(errors.Select(e => e * e).Average());
    var mae = errors.Average();
    var max = errors.Max();

    return new AccuracyResult
    {
        Scenario = scenario,
        Samples = errors.Count,
        Rmse = rmse,
        Mae = mae,
        MaxError = max
    };
}

static async Task<SecurityResult> RunSecurityChecksAsync(HttpClient http, int beaconId, byte[] keyBytes, JsonSerializerOptions options)
{
    await ProvisionKeyAsync(http, beaconId, keyBytes, options);

    long seq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var replayAttempts = 120;
    var replayDetected = 0;

    for (var i = 0; i < replayAttempts; i++)
    {
        seq++;
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var distances = new List<AnchorDistance>
        {
            new(1, 10.0, -60),
            new(2, 11.0, -61),
            new(3, 12.0, -62)
        };

        var valid = BuildPacket(beaconId, seq, ts, distances, keyBytes);
        using var c1 = new StringContent(JsonSerializer.Serialize(valid, options), Encoding.UTF8, "application/json");
        var first = await http.PostAsync("/api/telemetry/measurement", c1);
        if (!first.IsSuccessStatusCode)
        {
            continue;
        }

        var replay = BuildPacket(beaconId, seq, ts, distances, keyBytes);
        using var c2 = new StringContent(JsonSerializer.Serialize(replay, options), Encoding.UTF8, "application/json");
        var second = await http.PostAsync("/api/telemetry/measurement", c2);
        if ((int)second.StatusCode == 409)
        {
            replayDetected++;
        }

        await Task.Delay(70);
    }

    // Rate limiting effectiveness: one-second flood on same beacon.
    await ProvisionKeyAsync(http, beaconId + 1, keyBytes, options);
    var floodBeacon = beaconId + 1;
    long floodSeq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var floodRequests = 120;
    var blocked429 = 0;
    var ok200 = 0;

    var floodTasks = new List<Task<int>>(floodRequests);
    var floodTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    for (var i = 0; i < floodRequests; i++)
    {
        floodSeq++;
        var packet = BuildPacket(floodBeacon, floodSeq, floodTs, new List<AnchorDistance>
        {
            new(1, 10.0, -60),
            new(2, 11.0, -61),
            new(3, 12.0, -62)
        }, keyBytes);

        floodTasks.Add(PostTelemetryStatusAsync(http, packet, options));
    }

    var statuses = await Task.WhenAll(floodTasks);
    foreach (var s in statuses)
    {
        if (s == 429) blocked429++;
        if (s == 200) ok200++;
    }

    return new SecurityResult
    {
        ReplayAttempts = replayAttempts,
        ReplayDetected = replayDetected,
        ReplayDetectionRatePercent = replayAttempts == 0 ? 0 : replayDetected * 100.0 / replayAttempts,
        FloodRequests = floodRequests,
        FloodBlocked429 = blocked429,
        FloodAllowed200 = ok200,
        FloodBlockRatePercent = floodRequests == 0 ? 0 : blocked429 * 100.0 / floodRequests
    };
}

static async Task<MixedTestResult> RunMixedTestAsync(HttpClient http, int baseBeaconId, byte[] keyBytes, JsonSerializerOptions options)
{
    var floodBeacon = baseBeaconId;
    var legitBeaconIds = new[] { baseBeaconId + 1, baseBeaconId + 2, baseBeaconId + 3 };

    await ProvisionKeyAsync(http, floodBeacon, keyBytes, options);
    foreach (var lb in legitBeaconIds) await ProvisionKeyAsync(http, lb, keyBytes, options);

    // 4 якоря — то же, что EnsureAnchorsAsync; иначе 3D trilateration даёт 500
    var distances = new List<AnchorDistance>
    {
        new(1, 10.0, -60), new(2, 11.0, -61), new(3, 12.0, -62), new(4, 13.0, -63)
    };

    // Shared monotonic flood seq (only flood task writes it after Task.Run starts)
    var floodSeqHolder = new long[] { DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
    var legitSeqs = legitBeaconIds.ToDictionary(x => x, _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    var floodCounters = new int[2]; // [0]=total, [1]=blocked429

    using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var floodTask = Task.Run(async () =>
    {
        while (!testCts.IsCancellationRequested)
        {
            var s = Interlocked.Increment(ref floodSeqHolder[0]);
            var p = BuildPacket(floodBeacon, s, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), distances, keyBytes);
            var status = await PostTelemetryStatusAsync(http, p, options);
            Interlocked.Increment(ref floodCounters[0]);
            if (status == 429) Interlocked.Increment(ref floodCounters[1]);
            try { await Task.Delay(20, testCts.Token); } catch (OperationCanceledException) { break; }
        }
    });

    // Legit traffic: 3 beacons × 1 req per 2s (0.5 req/s each — well under 20/s limit)
    var legitTotal = 0;
    var legitBlocked = 0;
    while (!testCts.IsCancellationRequested)
    {
        foreach (var lb in legitBeaconIds)
        {
            if (testCts.IsCancellationRequested) break;
            legitSeqs[lb]++;
            var p = BuildPacket(lb, legitSeqs[lb], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), distances, keyBytes);
            try
            {
                var status = await PostTelemetryStatusAsync(http, p, options);
                legitTotal++;
                if (status == 429) legitBlocked++;
            }
            catch { /* ignore transient */ }
        }
        try { await Task.Delay(2000, testCts.Token); } catch (OperationCanceledException) { break; }
    }

    try { await floodTask; } catch { /* ignore */ }

    return new MixedTestResult
    {
        LegitTotal = legitTotal,
        LegitBlocked429 = legitBlocked,
        FalsePositiveRatePercent = legitTotal == 0 ? 0 : legitBlocked * 100.0 / legitTotal,
        FloodTotal = floodCounters[0],
        FloodBlocked429 = floodCounters[1],
        FloodBlockRatePercent = floodCounters[0] == 0 ? 0 : floodCounters[1] * 100.0 / floodCounters[0]
    };
}

static async Task<RecoveryResult> RunRecoveryTestAsync(HttpClient http, int baseBeaconId, byte[] keyBytes, JsonSerializerOptions options)
{
    var targetBeacon = baseBeaconId;
    await ProvisionKeyAsync(http, targetBeacon, keyBytes, options);

    // 4 якоря — то же, что EnsureAnchorsAsync; 3D trilateration без 4 якорей даёт 500
    var distances = new List<AnchorDistance>
    {
        new(1, 10.0, -60), new(2, 11.0, -61), new(3, 12.0, -62), new(4, 13.0, -63)
    };

    var tracePath = Path.Combine(AppContext.BaseDirectory, "artifacts", "benchmark_trace.log");

    // Shared monotonic sequence across all phases to prevent replay rejections
    var seqHolder = new long[] { DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };

    // Phase 1: Baseline – 20 probes at 200 ms interval
    var baselineLatencies = new List<double>();
    for (var i = 0; i < 20; i++)
    {
        var s = Interlocked.Increment(ref seqHolder[0]);
        var (ok, ms) = await PostTelemetryTimedAsync(http, targetBeacon, s, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), keyBytes, options);
        if (ok) baselineLatencies.Add(ms);
        await Task.Delay(200);
    }
    var baselineP95 = Percentile(baselineLatencies, 95);
    var baselineAvg = baselineLatencies.Count > 0 ? baselineLatencies.Average() : 0;
    await File.AppendAllTextAsync(tracePath, $"RECOVERY baseline P95={baselineP95:F1}ms avg={baselineAvg:F1}ms n={baselineLatencies.Count}{Environment.NewLine}");

    // Phase 2: Saturate rate limiter with ~65 req/s burst for 3 s
    var floodCounters2 = new int[2]; // [0]=total, [1]=allowed
    using var floodCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var floodTask = Task.Run(async () =>
    {
        while (!floodCts.Token.IsCancellationRequested)
        {
            var s = Interlocked.Increment(ref seqHolder[0]);
            var p = BuildPacket(targetBeacon, s, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), distances, keyBytes);
            var status = await PostTelemetryStatusAsync(http, p, options);
            Interlocked.Increment(ref floodCounters2[0]);
            if (status is >= 200 and < 300) Interlocked.Increment(ref floodCounters2[1]);
            try { await Task.Delay(15, floodCts.Token); } catch (OperationCanceledException) { break; }
        }
    });
    await floodTask;
    await File.AppendAllTextAsync(tracePath, $"RECOVERY flood done: total={floodCounters2[0]} allowed={floodCounters2[1]} blocked={floodCounters2[0] - floodCounters2[1]}{Environment.NewLine}");

    // Phase 3: Recovery probe – poll every 300 ms; log each status for diagnostics
    var recSw = Stopwatch.StartNew();
    var deadline = TimeSpan.FromSeconds(15);
    double? tRecover200 = null;
    double? tRecoverP95 = null;
    var probeLatencies = new List<double>();
    var probeStatusLog = new List<string>();

    while (recSw.Elapsed < deadline)
    {
        var s = Interlocked.Increment(ref seqHolder[0]);
        var packet = BuildPacket(targetBeacon, s, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), distances, keyBytes);
        using var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(packet, options), System.Text.Encoding.UTF8, "application/json");
        var probeSw = Stopwatch.StartNew();
        int probeStatus;
        try
        {
            var resp = await http.PostAsync("/api/telemetry/measurement", content);
            probeSw.Stop();
            probeStatus = (int)resp.StatusCode;
        }
        catch (Exception ex)
        {
            probeSw.Stop();
            probeStatus = -1;
            probeStatusLog.Add($"t={recSw.Elapsed.TotalSeconds:F2}s EX:{ex.GetType().Name}");
        }

        var entry = $"t={recSw.Elapsed.TotalSeconds:F2}s status={probeStatus} seq={s}";
        probeStatusLog.Add(entry);
        await File.AppendAllTextAsync(tracePath, $"  PROBE {entry}{Environment.NewLine}");

        if (probeStatus is >= 200 and < 300)
        {
            tRecover200 ??= recSw.Elapsed.TotalSeconds;
            probeLatencies.Add(probeSw.Elapsed.TotalMilliseconds);
            if (probeLatencies.Count >= 5 && tRecoverP95 == null)
            {
                if (Percentile(probeLatencies, 95) <= Math.Max(baselineP95 * 2.0, 200))
                {
                    tRecoverP95 = recSw.Elapsed.TotalSeconds;
                    break;
                }
            }
        }
        await Task.Delay(300);
    }

    return new RecoveryResult
    {
        BaselineP95Ms = baselineP95,
        BaselineAvgMs = baselineAvg,
        BaselineSamples = baselineLatencies.Count,
        TRecover200Sec = tRecover200 ?? -1,
        TRecoverP95Sec = tRecoverP95 ?? -1,
        TotalProbeTimeSec = recSw.Elapsed.TotalSeconds,
        ProbeStatusSummary = string.Join("; ", probeStatusLog.Take(20))
    };
}

static async Task<int> PostTelemetryStatusAsync(HttpClient http, PacketDto packet, JsonSerializerOptions options)
{
    using var content = new StringContent(JsonSerializer.Serialize(packet, options), Encoding.UTF8, "application/json");
    var resp = await http.PostAsync("/api/telemetry/measurement", content);
    return (int)resp.StatusCode;
}

static async Task ProvisionKeyAsync(HttpClient http, int beaconId, byte[] keyBytes, JsonSerializerOptions options)
{
    // BeaconSecrets ссылается на Beacons, поэтому заранее гарантируем наличие маяка.
    var getBeacon = await http.GetAsync($"/api/beacons/{beaconId}");
    if ((int)getBeacon.StatusCode == 404)
    {
        var createBeaconReq = new
        {
            id = beaconId,
            name = $"Bench_{beaconId}",
            macAddress = $"BENCH-{beaconId}",
            batteryLevel = 100,
            status = 1
        };
        using var createContent = new StringContent(JsonSerializer.Serialize(createBeaconReq, options), Encoding.UTF8, "application/json");
        var createResp = await http.PostAsync("/api/beacons", createContent);
        if (!createResp.IsSuccessStatusCode && (int)createResp.StatusCode != 409)
        {
            throw new InvalidOperationException($"Create beacon failed for {beaconId}: {(int)createResp.StatusCode} {await createResp.Content.ReadAsStringAsync()}");
        }
    }

    var req = new
    {
        keyBase64 = Convert.ToBase64String(keyBytes),
        keyVersion = 1,
        previousGraceDays = 7
    };

    using var content = new StringContent(JsonSerializer.Serialize(req, options), Encoding.UTF8, "application/json");
    var resp = await http.PostAsync($"/api/security/beacons/{beaconId}/key", content);
    if (!resp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Provision key failed for {beaconId}: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");
    }
}

static async Task<(bool Ok, double Ms)> PostTelemetryTimedAsync(HttpClient http, int beaconId, long seq, long timestampMs, byte[] keyBytes, JsonSerializerOptions options)
{
    var distances = new List<AnchorDistance>
    {
        new(1, 10.5, -60),
        new(2, 16.2, -62),
        new(3, 18.1, -58),
        new(4, 12.4, -64)
    };

    var packet = BuildPacket(beaconId, seq, timestampMs, distances, keyBytes);

    using var content = new StringContent(JsonSerializer.Serialize(packet, options), Encoding.UTF8, "application/json");
    var sw = Stopwatch.StartNew();
    try
    {
        var resp = await http.PostAsync("/api/telemetry/measurement", content);
        sw.Stop();
        return (resp.IsSuccessStatusCode, sw.Elapsed.TotalMilliseconds);
    }
    catch
    {
        sw.Stop();
        return (false, sw.Elapsed.TotalMilliseconds);
    }
}

static PacketDto BuildPacket(int beaconId, long seq, long timestampMs, List<AnchorDistance> distances, byte[] keyBytes)
{
    var canonical = BuildCanonical(beaconId, seq, timestampMs, distances);
    using var hmac = new HMACSHA256(keyBytes);
    var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));

    return new PacketDto
    {
        BeaconId = beaconId,
        Sequence = seq,
        Timestamp = timestampMs,
        KeyVersion = 1,
        BatteryLevel = 90,
        Signature = sig,
        Distances = distances
    };
}

static string BuildCanonical(int beaconId, long seq, long timestamp, List<AnchorDistance> distances)
{
    var ordered = distances.OrderBy(x => x.AnchorId).ToArray();
    var sb = new StringBuilder();
    sb.Append(beaconId).Append('|').Append(seq).Append('|').Append(timestamp).Append('|');
    foreach (var d in ordered)
    {
        sb.Append(d.AnchorId)
            .Append(':')
            .Append(d.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(':')
            .Append(d.Rssi ?? 0)
            .Append(';');
    }
    return sb.ToString();
}

static double Percentile(List<double> data, double p, bool average = false)
{
    if (data.Count == 0) return 0;
    if (average) return data.Average();

    var sorted = data.OrderBy(x => x).ToArray();
    var pos = (p / 100.0) * (sorted.Length - 1);
    var left = (int)Math.Floor(pos);
    var right = (int)Math.Ceiling(pos);
    if (left == right) return sorted[left];
    var frac = pos - left;
    return sorted[left] + (sorted[right] - sorted[left]) * frac;
}

static double NextGaussian(Random rnd)
{
    var u1 = 1.0 - rnd.NextDouble();
    var u2 = 1.0 - rnd.NextDouble();
    return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
}

public sealed record PerfProfile(string Name, int Beacons, int FrequencyHz, int DurationSec);

public sealed class BenchmarkResult
{
    public DateTime TimestampUtc { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public List<PerformanceResult> Performance { get; set; } = [];
    public List<AccuracyResult> Accuracy { get; set; } = [];
    public SecurityResult Security { get; set; } = new();
    public MixedTestResult? MixedTest { get; set; }
    public RecoveryResult? Recovery { get; set; }
}

public sealed class PerformanceResult
{
    public string Profile { get; set; } = string.Empty;
    public int Beacons { get; set; }
    public int FrequencyHz { get; set; }
    public int DurationSec { get; set; }
    public int RequestsTotal { get; set; }
    public int Success { get; set; }
    public int Fail { get; set; }
    public double ThroughputRps { get; set; }
    public double AvgMs { get; set; }
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public string? Error { get; set; }
}

public sealed class AccuracyResult
{
    public string Scenario { get; set; } = string.Empty;
    public int Samples { get; set; }
    public double Rmse { get; set; }
    public double Mae { get; set; }
    public double MaxError { get; set; }
}

public sealed class SecurityResult
{
    public int ReplayAttempts { get; set; }
    public int ReplayDetected { get; set; }
    public double ReplayDetectionRatePercent { get; set; }
    public int FloodRequests { get; set; }
    public int FloodBlocked429 { get; set; }
    public int FloodAllowed200 { get; set; }
    public double FloodBlockRatePercent { get; set; }
}

public sealed class MixedTestResult
{
    /// <summary>Легитимных запросов от 3 отдельных маяков при нормальном темпе (0.5 req/s)</summary>
    public int LegitTotal { get; set; }
    /// <summary>Легитимных запросов, заблокированных 429 — доля = FalsePositiveRate</summary>
    public int LegitBlocked429 { get; set; }
    public double FalsePositiveRatePercent { get; set; }
    /// <summary>Запросов от атакующего маяка (~50 req/s)</summary>
    public int FloodTotal { get; set; }
    public int FloodBlocked429 { get; set; }
    public double FloodBlockRatePercent { get; set; }
}

public sealed class RecoveryResult
{
    public double BaselineP95Ms { get; set; }
    public double BaselineAvgMs { get; set; }
    public int BaselineSamples { get; set; }
    /// <summary>Секунд от конца флуда до первого HTTP 200. -1 = не восстановился за 15 с</summary>
    public double TRecover200Sec { get; set; }
    /// <summary>Секунд от конца флуда до p95 ≤ 2× baseline. -1 = не измерено</summary>
    public double TRecoverP95Sec { get; set; }
    public double TotalProbeTimeSec { get; set; }
    /// <summary>Первые 20 probe-статусов для диагностики</summary>
    public string ProbeStatusSummary { get; set; } = string.Empty;
}

public sealed class PacketDto
{
    public int BeaconId { get; set; }
    public List<AnchorDistance> Distances { get; set; } = [];
    public long Timestamp { get; set; }
    public int? BatteryLevel { get; set; }
    public long Sequence { get; set; }
    public int KeyVersion { get; set; } = 1;
    public string Signature { get; set; } = string.Empty;
}

public sealed record AnchorDistance(int AnchorId, double Distance, int? Rssi);
