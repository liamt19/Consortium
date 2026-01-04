
using System.Collections.Immutable;

namespace Consortium.Core.UCI;

public class IOBarrier(int expectedArrivals)
{
    //  isready, uci, go
    public static readonly ImmutableArray<string> BarrierWhitelist = ["readyok", "uciok", "bestmove"];
    public static bool IsBarrierable(string uc) => BarrierWhitelist.Any(uc.StartsWith);


    private readonly List<BarrierImpl> _barriers = new();
    private readonly int _expectedArrivals = expectedArrivals;

    private bool HasBarrierFor(string engName, string response) => _barriers.Any(b => response.StartsWith(b.ExpectedResponse) && !b.HasArrived(engName));
    private BarrierImpl BarrierFor(string engName, string response) => _barriers.First(b => response.StartsWith(b.ExpectedResponse) && !b.HasArrived(engName));

    public void AddBarrier(string command, string response, Action? whenCompleted = null)
    {
        _barriers.Add(new BarrierImpl(command, response, _expectedArrivals, whenCompleted));
        LogVerbose($"info string added barrier for {command} expecting {response}");
    }

    /// <returns>True if <paramref name="response"/> was being anticipated for <paramref name="engName"/> (and shouldn't be outputted)</returns>
    public bool Arrive(string engName, string response)
    {
        if (!HasBarrierFor(engName, response))
            return false;

        BarrierImpl barrier = BarrierFor(engName, response);
        barrier.Arrive(engName);
        if (barrier.IsCompleted)
        {
            _barriers.Remove(barrier);
            LogVerbose($"info string barrier completed!!");
            barrier.WhenCompleted?.Invoke();
        }

        return true;
    }


    private readonly struct BarrierImpl(string command, string expectedResponse, int arrivals, Action? whenCompleted = null)
    {
        public readonly string Command { get; } = command;
        public readonly string ExpectedResponse { get; } = expectedResponse;
        public readonly Action? WhenCompleted { get; } = whenCompleted;

        private readonly Dictionary<string, bool> _arrived = new();

        public bool IsCompleted => _arrived.Count(a => a.Value) >= arrivals;
        public bool HasArrived(string engName) => this[engName];
        public void Arrive(string engName) => this[engName] = true;

        public bool this[string engName]
        {
            get
            {
                if (_arrived.TryGetValue(engName, out bool r)) return r;
                return false;
            }
            set
            {
                if (!_arrived.TryAdd(engName, value))
                    _arrived[engName] = value;
            }
        }
    }
}
