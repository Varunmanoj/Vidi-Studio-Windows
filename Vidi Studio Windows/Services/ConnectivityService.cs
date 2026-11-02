using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;
using Vidi_Studio_Windows.Models;

namespace Vidi_Studio_Windows.Services;

public enum OnlineService { Gemini, YouTube, Voices }
public enum ConnectionState { Checking, Available, Offline, Unreachable }

/// <summary>Application-wide, credential-free reachability. All notifications
/// are marshalled to the UI dispatcher; windows subscribe for their lifetime.</summary>
public sealed class ConnectivityService
{
    public static ConnectivityService Shared { get; } = new();
    private readonly DispatcherQueue dispatcher;
    private readonly DispatcherQueueTimer timer;
    private readonly Dictionary<OnlineService, ConnectionState> states = new();
    private CancellationTokenSource? checking;
    private int generation;
    public bool Active { get; set; } = true;
    public event EventHandler? Changed;
    private ConnectivityService()
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        NetworkInformation.NetworkStatusChanged += _ => dispatcher.TryEnqueue(RouteChanged);
        timer = dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += (_, _) => { if (Active && Enum.GetValues<OnlineService>().Any(s => !Available(s))) Check(); };
        timer.Start(); RouteChanged();
    }
    private static Uri Origin(OnlineService service) => new(service switch
    {
        OnlineService.Gemini => "https://generativelanguage.googleapis.com/",
        OnlineService.YouTube => "https://www.youtube.com/",
        _ => "https://huggingface.co/"
    });
    public ConnectionState State(OnlineService service) => states.GetValueOrDefault(service, ConnectionState.Checking);
    public bool Available(OnlineService service) => State(service) == ConnectionState.Available;
    public string? Reason(OnlineService service)
    {
        var name = service == OnlineService.Voices ? "voice downloads" : service.ToString();
        return State(service) switch
        {
            ConnectionState.Available => null,
            ConnectionState.Checking => $"Checking the connection to {name}…",
            ConnectionState.Offline => $"No internet connection. Connect to the internet to use {name}.",
            _ => $"Cannot reach {name}. Check your connection or try again later."
        };
    }
    public string? Notice => Enum.GetValues<OnlineService>().All(Available) ? null
        : states.Values.Contains(ConnectionState.Offline)
        ? "You’re offline. Online processing and YouTube playback need internet. You can continue working with local files and installed voices."
        : string.Join(" ", Enum.GetValues<OnlineService>().Select(Reason).Where(s => s != null));
    private bool HasRoute()
    {
        try { return NetworkInformation.GetConnectionProfiles().Any(p => p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None); }
        catch { return true; } // Detection failures are resolved by HTTPS, not an offline lockout.
    }
    private void RouteChanged()
    {
        generation++; checking?.Cancel(); checking = null;
        if (!HasRoute())
        {
            generation++; checking?.Cancel(); checking = null;
            foreach (var service in Enum.GetValues<OnlineService>()) states[service] = ConnectionState.Offline;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else Check();
    }
    public async void Check()
    {
        if (!HasRoute())
        {
            generation++; checking?.Cancel(); checking = null;
            foreach (var service in Enum.GetValues<OnlineService>()) states[service] = ConnectionState.Offline;
            Changed?.Invoke(this, EventArgs.Empty); return;
        }
        if (checking != null) return;
        var token = ++generation;
        var request = checking = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.WhenAll(Enum.GetValues<OnlineService>().Select(async service =>
            {
                var reachable = false;
                try
                {
                    using var handler = new HttpClientHandler { UseCookies = false };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    using var message = new HttpRequestMessage(HttpMethod.Head, Origin(service));
                    using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, request.Token);
                    reachable = OnlineRequirements.AcceptsHost(response.RequestMessage?.RequestUri?.Host, service);
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
                if (generation != token) return;
                states[service] = reachable ? ConnectionState.Available : ConnectionState.Unreachable;
                Changed?.Invoke(this, EventArgs.Empty);
            }));
        }
        finally { if (generation == token) checking = null; request.Dispose(); }
    }
    public void Require(OnlineService service)
    {
        if (Reason(service) is { } reason) { Check(); throw new ConnectivityException(reason); }
    }
}

public sealed class ConnectivityException : Exception
{
    public ConnectivityException(string message) : base(message) { }
}

public sealed class ConnectionRecoveryWatch : IDisposable
{
    private readonly DispatcherQueueTimer timer;
    private readonly HashSet<OnlineService> active = new();
    private DateTimeOffset? offlineSince;
    public bool Interrupted { get; private set; }
    public ConnectionRecoveryWatch(Action cancel)
    {
        timer = DispatcherQueue.GetForCurrentThread().CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => {
            if (!active.Any(s => ConnectivityService.Shared.State(s) == ConnectionState.Offline)) { offlineSince = null; return; }
            offlineSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - offlineSince.Value >= TimeSpan.FromSeconds(30)) { Interrupted = true; timer.Stop(); cancel(); }
        };
        timer.Start();
    }
    public void Consume(System.Text.Json.JsonElement data)
    {
        if (!data.TryGetProperty("networkService", out var name) || !data.TryGetProperty("networkActive", out var value)
            || !Enum.TryParse<OnlineService>(name.GetString(), true, out var service)) return;
        if (value.GetBoolean()) active.Add(service); else active.Remove(service);
        if (active.Count == 0) offlineSince = null;
    }
    public void Dispose() => timer.Stop();
}
