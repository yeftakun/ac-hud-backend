using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AssettoCorsaSharedMemory;

internal static class Program
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(1),
    };

    private static readonly object StateLock = new();
    private static readonly Dictionary<string, object?> State = new()
    {
        ["abs.active"] = false,
        ["tc.active"] = false,
    };

    private static readonly CancellationTokenSource StopToken = new();
    private static readonly bool UseActivationHeuristic = ReadBooleanEnvironment("AC_HUD_ENABLE_ACTIVATION_HEURISTIC");
    private static AssettoCorsa? _assettoCorsa;
    private static int _lastCompletedLap = -1;
    private static float _fuelAtLastLap;
    private static double? _fuelPerLap;
    private static string? _lastVehicle;
    private static string? _lastTrack;
    private static int _lastRedline;

    private static async Task Main()
    {
        var backendUrl = Environment.GetEnvironmentVariable("AC_HUD_BACKEND_URL")
            ?? "http://127.0.0.1:3001/telemetry/ingest";
        var intervalMs = ReadInterval();

        Console.WriteLine("AC HUD telemetry collector");
        Console.WriteLine($"Backend: {backendUrl}");
        Console.WriteLine($"Publish interval: {intervalMs} ms");
        Console.WriteLine("Waiting for Assetto Corsa shared memory...");

        var assettoCorsa = new AssettoCorsa
        {
            PhysicsInterval = intervalMs,
            GraphicsInterval = 100,
            StaticInfoInterval = 3000,
        };
        _assettoCorsa = assettoCorsa;

        assettoCorsa.PhysicsUpdated += OnPhysicsUpdated;
        assettoCorsa.GraphicsUpdated += OnGraphicsUpdated;
        assettoCorsa.StaticInfoUpdated += OnStaticInfoUpdated;
        assettoCorsa.Start();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            StopToken.Cancel();
        };

        try
        {
            await PublishLoop(backendUrl, intervalMs, StopToken.Token);
        }
        finally
        {
            assettoCorsa.Stop();
            _assettoCorsa = null;
        }
    }

    private static int ReadInterval()
    {
        var raw = Environment.GetEnvironmentVariable("AC_HUD_PUBLISH_INTERVAL_MS");
        return int.TryParse(raw, out var interval) ? Math.Clamp(interval, 16, 200) : 33;
    }

    private static bool ReadBooleanEnvironment(string key)
    {
        return bool.TryParse(Environment.GetEnvironmentVariable(key), out var value) && value;
    }

    private static void OnStaticInfoUpdated(object? sender, StaticInfoEventArgs eventArgs)
    {
        var info = eventArgs.StaticInfo;
        UpdateState(new Dictionary<string, object?>
        {
            ["redline"] = info.MaxRpm,
            ["vehicle.model"] = info.CarModel?.TrimEnd('\0', ' '),
            ["track.name"] = info.Track?.TrimEnd('\0', ' '),
        });

        var vehicle = info.CarModel?.TrimEnd('\0', ' ') ?? "unknown vehicle";
        var track = info.Track?.TrimEnd('\0', ' ') ?? "unknown track";
        if (vehicle != _lastVehicle || track != _lastTrack || info.MaxRpm != _lastRedline)
        {
            Console.WriteLine($"Connected: {vehicle} @ {track}, redline {info.MaxRpm}");
            _lastVehicle = vehicle;
            _lastTrack = track;
            _lastRedline = info.MaxRpm;
        }
    }

    private static void OnPhysicsUpdated(object? sender, PhysicsEventArgs eventArgs)
    {
        var physics = eventArgs.Physics;
        var wheelSlip = physics.WheelSlip ?? Array.Empty<float>();
        var maxSlip = wheelSlip.Length == 0 ? 0f : wheelSlip.Max(Math.Abs);
        var brake = physics.Brake;
        var throttle = physics.Gas;

        var absActive = UseActivationHeuristic && physics.Abs > 0 && brake > 0.2f && maxSlip > 0.08f;
        var tcActive = UseActivationHeuristic && physics.TC > 0 && throttle > 0.5f && maxSlip > 0.08f;
        var brakeBias = physics.BrakeBias <= 1.01f ? physics.BrakeBias * 100 : physics.BrakeBias;

        UpdateState(new Dictionary<string, object?>
        {
            ["speed"] = (int)Math.Round(Math.Max(0, physics.SpeedKmh), MidpointRounding.AwayFromZero),
            ["gear"] = FormatGear(physics.Gear),
            ["rpm"] = physics.Rpms,
            ["fuel"] = Math.Max(0, physics.Fuel),
            ["delta"] = physics.PerformanceMeter,
            ["abs"] = physics.Abs,
            ["tc"] = physics.TC,
            ["bb"] = brakeBias,
            ["abs.active"] = absActive,
            ["tc.active"] = tcActive,
            ["pit.active"] = physics.PitLimiterOn != 0,
            ["drs.active"] = physics.Drs > 0.5f || physics.DrsEnabled != 0,
            ["tyres.FL.psi"] = ReadArray(physics.WheelsPressure, 0),
            ["tyres.FR.psi"] = ReadArray(physics.WheelsPressure, 1),
            ["tyres.RL.psi"] = ReadArray(physics.WheelsPressure, 2),
            ["tyres.RR.psi"] = ReadArray(physics.WheelsPressure, 3),
            ["tyres.FL.temp"] = ReadArray(physics.TyreTempM, 0),
            ["tyres.FR.temp"] = ReadArray(physics.TyreTempM, 1),
            ["tyres.RL.temp"] = ReadArray(physics.TyreTempM, 2),
            ["tyres.RR.temp"] = ReadArray(physics.TyreTempM, 3),
        });
    }

    private static string FormatGear(int rawGear)
    {
        return (rawGear - 1) switch
        {
            -1 => "R",
            0 => "N",
            var gear => gear.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static void OnGraphicsUpdated(object? sender, GraphicsEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        var currentLapSeconds = graphics.iCurrentTime / 1000d;
        var lastLapSeconds = graphics.iLastTime / 1000d;
        var bestLapSeconds = graphics.iBestTime / 1000d;
        var fuel = ReadNumber("fuel");

        if (_lastCompletedLap >= 0 && graphics.CompletedLaps > _lastCompletedLap && fuel.HasValue)
        {
            var consumed = _fuelAtLastLap - fuel.Value;
            if (consumed > 0.01) _fuelPerLap = consumed;
        }

        if (graphics.CompletedLaps != _lastCompletedLap && fuel.HasValue)
        {
            _lastCompletedLap = graphics.CompletedLaps;
            _fuelAtLastLap = (float)fuel.Value;
        }

        var estimatedLaps = _fuelPerLap is > 0 && fuel.HasValue ? fuel.Value / _fuelPerLap.Value : (double?)null;

        UpdateState(new Dictionary<string, object?>
        {
            ["lap.counter"] = graphics.CompletedLaps,
            ["lap.current"] = currentLapSeconds > 0 ? currentLapSeconds : null,
            ["lap.last"] = lastLapSeconds > 0 ? lastLapSeconds : null,
            ["lap.best"] = bestLapSeconds > 0 ? bestLapSeconds : null,
            ["lap.est"] = estimatedLaps,
            ["position"] = graphics.Position,
        });
    }

    private static double? ReadNumber(string key)
    {
        lock (StateLock)
        {
            return State.TryGetValue(key, out var value) && value is IConvertible convertible
                ? convertible.ToDouble(null)
                : null;
        }
    }

    private static float? ReadArray(float[]? values, int index)
    {
        return values is { Length: > 0 } && index < values.Length ? values[index] : null;
    }

    private static void UpdateState(IReadOnlyDictionary<string, object?> values)
    {
        lock (StateLock)
        {
            foreach (var pair in values) State[pair.Key] = pair.Value;
        }
    }

    private static Dictionary<string, object?> SnapshotState()
    {
        lock (StateLock) return new Dictionary<string, object?>(State);
    }

    private static async Task PublishLoop(string backendUrl, int intervalMs, CancellationToken cancellationToken)
    {
        var wasConnected = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            var connected = _assettoCorsa?.IsRunning == true;
            if (connected != wasConnected)
            {
                Console.WriteLine(connected ? "Assetto Corsa shared memory connected." : "Assetto Corsa shared memory disconnected.");
                wasConnected = connected;
            }

            if (connected)
            {
                try
                {
                    using var response = await Http.PostAsJsonAsync(backendUrl, SnapshotState(), cancellationToken);
                    if (!response.IsSuccessStatusCode) Console.WriteLine($"Backend returned {(int)response.StatusCode}.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    Console.WriteLine($"Backend publish failed: {error.Message}");
                }
            }

            try
            {
                await Task.Delay(intervalMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
